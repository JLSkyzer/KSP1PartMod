using System;
using System.IO;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class ModelPathResolverTests
    {
        private string _root;

        [SetUp] public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ksp1pm-resolver-" + Guid.NewGuid().ToString("N"))).FullName;
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private string P(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

        private string Touch(string rel)
        {
            var p = P(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, "");
            return p;
        }

        private string Cfg(string rel) => Touch(rel);

        private ModelPathResolver R() => new ModelPathResolver(_root, () => DirectoryIndex.Build(_root));

        [Test] public void Resolve_Rule1_RootRelative() { Touch("Squad/Parts/Engine/x/model.mu"); Assert.AreEqual(P("Squad/Parts/Engine/x/model.mu"), R().Resolve(Cfg("Squad/Parts/Engine/x/part.cfg"), "Squad/Parts/Engine/x/model", false, out _)); }
        [Test] public void Resolve_Rule1_TriedSaysRule1() { Touch("a/m.mu"); R().Resolve(Cfg("b/part.cfg"), "a/m", false, out var t); Assert.AreEqual("1", t); }
        [Test] public void Resolve_Rule2_CfgRelative() { Touch("a/sub/m.mu"); Assert.AreEqual(P("a/sub/m.mu"), R().Resolve(Cfg("a/part.cfg"), "sub/m", false, out _)); }
        [Test] public void Resolve_MeshValue_CfgRelative() { Touch("a/thing.mu"); Assert.AreEqual(P("a/thing.mu"), R().Resolve(Cfg("a/part.cfg"), "thing.mu", true, out _)); }
        [Test] public void Resolve_Rule3_CfgBasenameForModelMu() { Touch("a/part.mu"); Touch("a/other.mu"); Assert.AreEqual(P("a/part.mu"), R().Resolve(Cfg("a/part.cfg"), "model.mu", true, out _)); }
        [Test] public void Resolve_Rule4_OnlyMuOfFolder() { Touch("a/thing.mu"); Assert.AreEqual(P("a/thing.mu"), R().Resolve(Cfg("a/part.cfg"), "other", false, out var t)); Assert.AreEqual("1,2,3,4", t); }
        [Test] public void Resolve_Rule4_TwoMuInFolder_NotUsed() { Touch("a/x.mu"); Touch("a/y.mu"); Assert.IsNull(R().Resolve(Cfg("a/part.cfg"), "model.mu", true, out _)); }
        [Test] public void Resolve_Rule5_UniqueNameUnderRoot() { Touch("c/d/unique.mu"); Assert.AreEqual(P("c/d/unique.mu"), R().Resolve(Cfg("b/part.cfg"), "unique", false, out var t)); Assert.AreEqual("1,2,3,4,5", t); }
        [Test] public void Resolve_Rule5_CaseInsensitiveName() { Touch("c/Unique.mu"); Assert.AreEqual(P("c/Unique.mu"), R().Resolve(Cfg("b/part.cfg"), "UNIQUE", false, out _)); }
        [Test] public void Resolve_Rule5_Ambiguous_ReturnsNullAndSaysWhy() { Touch("c/dup.mu"); Touch("d/dup.mu"); Assert.IsNull(R().Resolve(Cfg("b/part.cfg"), "dup", false, out var t)); StringAssert.Contains("in 2 folders", t); }
        [Test] public void Resolve_Rule5_NeverForModelMu() { Touch("c/model.mu"); Assert.IsNull(R().Resolve(Cfg("b/part.cfg"), "model", false, out _)); }
        [Test] public void Resolve_Rule5_IndexBuiltOnceAndOnlyWhenNeeded()
        {
            Touch("a/m.mu"); Touch("c/u.mu"); Touch("c/v.mu");
            var builds = 0;
            var r = new ModelPathResolver(_root, () => { builds++; return DirectoryIndex.Build(_root); });
            r.Resolve(Cfg("b/part.cfg"), "a/m", false, out _);
            Assert.AreEqual(0, builds);
            r.Resolve(Cfg("b/part.cfg"), "u", false, out _);
            r.Resolve(Cfg("b/part.cfg"), "v", false, out _);
            Assert.AreEqual(1, builds);
        }
        [Test] public void Index_ByFileName_IgnoresCase() { Touch("x/A.mu"); Touch("y/a.MU"); Assert.AreEqual(2, DirectoryIndex.Build(_root).ByFileName("a.mu").Count); }
        [Test]
        public void Index_UnreadableFolder_SkippedOthersKept()
        {
            Touch("x/a.mu"); Touch("y/b.mu");
            var index = DirectoryIndex.Build(_root,
                d => d.EndsWith("x") ? throw new UnauthorizedAccessException("denied") : Directory.GetFiles(d),
                Directory.GetDirectories);
            Assert.AreEqual(0, index.ByFileName("a.mu").Count);
            Assert.AreEqual(1, index.ByFileName("b.mu").Count);
        }

        [Test]
        public void Index_SubfolderListFails_KeepsWhatWasFound()
        {
            Touch("top.mu"); Touch("x/a.mu");
            var index = DirectoryIndex.Build(_root, Directory.GetFiles, d => throw new IOException("gone"));
            Assert.AreEqual(1, index.ByFileName("top.mu").Count);
            Assert.AreEqual(0, index.ByFileName("a.mu").Count);
        }

        [Test]
        public void Resolve_Rule5_IndexErrorDoesNotFailLaterParts()
        {
            Touch("c/u.mu"); Touch("bad/v.mu");
            var r = new ModelPathResolver(_root, () => DirectoryIndex.Build(_root,
                d => d.EndsWith("bad") ? throw new IOException("gone") : Directory.GetFiles(d), Directory.GetDirectories));
            Assert.IsNull(r.Resolve(Cfg("b/part.cfg"), "v", false, out _));
            Assert.AreEqual(P("c/u.mu"), r.Resolve(Cfg("b/part.cfg"), "u", false, out _));
        }

        [Test] public void Index_UnknownName_Empty() => Assert.AreEqual(0, DirectoryIndex.Build(_root).ByFileName("none.mu").Count);
    }
}
