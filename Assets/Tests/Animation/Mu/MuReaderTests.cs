using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Mu;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Mu
{
    public class MuReaderTests
    {
        private static MuFile Read(MuObj root, int version = 5) => MuReader.Read(MuBytes.File(root, version).Stream());

        private static readonly (string, string, int)[] RotX = { ("", "m_LocalRotation.x", 0) };

        // ---- header and tree ----

        [Test] public void Read_BadMagic_Throws() =>
            Assert.Throws<MuFormatException>(() => MuReader.Read(MuBytes.Header(magic: 1, version: 5).Stream()));

        [Test] public void Read_Header_KeepsVersionAndName()
        {
            var f = MuReader.Read(MuBytes.File(MuBytes.Obj("root"), version: 3, name: "hello").Stream());
            Assert.AreEqual(3, f.Version);
            Assert.AreEqual("hello", f.Name);
            Assert.AreEqual("root", f.Root.Name);
        }

        [Test] public void Read_ChildrenAndEnd_BuildTree() =>
            Assert.AreEqual("a/b", Read(MuBytes.Obj("a").Child(MuBytes.Obj("b"))).Root.Children[0].Path());

        [Test] public void Read_NestedChildren_KeepOrder()
        {
            var f = Read(MuBytes.Obj("r").Child(MuBytes.Obj("x").Child(MuBytes.Obj("y"))).Child(MuBytes.Obj("z")));
            CollectionAssert.AreEqual(new[] { "x", "z" }, f.Root.Children.Select(c => c.Name));
            Assert.AreEqual("r/x/y", f.Root.Children[0].Children[0].Path());
        }

        // ---- animation ----

        [Test] public void Read_AnimationEntry_KeepsAllCurveTypes()
        {
            var f = Read(MuBytes.Obj("root").Anim(clip: "deploy", curves: new[]
                { ("", "m_LocalRotation.x", 0), ("Nozzle", "_EmissiveColor.r", 1), ("", "m_Intensity", 2) }));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, f.Root.Animation.Clips[0].Curves.Select(c => c.Type));
        }

        [Test] public void Read_AnimationEntry_KeepsCurveAndKeyFields()
        {
            var f = Read(MuBytes.Obj("root").Anim("deploy", new[] { ("Nozzle", "_EmissiveColor.r", 1) },
                autoPlay: false, wrapMode: 8, keyTimes: new[] { 0f, 2.5f }));
            var anim = f.Root.Animation;
            Assert.AreEqual("deploy", anim.DefaultClip);
            Assert.IsFalse(anim.AutoPlay);
            var clip = anim.Clips[0];
            Assert.AreEqual(("deploy", 8, 2.5f), (clip.Name, clip.WrapMode, clip.Length));
            var curve = clip.Curves[0];
            Assert.AreEqual(("Nozzle", "_EmissiveColor.r", 1, 2), (curve.Path, curve.Property, curve.PreWrap, curve.PostWrap));
            var k = curve.Keys[1];
            Assert.AreEqual((2.5f, 25f, 0.25f, -0.25f, 3), (k.Time, k.Value, k.InTangent, k.OutTangent, k.TangentMode));
        }

        [Test] public void Read_AnimationWithSeveralClips_KeepsThemAll()
        {
            var f = Read(MuBytes.Obj("root").Anims(new[] { ("heat", RotX), ("extend", RotX) }, true, ""));
            CollectionAssert.AreEqual(new[] { "heat", "extend" }, f.Root.Animation.Clips.Select(c => c.Name));
            Assert.AreEqual("", f.Root.Animation.DefaultClip);
        }

        [Test] public void Read_AnimationOnChild_IsOnThatNode()
        {
            var f = Read(MuBytes.Obj("root").Child(MuBytes.Obj("door").Anim("open", RotX)));
            Assert.IsNull(f.Root.Animation);
            Assert.AreEqual("open", f.Root.Children[0].Animation.Clips[0].Name);
        }

        // ---- other entries and versioned layouts ----

        private static MuObj Everything() =>
            MuBytes.Obj("root").MeshFilter(4).MeshRenderer(0)
                .Child(MuBytes.Obj("kid").Light().BoxCollider2().Anim("blink", RotX))
                .Materials().Textures("a.dds", "b.dds");

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void Read_VersionedLayouts_EndAtLastByte(int version)
        {
            var bytes = MuBytes.File(Everything(), version);
            var f = MuReader.Read(bytes.Stream());
            var last = f.Entries[f.Entries.Count - 1];
            Assert.AreEqual(bytes.Length, last.Offset + last.Length);
            Assert.AreEqual("blink", f.Root.Children[0].Animation.Clips[0].Name);
        }

        private static readonly MuBytes Sample = MuBytes.File(Everything());
        private static readonly int HeaderAndObjectHeaders =
            MuBytes.HeaderLength() + MuBytes.ObjectHeaderLength("root") + MuBytes.ObjectHeaderLength("kid");

        [Test] public void Read_RecordsEntryOffsets() =>
            Assert.AreEqual(MuReader.Read(Sample.Stream()).Entries.Sum(e => e.Length) + HeaderAndObjectHeaders, Sample.Length);

        [Test] public void Read_OldVersion_ObjectHeaderHasExtraFloat()
        {
            var old = MuBytes.File(Everything(), version: 2);
            var headers = MuBytes.HeaderLength() + MuBytes.ObjectHeaderLength("root", 2) + MuBytes.ObjectHeaderLength("kid", 2);
            Assert.AreEqual(old.Length, MuReader.Read(old.Stream()).Entries.Sum(e => e.Length) + headers);
            Assert.AreEqual(8, headers - HeaderAndObjectHeaders);
        }

        [Test] public void Read_RecordsEntryTypesAndNodes()
        {
            var f = MuReader.Read(Sample.Stream());
            CollectionAssert.AreEqual(new[] { 7, 8, 0, 23, 28, 2, 1, 10, 12 }, f.Entries.Select(e => e.Type));
            CollectionAssert.AreEqual(new[] { "root", "root", "root", "root/kid", "root/kid", "root/kid", "root/kid", "root", "root" },
                f.Entries.Select(e => e.NodePath));
            CollectionAssert.AreEqual(new[] { 23, 28, 2 }, f.Root.Children[0].EntryTypes);
        }

        [Test] public void Read_UnknownEntryType_ThrowsWithOffset()
        {
            var bytes = MuBytes.File(MuBytes.Obj("root").Tag(99));
            var e = Assert.Throws<MuFormatException>(() => MuReader.Read(bytes.Stream()));
            Assert.AreEqual(bytes.Length - 4, e.Offset);
            StringAssert.Contains("99", e.Message);
        }

        [Test] public void Read_Truncated_Throws()
        {
            var full = MuBytes.File(Everything()).Bytes;
            var cut = full.Take(full.Length - 3).ToArray();
            Assert.Throws<MuFormatException>(() => MuReader.Read(new MemoryStream(cut)));
        }

        [Test] public void Read_MissingChildEnd_Throws()
        {
            var full = MuBytes.File(MuBytes.Obj("a").Child(MuBytes.Obj("b"))).Bytes;
            var cut = full.Take(full.Length - 4).ToArray(); // drop b's end tag
            Assert.Throws<MuFormatException>(() => MuReader.Read(new MemoryStream(cut)));
        }

        [Test] public void Signature_ListsNodesEntriesAndClips()
        {
            var sig = MuTreeSignature.Of(MuReader.Read(Sample.Stream()));
            Assert.AreEqual("root children=1 entries=[7,8,10,12]\nroot/kid children=0 entries=[23,28,2] clips=[blink:1]", sig);
        }

        // ---- installed files ----

        private static Dictionary<string, (MuFile file, string error)> _installed;

        private static Dictionary<string, (MuFile file, string error)> Installed()
        {
            if (_installed != null) return _installed;
            var all = InstalledKsp1.AllMu();
            var watch = Stopwatch.StartNew();
            var result = new Dictionary<string, (MuFile, string)>();
            foreach (var p in all)
            {
                try
                {
                    var f = MuReader.Read(p);
                    var last = f.Entries.Count == 0 ? MuBytes.HeaderLength(f.Name) : f.Entries[f.Entries.Count - 1].Offset + f.Entries[f.Entries.Count - 1].Length;
                    var size = new FileInfo(p).Length;
                    result[p] = (f, last == size ? null : $"stopped at {last} of {size}");
                }
                catch (MuFormatException e) { result[p] = (null, e.Message); }
            }
            TestContext.WriteLine($"read {all.Length} .mu files in {watch.Elapsed.TotalSeconds:F1} s");
            return _installed = result;
        }

        [Test] public void Read_EveryInstalledFile_ConsumedToEnd()
        {
            var bad = Installed().Where(kv => kv.Value.error != null)
                .Select(kv => $"{kv.Key}: {kv.Value.error}").ToList();
            CollectionAssert.IsEmpty(bad);
        }

        private static int CountStock(Func<int, bool> version) =>
            Installed().Count(kv => InstalledKsp1.IsStock(kv.Key) && kv.Value.file != null && version(kv.Value.file.Version));

        [Test] public void Read_InstalledFiles_VersionCounts()
        {
            TestContext.WriteLine($"v0-2 {CountStock(v => v <= 2)}, v3-5 {CountStock(v => v >= 3)}");
            Assert.GreaterOrEqual(CountStock(v => v <= 2), 50); // the log counted referenced models only
        }
    }
}
