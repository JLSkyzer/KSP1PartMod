using KSP1PartMod.Animation.Settings;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Fairings
{
    /// <summary>The two fairing keys of 0.15.0: on by default, an old file stays valid.</summary>
    public class FairingSettingsTests
    {
        [Test] public void Defaults_On()
        {
            var s = AnimationSettings.Parse("{}", out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Ksp1ProceduralFairings);
            Assert.IsTrue(s.Ksp1SafFairings);
            Assert.IsTrue(AnimationSettings.Defaults.Ksp1ProceduralFairings);
            Assert.IsTrue(AnimationSettings.Defaults.Ksp1SafFairings);
        }

        [Test] public void BothOff_Read_OtherKeysAsBefore()
        {
            var s = AnimationSettings.Parse("{\"ksp1_procedural_fairings\": false, \"ksp1_saf_fairings\": false, \"ksp1_wheels\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Ksp1ProceduralFairings);
            Assert.IsFalse(s.Ksp1SafFairings);
            Assert.IsFalse(s.Ksp1Wheels);
            Assert.IsTrue(s.FixFairingInit);
        }
    }
}
