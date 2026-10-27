using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class PerfCounterTests
    {
        private const string Line =
            @"^\[KSP1PartMod\] anim: perf: \d+ module\(s\), \d+ entr\(ies\), \d+ write\(s\), [\d.]+ ms over 60 s$";

        [Test] public void Perf_ReportsEvery60s()
        {
            var p = new PerfCounter(1000);
            p.Add(5, 2, 3, 4);
            Assert.IsFalse(p.TryReport(59_000, out _));
            Assert.IsTrue(p.TryReport(60_001, out var l));
            StringAssert.IsMatch(Line, l);
        }

        /// <summary>Ticks and writes add up; modules and entries are the largest frame of the window.</summary>
        [Test] public void Perf_SumsTimeAndWrites_PeakModulesAndEntries()
        {
            var p = new PerfCounter(1000);
            p.Add(5, 2, 3, 4);
            p.Add(7, 4, 1, 6);
            Assert.IsTrue(p.TryReport(60_000, out var l));
            Assert.AreEqual("[KSP1PartMod] anim: perf: 4 module(s), 3 entr(ies), 10 write(s), 12 ms over 60 s", l);
        }

        /// <summary>A report starts the next window: nothing again before another 60 s, counts from zero.</summary>
        [Test] public void Perf_AfterReport_NewWindow()
        {
            var p = new PerfCounter(1000);
            p.Add(5, 2, 3, 4);
            Assert.IsTrue(p.TryReport(60_000, out _));
            Assert.IsFalse(p.TryReport(119_999, out _));
            p.Add(1, 1, 1, 1);
            Assert.IsTrue(p.TryReport(120_000, out var l));
            Assert.AreEqual("[KSP1PartMod] anim: perf: 1 module(s), 1 entr(ies), 1 write(s), 1 ms over 60 s", l);
        }

        /// <summary>The window starts at <see cref="PerfCounter.Start"/> (a real timestamp), not at 0.</summary>
        [Test] public void Perf_Start_SetsWindowOrigin()
        {
            var p = new PerfCounter(1000);
            p.Start(1_000_000);
            Assert.IsFalse(p.TryReport(1_059_999, out _));
            Assert.IsTrue(p.TryReport(1_060_000, out _));
        }

        [Test] public void Perf_Milliseconds_FromTicksPerSecond()
        {
            var p = new PerfCounter(10_000_000);
            p.Add(15_000, 1, 1, 0);
            Assert.IsTrue(p.TryReport(600_000_000, out var l));
            StringAssert.EndsWith(", 1.5 ms over 60 s", l);
        }
    }
}
