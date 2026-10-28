using System;
using System.Collections.Generic;
using KSP1PartMod.Perf;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class TimerOverheadTests
    {
        private static Func<long> Counter(params long[] values)
        {
            var q = new Queue<long>(values);
            return () => q.Dequeue();
        }

        /// <summary>Timestamps 0, 1, 1, 2, 2 (a counter coarser than the call): 2 ticks over 4 gaps, 0.5 per timestamp.</summary>
        [Test] public void Mean_KeepsTheFraction()
        {
            Assert.AreEqual(0.5, TimerOverhead.Mean(Counter(0, 1, 1, 2, 2), 5), 1e-12);
            Assert.AreEqual(0, TimerOverhead.Mean(Counter(7, 7), 2));
            Assert.Throws<ArgumentNullException>(() => TimerOverhead.Mean(null, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerOverhead.Mean(() => 0, 1));
        }

        [Test] public void Median_OddPairs()
        {
            // Gaps 5, 1, 3, 100, 2: the median is 3, whatever the outlier.
            var ts = Counter(0, 5, 10, 11, 20, 23, 30, 130, 200, 202);
            Assert.AreEqual(3, TimerOverhead.Estimate(ts, 5));
        }

        [Test] public void Median_EvenPairs_MeanOfTheMiddleTwo()
        {
            var ts = Counter(0, 2, 10, 14, 20, 26, 30, 38);
            Assert.AreEqual(5, TimerOverhead.Estimate(ts, 4));
        }

        [Test] public void BackwardsCounter_CountsAsZero_BadArguments()
        {
            Assert.AreEqual(0, TimerOverhead.Estimate(Counter(10, 4), 1));
            Assert.Throws<ArgumentNullException>(() => TimerOverhead.Estimate(null, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimerOverhead.Estimate(() => 0, 0));
        }
    }
}
