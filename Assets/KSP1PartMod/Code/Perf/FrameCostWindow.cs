using System;
using System.Collections.Generic;

namespace KSP1PartMod.Perf
{
    /// <summary>The cost of one feature over a closed window, per frame.</summary>
    public sealed class FeatureCost
    {
        public PerfFeature Feature;
        public double AvgMs, P95Ms, MaxMs;
        /// <summary>The largest part and entry counts of one frame.</summary>
        public int Parts, Entries;
    }

    /// <summary>One closed window of frame costs (kept per scene for the perf report).</summary>
    public sealed class FrameWindowSummary
    {
        public string Scene;
        public double Seconds;
        public long Frames;
        public double Fps, FrameP95Ms;
        public double OursAvgMs, OursP95Ms, OursMaxMs;
        /// <summary>The estimated cost of the timestamps themselves, per frame.</summary>
        public double OverheadMs;
        /// <summary>The features that cost something, in <see cref="PerfFeature"/> order.</summary>
        public readonly List<FeatureCost> Features = new List<FeatureCost>();
    }

    /// <summary>Frame cost per feature (performance plan Task 5). Per frame: <see cref="Add"/>, <see cref="NotePart"/>,
    /// <see cref="NoteEntries"/>, then <see cref="EndFrame"/>, none of which allocates. A frame where a feature cost nothing
    /// counts as 0 for its average and p95 (the cost per frame of the scene). The p95 comes from a fixed log-scale histogram of
    /// <see cref="Buckets"/> buckets from 1 µs to 100 ms (factor ~1.13): it is the upper bound of its bucket, capped by the
    /// window's max, so it is at most ~13 % above the true value. Strings are built only when a window closes. Pure.</summary>
    public sealed class FrameCostWindow
    {
        public const int Buckets = 96;
        /// <summary>The bucket boundaries: 1 µs times 10^(5 k / 94), k = 0..94 (1 µs to 100 ms).</summary>
        public static readonly double[] BoundsMicroseconds = MakeBounds();
        public static readonly double BucketFactor = Math.Pow(10, 5.0 / (Buckets - 2));

        private static readonly int Features = PerfFeatures.Count;
        /// <summary>One whole-frame line, then at most one per feature.</summary>
        public static readonly int MaxLines = 1 + Features;

        private readonly long _frequency;
        private readonly long _windowTicks;
        private readonly long[] _boundTicks;

        // ---- the current frame
        private readonly long[] _frameTicks;
        private readonly int[] _frameParts, _frameEntries;
        private int _frameTimings;

        // ---- the window: per feature, then index Features = ours (all features of a frame)
        private readonly long[] _sum, _max;
        private readonly long[] _hist;
        private readonly int[] _maxParts, _maxEntries;
        private readonly long[] _frameHist = new long[Buckets];
        private long _frames, _frameTimeSum, _frameMax, _timings, _windowStart;
        private bool _started;

        private readonly string[] _lines = new string[MaxLines];

