using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP1PartMod.Animation.Textures;

namespace KSP1PartMod.Perf
{
    /// <summary>The exact log lines of the performance tools (0.13.0, plan Tasks 3 to 5). Numbers use the invariant culture:
    /// seconds <c>0.0#</c>, load milliseconds <c>0.#</c>, frame milliseconds <c>0.###</c>, MB <c>0</c> (a census bin
    /// <c>0.#</c>). A value the game did not give prints <c>n/a</c>; a step that did not run prints <c>—</c>.</summary>
    public static class PerfLines
    {
        public const string Prefix = "[KSP1PartMod] perf: ";
        public const string TexturesPrefix = "[KSP1PartMod] textures: ";
        public const string NotRun = "—";
        public const string NotAvailable = "n/a";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string S(double seconds) => seconds.ToString("0.0#", Inv);
        public static string Ms(double ms) => ms.ToString("0.#", Inv);
        public static string FrameMs(double ms) => ms.ToString("0.###", Inv);
        public static string Mb(long bytes) => TextureMemory.Mb(bytes);
        public static string BinMb(long bytes) => (bytes / TextureMemory.BytesPerMb).ToString("0.#", Inv);
        private static string OnOff(bool on) => on ? "on" : "off";
        private static string Opt(double? ms) => ms.HasValue ? Ms(ms.Value) + " ms" : NotRun;
        private static string OptMb(long? bytes) => bytes.HasValue ? Mb(bytes.Value) + " MB" : NotAvailable;

        // ---- Task 3: load timeline

        public static string Armed(bool loadTimeline, int timed, int total, IReadOnlyCollection<string> notFound, bool memoryLines,
            bool reportFile, bool frameCost, double windowSeconds, double overheadNs)
        {
            var missing = notFound != null && notFound.Count > 0 ? "; not found: " + string.Join(", ", notFound) : "";
            return Prefix + $"armed: load timeline {OnOff(loadTimeline)} ({timed} of {total} Redux phase(s) timed{missing}), " +
                   $"memory lines {OnOff(memoryLines)}, report file {OnOff(reportFile)}, frame cost {OnOff(frameCost)} " +
                   $"(window {windowSeconds.ToString("0.#", Inv)} s); timer overhead ~{overheadNs.ToString("0", Inv)} ns per timing";
        }

        public static string MemberNotFound(string member, PerfPhase phase) => MemberNotFound(member, phase.ToString());

        /// <summary>The same line for what is not a <see cref="PerfPhase"/> (flow steps, rebuild part names).</summary>
        public static string MemberNotFound(string member, string phase) =>
            Prefix + $"load: {member} not found, phase '{phase}' not timed";

        public static string ImportWorker(ImportSummary s) =>
            Prefix + $"import: worker: cache key {Ms(s.CacheKeyMs)} ms over {s.CacheKeyCalls} call(s), cfg read {Ms(s.CfgReadMs)} ms " +
            $"({s.CfgFiles} file(s)), MM finalize {Ms(s.MmFinalizeMs)} ms, tank catalog {Ms(s.TankCatalogMs)} ms, plume catalog " +
            $"{Ms(s.PlumeCatalogMs)} ms, localization catalog {Ms(s.LocalizationCatalogMs)} ms";

        public static string Import(ImportSummary s) =>
            Prefix + $"import: {s.Parts} part(s) in {S(s.WallS)} s wall over {s.Frames} frame(s): translate {S(s.TranslateS)} s " +
            $"(Redux importers {S(s.ReduxImportersS)} s, of which ours {S(s.OursS)} s over {s.OursCalls} call(s); prefab {S(s.PrefabS)} s, " +
            $"of which .mu read {S(s.MuReadS)} s over {s.Models} model(s) and textures {S(s.TexturesS)} s over {s.TextureLoads} load(s); " +
            $"other {S(s.OtherS)} s), localization {Ms(s.LocalizationMs)} ms, icons {S(s.IconsS)} s, release {Ms(s.ReleaseMs)} ms; " +
            $"outside our timings (frames, worker wait, loading screen) {S(s.OutsideS)} s";

        public static string ReportLines(ImportSummary s) =>
            Prefix + $"import: report lines: {s.ReportLines} in {Ms(s.ReportLinesMs)} ms ({s.Warnings} warning(s))";

        public static string SlowestImportParts(IEnumerable<SlowPart> parts, long frequency) =>
            Prefix + "import: slowest parts: " + Join(parts, p =>
                $"{p.Name} {Ms(PhaseClock.Ms(p.TotalTicks, frequency))} ms (prefab {Ms(PhaseClock.Ms(p.PrefabTicks, frequency))}, " +
                $"textures {Ms(PhaseClock.Ms(p.TextureTicks, frequency))})");

        public static string Reused(int parts, double ms) =>
            Prefix + $"import: reused {parts} cached part(s) in {Ms(ms)} ms; no import phase to time";

