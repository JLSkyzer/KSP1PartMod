using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The wheel keys of 0.14.0.</summary>
    public class WheelSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "ksp1_wheels", "wheel_steering", "wheel_motors", "wheel_brakes", "wheel_leg_locks", "wheel_damage", "wheel_bogeys",
            "wheel_ksp1_friction", "wheel_ksp1_deploy_timing",
        };

        private static bool[] Values(AnimationSettings s) => new[]
        {
            s.Ksp1Wheels, s.WheelSteering, s.WheelMotors, s.WheelBrakes, s.WheelLegLocks, s.WheelDamage, s.WheelBogeys,
            s.WheelKsp1Friction, s.WheelKsp1DeployTiming,
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            var v = Values(s);
            for (var i = 0; i < Keys.Length; i++) Assert.IsTrue(v[i], Keys[i]);
        }

        [Test] public void Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"ksp1_radiators\": false, \"ksp1_science\": false}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsFalse(s.Ksp1Radiators, "the older keys are read as before");
            Assert.IsFalse(s.Ksp1Science);
        }

        [Test] public void EachKey_ReadAlone()
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                var s = AnimationSettings.Parse("{\"" + Keys[i] + "\": false}", out var e);
                Assert.IsNull(e, Keys[i]);
                var v = Values(s);
                for (var j = 0; j < Keys.Length; j++) Assert.AreEqual(i != j, v[j], Keys[i] + " -> " + Keys[j]);
            }
        }

        [Test] public void WrongTypes_KeepTheDefault()
        {
            var wrong = AnimationSettings.Parse("{\"ksp1_wheels\": 1, \"wheel_steering\": \"no\", \"wheel_motors\": null, " +
                                                "\"wheel_brakes\": 0, \"wheel_leg_locks\": [], \"wheel_damage\": {}, \"wheel_bogeys\": \"false\", " +
                                                "\"wheel_ksp1_friction\": 2, \"wheel_ksp1_deploy_timing\": 0.5}", out var e);
            AssertDefaults(wrong);
            foreach (var key in Keys) StringAssert.Contains(key, e);
        }

        [Test] public void ShippedFile_DefaultsAndDocumented()
        {
            var text = File.ReadAllText(ShippedPath);
            var s = AnimationSettings.Parse(text, out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            var note = text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal));
            foreach (var key in Keys)
            {
                StringAssert.Contains(key + ":", note, "documented in _note");
                StringAssert.Contains("\"" + key + "\":", text, "present in the file");
            }
        }
    }
}
