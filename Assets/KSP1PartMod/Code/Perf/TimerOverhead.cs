using System;

namespace KSP1PartMod.Perf
{
    /// <summary>What one timing costs: the median gap between two back-to-back timestamps, in ticks. Called once at arming
    /// (<c>Stopwatch.GetTimestamp</c>); the frame line multiplies it by the timings taken per frame, so that the measurement's
    /// own share of "ours" is visible. Pure.</summary>
    public static class TimerOverhead
    {
        public static long Estimate(Func<long> timestamp, int pairs)
        {
            if (timestamp == null) throw new ArgumentNullException(nameof(timestamp));
            if (pairs < 1) throw new ArgumentOutOfRangeException(nameof(pairs));
            var gaps = new long[pairs];
            for (var i = 0; i < pairs; i++)
            {
                var a = timestamp();
                var b = timestamp();
                gaps[i] = Math.Max(0, b - a);
            }
            Array.Sort(gaps);
            return pairs % 2 == 1 ? gaps[pairs / 2] : (gaps[pairs / 2 - 1] + gaps[pairs / 2]) / 2;
        }

        /// <summary>The mean cost of one timestamp over <paramref name="count"/> back-to-back calls, in fractional ticks. The
        /// median of <see cref="Estimate"/> is a whole number of ticks, 0 when a timestamp costs less than one tick (a 10 MHz
        /// counter: 100 ns); the frame cost multiplies this by thousands of timings per frame, so it needs the fraction.</summary>
        public static double Mean(Func<long> timestamp, int count)
        {
            if (timestamp == null) throw new ArgumentNullException(nameof(timestamp));
            if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
            var first = timestamp();
            var last = first;
            for (var i = 1; i < count; i++) last = timestamp();
            return Math.Max(0, last - first) / (double)(count - 1);
        }
    }
}
