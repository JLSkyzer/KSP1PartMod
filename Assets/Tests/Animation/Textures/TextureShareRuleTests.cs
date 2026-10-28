using KSP1PartMod.Animation.Textures;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Textures
{
    public class TextureShareRuleTests
    {
        [TestCase("C:/GameData/Squad/Parts/tank.dds", false, TextureUsageClass.Color)]
        [TestCase("C:/GameData/Squad/Parts/tank.dds", true, TextureUsageClass.Normal)]
        [TestCase("C:/GameData/Squad/Parts/tank_NRM.dds", false, TextureUsageClass.Normal)]
        [TestCase("C:/GameData/Squad/Parts/tank_nrm.png", false, TextureUsageClass.Normal)]
        [TestCase("C:/GameData/Squad/Parts/NRM_tank.png", false, TextureUsageClass.Color)]
        [TestCase("C:/GameData/Squad/Parts/tankNRM.mbm", true, TextureUsageClass.Normal)]
        public void Classify_FollowsReduxNormalRule(string path, bool normalRequested, TextureUsageClass expected) =>
            Assert.AreEqual(expected, TextureShareRule.Classify(path, normalRequested));

        [Test] public void Classify_EmptyPath_Color() =>
            Assert.AreEqual(TextureUsageClass.Color, TextureShareRule.Classify("", false));

        [Test] public void SharingActive_OnlyAfterImportOutsideRelease()
        {
            Assert.IsFalse(TextureShareRule.SharingActive(false, false));
            Assert.IsTrue(TextureShareRule.SharingActive(true, false));
            Assert.IsFalse(TextureShareRule.SharingActive(true, true));
            Assert.IsFalse(TextureShareRule.SharingActive(false, true));
        }

        [Test] public void Lookup_Inactive_AlwaysRedux()
        {
            Assert.AreEqual(CacheLookup.RunRedux, TextureShareRule.Lookup(false, true, false, TextureUsageClass.Color, TextureUsageClass.Color));
            Assert.AreEqual(CacheLookup.RunRedux, TextureShareRule.Lookup(false, true, true, TextureUsageClass.Color, TextureUsageClass.Normal));
        }

        [Test] public void Lookup_NoEntry_Redux() =>
            Assert.AreEqual(CacheLookup.RunRedux, TextureShareRule.Lookup(true, false, false, null, TextureUsageClass.Color));

        [Test] public void Lookup_SameUsage_Shared() =>
            Assert.AreEqual(CacheLookup.Share, TextureShareRule.Lookup(true, true, false, TextureUsageClass.Normal, TextureUsageClass.Normal));

        [Test] public void Lookup_Destroyed_EvictedEvenWithSameUsage()
        {
            Assert.AreEqual(CacheLookup.EvictDestroyed, TextureShareRule.Lookup(true, true, true, TextureUsageClass.Color, TextureUsageClass.Color));
            Assert.AreEqual(CacheLookup.EvictDestroyed, TextureShareRule.Lookup(true, true, true, null, TextureUsageClass.Normal));
        }

        [Test] public void Lookup_OtherUsage_NotShared()
        {
            Assert.AreEqual(CacheLookup.EvictOtherUsage, TextureShareRule.Lookup(true, true, false, TextureUsageClass.Color, TextureUsageClass.Normal));
            Assert.AreEqual(CacheLookup.EvictOtherUsage, TextureShareRule.Lookup(true, true, false, TextureUsageClass.Normal, TextureUsageClass.Color));
        }

        [Test] public void Lookup_LoadedBeforeSharing_SharedAsReduxWould() =>
            Assert.AreEqual(CacheLookup.Share, TextureShareRule.Lookup(true, true, false, null, TextureUsageClass.Normal));

        [Test] public void Clear_DestroyingClearAlwaysRedux()
        {
            Assert.AreEqual(CacheClear.Redux, TextureShareRule.Clear(true, true));
            Assert.AreEqual(CacheClear.Redux, TextureShareRule.Clear(true, false));
        }

        [Test] public void Clear_NonDestroying_KeepsWhileSharing()
        {
            Assert.AreEqual(CacheClear.DropDestroyedOnly, TextureShareRule.Clear(false, true));
            Assert.AreEqual(CacheClear.Redux, TextureShareRule.Clear(false, false));
        }

        [Test] public void DestroyedGraph_KeepsTexturesOnlyWhileSharing()
        {
            Assert.IsTrue(TextureShareRule.KeepTexturesOfDestroyedGraph(true, true));
            Assert.IsFalse(TextureShareRule.KeepTexturesOfDestroyedGraph(true, false));
            Assert.IsFalse(TextureShareRule.KeepTexturesOfDestroyedGraph(false, true));
        }
    }
}
