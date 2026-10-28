using System.Linq;
using System.Threading;
using KSP1PartMod.Perf;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class PhaseClockTests
    {
        [Test] public void Add_SumsCountsAndMax()
        {
            var c = new PhaseClock();
            c.Add(PerfPhase.Translate, 5);
            c.Add(PerfPhase.Translate, 20);
            c.Add(PerfPhase.Translate, 7);
            c.Add(PerfPhase.Prefab, 3);
            var t = c.Get(PerfPhase.Translate);
            Assert.AreEqual(32, t.Ticks);
            Assert.AreEqual(3, t.Calls);
            Assert.AreEqual(20, t.MaxTicks);
            Assert.AreEqual(3, c.Get(PerfPhase.Prefab).Ticks);
            Assert.AreEqual(0, c.Get(PerfPhase.Icons).Calls);
        }

        [Test] public void Add_NegativeTicksCountAsZero()
        {
            var c = new PhaseClock();
            c.Add(PerfPhase.Icons, -4);
            Assert.AreEqual(0, c.Get(PerfPhase.Icons).Ticks);
            Assert.AreEqual(1, c.Get(PerfPhase.Icons).Calls);
        }

        [Test] public void EightThreads_ExactTotal()
        {
            var c = new PhaseClock();
            var threads = Enumerable.Range(0, 8).Select(n => new Thread(() =>
            {
                for (var i = 1; i <= 10000; i++) c.Add(PerfPhase.CfgRead, i);
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            var total = c.Get(PerfPhase.CfgRead);
            Assert.AreEqual(8L * 10000 * 10001 / 2, total.Ticks);
            Assert.AreEqual(80000, total.Calls);
            Assert.AreEqual(10000, total.MaxTicks);
        }

        [Test] public void Snapshot_CopiesAndReset_TouchesOnlyTheGivenPhases()
        {
            var c = new PhaseClock();
            c.Add(PerfPhase.Translate, 10);
            c.Add(PerfPhase.AnimationStep, 4);
            var snap = c.Snapshot();
            Assert.AreEqual(PhaseClock.PhaseCount, snap.Length);
            Assert.AreEqual(10, snap[(int)PerfPhase.Translate].Ticks);
            c.Reset(PhaseClock.ImportPhases);
            Assert.AreEqual(0, c.Get(PerfPhase.Translate).Ticks);
            Assert.AreEqual(0, c.Get(PerfPhase.Translate).MaxTicks);
            Assert.AreEqual(4, c.Get(PerfPhase.AnimationStep).Ticks, "ours kept");
            Assert.AreEqual(10, snap[(int)PerfPhase.Translate].Ticks, "the snapshot is a copy");
            c.Reset(PhaseClock.OurPhases);
            Assert.AreEqual(0, c.Get(PerfPhase.AnimationStep).Ticks);
        }

        [Test] public void ImportAndOurPhases_DoNotOverlap_RebuildInNeither()
        {
            CollectionAssert.IsEmpty(PhaseClock.ImportPhases.Intersect(PhaseClock.OurPhases));
            CollectionAssert.DoesNotContain(PhaseClock.ImportPhases, PerfPhase.Rebuild);
            CollectionAssert.DoesNotContain(PhaseClock.OurPhases, PerfPhase.Rebuild);
        }

        [Test] public void Add_AllocatesNothing()
        {
            var c = new PhaseClock();
            void Adds()
            {
                for (var i = 0; i < 10000; i++) c.Add((PerfPhase)(i % PhaseClock.PhaseCount), i);
            }

            Adds();
            Assert.That(Adds, Is.Not.AllocatingGCMemory());
        }

        [Test] public void Ms_AndSeconds()
        {
            Assert.AreEqual(1.5, PhaseClock.Ms(15000, 10000000), 1e-12);
            Assert.AreEqual(2.0, PhaseClock.Seconds(20, 10), 1e-12);
            Assert.AreEqual(0, PhaseClock.Ms(5, 0));
        }
    }
}
