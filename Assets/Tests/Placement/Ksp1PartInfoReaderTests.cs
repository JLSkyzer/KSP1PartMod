using System.Collections.Generic;
using KSP1PartMod.Placement;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Placement
{
    public class Ksp1PartInfoReaderTests
    {
        private static Ksp1PartInfo Read(params (string, string)[] values)
        {
            var map = new Dictionary<string, string>();
            foreach (var (k, v) in values) map[k] = v;
            return Ksp1PartInfoReader.Read("p", "m", key => map.TryGetValue(key, out var v) ? v : null);
        }

        [Test] public void ReadInfo_TrimsAndStripsComment() =>
            Assert.AreEqual("advRocketry", Read(("TechRequired", " advRocketry\t// was basic ")).TechRequired);
        [Test] public void ReadInfo_TechHiddenTrue_IsHidden() =>
            Assert.IsTrue(Read(("TechHidden", "True")).HiddenByConfig);
        [Test] public void ReadInfo_CategoryNone_IsHidden() =>
            Assert.IsTrue(Read(("category", "none")).HiddenByConfig);
        [Test] public void ReadInfo_MissingCategory_IsHidden() =>
            Assert.IsTrue(Read(("TechRequired", "start")).HiddenByConfig);
        [Test] public void ReadInfo_EmptyCategory_IsHidden() =>
            Assert.IsTrue(Read(("TechRequired", "start"), ("category", "  // none yet")).HiddenByConfig);
        [Test] public void ReadInfo_Unresearcheable_IsHidden() =>
            Assert.IsTrue(Read(("TechRequired", "unresearcheable"), ("category", "Utility")).HiddenByConfig);
        [Test] public void ReadInfo_MissingTechRequired_IsNullNotHidden()
        { var p = Read(("category", "Engine")); Assert.IsNull(p.TechRequired); Assert.IsFalse(p.HiddenByConfig); }
        [Test] public void ReadInfo_KeepsPartAndModNames()
        { var p = Read(("category", "Utility")); Assert.AreEqual("p", p.PartName); Assert.AreEqual("m", p.ModName); }
        [Test] public void ReadInfo_EmptyAfterComment_IsNull() =>
            Assert.IsNull(Read(("TechRequired", "  // nothing")).TechRequired);
        [Test] public void ReadInfo_TechHiddenGarbage_IsNotHidden() =>
            Assert.IsFalse(Read(("TechHidden", "maybe"), ("category", "Utility")).HiddenByConfig);

        [TestCase("size0, srf", "0v")] [TestCase("size1p5", "1.5v")] [TestCase("size3, size2", "3v")]
        [TestCase("mk2, srf", "mk2")] [TestCase("srf", "srf")] [TestCase(null, null)]
        public void ReadInfo_Size_IsLargestProfile(string profiles, string size) =>
            Assert.AreEqual(size, Read(("bulkheadProfiles", profiles)).Size);
    }
}
