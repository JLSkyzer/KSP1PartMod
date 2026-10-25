using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class IconFixTests
    {
        [TestCase(true, true, false, true)]
        [TestCase(false, true, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, true, false)]
        public void ShouldSwap(bool enabled, bool ksp1, bool already, bool expected) =>
            Assert.AreEqual(expected, IconFix.ShouldSwap(enabled, ksp1, already));

        [TestCase(1, true)] [TestCase(2, false)] [TestCase(10, true)] [TestCase(11, false)]
        [TestCase(100, true)] [TestCase(500, true)] [TestCase(1000, true)] [TestCase(1500, true)] [TestCase(1501, false)]
        public void ShouldLog_Milestones(int n, bool expected) => Assert.AreEqual(expected, IconFix.ShouldLog(n));

        [Test] public void Line() =>
            Assert.AreEqual("[KSP1PartMod] icons: rendered 10 KSP1 icon(s) with hotswapPartShaders=true " +
                            "(KSP2/Parts/Paintable -> KSP2/Scenery/Standard (Opaque)); last 'Mk1Pod' had [KSP2/Parts/Paintable x3]",
                IconFix.Line(10, "Mk1Pod", "KSP2/Parts/Paintable x3"));

        [Test] public void ShaderCensus_CountsByName() =>
            Assert.AreEqual("A x2, B x1", IconFix.ShaderCensus(new[] { "A", "B", "A", null }));
    }
}
