using System.Diagnostics;
using System.Threading;
using KSP1PartMod.Perf;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class PerfClockTests
    {
        [SetUp] public void SetUp() => PerfClock.ResetForTests();
        [TearDown] public void TearDown() => PerfClock.ResetForTests();

        [Test] public void Off_AddsNothing_AndStartIsZero()
        {
            Assert.AreEqual(0L, PerfClock.Start());
            PerfClock.NoteTicks(PerfPhase.AnimationStep, 100);
            PerfClock.Note(PerfPhase.Placement, Stopwatch.StartNew());
            PerfClock.Stop(PerfPhase.MuFix, 12345);
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.AnimationStep).Calls);
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.Placement).Calls);
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.MuFix).Calls);
        }

        [Test] public void On_AddsTicksAndCalls()
        {
            PerfClock.Enabled = true;
            PerfClock.NoteTicks(PerfPhase.AnimationStep, 100);
            PerfClock.NoteTicks(PerfPhase.AnimationStep, 50);
            var start = PerfClock.Start();
            Assert.AreNotEqual(0L, start);
            PerfClock.Stop(PerfPhase.MuFix, start);
            Assert.AreEqual(150, PerfClock.Clock.Get(PerfPhase.AnimationStep).Ticks);
            Assert.AreEqual(2, PerfClock.Clock.Get(PerfPhase.AnimationStep).Calls);
            Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.MuFix).Calls);
        }

        [Test] public void Note_UsesTheWatchTicks()
        {
            PerfClock.Enabled = true;
            var w = Stopwatch.StartNew();
            Thread.Sleep(2);
            w.Stop();
            PerfClock.Note(PerfPhase.ScienceScan, w);
            Assert.AreEqual(w.ElapsedTicks, PerfClock.Clock.Get(PerfPhase.ScienceScan).Ticks);
        }

        [Test] public void InRebuild_OnlyTheRebuildPhaseCounts()
        {
            PerfClock.Enabled = true;
            PerfClock.InRebuild = true;
            PerfClock.NoteTicks(PerfPhase.ShroudImport, 10);
            PerfClock.NoteTicks(PerfPhase.Textures, 10);
            PerfClock.NoteTicks(PerfPhase.Rebuild, 10);
            PerfClock.InRebuild = false;
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.ShroudImport).Calls);
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.Textures).Calls);
            Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.Rebuild).Calls);
        }

        [Test] public void InRebuild_IsPerThread()
        {
            PerfClock.Enabled = true;
            PerfClock.InRebuild = true;
            var t = new Thread(() => PerfClock.NoteTicks(PerfPhase.TankCatalog, 7));
            t.Start();
            t.Join();
            PerfClock.InRebuild = false;
            Assert.AreEqual(7, PerfClock.Clock.Get(PerfPhase.TankCatalog).Ticks);
        }
    }
}
