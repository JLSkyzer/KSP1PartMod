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
    }
}
