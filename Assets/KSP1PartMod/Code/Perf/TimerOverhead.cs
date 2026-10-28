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
    }
}
