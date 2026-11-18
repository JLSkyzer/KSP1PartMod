using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Model;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.4 (<c>fast_texture_lookup</c>): Redux's <c>Ksp1TextureLoader.ResolveTexturePathRecursive</c> runs
    /// <c>Directory.GetFiles(root, name + ext, AllDirectories)</c> once per extension (6) for every texture name it does not
    /// find next to the model, a full walk of <c>mods/KSP1</c> each time (about 0.26 to 0.37 s per missing name on the
    /// author's 8 562 files, measured under .NET 8 on 2026-11-18); its cache of results is emptied with the texture cache.
    /// A prefix answers from an index of every file under the root, built once per root and kept for the session (the
    /// files do not change while the game runs): same first match, in <c>Directory.GetFiles</c>'s own order, same
    /// extensions, same null for a miss. A summary line at the end of each import counts the lookups and the misses.
    /// Never throws; any failure falls back to Redux's own search for the rest of the session.</summary>
    internal static class TextureLookupIndex
    {
        public const string HarmonyId = "KSP1PartMod.TextureLookupIndex";
        private const string Prefix = "[KSP1PartMod] textures: lookup: ";

        private static readonly Dictionary<string, Dictionary<string, string>> Indexes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();
        private static string[] _extensions = { ".dds", ".mbm", ".png", ".jpg", ".jpeg", ".tga" };
        private static bool _broken;

        private static int _calls, _indexes, _files;
        private static long _lookupTicks, _buildTicks;
        private static readonly HashSet<string> MissNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryApply(Harmony harmony, bool enabled)
        {
            if (!enabled)
            {
                KSP1PartModPlugin.Log?.LogInfo(Prefix + "off (fast_texture_lookup false): Redux's recursive search");
                return false;
            }
            var target = AccessTools.Method(typeof(Ksp1TextureLoader), "ResolveTexturePathRecursive", new[] { typeof(string), typeof(string) });
            var ext = AccessTools.Field(typeof(Ksp1TextureLoader), "Extensions");
            if (target == null || !target.IsStatic || target.ReturnType != typeof(string) || ext == null || !(ext.GetValue(null) is string[] extensions))
            {
                KSP1PartModPlugin.Log?.LogWarning(Prefix + "Ksp1TextureLoader.ResolveTexturePathRecursive or Extensions not found, Redux's recursive search kept");
                return false;
            }
            _extensions = extensions.ToArray();
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TextureLookupIndex), nameof(BeforeResolve)));
            var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
            if (complete != null)
            {
                // Its own try: the index is already live, a failure here only loses the summary line.
                try { harmony.Patch(complete, postfix: new HarmonyMethod(typeof(TextureLookupIndex), nameof(AfterImportComplete))); }
                catch (Exception e) { KSP1PartModPlugin.Log?.LogWarning(Prefix + "summary patch failed, no summary line: " + e.Message); }
            }
            KSP1PartModPlugin.Log?.LogInfo(Prefix + "index armed on Ksp1TextureLoader.ResolveTexturePathRecursive (" + string.Join(" ", _extensions) + ")");
            return true;
        }

        /// <summary>Skips Redux's search when the index answers (any outcome, a miss included).</summary>
        internal static bool BeforeResolve(string gameDataRoot, string textureName, ref string __result)
        {
            if (_broken) return true;
            try
            {
                if (string.IsNullOrWhiteSpace(gameDataRoot) || string.IsNullOrWhiteSpace(textureName))
                {
                    __result = null;
                    return false;
                }
                var sw = Stopwatch.StartNew();
                lock (Gate)
                {
                    if (!Indexes.TryGetValue(gameDataRoot, out var index))
                    {
                        var build = Stopwatch.StartNew();
                        index = Build(Directory.GetFiles(gameDataRoot, "*", SearchOption.AllDirectories));
                        _buildTicks += build.ElapsedTicks;
                        _indexes++;
                        _files += index.Count;
                        Indexes[gameDataRoot] = index;
                    }
                    __result = Lookup(index, textureName, _extensions);
                    _calls++;
                    if (__result == null) MissNames.Add(textureName);
                    _lookupTicks += sw.ElapsedTicks;
                }
                return false;
            }
            catch (Exception e)
            {
                _broken = true;
                KSP1PartModPlugin.Log?.LogError(Prefix + "index failed, Redux's recursive search for the rest of the session: " + e);
                return true;
            }
        }

        /// <summary>File name (with extension) -> first path, in the order given (that of <c>Directory.GetFiles</c>).</summary>
        internal static Dictionary<string, string> Build(IEnumerable<string> paths)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                var name = Path.GetFileName(p);
                if (!string.IsNullOrEmpty(name) && !index.ContainsKey(name)) index[name] = p;
            }
            return index;
        }

        /// <summary>Redux's rule: the name without extension, tried with its own extension if it has one, else with each
        /// extension in order; the first hit wins.</summary>
        internal static string Lookup(Dictionary<string, string> index, string textureName, string[] extensions)
        {
            var stem = Path.GetFileNameWithoutExtension(textureName);
            var exts = Path.HasExtension(textureName) ? new[] { Path.GetExtension(textureName) } : extensions;
            foreach (var e in exts)
                if (index.TryGetValue(stem + e, out var path)) return path;
            return null;
        }

        internal static void AfterImportComplete()
        {
            try
            {
                lock (Gate)
                {
                    var f = Stopwatch.Frequency / 1000.0;
                    KSP1PartModPlugin.Log?.LogInfo(Prefix + $"{_calls} recursive lookup(s) answered from the index in {_lookupTicks / f:F1} ms, " +
                        $"{MissNames.Count} distinct missing name(s) (each cost Redux {_extensions.Length} full walks of the folder per import); " +
                        $"{_indexes} index(es) of {_files} file(s) built in {_buildTicks / f:F1} ms" +
                        (MissNames.Count > 0 ? "; missing: " + string.Join(", ", MissNames.Take(15)) + (MissNames.Count > 15 ? ", ..." : "") : ""));
                    _calls = 0;
                    _lookupTicks = 0;
                    _buildTicks = 0;
                }
            }
            catch (Exception)
            {
                // A summary line never matters more than the import.
            }
        }
    }
}
