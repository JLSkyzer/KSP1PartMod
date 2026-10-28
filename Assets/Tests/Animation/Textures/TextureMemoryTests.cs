using KSP1PartMod.Animation.Textures;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Textures
{
    public class TextureMemoryTests
    {
        private const long Mb = 1024 * 1024;

        [Test] public void Estimate_Dxt5_4096_FullChain()
        {
            // 16 MB top level, ~1/3 more for the chain: the 21.3 MB of the scan for a 4096² DXT5.
            var bytes = TextureMemory.Estimate(4096, 4096, 13, 8, true);
            Assert.AreEqual(16 * Mb, TextureMemory.Estimate(4096, 4096, 1, 8, true));
            Assert.AreEqual(22369648, bytes);
        }

        [Test] public void Estimate_Dxt1_LevelsBelowABlockCountAsOneBlock()
        {
            // 8x8 DXT1: 4 blocks (32 B), then 4x4, 2x2 and 1x1 each one 8-byte block.
            Assert.AreEqual(32 + 8 + 8 + 8, TextureMemory.Estimate(8, 8, 4, 4, true));
        }

        [Test] public void Estimate_Uncompressed_NonPowerOfTwo()
        {
            Assert.AreEqual(300 * 200 * 4, TextureMemory.Estimate(300, 200, 1, 32, false));
            Assert.AreEqual(300 * 200 * 4 + 150 * 100 * 4, TextureMemory.Estimate(300, 200, 2, 32, false));
        }

        [Test] public void Estimate_InvalidOrNoMips()
        {
            Assert.AreEqual(0, TextureMemory.Estimate(0, 16, 1, 32, false));
            Assert.AreEqual(0, TextureMemory.Estimate(16, 16, 1, 0, false));
            Assert.AreEqual(16 * 16 * 4, TextureMemory.Estimate(16, 16, 0, 32, false));
        }

        [Test] public void Margin_QuarterOfCardAtLeast1Gb()
        {
            Assert.AreEqual(1989, TextureMemory.MarginMb(7956));
            Assert.AreEqual(1024, TextureMemory.MarginMb(2048));
            Assert.AreEqual(1024, TextureMemory.MarginMb(0));
        }

        [Test] public void Raised_AddsKsp1BytesUnderTheCap()
        {
            var b = TextureMemory.RaisedBudgetMb(1024f, 600 * Mb, 7956, out var capped);
            Assert.AreEqual(1624f, b, 0.01f);
            Assert.IsFalse(capped);
        }

        [Test] public void Raised_CappedAtCardLessMargin()
        {
            var b = TextureMemory.RaisedBudgetMb(2048f, 7000 * Mb, 7956, out var capped);
            Assert.AreEqual(7956 - 1989, b, 0.01f);
            Assert.IsTrue(capped);
        }

        [Test] public void Raised_NeverBelowBase()
        {
            var b = TextureMemory.RaisedBudgetMb(3000f, 500 * Mb, 3072, out var capped);
            Assert.AreEqual(3000f, b, 0.01f);
            Assert.IsTrue(capped);
        }

        [Test] public void Raised_UnknownCardOrNothingToAdd_Base()
        {
            Assert.AreEqual(512f, TextureMemory.RaisedBudgetMb(512f, 100 * Mb, 0, out var c1));
            Assert.IsFalse(c1);
            Assert.AreEqual(512f, TextureMemory.RaisedBudgetMb(512f, 0, 8192, out var c2));
            Assert.IsFalse(c2);
        }

        [Test] public void Base_KeptWhileTheBudgetIsStillOurs()
        {
            Assert.AreEqual(1024f, TextureMemory.BaseBudgetMb(1624f, 1624f, 1024f));
        }

        [Test] public void Base_FirstTimeOrChangedByTheGame_TakesTheCurrentValue()
        {
            Assert.AreEqual(1624f, TextureMemory.BaseBudgetMb(1624f, null, 0f));
            Assert.AreEqual(2048f, TextureMemory.BaseBudgetMb(2048f, 1624f, 1024f));
        }

        [Test] public void Mb_Rounds() => Assert.AreEqual("2", TextureMemory.Mb(1.6 * Mb));
    }
}
