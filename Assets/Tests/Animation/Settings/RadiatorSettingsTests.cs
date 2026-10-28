using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The radiator keys of 0.11.0.</summary>
    public class RadiatorSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "ksp1_radiators", "radiator_deploy_bridge", "radiator_auto_on", "radiator_flux_scale", "radiator_ksp1_rates",
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            Assert.IsTrue(s.Ksp1Radiators);
            Assert.IsTrue(s.RadiatorDeployBridge);
            Assert.IsTrue(s.RadiatorAutoOn);
            Assert.AreEqual(1.0, s.RadiatorFluxScale, 1e-12);
            Assert.IsFalse(s.RadiatorKsp1Rates, "radiators draw KSP2's EC per kW by default");
        }

        [Test] public void Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"isru_ksp1_rates\": true, \"ksp1_converters\": false}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsTrue(s.IsruKsp1Rates, "the older keys are read as before");
        }

        [Test] public void Read()
        {
            var s = AnimationSettings.Parse("{\"ksp1_radiators\": false, \"radiator_deploy_bridge\": false, \"radiator_auto_on\": false, " +
                                            "\"radiator_flux_scale\": 0.5, \"radiator_ksp1_rates\": true}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Ksp1Radiators);
            Assert.IsFalse(s.RadiatorDeployBridge);
            Assert.IsFalse(s.RadiatorAutoOn);
            Assert.AreEqual(0.5, s.RadiatorFluxScale, 1e-12);
            Assert.IsTrue(s.RadiatorKsp1Rates);
        }

        [Test] public void FluxScale_Clamped_WrongTypes_KeepTheDefault()
        {
            var low = AnimationSettings.Parse("{\"radiator_flux_scale\": 0}", out var e);
            Assert.AreEqual(0.01, low.RadiatorFluxScale, 1e-12);
            StringAssert.Contains("radiator_flux_scale", e);

            var high = AnimationSettings.Parse("{\"radiator_flux_scale\": 500}", out var e2);
            Assert.AreEqual(100, high.RadiatorFluxScale);
            StringAssert.Contains("radiator_flux_scale", e2);

            var wrong = AnimationSettings.Parse("{\"ksp1_radiators\": 1, \"radiator_auto_on\": \"yes\", \"radiator_flux_scale\": \"x\", " +
                                                "\"radiator_ksp1_rates\": 3, \"radiator_deploy_bridge\": null}", out var e3);
            AssertDefaults(wrong);
            foreach (var key in Keys) StringAssert.Contains(key, e3);
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
