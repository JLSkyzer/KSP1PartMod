using System.Diagnostics;
using System.Linq;
using KSP.Game;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Game.Perf;
using KSP1PartMod.Perf;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Performance plan Task 5: the frame cost per feature, owned by <see cref="ChannelFlush"/>. A module cannot run in
    /// EditMode, so the "Drive-shaped" frame calls the same glue as <c>Module_Ksp1Animations.Drive</c> (one
    /// <see cref="ChannelFlush.Lap"/> per block and per entry, the parts and entries per feature, the 0.4.0
    /// <see cref="ChannelFlush.NoteDrive"/>), then the real radiator patch, the real flush and <see cref="ChannelFlush.EndFrame"/>
    /// as <c>LateUpdate</c> does.</summary>
    public class FrameCostTests
    {
        private static readonly int Features = PerfFeatures.Count;

        [SetUp] public void SetUp()
        {
            RadiatorRuntime.Reset();
            PerfReportWriter.Reset();
        }

        [TearDown] public void TearDown()
        {
            ChannelFlush.ResetFrameCostForTests(false);
            PerfReportWriter.Reset();
        }

        /// <summary>One frame: every feature fed, as <c>Drive</c> (laps), the patches and the flush do, then the frame ends.</summary>
        private static void Frame(FrameCostWindow w, int entriesPerFeature)
        {
            var start = Stopwatch.GetTimestamp();
            var t = start;
            for (var f = 0; f < Features; f++)
            {
                for (var e = 0; e < entriesPerFeature; e++) t = ChannelFlush.Lap(w, (PerfFeature)f, t);
                w.NotePart((PerfFeature)f);
                w.NoteEntries((PerfFeature)f, entriesPerFeature);
            }
            ChannelFlush.NoteDrive(t - start, Features * entriesPerFeature);
            RadiatorRuntime.AfterCoolerFixedUpdate(null); // the real patch, timed in RadiatorPatch (returns at once: not armed)
            ChannelFlush.AddSince(PerfFeature.DrillPatch, t);
            ChannelFlush.FlushAll();
            ChannelFlush.EndFrame(0.016f);
        }

        [Test] public void DriveShapedFrames_DoNotAllocate()
        {
            ChannelFlush.ResetFrameCostForTests(true, windowSeconds: 300);
            var w = ChannelFlush.FrameWindowOrCreate();
            Assert.IsNotNull(w);
            Frame(w, 3); // warm-up: the 0.4.0 counter and any first-call state
            Assert.That(() =>
            {
                for (var i = 0; i < 10_000; i++) Frame(w, 3);
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(10_001, w.Frames);
            Assert.AreSame(w, ChannelFlush.FrameWindow, "one window for the session");
        }

        /// <summary>No KSP1 module: the radiator patch alone still counts, and the frame ends.</summary>
        [Test] public void FrameWithoutModule_EndsAndCountsThePatches()
        {
            ChannelFlush.ResetFrameCostForTests(true, windowSeconds: 300);
            var w = ChannelFlush.FrameWindowOrCreate();
            w.TimerOverheadTicks = 1;
            RadiatorRuntime.AfterCoolerFixedUpdate(null);
            ChannelFlush.AddSince(PerfFeature.DrillPatch, Stopwatch.GetTimestamp() - Stopwatch.Frequency / 1000); // ~1 ms
            ChannelFlush.EndFrame(0.02f);
            Assert.AreEqual(1, w.Frames);

            ChannelFlush.NoteGameState(GameState.VehicleAssemblyBuilder); // closes the window
            var last = w.Last;
            Assert.IsNotNull(last);
            Assert.AreEqual("other", last.Scene);
            Assert.AreEqual(1, last.Frames);
            Assert.Greater(last.OverheadMs, 0, "two timings counted: the radiator patch and the drill patch");
            var drill = last.Features.Single(f => f.Feature == PerfFeature.DrillPatch);
            Assert.GreaterOrEqual(drill.AvgMs, 0.9);
            Assert.AreEqual(50, last.Fps, 0.01);
        }

        [Test] public void SceneSwitch_ClosesTheWindow_AndKeepsItForTheReport()
        {
            ChannelFlush.ResetFrameCostForTests(true, windowSeconds: 300);
            var w = ChannelFlush.FrameWindowOrCreate();
            ChannelFlush.NoteGameState(GameState.VehicleAssemblyBuilder);
            Assert.IsNull(w.Last, "no frame yet: nothing to close");
            Assert.AreEqual("VAB", ChannelFlush.FrameScene);

            for (var i = 0; i < 5; i++) Frame(w, 1);
            ChannelFlush.NoteGameState(GameState.VehicleAssemblyBuilder);
            Assert.IsNull(w.Last, "same scene: the window stays open");

            ChannelFlush.NoteGameState(GameState.FlightView);
            Assert.IsNotNull(w.Last);
            Assert.AreEqual("VAB", w.Last.Scene);
            Assert.AreEqual(5, w.Last.Frames);
            Assert.AreEqual(0, w.Frames, "the next window starts empty");
            Assert.AreEqual("flight", ChannelFlush.FrameScene);
            Assert.AreSame(w.Last, PerfReportWriter.FrameWindows.Single(x => x.Scene == "VAB"));

            Frame(w, 1);
            ChannelFlush.NoteGameState(GameState.Map3DView);
            Assert.AreEqual("flight", w.Last.Scene);
            Assert.AreEqual("map", ChannelFlush.FrameScene);
            Assert.AreEqual(2, PerfReportWriter.FrameWindows.Count);
        }

        [Test] public void Scenes()
        {
            Assert.AreEqual("VAB", ChannelFlush.SceneOf(GameState.VehicleAssemblyBuilder));
            Assert.AreEqual("flight", ChannelFlush.SceneOf(GameState.FlightView));
            Assert.AreEqual("map", ChannelFlush.SceneOf(GameState.Map3DView));
            Assert.AreEqual("KSC", ChannelFlush.SceneOf(GameState.KerbalSpaceCenter));
            Assert.AreEqual("other", ChannelFlush.SceneOf(GameState.MainMenu));
        }

        /// <summary><c>perf_counter</c> off (the default): every timed site reads the switch and nothing else, and the window
        /// is never created.</summary>
        [Test] public void PerfOff_TheWindowIsNeverCreated()
        {
            ChannelFlush.ResetFrameCostForTests(false);
            Assume.That(ChannelFlush.PerfOn, NUnit.Framework.Is.False, "the EditMode settings are the defaults");
            // The simulated Drive takes the window only when PerfOn, as Module_Ksp1Animations.Drive does.
            var w = ChannelFlush.PerfOn ? ChannelFlush.FrameWindowOrCreate() : null;
            ChannelFlush.Lap(w, PerfFeature.Clips, Stopwatch.GetTimestamp());
            RadiatorRuntime.AfterCoolerFixedUpdate(null);
            ChannelFlush.AddSince(PerfFeature.DrillPatch, Stopwatch.GetTimestamp());
            ChannelFlush.FlushAll();
            ChannelFlush.EndFrame(0.016f);
            ChannelFlush.NoteGameState(GameState.VehicleAssemblyBuilder);
            Assert.IsNull(ChannelFlush.FrameWindowOrCreate());
            Assert.IsNull(ChannelFlush.FrameWindow);
            Assert.AreEqual("other", ChannelFlush.FrameScene, "no scene kept either");
        }

        /// <summary>Not an assertion of speed: prints what one timing costs on this machine and what a 1000-entry frame pays in
        /// timestamps (the report's "measured timer overhead in EditMode").</summary>
        [Test] public void TimerOverhead_Printed()
        {
            ChannelFlush.ResetFrameCostForTests(true, windowSeconds: 300);
            var w = ChannelFlush.FrameWindowOrCreate();
            var ns = w.TimerOverheadTicks * 1e9 / Stopwatch.Frequency;
            const int frames = 200, entries = 1000;
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
            {
                var t = Stopwatch.GetTimestamp();
                for (var e = 0; e < entries; e++) t = ChannelFlush.Lap(w, PerfFeature.Clips, t);
                ChannelFlush.EndFrame(0.016f);
            }
            var perFrameMs = sw.Elapsed.TotalMilliseconds / frames;
            TestContext.Out.WriteLine($"timer overhead: {ns:0.#} ns per timestamp (Stopwatch.Frequency {Stopwatch.Frequency}); " +
                                      $"{entries} laps + end of frame: {perFrameMs:0.###} ms per frame");
            Assert.Greater(w.Frames, 0);
        }
    }
}