        public static string Ours(OurSteps o) =>
            Prefix + $"ours: mu fix {Opt(o.MuFixMs)}, shroud import {Opt(o.ShroudImportMs)}, B9 {Opt(o.B9Ms)}, native importers " +
            $"{Opt(o.NativeImportersMs)}, science scan {Opt(o.ScienceScanMs)}, tracks read {Opt(o.TracksReadMs)} (worker), " +
            $"animation step {Opt(o.AnimationStepMs)}, placement {Opt(o.PlacementMs)}";

        public static string Rebuilds(RebuildRecord r) =>
            Prefix + $"rebuilds since {r.Since}: {r.Count} prefab rebuild(s) in {Ms(r.Ms)} ms (textures {Ms(r.TexturesMs)} ms over " +
            $"{r.TextureLoads} load(s)); slowest: " +
            Join(r.Slowest, p => $"{p.Name} {Ms(PhaseClock.Ms(p.TotalTicks, r.Frequency))} ms");

        public static string CampaignLoad(double seconds, string state) =>
            Prefix + $"campaign load: {S(seconds)} s from Loading to {state}";

        public static string GameStart(double seconds) => Prefix + $"game start: main menu after {S(seconds)} s";

        public static string LoadStopped(string exception) => Prefix + "load: stopped for this session: " + exception;

        // ---- Task 4: memory, census, report file

        public static string Memory(MemorySnapshot m) =>
            Prefix + $"memory at {m.Moment}: allocated {OptMb(m.Allocated)}, reserved {OptMb(m.Reserved)}, mono " +
            $"{(m.MonoUsed.HasValue ? Mb(m.MonoUsed.Value) : NotAvailable)}/{OptMb(m.MonoHeap)}, process working set " +
            $"{OptMb(m.WorkingSet)}, private {OptMb(m.PrivateBytes)}; system RAM " +
            $"{(m.SystemRamMb.HasValue ? m.SystemRamMb.Value.ToString(Inv) + " MB" : NotAvailable)}";

        private static string Bin(CensusBin b) => $"{b.Count} ({BinMb(b.Bytes)})";

        public static string Census(TextureCensus c)
        {
            var sizes = string.Join(", ", TextureCensus.SizeLabels.Select((l, i) => $"{l} {Bin(c.BySize[i])}"));
            var formats = c.All.Count == 0 ? "none" : string.Join(", ", c.ByFormat.Select(kv => $"{kv.Key} {Bin(kv.Value)}"));
            return TexturesPrefix + $"census at {c.When}: {c.All.Count} live KSP1 texture(s) ~{Mb(c.All.Bytes)} MB; by format {formats}; " +
                   $"by size {sizes}; without mipmaps {Bin(c.WithoutMipmaps)}; uncompressed {Bin(c.Uncompressed)}; not a power of two " +
                   $"{Bin(c.NotPowerOfTwo)}; readable {c.Readable.Count} ({BinMb(c.Readable.Bytes)} MB of RAM); census {Ms(c.CensusMs)} ms";
        }

        public static string Largest(TextureCensus c) =>
            TexturesPrefix + $"largest at {c.When}: " + Join(c.Largest, t => $"{t.Name} {t.Width}x{t.Height} {t.Format} {BinMb(t.Bytes)} MB");

        public static string ReportWritten(string path, string when) => Prefix + $"report written to {path} ({when})";

        public static string ReportOff(string error) => Prefix + "report not written, off for this session: " + error;

        // ---- Task 5: frame cost

        public static string FrameWindow(FrameWindowSummary w) =>
            Prefix + $"frame: {w.Scene} over {S(w.Seconds)} s, {w.Frames} frame(s), {w.Fps.ToString("0.#", Inv)} fps (frame p95 " +
            $"{FrameMs(w.FrameP95Ms)} ms); ours {FrameMs(w.OursAvgMs)} ms avg, {FrameMs(w.OursP95Ms)} ms p95, {FrameMs(w.OursMaxMs)} ms max " +
            $"per frame; timing overhead ~{FrameMs(w.OverheadMs)} ms per frame";

        public static string FrameFeature(string scene, FeatureCost f) =>
            Prefix + $"frame: {scene}: {PerfFeatures.Label(f.Feature)} {FrameMs(f.AvgMs)} ms avg, {FrameMs(f.P95Ms)} ms p95, " +
            $"{FrameMs(f.MaxMs)} ms max per frame" + (PerfFeatures.HasCounts(f.Feature) ? $"; {f.Parts} part(s), {f.Entries} entr(ies)" : "");

        private static string Join<T>(IEnumerable<T> items, System.Func<T, string> one)
        {
            var list = items?.Select(one).ToList();
            return list == null || list.Count == 0 ? "none" : string.Join(", ", list);
        }
    }
}
