using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The native-module keys of 0.8.0.</summary>
    public class NativeSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        [Test] public void CrossfeedToggle_DefaultsOn() => Assert.IsTrue(AnimationSettings.Defaults.Ksp1CrossfeedToggle);

        [Test] public void CrossfeedToggle_OldFileWithoutTheKey_On()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Ksp1CrossfeedToggle, "independent of enabled");
        }

        [Test] public void CrossfeedToggle_IsRead_WrongTypeKeepsTheDefault()
        {
            Assert.IsFalse(AnimationSettings.Parse("{\"ksp1_crossfeed_toggle\": false}", out var e).Ksp1CrossfeedToggle);
            Assert.IsNull(e);
            Assert.IsTrue(AnimationSettings.Parse("{\"ksp1_crossfeed_toggle\": 0}", out var e2).Ksp1CrossfeedToggle);
            StringAssert.Contains("ksp1_crossfeed_toggle", e2);
        }

        [Test] public void CrossfeedToggle_ShippedFile_OnAndDocumented()
        {
            var text = File.ReadAllText(ShippedPath);
            var s = AnimationSettings.Parse(text, out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Ksp1CrossfeedToggle);
            StringAssert.Contains("\"ksp1_crossfeed_toggle\": true", text);
            var note = text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal));
            StringAssert.Contains("ksp1_crossfeed_toggle:", note);
        }

        [Test] public void Heatshields_Defaults_OnScale1CharOn()
        {
            var d = AnimationSettings.Defaults;
            Assert.IsTrue(d.Ksp1Heatshields);
            Assert.AreEqual(1.0, d.AblatorRateScale);
            Assert.IsTrue(d.AblatorChar);
            var old = AnimationSettings.Parse("{\"enabled\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsTrue(old.Ksp1Heatshields, "independent of enabled");
            Assert.AreEqual(1.0, old.AblatorRateScale);
        }

        [Test] public void Heatshields_AreRead_ScaleClamped_WrongTypeKeepsTheDefault()
        {
            var s = AnimationSettings.Parse("{\"ksp1_heatshields\": false, \"ablator_rate_scale\": 2, \"ablator_char\": false}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.Ksp1Heatshields);
            Assert.AreEqual(2.0, s.AblatorRateScale, 1e-6);
            Assert.IsFalse(s.AblatorChar);

            var clamped = AnimationSettings.Parse("{\"ablator_rate_scale\": 0}", out var e2);
            Assert.AreEqual(0.01, clamped.AblatorRateScale, 1e-6);
            StringAssert.Contains("ablator_rate_scale", e2);

            var wrong = AnimationSettings.Parse("{\"ablator_rate_scale\": \"x\", \"ksp1_heatshields\": 1}", out var e3);
            Assert.AreEqual(1.0, wrong.AblatorRateScale);
            Assert.IsTrue(wrong.Ksp1Heatshields);
            StringAssert.Contains("ablator_rate_scale", e3);
            StringAssert.Contains("ksp1_heatshields", e3);
        }

        [Test] public void Heatshields_ShippedFile_DefaultsAndDocumented()
        {
            var text = File.ReadAllText(ShippedPath);
            var s = AnimationSettings.Parse(text, out var e);
            Assert.IsNull(e);
            Assert.IsTrue(s.Ksp1Heatshields);
            Assert.AreEqual(1.0, s.AblatorRateScale);
            Assert.IsTrue(s.AblatorChar);
            var note = text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal));
            foreach (var key in new[] { "ksp1_heatshields:", "ablator_rate_scale:", "ablator_char:" })
                StringAssert.Contains(key, note);
        }
    }
}
