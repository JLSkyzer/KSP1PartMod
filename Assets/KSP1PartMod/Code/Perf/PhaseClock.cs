using System;
using System.Collections.Generic;
using System.Threading;

namespace KSP1PartMod.Perf
{
    /// <summary>The load phases we time (performance plan, research (b), plus our own steps). Phases nest: <see cref="MuRead"/>
    /// and <see cref="Textures"/> sit inside <see cref="Prefab"/>, which sits inside <see cref="Translate"/>; each phase is the
    /// sum of its own calls. Values are only ever appended.</summary>
    public enum PerfPhase
    {
        // ---- Redux's KSP1 import, worker thread
        CacheKey, ConfigGet, CfgRead, MmFinalize, TankCatalog, PlumeCatalog, LocalizationCatalog,
        // ---- Redux's KSP1 import, main thread
        ImportWall, Translate, ReduxImporters, Prefab, MuRead, Textures, LocalizationRegister, Icons, Release, Rebuild,
        // ---- both threads (ReportWarnings is the Warn share of ReportLines)
        ReportLines, ReportWarnings,
        // ---- ours
        MuFix, ShroudImport, B9Postfixes, OurImporters, ScienceScan, TracksRead, AnimationStep, Placement,
    }

    /// <summary>The totals of one phase: the ticks of all its calls, the call count and the longest call.</summary>
    public struct PhaseTotals
    {
        public long Ticks;
        public long Calls;
        public long MaxTicks;
    }

    /// <summary>Thread-safe accumulators, one per <see cref="PerfPhase"/>: Redux's worker phases add from a <c>Task.Run</c>
    /// thread while the main thread adds its own. <see cref="Add"/> is lock-free (<c>Interlocked</c>) and allocates nothing.
    /// Time is passed in as timestamp ticks, never read here. Pure.</summary>
    public sealed class PhaseClock
    {
        public static readonly int PhaseCount = Enum.GetValues(typeof(PerfPhase)).Length;

        private readonly long[] _ticks = new long[PhaseCount];
        private readonly long[] _calls = new long[PhaseCount];
        private readonly long[] _max = new long[PhaseCount];

        public void Add(PerfPhase phase, long ticks)
        {
            var i = (int)phase;
            if ((uint)i >= (uint)PhaseCount) return;
            if (ticks < 0) ticks = 0;
            Interlocked.Add(ref _ticks[i], ticks);
            Interlocked.Increment(ref _calls[i]);
            var seen = Interlocked.Read(ref _max[i]);
            while (ticks > seen)
            {
                var was = Interlocked.CompareExchange(ref _max[i], ticks, seen);
                if (was == seen) break;
                seen = was;
            }
        }

        public PhaseTotals Get(PerfPhase phase)
        {
            var i = (int)phase;
            if ((uint)i >= (uint)PhaseCount) return default;
            return new PhaseTotals
            {
                Ticks = Interlocked.Read(ref _ticks[i]),
                Calls = Interlocked.Read(ref _calls[i]),
                MaxTicks = Interlocked.Read(ref _max[i]),
            };
        }

        /// <summary>A copy of every phase, indexed by <c>(int)phase</c>.</summary>
        public PhaseTotals[] Snapshot()
        {
            var copy = new PhaseTotals[PhaseCount];
            for (var i = 0; i < PhaseCount; i++) copy[i] = Get((PerfPhase)i);
            return copy;
        }

        /// <summary>Zeroes the given phases only (the others keep counting).</summary>
        public void Reset(IEnumerable<PerfPhase> phases)
        {
            if (phases == null) return;
            foreach (var p in phases)
            {
                var i = (int)p;
                if ((uint)i >= (uint)PhaseCount) continue;
                Interlocked.Exchange(ref _ticks[i], 0);
                Interlocked.Exchange(ref _calls[i], 0);
                Interlocked.Exchange(ref _max[i], 0);
            }
        }

        /// <summary>The phases of one KSP1 import (reset once its lines are written): Redux's, without the rebuilds.</summary>
        public static readonly IReadOnlyList<PerfPhase> ImportPhases = new[]
        {
            PerfPhase.CacheKey, PerfPhase.ConfigGet, PerfPhase.CfgRead, PerfPhase.MmFinalize, PerfPhase.TankCatalog,
            PerfPhase.PlumeCatalog, PerfPhase.LocalizationCatalog, PerfPhase.ImportWall, PerfPhase.Translate,
            PerfPhase.ReduxImporters, PerfPhase.Prefab, PerfPhase.MuRead, PerfPhase.Textures, PerfPhase.LocalizationRegister,
            PerfPhase.Icons, PerfPhase.Release, PerfPhase.ReportLines, PerfPhase.ReportWarnings,
        };

        /// <summary>Our own steps (reset once the <c>perf: ours:</c> line is written, at the end of the animation step, after
        /// the import lines that also read <see cref="PerfPhase.OurImporters"/>).</summary>
        public static readonly IReadOnlyList<PerfPhase> OurPhases = new[]
        {
            PerfPhase.MuFix, PerfPhase.ShroudImport, PerfPhase.B9Postfixes, PerfPhase.OurImporters, PerfPhase.ScienceScan,
            PerfPhase.TracksRead, PerfPhase.AnimationStep, PerfPhase.Placement,
        };

        /// <summary>Milliseconds of <paramref name="ticks"/> at <paramref name="frequency"/> ticks per second (0 when the
        /// frequency is not positive).</summary>
        public static double Ms(long ticks, long frequency) => frequency <= 0 ? 0 : ticks * 1000.0 / frequency;

        public static double Seconds(long ticks, long frequency) => frequency <= 0 ? 0 : (double)ticks / frequency;
    }
}
