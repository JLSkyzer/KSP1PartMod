using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            CollectionAssert.AreEqual(new[] { "_EmissionColor", "_EmissiveColor" }, s.ShaderPropertyMap["_EmissiveColor"]);
            CollectionAssert.AreEqual(new[] { "_TintColor" }, s.ShaderPropertyMap["_TintColor"]);
            CollectionAssert.AreEqual(new[] { "_Color" }, s.ShaderPropertyMap["_Color"]);
            CollectionAssert.AreEqual(new[] { "_BurnColor" }, s.ShaderPropertyMap["_BurnColor"]);
            Assert.AreEqual(4, s.ShaderPropertyMap.Count);
            Assert.IsFalse(s.ShaderPropertyMap.ContainsKey("_note"));
            Assert.IsTrue(s.MaterialCurves && s.LightCurves && s.EmissionSetup && s.ThrottleEffects && s.HeatGlow);
            CollectionAssert.IsEmpty(s.SkipModules);
        }

        [Test] public void Settings_RootNotObject_DefaultsAndError()
        {
            var s = AnimationSettings.Parse("[]", out var e);
            StringAssert.Contains("settings root is not a JSON object", e);
            Assert.AreSame(AnimationSettings.Defaults, s);
        }

        [Test] public void Settings_DefaultMap_NotMutableThroughCast()
        {
            var map = AnimationSettings.Defaults.ShaderPropertyMap;
            Assert.IsFalse(map is Dictionary<string, string[]>);
            Assert.Throws<System.NotSupportedException>(() => ((IDictionary<string, string[]>)map)["_X"] = new[] { "_Y" });
        }

        [Test] public void Settings_ParsedDefaultMap_HasItsOwnArrays()
        {
            var s = AnimationSettings.Parse("{\"enabled\": true}", out _);
            Assert.AreNotSame(AnimationSettings.Defaults.ShaderPropertyMap["_BurnColor"], s.ShaderPropertyMap["_BurnColor"]);
            CollectionAssert.AreEqual(new[] { "_BurnColor" }, s.ShaderPropertyMap["_BurnColor"]);
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
            Assert.AreEqual((true, true, false, true), (d.Enabled, d.FixMuReader, d.OwnFirstDeployable, d.Diagnostics));
            CollectionAssert.AreEqual(new[] { "_EmissionColor", "_EmissiveColor" }, d.ShaderPropertyMap["_EmissiveColor"]);
        }

        [Test] public void Settings_NewKeys_DefaultTrue()
        {
            var s = AnimationSettings.Defaults;
            Assert.IsTrue(s.MaterialCurves && s.LightCurves && s.EmissionSetup && s.ThrottleEffects && s.HeatGlow);
            CollectionAssert.IsEmpty(s.SkipModules);
        }

        [Test] public void Settings_DefaultMap_EmissionColorFirst() =>
            Assert.AreEqual("_EmissionColor", AnimationSettings.Defaults.ShaderPropertyMap["_EmissiveColor"][0]);

        [Test] public void Settings_DefaultMap_HasTheFourKeys() =>
            CollectionAssert.AreEquivalent(new[] { "_EmissiveColor", "_TintColor", "_Color", "_BurnColor" },
                AnimationSettings.Defaults.ShaderPropertyMap.Keys);

        [Test] public void Settings_NewKeys_AreRead()
        {
            var s = AnimationSettings.Parse("{\"material_curves\": false, \"light_curves\": false, \"emission_setup\": false, " +
                                            "\"throttle_effects\": false, \"heat_glow\": false}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual((false, false, false, false, false), (s.MaterialCurves, s.LightCurves, s.EmissionSetup, s.ThrottleEffects, s.HeatGlow));
            Assert.IsTrue(s.Enabled);
        }

        [Test] public void Settings_NewKeys_WrongType_KeepDefaultAndReport()
        {
            var s = AnimationSettings.Parse("{\"material_curves\": 0, \"heat_glow\": \"no\"}", out var e);
            StringAssert.Contains("material_curves", e);
            StringAssert.Contains("heat_glow", e);
            Assert.IsTrue(s.MaterialCurves && s.HeatGlow);
        }

        [Test] public void Settings_SkipModules_AreRead()
        {
            var s = AnimationSettings.Parse("{\"skip_modules\": [\" FXModuleThrottleEffects \", \"ModuleColorChanger\", \"ModuleColorChanger\"]}", out var e);
            Assert.IsNull(e);
            CollectionAssert.AreEqual(new[] { "FXModuleThrottleEffects", "ModuleColorChanger" }, s.SkipModules);
            Assert.IsFalse(s.SkipModules.Contains("modulecolorchanger"), "ordinal");
        }

        [Test] public void Settings_SkipModules_WrongType_KeepsDefaultAndReports()
        {
            var s = AnimationSettings.Parse("{\"skip_modules\": 3}", out var e);
            CollectionAssert.IsEmpty(s.SkipModules);
            Assert.IsNotNull(e);
            StringAssert.Contains("skip_modules", e);
        }

        [Test] public void Settings_SkipModules_BadItem_SkippedAndReported()
        {
            var s = AnimationSettings.Parse("{\"skip_modules\": [\"ModuleColorChanger\", 4, \" \"]}", out var e);
            CollectionAssert.AreEqual(new[] { "ModuleColorChanger" }, s.SkipModules);
            StringAssert.Contains("skip_modules", e);
        }

        [Test] public void Settings_SkipModules_NotMutableThroughCast() =>
            Assert.IsFalse(AnimationSettings.Parse("{\"skip_modules\": [\"A\"]}", out _).SkipModules is ICollection<string> c && !c.IsReadOnly);

        [Test] public void Settings_ShippedFile_DocumentsEveryNewKey()
        {
            var text = File.ReadAllText(ShippedPath);
            foreach (var key in new[] { "material_curves", "light_curves", "emission_setup", "throttle_effects", "heat_glow", "skip_modules", "perf_counter" })
            {
                StringAssert.Contains($"\"{key}\":", text);
                StringAssert.Contains($"{key}:", text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal)), key + " in _note");
            }
        }

        [Test] public void Settings_PerfCounter_DefaultFalseReadWrongTypeKeepsDefault()
        {
            Assert.IsFalse(AnimationSettings.Defaults.PerfCounter);
            Assert.IsFalse(AnimationSettings.Parse(File.ReadAllText(ShippedPath), out _).PerfCounter, "shipped off");
            var s = AnimationSettings.Parse("{\"perf_counter\": true}", out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.PerfCounter);
            var bad = AnimationSettings.Parse("{\"perf_counter\": \"yes\"}", out var e2);
            Assert.IsFalse(bad.PerfCounter);
            StringAssert.Contains("perf_counter", e2);
        }

        [Test] public void Settings_Switches_AreRead()
        {
            var s = AnimationSettings.Parse(
                "{\"enabled\": false, \"fix_mu_reader\": false, \"own_first_deployable\": true}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual((false, false, true), (s.Enabled, s.FixMuReader, s.OwnFirstDeployable));
        }

        [Test] public void Settings_NewSwitches_DefaultTrueAndRead()
        {
            var d = AnimationSettings.Defaults;
            Assert.AreEqual((true, true, true), (d.FollowReduxDeployables, d.HideMaterialLessRenderers, d.FixPartIcons));
            var s = AnimationSettings.Parse(
                "{\"follow_redux_deployables\": false, \"hide_material_less_renderers\": false, \"fix_part_icons\": false}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual((false, false, false), (s.FollowReduxDeployables, s.HideMaterialLessRenderers, s.FixPartIcons));
            Assert.IsTrue(s.Enabled);
        }

        [Test] public void Settings_ShippedFile_HasNewSwitchesOn()
        {
            var s = AnimationSettings.Parse(File.ReadAllText(ShippedPath), out var e);
            Assert.IsNull(e);
            Assert.AreEqual((true, true, true), (s.FollowReduxDeployables, s.HideMaterialLessRenderers, s.FixPartIcons));
            var text = File.ReadAllText(ShippedPath);
            StringAssert.Contains("\"follow_redux_deployables\"", text);
            StringAssert.Contains("\"hide_material_less_renderers\"", text);
            StringAssert.Contains("\"fix_part_icons\"", text);
        }

        [Test] public void Settings_SubcategoryBar_DefaultTrueAndRead()
        {
            Assert.IsTrue(AnimationSettings.Defaults.SubcategoryBar);
            var s = AnimationSettings.Parse("{\"subcategory_bar\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.SubcategoryBar);
            Assert.IsTrue(s.Enabled);
            var shipped = AnimationSettings.Parse(File.ReadAllText(ShippedPath), out var e2);
            Assert.IsNull(e2);
            Assert.IsTrue(shipped.SubcategoryBar);
            StringAssert.Contains("\"subcategory_bar\"", File.ReadAllText(ShippedPath));
        }

        [Test] public void Settings_SubcategoryBar_WrongType_KeepsDefault()
        {
            var s = AnimationSettings.Parse("{\"subcategory_bar\": 1}", out var e);
            StringAssert.Contains("subcategory_bar", e);
            Assert.IsTrue(s.SubcategoryBar);
        }

        [Test] public void Settings_Diagnostics_IsRead()
        {
            var s = AnimationSettings.Parse("{\"diagnostics\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Diagnostics);
            Assert.IsTrue(s.Enabled);
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
