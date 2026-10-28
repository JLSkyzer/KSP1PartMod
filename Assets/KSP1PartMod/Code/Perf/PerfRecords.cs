using System;
using System.Collections.Generic;

namespace KSP1PartMod.Perf
{
    /// <summary>Process memory at one moment, in bytes. A null value is one the API did not give ("n/a").</summary>
    public sealed class MemorySnapshot
    {
        public string Moment;
        public long? Allocated, Reserved, MonoUsed, MonoHeap, WorkingSet, PrivateBytes;
        public long? TextureCurrent, TextureDesired, TextureNonStreaming, TextureTotal;
        /// <summary>The texture streaming budget, in MB.</summary>
        public double? BudgetMb;
        /// <summary>The live KSP1 textures of the census, in bytes.</summary>
        public long? Ksp1Live;
        public long? SystemRamMb;
    }

    /// <summary>A game flow step, as the game timed it (<c>[System] &lt;step&gt; completed in &lt;s&gt;s.</c>).</summary>
    public sealed class FlowStep
    {
        public string Name;
        public double Seconds;
    }

    /// <summary>The session facts of the report header.</summary>
    public sealed class PerfSessionHeader
    {
        public string Started, ModVersion, GameVersion, UnityVersion, Card;
        public long? CardMb, SystemRamMb;
        /// <summary>The time from game start to the first main menu, in seconds.</summary>
        public double? MainMenuAfterSeconds;
        /// <summary>The campaign loads of the session ("&lt;s&gt; s to &lt;state&gt;").</summary>
        public readonly List<string> CampaignLoads = new List<string>();
    }

    /// <summary>One KSP1 import, from the <see cref="PhaseClock"/> at its end (or a cached reload: <see cref="Reused"/>).</summary>
    public sealed class ImportSummary
    {
        public bool Reused;
        public int Parts;
        public long Frames;
        public double WallS;
        public double TranslateS, ReduxImportersS, OursS, PrefabS, MuReadS, TexturesS, IconsS;
        public long OursCalls, Models, TextureLoads;
        public double LocalizationMs, ReleaseMs;
        /// <summary>The worker phases.</summary>
        public double CacheKeyMs, CfgReadMs, MmFinalizeMs, TankCatalogMs, PlumeCatalogMs, LocalizationCatalogMs;
        public long CacheKeyCalls;
        public int CfgFiles;
        public long ReportLines, Warnings;
        public double ReportLinesMs;
        public SlowPart[] Slowest = new SlowPart[0];
        public long Frequency;

        /// <summary>Translate less Redux's importers and the prefab: the rest of a part's translation.</summary>
        public double OtherS => Math.Max(0, TranslateS - ReduxImportersS - PrefabS);

        /// <summary>Wall time less the main-thread phases: the frames between parts, the worker wait and the loading screen.</summary>
        public double OutsideS => Math.Max(0, WallS - TranslateS - IconsS - LocalizationMs / 1000 - ReleaseMs / 1000);

        public static ImportSummary From(PhaseTotals[] p, long frequency, int parts, long wallTicks, long frames, int cfgFiles)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            double S(PerfPhase x) => PhaseClock.Seconds(p[(int)x].Ticks, frequency);
            double Ms(PerfPhase x) => PhaseClock.Ms(p[(int)x].Ticks, frequency);
            return new ImportSummary
            {
                Parts = parts,
                Frames = frames,
                WallS = PhaseClock.Seconds(wallTicks, frequency),
                TranslateS = S(PerfPhase.Translate),
                ReduxImportersS = S(PerfPhase.ReduxImporters),
                OursS = S(PerfPhase.OurImporters),
                OursCalls = p[(int)PerfPhase.OurImporters].Calls,
                PrefabS = S(PerfPhase.Prefab),
                MuReadS = S(PerfPhase.MuRead),
                Models = p[(int)PerfPhase.MuRead].Calls,
                TexturesS = S(PerfPhase.Textures),
                TextureLoads = p[(int)PerfPhase.Textures].Calls,
                IconsS = S(PerfPhase.Icons),
                LocalizationMs = Ms(PerfPhase.LocalizationRegister),
                ReleaseMs = Ms(PerfPhase.Release),
                CacheKeyMs = Ms(PerfPhase.CacheKey),
                CacheKeyCalls = p[(int)PerfPhase.CacheKey].Calls,
                CfgReadMs = Ms(PerfPhase.CfgRead),
                CfgFiles = cfgFiles,
                MmFinalizeMs = Ms(PerfPhase.MmFinalize),
                TankCatalogMs = Ms(PerfPhase.TankCatalog),
                PlumeCatalogMs = Ms(PerfPhase.PlumeCatalog),
                LocalizationCatalogMs = Ms(PerfPhase.LocalizationCatalog),
                ReportLines = p[(int)PerfPhase.ReportLines].Calls,
                ReportLinesMs = Ms(PerfPhase.ReportLines),
                Warnings = p[(int)PerfPhase.ReportWarnings].Calls,
                Frequency = frequency,
            };
        }
    }

    /// <summary>Our load steps, in ms; null for a step that did not run ("—").</summary>
    public sealed class OurSteps
    {
        public double? MuFixMs, ShroudImportMs, B9Ms, NativeImportersMs, ScienceScanMs, TracksReadMs, AnimationStepMs, PlacementMs;

        /// <summary>A phase with no call is a step that did not run.</summary>
        public static OurSteps From(PhaseTotals[] p, long frequency)
        {
            double? Ms(PerfPhase x) => p[(int)x].Calls > 0 ? PhaseClock.Ms(p[(int)x].Ticks, frequency) : (double?)null;
            return new OurSteps
            {
                MuFixMs = Ms(PerfPhase.MuFix),
                ShroudImportMs = Ms(PerfPhase.ShroudImport),
                B9Ms = Ms(PerfPhase.B9Postfixes),
                NativeImportersMs = Ms(PerfPhase.OurImporters),
                ScienceScanMs = Ms(PerfPhase.ScienceScan),
                TracksReadMs = Ms(PerfPhase.TracksRead),
                AnimationStepMs = Ms(PerfPhase.AnimationStep),
                PlacementMs = Ms(PerfPhase.Placement),
            };
        }
    }

    /// <summary>The prefab rebuilds between two memory moments.</summary>
    public sealed class RebuildRecord
    {
        public string Since, Until;
        public long Count;
        public double Ms, TexturesMs;
        public long TextureLoads;
        public SlowPart[] Slowest = new SlowPart[0];
        public long Frequency;
    }
}
