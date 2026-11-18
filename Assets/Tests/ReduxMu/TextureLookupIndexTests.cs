using System;
using System.Collections.Generic;
using System.IO;
using KSP1PartMod.Game;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.4: the index gives the file Redux's own recursive search gives (same order, same extensions, same
    /// null), on a real folder tree.</summary>
    public class TextureLookupIndexTests
    {
        private static readonly string[] Extensions = { ".dds", ".mbm", ".png", ".jpg", ".jpeg", ".tga" };
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ksp1pm-texidx-" + Guid.NewGuid().ToString("N"));
            foreach (var rel in new[]
            {
                "ModA/Parts/tank/body.dds", "ModA/Parts/tank/body.png", "ModB/Assets/body.dds",
                "ModB/Assets/Shared_NRM.PNG", "ModB/Assets/deep/x/y/emissive.tga", "ModA/readme.txt",
                "ModC/Textures/flag.jpeg", "ModC/Textures/ambiguous.png", "ModA/Z/ambiguous.mbm",
            })
            {
                var p = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, "x");
            }
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch (Exception) { }
        }

        /// <summary>Redux 26w46a's <c>ResolveTexturePathRecursive</c>, without its cache.</summary>
        private string Redux(string textureName)
        {
            var stem = Path.GetFileNameWithoutExtension(textureName);
            var exts = Path.HasExtension(textureName) ? new[] { Path.GetExtension(textureName) } : Extensions;
            foreach (var e in exts)
            {
                var files = Directory.GetFiles(_root, stem + e, SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            return null;
        }

        [Test]
        public void SameAnswerAsRedux_ForHitsMissesExtensionsAndCase()
        {
            var index = TextureLookupIndex.Build(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
            foreach (var name in new[]
            {
                "body", "body.png", "BODY", "Shared_NRM", "shared_nrm.png", "emissive", "flag", "ambiguous",
                "ambiguous.png", "missing", "missing.dds", "readme", "readme.txt", "Parts/tank/body",
            })
                Assert.AreEqual(Redux(name), TextureLookupIndex.Lookup(index, name, Extensions), name);
        }

        [Test]
        public void FirstPathWins_InTheOrderGiven()
        {
            var index = TextureLookupIndex.Build(new List<string> { "a/one/t.dds", "b/two/t.dds", "c/T.DDS" });
            Assert.AreEqual("a/one/t.dds", TextureLookupIndex.Lookup(index, "t", Extensions));
        }

        [Test]
        public void ExtensionOrder_DdsBeforePng()
        {
            var index = TextureLookupIndex.Build(new List<string> { "x/t.png", "y/t.dds" });
            Assert.AreEqual("y/t.dds", TextureLookupIndex.Lookup(index, "t", Extensions));
            Assert.AreEqual("x/t.png", TextureLookupIndex.Lookup(index, "t.png", Extensions));
        }
    }
}
