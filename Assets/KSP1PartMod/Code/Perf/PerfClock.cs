using System;
using System.Diagnostics;

namespace KSP1PartMod.Perf
{
    /// <summary>The session's load clock (0.13.0, plan Task 3): one <see cref="PhaseClock"/> fed by the load timeline's timing
    /// patches and by our own timed steps. Off (<see cref="Enabled"/> false, as with <c>perf_load_timeline</c> false or after
    /// a timing failure), every call returns at once and adds nothing. Thread-safe: the clock is lock-free, and the rebuild
    /// flag is per thread. Uses only <c>System.Diagnostics.Stopwatch</c>.</summary>
    public static class PerfClock
    {
        public static readonly PhaseClock Clock = new PhaseClock();

        private static volatile bool _enabled;

        /// <summary>Set by the load timeline when it is armed; cleared when it stops.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        [ThreadStatic] private static bool _inRebuild;

        /// <summary>True on this thread while Redux rebuilds a prefab after the import: the import phases (and our import-time
        /// steps that run again inside a rebuild) are then not added, so that a later VAB visit does not inflate the next
        /// import's figures. Only <see cref="PerfPhase.Rebuild"/> counts.</summary>
        public static bool InRebuild
        {
            get => _inRebuild;
            set => _inRebuild = value;
        }

        public static long Frequency => Stopwatch.Frequency;

        /// <summary>A start timestamp, or 0 when the clock is off (then <see cref="Stop"/> adds nothing).</summary>
        public static long Start() => _enabled ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>Adds the time since <paramref name="start"/> (from <see cref="Start"/>) to <paramref name="phase"/>.</summary>
        public static void Stop(PerfPhase phase, long start)
        {
            if (start == 0L) return;
            NoteTicks(phase, Stopwatch.GetTimestamp() - start);
        }

        /// <summary>Adds the elapsed time of an existing step's <paramref name="watch"/> to <paramref name="phase"/>.</summary>
        public static void Note(PerfPhase phase, Stopwatch watch)
        {
            if (watch == null) return;
            NoteTicks(phase, watch.ElapsedTicks);
        }

        /// <summary>Adds <paramref name="ticks"/> (timestamp ticks) to <paramref name="phase"/>, unless the clock is off or
        /// this thread is inside a rebuild (<see cref="PerfPhase.Rebuild"/> excepted).</summary>
        public static void NoteTicks(PerfPhase phase, long ticks)
        {
            if (!_enabled) return;
            if (_inRebuild && phase != PerfPhase.Rebuild) return;
            Clock.Add(phase, ticks);
        }

        /// <summary>Back to the state of a new session (tests).</summary>
        public static void ResetForTests()
        {
            _enabled = false;
            _inRebuild = false;
            Clock.Reset((PerfPhase[])Enum.GetValues(typeof(PerfPhase)));
        }
    }
}
