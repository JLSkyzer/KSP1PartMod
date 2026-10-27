using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class TrackCacheTests
    {
        private string _root;

        [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ksp1pm-tracks-" + Guid.NewGuid().ToString("N"))).FullName;

        [TearDown]
        public void TearDown()
        {
            TrackCacheWorker.BeforeBuild = null;
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string P(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

        private string Write(string rel, byte[] bytes)
        {
            var p = P(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllBytes(p, bytes);
            return p;
        }

        /// <summary>A model whose root carries clips glow (material), deploy (transform) and other (material).</summary>
        private void ThreeClipModel(string rel) =>
            Write(rel, MuBytes.File(MuBytes.Obj("root").Anims(new[]
            {
                ("glow", new[] { ("Nozzle", "_EmissiveColor.r", 1) }),
                ("deploy", new[] { ("Leg", "m_LocalPosition.x", 0) }),
                ("other", new[] { ("Nozzle", "_TintColor.a", 1), ("L", "m_DrawHalo", 2), ("", "volume", 3) }),
            }, false, "glow")).Bytes);

        private PartSnapshot Snapshot(string part, string[] refs, params string[] clips)
        {
            Write("p/" + part + ".cfg", new byte[0]);
            return new PartSnapshot
            {
                Part = part, CfgPath = P("p/" + part + ".cfg"), GameDataRoot = _root,
                ModelRefs = refs, RefIsMesh = refs.Select(_ => false).ToArray(), Clips = clips,
            };
        }

        private ModelPathResolver R() => new ModelPathResolver(_root, () => DirectoryIndex.Build(_root));

        private PartTracks Build(PartSnapshot s, Func<string, MuFile> read = null) =>
            TrackCacheBuilder.Build(s, R(), read ?? MuReader.Read);

        private static PartTracks Ok() => new PartTracks { Sets = new TrackSet[0] };

        // --- builder ---

        [Test]
        public void Builder_KeepsOnlyNamedClipsWithTracks()
        {
            ThreeClipModel("m/a.mu");
            var t = Build(Snapshot("a", new[] { "m/a" }, "glow"));
            CollectionAssert.AreEqual(new[] { "glow" }, t.Sets.Select(s => s.Clip));
            Assert.IsFalse(t.Failed);
        }

        [Test]
        public void Builder_TransformOnlyClipNamed_NoSet()
        {
            ThreeClipModel("m/a.mu");
            CollectionAssert.AreEqual(new[] { "glow" }, Build(Snapshot("a", new[] { "m/a" }, "glow", "deploy")).Sets.Select(s => s.Clip));
        }

        [Test]
        public void Builder_CountsDropsOfNamedClips()
        {
            ThreeClipModel("m/a.mu");
            var d = Build(Snapshot("a", new[] { "m/a" }, "other")).Drops;
            Assert.AreEqual((1, 1), (d.DrawHalo, d.Audio));
        }

        [Test]
        public void Builder_SetCarriesHostPathAndModelIndex()
        {
            ThreeClipModel("m/a.mu");
            ThreeClipModel("m/b.mu");
            var t = Build(Snapshot("a", new[] { "m/a", "m/b" }, "glow"));
            CollectionAssert.AreEqual(new[] { 0, 1 }, t.Sets.Select(s => s.ModelIndex));
            Assert.AreEqual("root", t.Sets[0].HostPath);
        }

        [Test]
        public void Builder_SameFileTwoRefs_ReadOnce()
        {
            ThreeClipModel("m/a.mu");
            var reads = 0;
            var once = TrackCacheBuilder.ReadOnce(p => { reads++; return MuReader.Read(p); });
            var t = Build(Snapshot("a", new[] { "m/a", "m/a.mu" }, "glow"), once);
            Assert.AreEqual(1, reads);
            Assert.AreEqual(2, t.Sets.Count);
        }

        [Test]
        public void ReadOnce_RepeatsTheFailureWithoutRereading()
        {
            var reads = 0;
            var once = TrackCacheBuilder.ReadOnce(p => { reads++; throw new MuFormatException(0, "bad"); });
            Assert.Throws<MuFormatException>(() => once("x"));
            Assert.Throws<MuFormatException>(() => once("X"));
            Assert.AreEqual(1, reads);
        }

        [Test]
        public void TrackCacheBuilder_UnreadableModel_MarksPartFailedNoThrow()
        {
            Write("m/bad.mu", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var t = Build(Snapshot("a", new[] { "m/bad" }, "glow"));
            Assert.IsTrue(t.Failed);
            StringAssert.Contains("MuFormatException", t.Note);
        }

        [Test]
        public void TrackCacheBuilder_MissingModel_NotesRulesTried()
        {
            var t = Build(Snapshot("a", new[] { "nope" }, "glow"));
            StringAssert.Contains("not found (1,2,3,4,5", t.Note);
            Assert.IsFalse(t.Failed);
            Assert.AreEqual(1, t.Missing.Count);
        }

        // --- snapshot from a cfg ---

        private static PartAnimationPlan Plan(params AnimEntryDef[] entries) =>
            new PartAnimationPlan(entries, new[] { "reduxClip" }, null, false, new string[0]);

        [Test]
        public void Snapshot_ModelNodesWin()
        {
            var part = CfgText.Parse("PART\n{\nmodel = x/y\nMODEL\n{\nmodel = a/b\n}\nMODEL\n{\nmodel = c/d\n}\n}").GetNodes("PART").Single();
            var s = PartSnapshot.From("p", "cfg", "root", part, Plan());
            CollectionAssert.AreEqual(new[] { "a/b", "c/d" }, s.ModelRefs);
            CollectionAssert.AreEqual(new[] { false, false }, s.RefIsMesh);
        }

        [Test]
        public void Snapshot_ModelValueThenMeshDefault()
        {
            var withModel = CfgText.Parse("PART\n{\nmodel = x/y\nmesh = z.mu\n}").GetNodes("PART").Single();
            CollectionAssert.AreEqual(new[] { "x/y" }, PartSnapshot.From("p", "cfg", "root", withModel, Plan()).ModelRefs);
            var bare = CfgText.Parse("PART\n{\nname = p\n}").GetNodes("PART").Single();
            var s = PartSnapshot.From("p", "cfg", "root", bare, Plan());
            CollectionAssert.AreEqual(new[] { "model.mu" }, s.ModelRefs);
            CollectionAssert.AreEqual(new[] { true }, s.RefIsMesh);
        }

        [Test]
        public void Snapshot_Clips_EntriesReduxAndParachute()
        {
            var part = CfgText.Parse("PART\n{\nMODULE\n{\nname = ModuleParachute\nsemiDeployedAnimation = semi\n}\n}").GetNodes("PART").Single();
            var s = PartSnapshot.From("p", "cfg", "root", part,
                Plan(new AnimEntryDef { Clip = "a", SecondClip = "b" }, new AnimEntryDef { Clip = "a" }));
            CollectionAssert.AreEquivalent(new[] { "a", "b", "reduxClip", "semi", "fullyDeploy" }, s.Clips);
        }

        // --- cache ---

        [Test]
        public void TrackCache_NewGeneration_DropsOldParts()
        {
            var g1 = TrackCache.Begin();
            var g2 = TrackCache.Begin();
            TrackCache.Put(g1, "a", Ok());
            Assert.IsFalse(TrackCache.TryGet("a", out _));
            TrackCache.Put(g2, "a", Ok());
            Assert.IsTrue(TrackCache.TryGet("a", out _));
        }

        [Test]
        public void TrackCache_Begin_ClearsAndUnsetsReady()
        {
            var g = TrackCache.Begin();
            TrackCache.Put(g, "a", Ok());
            TrackCache.MarkReady(g);
            Assert.IsTrue(TrackCache.Ready);
            TrackCache.Begin();
            Assert.IsFalse(TrackCache.Ready);
            Assert.IsFalse(TrackCache.TryGet("a", out _));
        }

        [Test]
        public void TrackCache_StaleMarkReady_Ignored()
        {
            var g1 = TrackCache.Begin();
            TrackCache.Begin();
            TrackCache.MarkReady(g1);
            Assert.IsFalse(TrackCache.Ready);
        }

        // --- worker ---

        private List<PartSnapshot> Parts()
        {
            ThreeClipModel("m/a.mu");
            Write("m/bad.mu", new byte[] { 9, 9, 9, 9 });
            return new List<PartSnapshot>
            {
                Snapshot("a", new[] { "m/a" }, "glow", "other"),
                Snapshot("b", new[] { "m/a" }, "glow"),
                Snapshot("c", new[] { "m/bad" }, "glow"),
                Snapshot("d", new[] { "nope" }, "glow"),
            };
        }

        [Test]
        public void TrackCacheWorker_StartReturnsBeforeWork()
        {
            var parts = Parts();
            using (var gate = new ManualResetEventSlim())
            {
                TrackCacheWorker.BeforeBuild = _ => gate.Wait(5000);
                var g = TrackCache.Begin();
                TrackCacheWorker.Start(g, parts, _ => { });
                Assert.IsFalse(TrackCache.Ready);
                gate.Set();
                Assert.IsTrue(SpinWait.SpinUntil(() => TrackCache.Ready, 5000));
            }
        }

        [Test]
        public void TrackCacheWorker_ExceptionInOnePart_PartFailedOthersCached()
        {
            var parts = Parts();
            TrackCacheWorker.BeforeBuild = s => { if (s.Part == "a") throw new InvalidOperationException("boom"); };
            var lines = Run(parts);
            Assert.IsTrue(TrackCache.TryGet("a", out var a));
            Assert.IsTrue(a.Failed);
            StringAssert.Contains("InvalidOperationException: boom", a.Note);
            Assert.IsTrue(TrackCache.TryGet("b", out var b));
            Assert.AreEqual(1, b.Sets.Count);
            Assert.That(lines, Has.Some.EqualTo("[KSP1PartMod] anim: tracks: a failed: " + a.Note));
        }

        [Test]
        public void TrackCacheWorker_LinesForMissingFailedAndDropped()
        {
            var lines = Run(Parts());
            Assert.That(lines, Has.Some.StartsWith("[KSP1PartMod] anim: tracks: d model 'nope' not found (1,2,3,4,5"));
            Assert.That(lines, Has.Some.StartsWith("[KSP1PartMod] anim: tracks: c failed: MuFormatException"));
            Assert.That(lines, Has.Some.EqualTo("[KSP1PartMod] anim: tracks: a dropped 1 m_DrawHalo, 1 audio, 0 particle curve(s)"));
        }

        [Test]
        public void Worker_SummaryLine_Format() =>
            StringAssert.IsMatch(@"^\[KSP1PartMod\] anim: tracks: read \d+ model\(s\) for \d+ part\(s\) in \d+ ms on a worker; \d+ clip\(s\) with \d+ track\(s\); \d+ failed$", RunAndGetSummary());

        [Test]
        public void Worker_SummaryLine_Counts() =>
            StringAssert.IsMatch(@"read 2 model\(s\) for 4 part\(s\) in \d+ ms on a worker; 3 clip\(s\) with 3 track\(s\); 1 failed$", RunAndGetSummary());

        private string RunAndGetSummary() => Run(Parts()).Single(l => l.Contains(" anim: tracks: read "));

        private static List<string> Run(IReadOnlyList<PartSnapshot> parts)
        {
            var lines = new ConcurrentQueue<string>();
            var g = TrackCache.Begin();
            TrackCacheWorker.Start(g, parts, lines.Enqueue);
            Assert.IsTrue(SpinWait.SpinUntil(() => TrackCache.Ready, 10000), "worker did not finish");
            return lines.ToList();
        }
    }
}
