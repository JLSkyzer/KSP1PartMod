using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The science keys of 0.12.0 (science plan, Global Constraints).</summary>
    public class ScienceSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "ksp1_science", "science_generate_definitions", "science_definitions_via_patch_manager", "science_report_storage",
            "science_value_scale", "science_transmit_split", "science_native_ids", "science_fallback_ids", "science_skip_ids",
            "science_deploy_animations", "science_wait_for_animation",
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            Assert.IsTrue(s.Ksp1Science);
            Assert.IsTrue(s.ScienceGenerateDefinitions);
            Assert.IsTrue(s.ScienceDefinitionsViaPatchManager);
            Assert.IsTrue(s.ScienceReportStorage);
            Assert.AreEqual(1.0, s.ScienceValueScale, 1e-12);
            Assert.IsTrue(s.ScienceTransmitSplit);
            CollectionAssert.AreEquivalent(new[] { "crewReport" }, s.ScienceNativeIds.Keys);
            Assert.AreEqual("CrewObservation", s.ScienceNativeIds["crewReport"]);
            Assert.AreEqual(9, s.ScienceFallbackIds.Count);
            Assert.AreEqual("EnvironmentSurvey", s.ScienceFallbackIds["mysteryGoo"]);
            Assert.AreEqual("AtmosphereSurvey", s.ScienceFallbackIds["barometerScan"]);
            Assert.AreEqual("MarineSurvey", s.ScienceFallbackIds["seismicScan"]);
            Assert.AreEqual("OrbitalSurvey", s.ScienceFallbackIds["infraredTelescope"]);
            Assert.AreEqual("RadiationReport", s.ScienceFallbackIds["magnetometer"]);
            CollectionAssert.AreEqual(new[] { "evaReport", "evaScience", "surfaceSample", "ROCScience" }, s.ScienceSkipIds.ToArray());
            Assert.IsTrue(s.ScienceDeployAnimations);
            Assert.IsTrue(s.ScienceWaitForAnimation);
        }

        [Test] public void Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"ksp1_radiators\": false, \"radiator_flux_scale\": 2}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsFalse(s.Ksp1Radiators, "the older keys are read as before");
            Assert.AreEqual(2, s.RadiatorFluxScale, 1e-12);
        }

        [Test] public void Read()
        {
            var s = AnimationSettings.Parse("{\"ksp1_science\": false, \"science_generate_definitions\": false, " +
                                            "\"science_definitions_via_patch_manager\": false, \"science_report_storage\": false, " +
                                            "\"science_value_scale\": 0.5, \"science_transmit_split\": false, " +
                                            "\"science_native_ids\": {\"_note\": \"x\", \"crewReport\": \"CrewObservation\", \"mysteryGoo\": \" EnvironmentSurvey \"}, " +
                                            "\"science_fallback_ids\": {\"seismicScan\": \"CrewReport\"}, " +
                                            "\"science_skip_ids\": [\"evaReport\", \" bd_poke \", \"evaReport\"], " +
                                            "\"science_deploy_animations\": false, \"science_wait_for_animation\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Ksp1Science);
            Assert.IsFalse(s.ScienceGenerateDefinitions);
            Assert.IsFalse(s.ScienceDefinitionsViaPatchManager);
            Assert.IsFalse(s.ScienceReportStorage);
            Assert.AreEqual(0.5, s.ScienceValueScale, 1e-12);
            Assert.IsFalse(s.ScienceTransmitSplit);
            CollectionAssert.AreEquivalent(new[] { "crewReport", "mysteryGoo" }, s.ScienceNativeIds.Keys, "_note is a comment");
            Assert.AreEqual("EnvironmentSurvey", s.ScienceNativeIds["mysteryGoo"], "trimmed");
            Assert.AreEqual(1, s.ScienceFallbackIds.Count, "an object replaces the whole table");
            Assert.AreEqual("CrewReport", s.ScienceFallbackIds["seismicScan"]);
            CollectionAssert.AreEqual(new[] { "evaReport", "bd_poke" }, s.ScienceSkipIds.ToArray());
            Assert.IsFalse(s.ScienceDeployAnimations);
            Assert.IsFalse(s.ScienceWaitForAnimation);
        }

        [Test] public void EmptyForms()
        {
            var s = AnimationSettings.Parse("{\"science_native_ids\": {}, \"science_fallback_ids\": {}, \"science_skip_ids\": []}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual(0, s.ScienceNativeIds.Count);
            Assert.AreEqual(0, s.ScienceFallbackIds.Count);
            Assert.AreEqual(0, s.ScienceSkipIds.Count);
        }

        [Test] public void ValueScale_Clamped()
        {
            var low = AnimationSettings.Parse("{\"science_value_scale\": 0}", out var e);
            Assert.AreEqual(0.01, low.ScienceValueScale, 1e-12);
            StringAssert.Contains("science_value_scale", e);
            var high = AnimationSettings.Parse("{\"science_value_scale\": 1000}", out var e2);
            Assert.AreEqual(100, high.ScienceValueScale, 1e-12);
            StringAssert.Contains("science_value_scale", e2);
        }

        [Test] public void MalformedNativeIds_DefaultsKept_Noted()
        {
            var list = AnimationSettings.Parse("{\"science_native_ids\": [\"crewReport\"]}", out var e);
            Assert.AreEqual("CrewObservation", list.ScienceNativeIds["crewReport"]);
            StringAssert.Contains("science_native_ids", e);

            var badItems = AnimationSettings.Parse("{\"science_native_ids\": {\"crewReport\": 3, \"a\": \"\", \"b\": \"OrbitalSurvey\"}}", out var e2);
            CollectionAssert.AreEquivalent(new[] { "b" }, badItems.ScienceNativeIds.Keys, "bad items ignored, the rest read");
            StringAssert.Contains("science_native_ids.crewReport", e2);
            StringAssert.Contains("science_native_ids.a", e2);
        }

        [Test] public void WrongTypes_KeepTheDefault()
        {
            var wrong = AnimationSettings.Parse("{\"ksp1_science\": 1, \"science_generate_definitions\": \"yes\", " +
                                                "\"science_definitions_via_patch_manager\": null, \"science_report_storage\": 0, " +
                                                "\"science_value_scale\": \"x\", \"science_transmit_split\": [], \"science_native_ids\": 3, " +
                                                "\"science_fallback_ids\": \"table\", \"science_skip_ids\": {}, " +
                                                "\"science_deploy_animations\": \"on\", \"science_wait_for_animation\": 2}", out var e);
            AssertDefaults(wrong);
            foreach (var key in Keys) StringAssert.Contains(key, e);
        }

        [Test] public void EachSettingsObject_ItsOwnTables()
        {
            Assert.AreNotSame(AnimationSettings.Parse("{}", out _).ScienceFallbackIds, ScienceDefaults.FallbackIds);
            Assert.AreEqual(ScienceDefaults.FallbackIds.Count, AnimationSettings.Parse("{\"enabled\": true}", out _).ScienceFallbackIds.Count);
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
