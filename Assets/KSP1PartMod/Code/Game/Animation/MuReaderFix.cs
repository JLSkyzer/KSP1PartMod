using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSP1PartMod.Animation.Mu;
using Redux.Ksp1Import.Model;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Makes Redux's <see cref="Ksp1MuReader"/> read <c>.mu</c> models of versions 0 to 2. In those
    /// versions each object header carries one float after the scale; <c>Ksp1MuReader.ReadObject</c> reads only
    /// position, rotation and scale, so it takes that float for an entry tag and throws. The patch inserts one
    /// call in <c>ReadObject</c>, right after the scale is stored, that reads the extra float when the file
    /// version is below 3 and does nothing otherwise.
    ///
    /// A transpiler rather than a prefix: the missing read sits in the middle of <c>ReadObject</c>, and a prefix
    /// could only skip it by re-implementing the whole object loop. The guard is behavioural as well as by name:
    /// no patch when Redux already reads a version 2 file, and the patch is taken back if the patched reader then
    /// fails on a version 2 or 3 file (Redux changed in a way the member checks cannot see).</summary>
    internal static class MuReaderFix
    {
        public const string HarmonyId = "KSP1PartMod.MuReaderFix";

        private const string Log = "[KSP1PartMod] mu: ";
        private const string TargetMethod = "ReadObject";
        private const string ReaderField = "_reader";
        private const string ScaleField = "LocalScale";

        private static FieldInfo _readerField, _scaleField;

        /// <summary>Patches the reader only when every target is found as expected and Redux's reader, as it is,
        /// fails on a generated version 2 file. After patching, it reads a generated version 2 and version 3 file
        /// again; if either fails, it removes its patch. report is the line to log.</summary>
        public static bool TryApply(Harmony h, out string report)
        {
            var method = Find(typeof(Ksp1MuReader), out report);
            if (method == null) return false;
            if (SelfCheck(2) == null)
            {
                report = $"{Log}fix skipped: Redux already reads v0-2";
                return false;
            }
            h.Patch(method, transpiler: new HarmonyMethod(typeof(MuReaderFix), nameof(Transpiler)));
            var failure = SelfCheck(2) ?? SelfCheck(3);
            if (failure == null) return true;
            h.Unpatch(method, HarmonyPatchType.Transpiler, h.Id);
            report = $"{Log}fix skipped: patched reader failed the self-check ({failure})";
            return false;
        }

        public static void Remove(Harmony h)
        {
            var method = AccessTools.Method(typeof(Ksp1MuReader), TargetMethod, new[] { typeof(int) });
            if (method != null) h.Unpatch(method, HarmonyPatchType.Transpiler, h.Id);
        }

        /// <summary>Reads, with Redux's reader as it is now, a generated file of this version: a root object with
        /// one child. Null when it reads as expected, else the reason.</summary>
        internal static string SelfCheck(int version)
        {
            var path = Path.Combine(Path.GetTempPath(), $"KSP1PartMod.mucheck.{Guid.NewGuid():N}.mu");
            try
            {
                File.WriteAllBytes(path, CheckFile(version));
                var root = Ksp1MuReader.Read(path).Root;
                return root.Name == "root" && root.Children.Count == 1 && root.Children[0].Name == "child"
                       && root.Children[0].LocalScale.y == 2f
                    ? null
                    : $"v{version} file read with the wrong tree";
            }
            catch (Exception e)
            {
                return $"v{version}: {e.GetType().Name}: {e.Message}";
            }
            finally
            {
                try { File.Delete(path); }
                catch (Exception) { /* a temp file left behind is harmless */ }
            }
        }

        /// <summary>Magic, version, name, then a root holding one child (scale 2,2,2) closed by an end tag.</summary>
        private static byte[] CheckFile(int version)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(MuReader.Magic);
                    w.Write(version);
                    w.Write("check");
                    WriteHeader(w, "root", 1f, version);
                    w.Write(0); // child start
                    WriteHeader(w, "child", 2f, version);
                    w.Write(1); // child end
                }
                return ms.ToArray();
            }
        }

        private static void WriteHeader(BinaryWriter w, string name, float scale, int version)
        {
            w.Write(name);
            foreach (var f in new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, scale, scale, scale }) w.Write(f);
            for (var i = MuReader.ObjectHeaderExtraFloats(version); i > 0; i--) w.Write(scale);
        }

        /// <summary>The line <see cref="TryApply"/> would log for this reader type after the member checks only
        /// (no self-check, no patch).</summary>
        internal static string ReportFor(Type readerType)
        {
            Find(readerType, out var report);
            return report;
        }

        /// <summary>Called by the patched <c>ReadObject</c> after the scale: reads the extra header floats of the
        /// file's version.</summary>
        public static void SkipObjectHeaderTail(BinaryReader reader, int version)
        {
            for (var i = MuReader.ObjectHeaderExtraFloats(version); i > 0; i--) reader.ReadSingle();
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ci in instructions)
            {
                yield return ci;
                if (!ci.StoresField(_scaleField)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, _readerField);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return CodeInstruction.Call(typeof(MuReaderFix), nameof(SkipObjectHeaderTail));
            }
        }

        /// <summary>The method to patch, or null with the reason. Checks names, parameter and return types, and
        /// that the method stores the scale exactly once.</summary>
        private static MethodInfo Find(Type readerType, out string report)
        {
            var method = AccessTools.DeclaredMethod(readerType, TargetMethod, new[] { typeof(int) });
            if (method == null || method.IsStatic || method.ReturnType != typeof(MuObject))
                return Skip($"Ksp1MuReader.{TargetMethod}(int) returning MuObject", out report);
            var reader = AccessTools.DeclaredField(readerType, ReaderField);
            if (reader == null || reader.IsStatic || reader.FieldType != typeof(BinaryReader))
                return Skip($"Ksp1MuReader.{ReaderField} (BinaryReader)", out report);
            var scale = AccessTools.DeclaredField(typeof(MuObject), ScaleField);
            if (scale == null || scale.IsStatic) return Skip($"MuObject.{ScaleField}", out report);
            var stores = PatchProcessor.GetOriginalInstructions(method).Count(ci => ci.StoresField(scale));
            if (stores != 1) return Skip($"a single store of MuObject.{ScaleField} in {TargetMethod} ({stores} found)", out report);
            _readerField = reader;
            _scaleField = scale;
            report = $"{Log}fix enabled on Ksp1MuReader.{TargetMethod}";
            return method;
        }

        private static MethodInfo Skip(string member, out string report)
        {
            report = $"{Log}fix skipped: {member} not found in Ksp2Redux {typeof(Ksp1MuReader).Assembly.GetName().Version}";
            return null;
        }
    }
}
