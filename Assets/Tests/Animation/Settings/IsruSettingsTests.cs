using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The ISRU keys of 0.10.0 (converters, drills, scanners, Ore remap, harvest multiplier, heat, active clip).</summary>
    public class IsruSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "ksp1_converters", "ksp1_drills", "ksp1_survey_scanners", "ksp1_resource_scanners", "ksp1_ore_resource",
            "harvest_multiplier", "isru_heat_scale", "isru_active_animation", "isru_ksp1_rates",
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            Assert.IsTrue(s.Ksp1Converters);
            Assert.IsTrue(s.Ksp1Drills);
            Assert.IsTrue(s.Ksp1SurveyScanners);
            Assert.IsTrue(s.Ksp1ResourceScanners);
            Assert.AreEqual("CarbonRegolith", s.Ksp1OreResource);
            Assert.AreEqual("CarbonRegolith", s.OreRemapTarget);
            Assert.AreEqual(1.0, s.HarvestMultiplier, 1e-12);
            Assert.AreEqual(1.0, s.IsruHeatScale);
            Assert.IsTrue(s.IsruActiveAnimation);
            Assert.IsFalse(s.IsruKsp1Rates, "KSP1 converters run at KSP2's pace per EC by default");
        }

        [Test] public void Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"ksp1_heatshields\": true}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
        }

        [Test] public void Read()
        {
            var s = AnimationSettings.Parse("{\"ksp1_converters\": false, \"ksp1_drills\": false, \"ksp1_survey_scanners\": false, " +
                                            "\"ksp1_resource_scanners\": false, \"ksp1_ore_resource\": \"  Water \", " +
                                            "\"harvest_multiplier\": 0.2, \"isru_heat_scale\": 0, \"isru_active_animation\": false, " +
                                            "\"isru_ksp1_rates\": true}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Ksp1Converters);
            Assert.IsFalse(s.Ksp1Drills);
            Assert.IsFalse(s.Ksp1SurveyScanners);
            Assert.IsFalse(s.Ksp1ResourceScanners);
            Assert.AreEqual("Water", s.Ksp1OreResource, "trimmed");
            Assert.AreEqual("Water", s.OreRemapTarget);
            Assert.AreEqual(0.2, s.HarvestMultiplier, 1e-12);
            Assert.AreEqual(0, s.IsruHeatScale, "0 = no heat");
            Assert.IsFalse(s.IsruActiveAnimation);
            Assert.IsTrue(s.IsruKsp1Rates);
        }

        [Test] public void OreResource_EmptyOrTestRocks_KeepsReduxMapping()
        {
            var empty = AnimationSettings.Parse("{\"ksp1_ore_resource\": \"   \"}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual("", empty.Ksp1OreResource);
            Assert.IsNull(empty.OreRemapTarget, "\"\" means Redux's mapping");
            var rocks = AnimationSettings.Parse("{\"ksp1_ore_resource\": \"TestRocks\"}", out var e2);
            Assert.IsNull(e2);
            Assert.AreEqual("TestRocks", rocks.Ksp1OreResource);
            Assert.IsNull(rocks.OreRemapTarget, "TestRocks is Redux's own mapping");
        }

        [Test] public void Clamped_WrongTypes_KeepTheDefault()
        {
            var low = AnimationSettings.Parse("{\"harvest_multiplier\": 0, \"isru_heat_scale\": -1}", out var e);
            Assert.AreEqual(0.01, low.HarvestMultiplier, 1e-12);
            Assert.AreEqual(0, low.IsruHeatScale);
            StringAssert.Contains("harvest_multiplier", e);
            StringAssert.Contains("isru_heat_scale", e);

            var high = AnimationSettings.Parse("{\"harvest_multiplier\": 300, \"isru_heat_scale\": 50}", out _);
            Assert.AreEqual(100, high.HarvestMultiplier);
            Assert.AreEqual(10, high.IsruHeatScale);

            var wrong = AnimationSettings.Parse("{\"ksp1_converters\": 1, \"ksp1_ore_resource\": 3, \"isru_heat_scale\": \"x\"}", out var e3);
            Assert.IsTrue(wrong.Ksp1Converters);
            Assert.AreEqual("CarbonRegolith", wrong.Ksp1OreResource);
            Assert.AreEqual(1, wrong.IsruHeatScale);
            StringAssert.Contains("ksp1_converters", e3);
            StringAssert.Contains("ksp1_ore_resource", e3);
            StringAssert.Contains("isru_heat_scale", e3);
        }

        /// <summary>The 0.05 abundance guess of the first drill importer is gone (ruling of 2026-10-28): its key is reported as
        /// unknown and the multiplier keeps its default.</summary>
        [Test] public void OldAbundanceKey_ReportedUnknown_MultiplierDefault()
        {
            var s = AnimationSettings.Parse("{\"harvest_abundance_at_full_density\": 0.2}", out var e);
            Assert.AreEqual(1.0, s.HarvestMultiplier, 1e-12);
            StringAssert.Contains("harvest_abundance_at_full_density", e);
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
