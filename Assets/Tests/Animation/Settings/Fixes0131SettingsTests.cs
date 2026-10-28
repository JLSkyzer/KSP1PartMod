using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The keys of the 0.13.1 in-game fixes.</summary>
    public class Fixes0131SettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys = { "chute_canopy_reset", "plume_diagnostics", "fix_engine_emissive_curve" };

        private static bool[] Values(AnimationSettings s) => new[] { s.ChuteCanopyReset, s.PlumeDiagnostics, s.FixEngineEmissiveCurve };

        private static void AssertDefaults(AnimationSettings s)
        {
            var v = Values(s);
            for (var i = 0; i < Keys.Length; i++) Assert.IsTrue(v[i], Keys[i]);
        }

        [Test] public void Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"perf_counter\": true}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsFalse(s.Enabled, "the older keys are read as before");
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
            var s = AnimationSettings.Parse("{\"chute_canopy_reset\": 0, \"plume_diagnostics\": \"no\", \"fix_engine_emissive_curve\": null}", out var e);
            AssertDefaults(s);
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
