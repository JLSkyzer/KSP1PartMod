using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    public class AnimationSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        [Test] public void Settings_BadJson_DefaultsAndError()
        {
            var s = AnimationSettings.Parse("{", out var e);
            Assert.IsNotNull(e);
            Assert.IsTrue(s.FixMuReader);
            Assert.AreSame(AnimationSettings.Defaults, s);
        }

        [Test] public void Settings_ShippedFile_ParsesWithoutError()
        {
            var s = AnimationSettings.Parse(File.ReadAllText(ShippedPath), out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Enabled);
            Assert.IsTrue(s.FixMuReader);
            Assert.IsFalse(s.OwnFirstDeployable);
            CollectionAssert.AreEqual(new[] { "_EmissiveColor", "_EmissionColor" }, s.ShaderPropertyMap["_EmissiveColor"]);
            CollectionAssert.AreEqual(new[] { "_BurnColor" }, s.ShaderPropertyMap["_BurnColor"]);
            Assert.IsFalse(s.ShaderPropertyMap.ContainsKey("_note"));
        }

        [Test] public void Settings_Empty_Defaults()
        {
            var s = AnimationSettings.Parse("  ", out var e);
            Assert.IsNull(e);
            Assert.AreSame(AnimationSettings.Defaults, s);
        }

        [Test] public void Settings_Defaults_AreDocumentedValues()
        {
            var d = AnimationSettings.Defaults;
            Assert.AreEqual((true, true, false), (d.Enabled, d.FixMuReader, d.OwnFirstDeployable));
            CollectionAssert.AreEqual(new[] { "_EmissiveColor", "_EmissionColor" }, d.ShaderPropertyMap["_EmissiveColor"]);
        }

        [Test] public void Settings_Switches_AreRead()
        {
            var s = AnimationSettings.Parse(
                "{\"enabled\": false, \"fix_mu_reader\": false, \"own_first_deployable\": true}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual((false, false, true), (s.Enabled, s.FixMuReader, s.OwnFirstDeployable));
        }

        [Test] public void Settings_WrongType_KeepsDefaultAndReports()
        {
            var s = AnimationSettings.Parse("{\"fix_mu_reader\": \"no\", \"enabled\": false}", out var e);
            StringAssert.Contains("fix_mu_reader", e);
            Assert.IsTrue(s.FixMuReader);
            Assert.IsFalse(s.Enabled);
        }

        [Test] public void Settings_UnknownKey_Reported()
        {
            AnimationSettings.Parse("{\"fix_mu_readr\": false}", out var e);
            StringAssert.Contains("fix_mu_readr", e);
        }

        [Test] public void Settings_ShaderMap_ReplacesDefaultAndSkipsBadEntries()
        {
            var s = AnimationSettings.Parse(
                "{\"shader_property_map\": {\"_Color\": [\"_BaseColor\"], \"_Bad\": 3, \"_note\": \"x\"}}", out var e);
            CollectionAssert.AreEqual(new[] { "_BaseColor" }, s.ShaderPropertyMap["_Color"]);
            Assert.IsFalse(s.ShaderPropertyMap.ContainsKey("_EmissiveColor"));
            Assert.IsFalse(s.ShaderPropertyMap.ContainsKey("_Bad"));
            StringAssert.Contains("_Bad", e);
        }
    }
}
