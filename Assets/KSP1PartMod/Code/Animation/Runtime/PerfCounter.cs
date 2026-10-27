using System;
using System.Globalization;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The opt-in cost counter of the KSP1 animations (spec "Runtime cost budget", with <c>diagnostics</c>):
    /// one <see cref="Add"/> per frame with the timestamp ticks spent in every <c>Drive</c> and the flush, and one line
    /// every 60 s: <c>[KSP1PartMod] anim: perf: &lt;n&gt; module(s), &lt;e&gt; entr(ies), &lt;w&gt; write(s), &lt;ms&gt; ms
    /// over 60 s</c>. Time and writes add up over the window; modules and entries are the largest frame of it.
    /// <see cref="Add"/> and a <see cref="TryReport"/> that does not report allocate nothing.</summary>
    public sealed class PerfCounter
    {
        private const int WindowSeconds = 60;

        private readonly long _ticksPerSecond;
        private long _windowStart, _ticks, _writes;
        private int _modules, _entries;

        /// <param name="ticksPerSecond">The timestamp frequency (<c>Stopwatch.Frequency</c>).</param>
        public PerfCounter(long ticksPerSecond)
        {
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            _ticksPerSecond = ticksPerSecond;
        }

        /// <summary>The first window starts at <paramref name="now"/> (0 until called).</summary>
        public void Start(long now) => _windowStart = now;

        public void Add(long ticks, int modules, int entries, int writes)
        {
            _ticks += ticks;
            _writes += writes;
            if (modules > _modules) _modules = modules;
            if (entries > _entries) _entries = entries;
        }

        /// <summary>True, with the line, once 60 s have passed since the window began; the next window then begins at
        /// <paramref name="now"/>, empty.</summary>
        public bool TryReport(long now, out string line)
        {
            line = null;
            if (now - _windowStart < WindowSeconds * _ticksPerSecond) return false;
            var ms = _ticks * 1000.0 / _ticksPerSecond;
            line = "[KSP1PartMod] anim: perf: " + _modules + " module(s), " + _entries + " entr(ies), " + _writes +
                   " write(s), " + ms.ToString("0.###", CultureInfo.InvariantCulture) + " ms over " + WindowSeconds + " s";
            _windowStart = now;
            _ticks = _writes = 0;
            _modules = _entries = 0;
            return true;
        }
    }
}
