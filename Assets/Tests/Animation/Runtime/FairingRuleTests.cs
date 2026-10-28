using System.Collections.Generic;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class FairingRuleTests
    {
        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        public void NeedsEmptyColliders(bool ksp1, bool isNull, bool expected) =>
            Assert.AreEqual(expected, FairingRule.NeedsEmptyColliders(ksp1, isNull));

        [TestCase(true, false, true, true, TestName = "KSP1, no Module_Color, material: skipped")]
        [TestCase(true, true, true, false, TestName = "KSP1 with Module_Color: Redux runs")]
        [TestCase(true, false, false, false, TestName = "KSP1, no material: Redux's fallback branch runs")]
        [TestCase(false, false, true, false, TestName = "KSP2 part: never touched")]
        public void SkipMaterialLoaded(bool ksp1, bool hasColor, bool hasMaterial, bool expected) =>
            Assert.AreEqual(expected, FairingRule.SkipMaterialLoaded(ksp1, hasColor, hasMaterial));

        [Test] public void FirstTime_OncePerPart()
        {
            var seen = new HashSet<string>(FairingRule.Names);
            Assert.IsTrue(FairingRule.FirstTime(seen, "LiquidEngineRE-I2"));
            Assert.IsFalse(FairingRule.FirstTime(seen, "LiquidEngineRE-I2"));
            Assert.IsTrue(FairingRule.FirstTime(seen, "liquidEngineRE-I2"), "ordinal");
            Assert.IsFalse(FairingRule.FirstTime(null, "x"));
        }

        [Test] public void CollidersLine() =>
            Assert.AreEqual("[KSP1PartMod] fairing: LiquidEngineRE-I2 base colliders were null (Redux looks for transform 'model'), set empty",
                FairingRule.CollidersLine("LiquidEngineRE-I2"));

        [Test] public void MaterialLine() =>
            StringAssert.StartsWith("[KSP1PartMod] fairing: RK-7 has no Module_Color, Redux's OnMaterialLoaded skipped",
                FairingRule.MaterialLine("RK-7"));
    }
}
