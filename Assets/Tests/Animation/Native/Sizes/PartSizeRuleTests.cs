using KSP1PartMod.Native;
using KSP1PartMod.Native.Sizes;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Sizes
{
    /// <summary><see cref="PartSizeRule"/> (0.16.0, Task 1). The check against KSP2's own registry is in the ReduxMu tests.</summary>
    public class PartSizeRuleTests
    {
        [TestCase("size0", 0.625f)] [TestCase("size1", 1.25f)] [TestCase("size2", 2.5f)] [TestCase("size3", 3.75f)]
        [TestCase("size4", 5f)] [TestCase("size5", 7.5f)] [TestCase("size00", 0.3125f)] [TestCase("size000", 0.15625f)]
        [TestCase("size1p2", 1.5f)] [TestCase("size3p4", 4.25f)] [TestCase("size0p5", 0.9375f)] [TestCase("size4p5", 6.25f)]
        [TestCase("Size1P5", 1.875f)]
        public void TokenDiameter_Ksp1Convention(string token, float diameter) =>
            Assert.AreEqual(diameter, PartSizeRule.TokenDiameter(token).Value, 1e-5f);

        [TestCase("srf")] [TestCase("mk2")] [TestCase("size")] [TestCase("sizeX")] [TestCase("size1p")] [TestCase("size01")] [TestCase(null)]
        public void TokenDiameter_NotASize_IsNull(string token) => Assert.IsNull(PartSizeRule.TokenDiameter(token));

        [TestCase("size1p2", "SM")] [TestCase("size3p4", "LG+")] [TestCase("size000", "XXS")] [TestCase("size4p5", "XL")]
        public void UnknownToken_ClosestKey(string token, string key) =>
            Assert.AreEqual(key, PartSizeRule.Closest(PartSizeRule.FromTokens(token).Value));

        [Test] public void FromTokens_Largest() =>
            Assert.AreEqual(4.25f, PartSizeRule.FromTokens("srf, size1p2;size3p4 size0").Value, 1e-5f);

        [Test] public void Closest_TieGoesToTheFirst() => Assert.AreEqual("XL", PartSizeRule.Closest(6.25f));

        [Test] public void SurfaceOnly_Srf_KeepsRedux()
        {
            var d = PartSizeRule.Decide("SM", true, "srf", new (bool, string)[0]);
            Assert.AreEqual("SM", d.Key);
            Assert.AreEqual(SizeSource.Redux, d.Source);
            Assert.AreEqual(0, d.Steps);
        }

        [Test] public void SurfaceOnly_UnknownToken_WhenReduxFellBack()
        {
            var d = PartSizeRule.Decide("SM", true, "size3p4, srf", new[] { (false, "SM") });
            Assert.AreEqual("LG+", d.Key);
            Assert.AreEqual(SizeSource.Token, d.Source);
            Assert.AreEqual(5, d.Steps);
            Assert.AreEqual(SizeSource.Redux, PartSizeRule.Decide("MD", false, "size2, size3p4", null).Source, "Redux knew a token");
        }

        [Test] public void StackPart_LargestStackNode()
        {
            var d = PartSizeRule.Decide("XS", false, "size0", new[] { (true, "SM"), (false, "2XL"), (true, "MD") });
            Assert.AreEqual("MD", d.Key);
            Assert.AreEqual(SizeSource.Nodes, d.Source);
            Assert.AreEqual(4, d.Steps);
        }

        [Test] public void StackPart_WithoutEstimate_KeepsTheNodesLegacyKey()
        {
            // Without a model estimate the node's key is its legacy size (size = 2 -> MD), which the rule keeps.
            var d = PartSizeRule.Decide("SM", true, "", new[] { (true, "MD") });
            Assert.AreEqual("MD", d.Key);
            Assert.AreEqual(SizeSource.Nodes, d.Source);
        }

        [Test] public void EveryKey_HasACategory()
        {
            foreach (var e in PartSizeRule.Registry)
            {
                Assert.AreEqual(e.Category, PartSizeRule.CategoryOf(e.Key));
                Assert.AreEqual(e.Category, PartSizeRule.CategoryOf(e.Key.ToLowerInvariant()), "any case");
            }
            Assert.IsNull(PartSizeRule.CategoryOf("7XL"));
        }

        [Test] public void Tally_Lines()
        {
            var t = new EngineSizeLines.SizeTally();
            t.Note("Bluedog_DB", "a", "SM", "MD", SizeSource.Nodes, 2, false);
            t.Note("Bluedog_DB", "b", "SM", "SM", SizeSource.Nodes, 0, false);
            t.Note("Bluedog_DB", "c", "SM", "LG+", SizeSource.Token, 5, true);
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] size: Bluedog_DB: 1 from nodes, 1 from tokens, 1 unchanged; 2 moved by 2+ steps",
                "[KSP1PartMod] size: largest moves: c SM -> LG+ (5), a SM -> MD (2)",
                "[KSP1PartMod] size: docking parts relabelled: c SM -> LG+",
            }, EngineSizeLines.SizeSummary(t, 10));
        }
    }
}
