using System;
using System.Linq;
using KSP1PartMod.Perf;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class FrameCostWindowTests
    {
        /// <summary>One tick is one microsecond.</summary>
        private const long Us = 1000000;

        private static FrameCostWindow Window(double seconds = 10)
        {
            var w = new FrameCostWindow(Us, seconds);
            w.Start(0);
            return w;
        }

        /// <summary>1000 frames: clips cost i µs in frame i (1..1000); the material costs 100 µs in one frame of ten, 0 otherwise.</summary>
        private static FrameCostWindow KnownSamples()
        {
            var w = Window();
            w.TimerOverheadTicks = 2;
            for (var i = 1; i <= 1000; i++)
            {
                w.Add(PerfFeature.Clips, i);
                w.NotePart(PerfFeature.Clips);
                w.NoteEntries(PerfFeature.Clips, 3);
                if (i % 10 == 0)
                {
                    w.Add(PerfFeature.Material, 100);
                    w.NotePart(PerfFeature.Material);
                    w.NotePart(PerfFeature.Material);
                }
                w.EndFrame(10000);
            }
            return w;
        }

        [Test] public void KnownSamples_AverageMaxAndP95WithinTheBucket()
        {
            var w = KnownSamples();
            Assert.IsTrue(w.TryReport(10 * Us, "VAB", out var lines, out var count));
            var s = w.Last;
            Assert.AreEqual(1000, s.Frames);
            Assert.AreEqual(10.0, s.Seconds, 1e-9);
            Assert.AreEqual(100.0, s.Fps, 1e-9);
            Assert.AreEqual(10.0, s.FrameP95Ms, 1e-9, "every frame 10 ms: capped by the max");

            var clips = s.Features.Single(f => f.Feature == PerfFeature.Clips);
            Assert.AreEqual(0.5005, clips.AvgMs, 1e-9);
            Assert.AreEqual(1.0, clips.MaxMs, 1e-9);
            Assert.That(clips.P95Ms, NUnit.Framework.Is.GreaterThanOrEqualTo(0.950));
            Assert.That(clips.P95Ms, NUnit.Framework.Is.LessThanOrEqualTo(0.950 * FrameCostWindow.BucketFactor + 0.001));
            Assert.AreEqual(1, clips.Parts);
            Assert.AreEqual(3, clips.Entries);

            var material = s.Features.Single(f => f.Feature == PerfFeature.Material);
            Assert.AreEqual(0.01, material.AvgMs, 1e-9, "frames without a cost count as 0");
            Assert.AreEqual(0.1, material.MaxMs, 1e-9);
            Assert.That(material.P95Ms, NUnit.Framework.Is.GreaterThanOrEqualTo(0.1 - 1e-9));
            Assert.That(material.P95Ms, NUnit.Framework.Is.LessThanOrEqualTo(0.1 * FrameCostWindow.BucketFactor + 0.001));
            Assert.AreEqual(2, material.Parts);

            Assert.AreEqual(0.5105, s.OursAvgMs, 1e-9);
            Assert.AreEqual(1.1, s.OursMaxMs, 1e-9, "frame 1000: 1000 µs of clips and 100 of material");
            // 1100 timings over 1000 frames, 2 µs each.
            Assert.AreEqual(0.0022, s.OverheadMs, 1e-9);

            Assert.AreEqual(3, count, "the frame line, then one per feature that cost something");
            Assert.AreEqual(PerfLines.FrameWindow(s), lines[0]);
            Assert.AreEqual(PerfLines.FrameFeature("VAB", clips), lines[1]);
            Assert.AreEqual(PerfLines.FrameFeature("VAB", material), lines[2]);
            StringAssert.StartsWith("[KSP1PartMod] perf: frame: VAB: 2a material 0.01 ms avg, 0.1 ms p95, 0.1 ms max per frame; 2 part(s), 0 entr(ies)", lines[2]);
            Assert.IsNull(lines[3]);
        }

        /// <summary>A change of scene closes the window in the middle of a frame: what that frame gathered so far is dropped
        /// with the window, never carried into the next scene's first frame.</summary>
        [Test] public void ForcedClose_DropsTheOpenFrame()
        {
            var w = Window();
            w.TimerOverheadTicks = 1;
            w.Add(PerfFeature.Clips, 10);
            w.EndFrame(10000);
            w.Add(PerfFeature.Material, 500, 2); // the frame in progress when the scene changes
            w.NotePart(PerfFeature.Material);
            w.NoteEntries(PerfFeature.Material, 4);
            Assert.IsTrue(w.TryReport(Us, "VAB", out _, out _, force: true));
            Assert.IsFalse(w.Last.Features.Any(f => f.Feature == PerfFeature.Material), "not ended: not in the VAB window");

            w.Add(PerfFeature.Clips, 10);
            w.EndFrame(10000);
            Assert.IsTrue(w.TryReport(2 * Us, "flight", out _, out _, force: true));
            var s = w.Last;
            Assert.IsFalse(s.Features.Any(f => f.Feature == PerfFeature.Material), "not carried into the flight window");
            Assert.AreEqual(0.001, s.OverheadMs, 1e-12, "one timing in one frame: the dropped frame's two are gone");
        }

        /// <summary>A site timed on its own takes two timestamps; the overhead counts both.</summary>
        [Test] public void Timings_CountEveryTimestamp()
        {
            var w = Window();
            w.TimerOverheadTicks = 1; // 1 µs per timing
            w.Add(PerfFeature.RadiatorPatch, 5, 2);
            w.Add(PerfFeature.Clips, 5);
            w.NoteParts(PerfFeature.Flush, 7);
            w.Add(PerfFeature.Flush, 5, 2);
            w.EndFrame(10000);
            Assert.IsTrue(w.TryReport(Us, "flight", out _, out _, force: true));
            Assert.AreEqual(0.005, w.Last.OverheadMs, 1e-12, "2 + 1 + 2 timings");
            Assert.AreEqual(7, w.Last.Features.Single(f => f.Feature == PerfFeature.Flush).Parts);
        }

        [Test] public void Buckets_96_From1UsTo100Ms()
        {
            Assert.AreEqual(96, FrameCostWindow.Buckets);
            Assert.AreEqual(95, FrameCostWindow.BoundsMicroseconds.Length);
            Assert.AreEqual(1.0, FrameCostWindow.BoundsMicroseconds[0], 1e-12);
            Assert.AreEqual(100000.0, FrameCostWindow.BoundsMicroseconds[94], 1e-6);
            Assert.AreEqual(1.13, FrameCostWindow.BucketFactor, 0.001);
        }

        [Test] public void AboveTheTopBucket_P95IsTheMax()
        {
            var w = Window();
            for (var i = 0; i < 20; i++)
            {
                w.Add(PerfFeature.Flush, 250000);
                w.EndFrame(300000);
            }
            Assert.IsTrue(w.TryReport(10 * Us, "flight", out _, out _));
            Assert.AreEqual(250.0, w.Last.Features.Single().P95Ms, 1e-9);
        }

        [Test] public void EmptyWindow_NoLine()
        {
            var w = Window();
            Assert.IsFalse(w.TryReport(100 * Us, "VAB", out _, out var count));
            Assert.AreEqual(0, count);
            Assert.IsFalse(w.TryReport(200 * Us, "VAB", out _, out _, force: true));
            Assert.IsNull(w.Last);
        }

        [Test] public void FramesWithoutAnyCost_OnlyTheFrameLine()
        {
            var w = Window();
            for (var i = 0; i < 5; i++) w.EndFrame(16000);
            Assert.IsTrue(w.TryReport(10 * Us, null, out var lines, out var count));
            Assert.AreEqual(1, count);
            StringAssert.StartsWith("[KSP1PartMod] perf: frame: other over 10.0 s, 5 frame(s), 62.5 fps", lines[0]);
        }

        [Test] public void WindowLength_FromTheSetting()
        {
            var w = Window(2.5);
            w.EndFrame(1000);
            Assert.IsFalse(w.TryReport(2499999, "VAB", out _, out _));
            Assert.IsTrue(w.TryReport(2500000, "VAB", out _, out _));
            Assert.AreEqual(2.5, w.WindowSeconds);
            w.EndFrame(1000);
            Assert.IsFalse(w.TryReport(4999999, "VAB", out _, out _), "the next window begins at the report");
            Assert.IsTrue(w.TryReport(5000000, "VAB", out _, out _));
        }

        [Test] public void Force_ClosesEarly_AndTheNextWindowIsEmpty()
        {
            var w = Window();
            w.Add(PerfFeature.Clips, 50);
            w.EndFrame(1000);
            Assert.IsTrue(w.TryReport(Us, "VAB", out _, out _, force: true));
            Assert.AreEqual(1.0, w.Last.Seconds, 1e-9);
            Assert.AreEqual(0, w.Frames);
            w.EndFrame(1000);
            Assert.IsTrue(w.TryReport(11 * Us, "flight", out _, out _));
            Assert.AreEqual(1, w.Last.Frames);
            Assert.AreEqual(0, w.Last.Features.Count, "the previous window's cost does not leak");
        }

        [Test] public void NotStarted_FirstReportStartsTheWindow()
        {
            var w = new FrameCostWindow(Us, 10);
            w.EndFrame(1000);
            Assert.IsFalse(w.TryReport(50 * Us, "VAB", out _, out _));
            Assert.IsTrue(w.TryReport(60 * Us, "VAB", out _, out _));
        }

        [Test] public void AddAndEndFrame_10000Frames_AllocateNothing()
        {
            var w = Window();
            var features = (PerfFeature[])Enum.GetValues(typeof(PerfFeature));
            void Frames()
            {
                for (var i = 0; i < 10000; i++)
                {
                    foreach (var f in features)
                    {
                        w.Add(f, i % 5000);
                        w.NotePart(f);
                        w.NoteEntries(f, 2);
                    }
                    w.EndFrame(16000);
                }
            }

            Frames();
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }

        [Test] public void Constructor_RefusesBadArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameCostWindow(0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameCostWindow(Us, 0));
        }
    }
}
