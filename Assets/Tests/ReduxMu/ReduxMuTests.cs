using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;
using Redux.Ksp1Import.Model;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Redux's own <c>.mu</c> reader (<see cref="Ksp1MuReader"/>) on the installed stock files and on
    /// generated ones, with and without <see cref="MuReaderFix"/>.</summary>
    public class ReduxMuTests
    {
        private const string TestHarmonyId = "KSP1PartMod.MuReaderFix.Tests";

        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "KSP1PartMod.ReduxMuTests");

        [OneTimeTearDown]
        public void DeleteTempFiles()
        {
            if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
        }

        // ---- the cause ----

        /// <summary>For each stock file Redux fails on, removes the 4 bytes that follow the 10 header floats of
        /// every object, and gives the result to Redux again. If every such file then loads with the same tree as
        /// our reader finds, the object header width is the one and only cause.</summary>
        [Test] public void Diagnose_OldFilesFail_OnlyByObjectHeaderWidth()
        {
            var failing = FailingStock();
            Assert.IsNotEmpty(failing);
            var extraValues = new SortedSet<float>();
            var causes = new List<string>();
            foreach (var p in failing)
            {
                var stripped = WithoutObjectHeaderTails(p, extraValues);
                string cause;
                try
                {
                    var redux = Ksp1MuReader.Read(stripped);
                    cause = ReduxNamesAndClips(redux) == OurNamesAndClips(p)
                        ? "object header: 11 floats in v0-2, Ksp1MuReader.ReadObject reads 10"
                        : "loads without the extra float, but the tree differs from ours";
                }
                catch (Exception e) { cause = "still fails without the extra float: " + e.Message; }
                causes.Add(cause);
                if (!cause.StartsWith("object header")) TestContext.WriteLine($"{Rel(p)}: {cause}");
            }
            var distinct = causes.Distinct().ToList();
            TestContext.WriteLine($"{failing.Count} failing stock file(s), versions " +
                                  string.Join(",", failing.Select(p => Ours(p).Version).Distinct().OrderBy(v => v)) +
                                  $"; first Redux error: {ReduxError(failing[0]).Message}");
            TestContext.WriteLine("extra float values: " + F(extraValues.ToArray()));
            TestContext.WriteLine($"{_headers} object headers, 11th float equal to the x scale in {_extraIsScaleX}");
            TestContext.WriteLine("headers whose 11th float is not 1 (pos3 rot4 scale3 extra):\n" + string.Join("\n", Samples));
            TestContext.WriteLine("causes: " + string.Join(" | ", distinct));
            Assert.AreEqual(1, distinct.Count, string.Join(" | ", distinct));
            StringAssert.StartsWith("object header", distinct[0]);
        }

        // ---- the fix on installed files ----

        [Test] public void ReduxReader_WithoutFix_FailsOnOldFiles()
        {
            var failing = FailingStock();
            TestContext.WriteLine($"stock: {OldStock().Count()} v0-2, {V3To5().Count()} v3-5, {failing.Count} failing in Redux");
            CollectionAssert.AreEquivalent(OldStock(), failing); // every v0-2 file fails, no v3-5 file does
        }

        [Test] public void ReduxReader_WithFix_LoadsAllStockFiles()
        {
            using (Fixed()) CollectionAssert.IsEmpty(AllStock().Where(Throws).Select(p => $"{Rel(p)}: {ReduxError(p).Message}"));
        }

        [Test] public void ReduxReader_WithFix_V3To5TreesUnchanged()
        {
            var files = V3To5().ToList();
            Assert.IsNotEmpty(files);
            var before = files.ToDictionary(p => p, ReduxSignature);
            using (Fixed())
                foreach (var p in files) Assert.AreEqual(before[p], ReduxSignature(p), p);
        }

        [Test] public void ReduxReader_WithFix_MatchesOurReaderOnOldFiles()
        {
            var files = OldStock().ToList();
            Assert.IsNotEmpty(files);
            using (Fixed())
                foreach (var p in files) Assert.AreEqual(OurNamesAndClips(p), ReduxNamesAndClips(Ksp1MuReader.Read(p)), p);
        }

        // ---- the fix on generated files (no install needed) ----

        private static MuObj Sample() =>
            MuBytes.Obj("root").Child(MuBytes.Obj("door").Anim("open", new[] { ("", "m_LocalRotation.x", 0) }));

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void ReduxReader_GeneratedOldFile_FailsWithoutFixLoadsWithFix(int version)
        {
            var path = WriteTemp(MuBytes.File(Sample(), version).Bytes, $"v{version}.mu");
            Assert.Throws<InvalidDataException>(() => Ksp1MuReader.Read(path));
            using (Fixed())
            {
                var root = Ksp1MuReader.Read(path).Root;
                Assert.AreEqual("open", root.Children[0].Animation.Clips[0].Name);
                Assert.AreEqual(1f, root.LocalScale.x);
            }
        }

        [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void ReduxReader_GeneratedNewFile_SameWithFix(int version)
        {
            var path = WriteTemp(MuBytes.File(Sample(), version).Bytes, $"v{version}.mu");
            var before = ReduxSignature(path);
            using (Fixed()) Assert.AreEqual(before, ReduxSignature(path));
        }

        [Test] public void Fix_RemovedAfterUse_OldFileFailsAgain()
        {
            var path = WriteTemp(MuBytes.File(Sample(), 2).Bytes, "removed.mu");
            using (Fixed()) Ksp1MuReader.Read(path);
            Assert.Throws<InvalidDataException>(() => Ksp1MuReader.Read(path));
        }

        // ---- the guard ----

        [Test] public void Fix_TargetMissing_ReportsSkipAndPatchesNothing() =>
            StringAssert.StartsWith("[KSP1PartMod] mu: fix skipped:", MuReaderFix.ReportFor(typeof(object)));

        [Test] public void Fix_TargetPresent_ReportsEnabled() =>
            Assert.AreEqual("[KSP1PartMod] mu: fix enabled on Ksp1MuReader.ReadObject",
                MuReaderFix.ReportFor(typeof(Ksp1MuReader)));

        // ---- helpers ----

        private sealed class FixScope : IDisposable
        {
            private readonly Harmony _harmony = new Harmony(TestHarmonyId);

            public FixScope()
            {
                if (!MuReaderFix.TryApply(_harmony, out var report)) Assert.Fail(report);
            }

            public void Dispose() => MuReaderFix.Remove(_harmony);
        }

        private static IDisposable Fixed() => new FixScope();

        private static string[] _allStock;
        private static string[] AllStock() => _allStock ??= InstalledKsp1.AllMu().Where(InstalledKsp1.IsStock).ToArray();

        private static readonly Dictionary<string, MuFile> OurFiles = new Dictionary<string, MuFile>();
        private static MuFile Ours(string p) => OurFiles.TryGetValue(p, out var f) ? f : OurFiles[p] = MuReader.Read(p);

        private static IEnumerable<string> OldStock() => AllStock().Where(p => Ours(p).Version <= 2);
        private static IEnumerable<string> V3To5() => AllStock().Where(p => Ours(p).Version >= 3);

        private static Exception ReduxError(string p)
        {
            try
            {
                Ksp1MuReader.Read(p);
                return null;
            }
            catch (Exception e) { return e; }
        }

        private static bool Throws(string p) => ReduxError(p) != null;

        /// <summary>The stock files where Redux's reader throws (without the fix).</summary>
        private static List<string> FailingStock() => AllStock().Where(Throws).ToList();

        private static string Rel(string p) => p.Substring(InstalledKsp1.Root.Length).TrimStart('\\', '/');

        private static string WriteTemp(byte[] bytes, string name)
        {
            Directory.CreateDirectory(TempDir);
            var path = Path.Combine(TempDir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        /// <summary>A copy of the file without the 4 bytes that follow each object's position, rotation and scale.
        /// Object headers start after the file header (root) and after each child start tag.</summary>
        private static string WithoutObjectHeaderTails(string p, ISet<float> extraValues)
        {
            var bytes = File.ReadAllBytes(p);
            var f = Ours(p);
            var starts = new List<long> { FileHeaderLength(bytes) };
            starts.AddRange(f.Entries.Where(e => e.Type == MuEntryType.ChildStart).Select(e => e.Offset + 4));
            var cuts = new SortedSet<long>();
            using (var br = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                foreach (var start in starts)
                {
                    br.BaseStream.Position = start;
                    br.ReadString();
                    var floats = Enumerable.Range(0, 11).Select(_ => br.ReadSingle()).ToArray();
                    _headers++;
                    if (floats[10] == floats[7]) _extraIsScaleX++;
                    if (floats[10] != 1f && Samples.Count < 8) Samples.Add($"{Rel(p)}@{start}: {F(floats)}");
                    extraValues.Add(floats[10]);
                    cuts.Add(br.BaseStream.Position - 4);
                }
            var output = new MemoryStream(bytes.Length);
            long from = 0;
            foreach (var cut in cuts)
            {
                output.Write(bytes, (int)from, (int)(cut - from));
                from = cut + 4;
            }
            output.Write(bytes, (int)from, (int)(bytes.Length - from));
            return WriteTemp(output.ToArray(), "stripped_" + Path.GetFileName(p));
        }

        private static long FileHeaderLength(byte[] bytes)
        {
            using (var br = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                br.ReadInt32();
                br.ReadInt32();
                br.ReadString();
                return br.BaseStream.Position;
            }
        }

        /// <summary>Node paths depth first, each with its clips as name:curveCount, from our reader.</summary>
        private static string OurNamesAndClips(string p)
        {
            var sb = new StringBuilder();
            void Walk(MuNode n)
            {
                sb.Append(n.Path());
                if (n.Animation != null)
                    sb.Append(" [").Append(string.Join(",", n.Animation.Clips.Select(c => c.Name + ":" + c.Curves.Count))).Append(']');
                sb.Append('\n');
                foreach (var c in n.Children) Walk(c);
            }
            Walk(Ours(p).Root);
            return sb.ToString();
        }

        /// <summary>The same text as <see cref="OurNamesAndClips"/>, from Redux's tree.</summary>
        private static string ReduxNamesAndClips(Ksp1MuReader r)
        {
            var sb = new StringBuilder();
            void Walk(MuObject o, string parent)
            {
                var path = parent == null ? o.Name : parent + "/" + o.Name;
                sb.Append(path);
                if (o.Animation != null)
                    sb.Append(" [").Append(string.Join(",", o.Animation.Clips.Select(c => c.Name + ":" + c.Curves.Count))).Append(']');
                sb.Append('\n');
                foreach (var c in o.Children) Walk(c, path);
            }
            Walk(r.Root, null);
            return sb.ToString();
        }

        /// <summary>Redux's tree in the shape of <see cref="MuTreeSignature"/>: one line per object with its path,
        /// child count, transform, the components Redux kept, and clips; then the material and texture counts.</summary>
        private static string ReduxSignature(string p)
        {
            var r = Ksp1MuReader.Read(p);
            var sb = new StringBuilder();
            void Walk(MuObject o, string parent)
            {
                var path = parent == null ? o.Name : parent + "/" + o.Name;
                sb.Append(path).Append(" children=").Append(o.Children.Count)
                    .Append(" t=").Append(F(o.LocalPosition.x, o.LocalPosition.y, o.LocalPosition.z,
                        o.LocalRotation.x, o.LocalRotation.y, o.LocalRotation.z, o.LocalRotation.w,
                        o.LocalScale.x, o.LocalScale.y, o.LocalScale.z))
                    .Append(" parts=[");
                var parts = new List<string>();
                if (o.Mesh != null) parts.Add("mesh");
                if (o.Renderer != null) parts.Add("renderer");
                if (o.SkinnedMesh != null) parts.Add("skinned");
                if (o.MeshCollider != null) parts.Add("meshCollider");
                if (o.SphereCollider != null) parts.Add("sphere");
                if (o.CapsuleCollider != null) parts.Add("capsule");
                if (o.BoxCollider != null) parts.Add("box");
                if (o.Light != null) parts.Add("light");
                sb.Append(string.Join(",", parts)).Append(']');
                if (o.Animation != null)
                    sb.Append(" clips=[").Append(string.Join(",", o.Animation.Clips.Select(c => c.Name + ":" + c.Curves.Count))).Append(']');
                sb.Append('\n');
                foreach (var c in o.Children) Walk(c, path);
            }
            Walk(r.Root, null);
            sb.Append("materials=").Append(r.Materials.Count).Append(" textures=").Append(r.Textures.Count);
            return sb.ToString();
        }

        private static readonly List<string> Samples = new List<string>();
        private static int _headers, _extraIsScaleX;

        private static string F(params float[] values) =>
            string.Join(",", values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