        /// <param name="frequency">Timestamp ticks per second (<c>Stopwatch.Frequency</c>).</param>
        /// <param name="windowSeconds">The window length (<c>perf_window_seconds</c>).</param>
        public FrameCostWindow(long frequency, double windowSeconds)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            if (!(windowSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _frequency = frequency;
            WindowSeconds = windowSeconds;
            _windowTicks = (long)Math.Round(windowSeconds * frequency);
            _boundTicks = new long[BoundsMicroseconds.Length];
            for (var i = 0; i < _boundTicks.Length; i++) _boundTicks[i] = (long)Math.Ceiling(BoundsMicroseconds[i] * frequency / 1e6);
            _frameTicks = new long[Features];
            _frameParts = new int[Features];
            _frameEntries = new int[Features];
            _sum = new long[Features + 1];
            _max = new long[Features + 1];
            _hist = new long[(Features + 1) * Buckets];
            _maxParts = new int[Features];
            _maxEntries = new int[Features];
        }

        public double WindowSeconds { get; }
        public long Frequency => _frequency;
        /// <summary>The median cost of one timestamp pair (<see cref="TimerOverhead.Estimate"/>), in ticks.</summary>
        public long TimerOverheadTicks { get; set; }
        /// <summary>Frames ended in the open window.</summary>
        public long Frames => _frames;

        public void Start(long now)
        {
            _windowStart = now;
            _started = true;
        }

        public void Add(PerfFeature feature, long ticks)
        {
            var i = (int)feature;
            if ((uint)i >= (uint)Features) return;
            if (ticks > 0) _frameTicks[i] += ticks;
            _frameTimings++;
        }

        public void NotePart(PerfFeature feature)
        {
            var i = (int)feature;
            if ((uint)i < (uint)Features) _frameParts[i]++;
        }

        public void NoteEntries(PerfFeature feature, int count)
        {
            var i = (int)feature;
            if ((uint)i < (uint)Features && count > 0) _frameEntries[i] += count;
        }

        /// <param name="frameTicks">The frame's own duration (for the fps and the frame p95).</param>
        public void EndFrame(long frameTicks)
        {
            long ours = 0;
            for (var f = 0; f < Features; f++)
            {
                var v = _frameTicks[f];
                ours += v;
                Sample(f, v);
                if (_frameParts[f] > _maxParts[f]) _maxParts[f] = _frameParts[f];
                if (_frameEntries[f] > _maxEntries[f]) _maxEntries[f] = _frameEntries[f];
                _frameTicks[f] = 0;
                _frameParts[f] = 0;
                _frameEntries[f] = 0;
            }
            Sample(Features, ours);
            if (frameTicks < 0) frameTicks = 0;
            _frameTimeSum += frameTicks;
            if (frameTicks > _frameMax) _frameMax = frameTicks;
            _frameHist[Bucket(frameTicks)]++;
            _timings += _frameTimings;
            _frameTimings = 0;
            _frames++;
        }

        private void Sample(int index, long v)
        {
            _sum[index] += v;
            if (v > _max[index]) _max[index] = v;
            _hist[index * Buckets + Bucket(v)]++;
        }

        /// <summary>Bucket 0 is below 1 µs (zero included), bucket k in 1..94 below bound k, bucket 95 at 100 ms or more.</summary>
        private int Bucket(long ticks)
        {
            int lo = 0, hi = _boundTicks.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (ticks < _boundTicks[mid]) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>True once the window has lasted <see cref="WindowSeconds"/> (or at once with <paramref name="force"/>, for
        /// a change of scene) and has at least one frame: <paramref name="lines"/> is the preallocated line array, its first
        /// <paramref name="count"/> entries filled, and <see cref="Last"/> the closed window. The next window begins at
        /// <paramref name="now"/>, empty. An empty window gives no line (it restarts when forced).</summary>
        public bool TryReport(long now, string scene, out string[] lines, out int count, bool force = false)
        {
            lines = _lines;
            count = 0;
            if (!_started)
            {
                Start(now);
                return false;
            }
            if (!force && now - _windowStart < _windowTicks) return false;
            if (_frames == 0)
            {
                if (force) Start(now);
                return false;
            }
            var summary = Summarize(now, scene);
            lines[count++] = PerfLines.FrameWindow(summary);
            foreach (var fc in summary.Features) lines[count++] = PerfLines.FrameFeature(summary.Scene, fc);
            for (var i = count; i < lines.Length; i++) lines[i] = null;
            Last = summary;
            Clear(now);
            return true;
        }

        /// <summary>The last closed window (null before the first).</summary>
        public FrameWindowSummary Last { get; private set; }

        private FrameWindowSummary Summarize(long now, string scene)
        {
            var s = new FrameWindowSummary
            {
                Scene = string.IsNullOrEmpty(scene) ? "other" : scene,
                Seconds = PhaseClock.Seconds(now - _windowStart, _frequency),
                Frames = _frames,
                Fps = _frameTimeSum > 0 ? _frames * (double)_frequency / _frameTimeSum : 0,
                FrameP95Ms = P95Ms(_frameHist, 0, _frameMax),
                OursAvgMs = AvgMs(Features),
                OursP95Ms = P95Ms(_hist, Features * Buckets, _max[Features]),
                OursMaxMs = PhaseClock.Ms(_max[Features], _frequency),
                OverheadMs = PhaseClock.Ms(TimerOverheadTicks, _frequency) * _timings / _frames,
            };
            for (var f = 0; f < Features; f++)
            {
                if (_sum[f] <= 0) continue;
                s.Features.Add(new FeatureCost
                {
                    Feature = (PerfFeature)f,
                    AvgMs = AvgMs(f),
                    P95Ms = P95Ms(_hist, f * Buckets, _max[f]),
                    MaxMs = PhaseClock.Ms(_max[f], _frequency),
                    Parts = _maxParts[f],
                    Entries = _maxEntries[f],
                });
            }
            return s;
        }

        private double AvgMs(int index) => PhaseClock.Ms(_sum[index], _frequency) / _frames;

        /// <summary>The upper bound of the bucket holding the 95th percentile, capped by the window's max.</summary>
        private double P95Ms(long[] hist, int offset, long max)
        {
            var rank = (long)Math.Ceiling(_frames * 0.95);
            long seen = 0;
            for (var b = 0; b < Buckets; b++)
            {
                seen += hist[offset + b];
                if (seen < rank) continue;
                var upper = b < _boundTicks.Length ? _boundTicks[b] : max;
                return PhaseClock.Ms(Math.Min(upper, max), _frequency);
            }
            return PhaseClock.Ms(max, _frequency);
        }

        private void Clear(long now)
        {
            Array.Clear(_sum, 0, _sum.Length);
            Array.Clear(_max, 0, _max.Length);
            Array.Clear(_hist, 0, _hist.Length);
            Array.Clear(_maxParts, 0, _maxParts.Length);
            Array.Clear(_maxEntries, 0, _maxEntries.Length);
            Array.Clear(_frameHist, 0, _frameHist.Length);
            _frames = _frameTimeSum = _frameMax = _timings = 0;
            Start(now);
        }

        private static double[] MakeBounds()
        {
            var bounds = new double[Buckets - 1];
            for (var k = 0; k < bounds.Length; k++) bounds[k] = Math.Pow(10, 5.0 * k / (Buckets - 2));
            return bounds;
        }
    }
}
