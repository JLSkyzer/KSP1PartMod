using System.IO;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The performance keys of 0.13.0 (<c>perf_counter</c> is 0.4.0's).</summary>
    public class PerfSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "perf_load_timeline", "perf_memory_lines", "perf_report_file", "perf_top_n", "perf_window_seconds",
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            Assert.IsTrue(s.PerfLoadTimeline);
            Assert.IsTrue(s.PerfMemoryLines);
            Assert.IsTrue(s.PerfReportFile);
            Assert.AreEqual(10, s.PerfTopN);
            Assert.AreEqual(10.0, s.PerfWindowSeconds, 1e-12);
        }

        [Test] public void Defaults()
        {
            AssertDefaults(AnimationSettings.Defaults);
            Assert.IsFalse(AnimationSettings.Defaults.PerfCounter, "the frame cost stays opt-in");
        }

        [Test] public void OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"perf_counter\": true, \"ksp1_science\": false}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsTrue(s.PerfCounter, "the older keys are read as before");
            Assert.IsFalse(s.Ksp1Science);
        }

        [Test] public void Read_IndependentOfEnabled()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"perf_load_timeline\": false, \"perf_memory_lines\": false, " +
                                            "\"perf_report_file\": false, \"perf_top_n\": 25, \"perf_window_seconds\": 2.5}", out var e);
            Assert.IsNull(e);
            Assert.IsFalse(s.PerfLoadTimeline);
            Assert.IsFalse(s.PerfMemoryLines);
            Assert.IsFalse(s.PerfReportFile);
            Assert.AreEqual(25, s.PerfTopN);
            Assert.AreEqual(2.5, s.PerfWindowSeconds, 1e-12);
        }

        [Test] public void Clamped()
        {
            var low = AnimationSettings.Parse("{\"perf_top_n\": -3, \"perf_window_seconds\": 1}", out var e);
            Assert.AreEqual(1, low.PerfTopN);
            Assert.AreEqual(2.0, low.PerfWindowSeconds, 1e-12);
            StringAssert.Contains("perf_top_n", e);
            StringAssert.Contains("perf_window_seconds", e);

            var high = AnimationSettings.Parse("{\"perf_top_n\": 500, \"perf_window_seconds\": 1000}", out var e2);
            Assert.AreEqual(50, high.PerfTopN);
            Assert.AreEqual(300.0, high.PerfWindowSeconds, 1e-12);
            StringAssert.Contains("clamped", e2);

            var zero = AnimationSettings.Parse("{\"perf_top_n\": 0}", out var e3);
            Assert.AreEqual(10, zero.PerfTopN, "0 is refused, the default kept");
            StringAssert.Contains("perf_top_n", e3);
        }

        [Test] public void WrongTypes_KeepTheDefault()
        {
            var s = AnimationSettings.Parse("{\"perf_load_timeline\": 1, \"perf_memory_lines\": \"yes\", \"perf_report_file\": null, " +
                                            "\"perf_top_n\": 2.5, \"perf_window_seconds\": \"x\"}", out var e);
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
