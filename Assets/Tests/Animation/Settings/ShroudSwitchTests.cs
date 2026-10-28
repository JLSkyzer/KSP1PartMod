using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>Engine shrouds (0.7.0, Task 1): the <c>ksp1_engine_shrouds</c> key and what arms the import patches.</summary>
    public class ShroudSwitchTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        [Test] public void Arm_BothOn_Armed() => Assert.AreEqual(ShroudArming.Armed, ShroudSwitch.Arm(true, true));

        [Test] public void Arm_SwitchOff_OffBySwitch() => Assert.AreEqual(ShroudArming.OffBySwitch, ShroudSwitch.Arm(true, false));

        [Test] public void Arm_AnimationsDisabled_OffAnimationsDisabled() =>
            Assert.AreEqual(ShroudArming.OffAnimationsDisabled, ShroudSwitch.Arm(false, true));

        [Test] public void Arm_BothOff_TheSwitchWins() => Assert.AreEqual(ShroudArming.OffBySwitch, ShroudSwitch.Arm(false, false));

        [Test] public void Setting_DefaultTrue_ShippedTrue()
        {
            Assert.IsTrue(AnimationSettings.Defaults.Ksp1EngineShrouds);
            var s = AnimationSettings.Parse(File.ReadAllText(ShippedPath), out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Ksp1EngineShrouds);
        }

        [Test] public void Setting_IsRead_OldFileWithoutTheKeyStaysValid()
        {
            var off = AnimationSettings.Parse("{\"ksp1_engine_shrouds\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(off.Ksp1EngineShrouds);
            var old = AnimationSettings.Parse("{\"enabled\": true}", out var e2);
            Assert.IsNull(e2);
            Assert.IsTrue(old.Ksp1EngineShrouds);
        }

        [Test] public void Setting_WrongType_KeepsDefaultAndReports()
        {
            var s = AnimationSettings.Parse("{\"ksp1_engine_shrouds\": \"no\"}", out var e);
            StringAssert.Contains("ksp1_engine_shrouds", e);
            Assert.IsTrue(s.Ksp1EngineShrouds);
        }

        [Test] public void Setting_DocumentedInTheShippedNote()
        {
            var text = File.ReadAllText(ShippedPath);
            StringAssert.Contains("\"ksp1_engine_shrouds\":", text);
            StringAssert.Contains("ksp1_engine_shrouds:", text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal)));
        }
    }
}
