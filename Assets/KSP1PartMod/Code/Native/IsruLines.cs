using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    public enum MinableSource { ResourceMaps, NativeDrill, Fallback }

    /// <summary>Why a converted drill or scanner is always extended (no deploy state followed).</summary>
    public enum ExtendedReason { NoDeployClip, AnimationsOff, GatedByB9 }

    /// <summary>The ISRU log lines of the converters and drills plan, Tasks 2 to 4 (exact text). Numbers: G6, invariant.</summary>
    public static class IsruLines
    {
        public const string Prefix = NativeLines.Prefix;
        public const string RuntimePrefix = "[KSP1PartMod] isru: ";
        public const string AnimPrefix = "[KSP1PartMod] anim: ";

        private static string Inv(double v) => IsruCfg.Inv(v);

        private static string Flows(IEnumerable<IsruFlow> flows)
        {
            var list = (flows ?? Enumerable.Empty<IsruFlow>()).Select(f => f.Name + " " + Inv(f.Rate)).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        private static string Fields(IEnumerable<(string Field, int Parts)> fields)
        {
            var list = (fields ?? Enumerable.Empty<(string, int)>()).Select(f => f.Item1 + " " + f.Item2).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // ---- Task 2: ore remap and converters ----

        public static string OreImported(string resource) =>
            Prefix + $"ore: KSP1 Ore imported as {resource} (Redux's mapping: TestRocks, which has no resource map)";

        public static string OreNotAResource(string resource) =>
            Prefix + $"ore: {resource} is not a game resource, Ore stays TestRocks (KSP1 drills on Ore skipped)";

        public const string OreTableNotFound =
            Prefix + "ore: Redux's resource table not found, Ore stays TestRocks (KSP1 drills on Ore skipped)";

        /// <summary>KSP1's own Ore unit mass (t). S: KSP1's public wiki; the stock <c>ResourcesGeneric.cfg</c> is in neither
        /// install. Only for the log.</summary>
        public const double Ksp1OreTonnesPerUnit = 0.01;

        /// <summary>The target check waits for the game's resource database (not in the plan's list).</summary>
        public static string OreCheckDeferred(string resource) =>
            Prefix + $"ore: resource database not ready, the check of {resource} waits for the next importer registry or the first converter " +
            "(parts imported until then already hold it)";

        /// <summary>One mode of a converted part; <paramref name="mode"/> is its position in the native formula list. A mode
        /// that moves KSP1 Ore gets Redux's Ore unit mass (<paramref name="oreTonnesPerUnit"/>, t per KSP1 unit, null when
        /// unknown) against KSP1's: its mass in and out then differ by that ratio.</summary>
        public static string ConverterMode(string part, IsruFormula f, int mode, double? oreTonnesPerUnit = null) =>
            Prefix + $"{part}: converter '{f.LabelSource}' -> mode {mode}: in {Flows(f.Inputs)} out {Flows(f.Outputs)} " +
            $"(mass in {(f.MassInKnown ? Inv(f.MassIn) : "?")} t/s, out {(f.MassOutKnown ? Inv(f.MassOut) : "?")} t/s), " +
            $"heat {Inv(f.FluxGenerated)} kW" +
            (oreTonnesPerUnit is double ore && ore > 0
                ? $"; Ore weighs {Inv(ore)} t per KSP1 unit in Redux, {Inv(Ksp1OreTonnesPerUnit)} t in KSP1: Ore mass x{Inv(ore / Ksp1OreTonnesPerUnit)}"
                : "");

        public static string ConvertersSummary(int parts, int modes, int severalConverters, int dropped, IEnumerable<(string Field, int Parts)> notReproduced) =>
            Prefix + $"converters: {parts} part(s) got {ConverterMapping.NativeModule} with {modes} mode(s) " +
            $"({severalConverters} part(s) with several KSP1 converters, one runs at a time); modes dropped: {dropped} (unknown resource); " +
            $"not reproduced: {Fields(notReproduced)}";

        /// <summary>A converter that failed on a part: Redux's result, no converter at all on that part.</summary>
        public static string ConverterFailed(string error) =>
            "no Module_ResourceConverter on this part (every KSP1 converter of it dropped, as Redux 0.9.0 does): " + error;

        public static string ConverterNamesAdded(int terms) =>
            Prefix + $"converter names: {terms} term(s) added to Redux's part localization";

        public static string ConverterNamesNotRegistered(string member) =>
            Prefix + $"converter names not registered ({member} not found), raw keys shown";

        /// <summary>Not in the plan's list (the plan says "logged once"): the importers outside the game.</summary>
        public const string NoResourceDatabase =
            Prefix + "isru: no KSP2 resource database, every resource taken as known";

        // ---- Task 3: references, drills, scanners ----

        public static string Distance(double metres) =>
            metres < 0 ? "no bound" : metres >= 1000 ? Inv(metres / 1000) + " km" : Inv(metres) + " m";

        /// <summary>An altitude window; -1 (or below 0) is no bound.</summary>
        public static string Altitude(double min, double max) =>
            min < 0 && max < 0 ? "any" : Distance(min) + " to " + Distance(max);

        public static string Inclination(double min, double max) => Inv(min) + "° to " + Inv(max) + "°";

        private static string DrillSegment(NativeDrillRef d, Func<string, double> massPerUnit) =>
            $"{d.Part} {Inv(d.Ec)} EC/s, {Inv(d.HeatPerEc)} kW per EC/s, " +
            string.Join(", ", d.Outputs.Select(o =>
            {
                var m = massPerUnit?.Invoke(o.Name) ?? double.NaN;
                return m >= 0 && !double.IsInfinity(m) ? $"{o.Name} {Inv(o.Rate * m * 3600)} t/h" : $"{o.Name} {Inv(o.Rate)} units/s";
            }));

        private static string ScannerSegment(NativeScannerRef s) =>
            $"{s.Part} {Inv(s.Ec)} EC/s, {string.Join(", ", s.Resources)}, altitude {Altitude(s.MinAltitude, s.MaxAltitude)}, " +
            $"inclination {Inclination(s.MinInclination, s.MaxInclination)}";

        /// <summary>The reference lines: one <c>no KSP2 … found, fallback …</c> line per part that fell back, and the full
        /// line when at least one was read from the game (the fallback values then appear in it too).</summary>
        /// <param name="massPerUnit">KSP2 unit mass (t) of a resource, for the drill's t/h; null or unknown: units/s.</param>
        public static IReadOnlyList<string> IsruReference(NativeIsruRef r, Func<string, double> massPerUnit)
        {
            r = r ?? NativeIsruRef.Fallback;
            var lines = new List<string>();
            if (r.Drill.IsFallback) lines.Add(Prefix + "isru reference: no KSP2 drill found, fallback " + DrillSegment(r.Drill, massPerUnit));
            if (r.ConvertersAreFallback) lines.Add(Prefix + $"isru reference: no KSP2 converter found, fallback median {Inv(r.ConverterHeatPerEc)} kW per EC/s");
            if (r.Scanner.IsFallback) lines.Add(Prefix + "isru reference: no KSP2 scanner found, fallback " + ScannerSegment(r.Scanner));
            if (!r.Drill.IsFallback || !r.ConvertersAreFallback || !r.Scanner.IsFallback)
                lines.Add(Prefix + $"isru reference: drill {DrillSegment(r.Drill, massPerUnit)}; " +
                          $"converters {r.ConverterModes} mode(s), median {Inv(r.ConverterHeatPerEc)} kW per EC/s; " +
                          $"scanner {ScannerSegment(r.Scanner)}");
            return lines;
        }

        private static string SourceText(MinableSource s) =>
            s == MinableSource.ResourceMaps ? "resource maps" : s == MinableSource.NativeDrill ? "the KSP2 drill" : "fallback";

        /// <summary>From the maps: one line per resource (the list, then that resource's bodies). Otherwise one line.</summary>
        public static IReadOnlyList<string> Minable(IEnumerable<string> resources, MinableSource source,
            IEnumerable<(string Resource, IReadOnlyList<string> Bodies)> maps)
        {
            var head = Prefix + $"minable resources: {string.Join(", ", resources ?? Enumerable.Empty<string>())} (from {SourceText(source)})";
            var perResource = (maps ?? Enumerable.Empty<(string, IReadOnlyList<string>)>()).ToList();
            if (source != MinableSource.ResourceMaps || perResource.Count == 0) return new[] { head };
            return perResource.Select(m =>
            {
                var bodies = m.Item2 ?? Array.Empty<string>();
                return head + $"; {m.Item1} map on {bodies.Count} body/bodies: {string.Join(", ", bodies)}";
            }).ToList();
        }

        /// <summary>One harvested resource of a converted drill, compared with the native drill.</summary>
        public static string Drill(string part, HarvesterMapping m, HarvestOutput o, NativeDrillRef native)
        {
            native = native ?? NativeIsruRef.FallbackDrill;
            var transform = string.IsNullOrEmpty(m.ImpactTransform) ? "(none)" : m.ImpactTransform;
            return Prefix + $"{part}: drill: {o.Ksp1Name} -> {o.Name} at {Inv(o.Rate)} units/s at density 1 " +
                   $"(Efficiency {Inv(o.Efficiency)} x abundance {Inv(m.Abundance)}), EC {Inv(m.Formula.EcInput)}/s, " +
                   $"heat {Inv(m.Formula.FluxGenerated)} kW, impact '{transform}' {Inv(m.ImpactRange)} m; " +
                   $"KSP2 drill: {Inv(native.RateOf(o.Name))} units/s for {Inv(native.Ec)} EC/s";
        }

        public static string DrillsSummary(int parts, IEnumerable<string> resources, int ocean, int atmospheric, int exospheric, int noMap,
            int asteroidParts, IEnumerable<(string Field, int Parts)> notReproduced) =>
            Prefix + $"drills: {parts} part(s) got {HarvesterMapping.NativeModule} ({string.Join(", ", resources ?? Enumerable.Empty<string>())}); " +
            $"skipped: {ocean} ocean, {atmospheric} atmospheric, {exospheric} exospheric, {noMap} resource without a map; " +
            $"{HarvesterMapping.AsteroidDrillModule}/{HarvesterMapping.CometDrillModule} ignored on {asteroidParts} part(s) (no asteroids or comets in KSP2); " +
            $"not reproduced: {Fields(notReproduced)}";

        public static string Scanner(string part, ScannerMapping s) =>
            Prefix + $"{part}: scanner: {(s.Kind == ScannerKind.Surveyor ? "surveyor" : "resource scanner")} -> {ScannerMapping.NativeModule}: " +
            $"{string.Join(", ", s.ScannableResources)}, {Inv(s.RequiredResources.Where(f => IsruResources.IsElectricCharge(f.Name)).Sum(f => f.Rate))} EC/s, " +
            $"{Inv(s.TimeToComplete)} s, altitude {Altitude(s.MinAltitude, s.MaxAltitude)}, inclination {Inclination(s.MinInclination, s.MaxInclination)}";

        /// <summary>Not in the plan's list (ruling: the abundance is a guess, so its value is logged once, at the first drill).</summary>
        public static string HarvestAbundance(double abundance, bool isDefault) =>
            Prefix + $"drills: a density of 1 counts as a KSP1 abundance of {Inv(abundance)} " +
            $"(harvest_abundance_at_full_density{(isDefault ? ", default" : "")}; a guess, to calibrate in game)";

        /// <summary>A part that already holds a <c>Data_Mine</c> (a future Redux importer, or a rebuild): nothing added.</summary>
        public const string ExistingMineNote = HarvesterMapping.Ksp1Module + ": the part already has a Data_Mine, ours not added";

        /// <summary>A part that already holds a <c>Data_ResourceScanner</c>: nothing added.</summary>
        public const string ExistingScannerNote = "the part already has a Data_ResourceScanner, ours not added";

        /// <summary>A part with both KSP1 scanners: one native scanner per part, the surveyor's.</summary>
        public const string SurveyorWinsNote = ScannerMapping.ResourceScannerModule + ": the part also has a " +
                                               ScannerMapping.SurveyorModule + ", whose " + ScannerMapping.NativeModule + " wins (one per part)";

        public static string OrbitalScannerFolded(int count) =>
            $"{ScannerMapping.OrbitalScannerModule} x{count} folded into the surveyor's {ScannerMapping.NativeModule} (no importer of ours)";

        public static string AsteroidDrillIgnored(int count) =>
            $"{HarvesterMapping.AsteroidDrillModule}/{HarvesterMapping.CometDrillModule} x{count} ignored (no asteroids or comets in KSP2)";

        public static string ScannersSummary(int surveyors, int resourceScanners, int leftOut, int noScannable) =>
            Prefix + $"scanners: {surveyors} survey scanner(s) and {resourceScanners} resource scanner(s) got {ScannerMapping.NativeModule}; " +
            $"{leftOut} {ScannerMapping.ResourceScannerModule} left out (RequiresUnlock: KSP1 reads local abundance after a survey, KSP2 has no part readout), " +
            $"{noScannable} with no scannable resource";

        // ---- Task 4: runtime ----

        private static string OnOff(bool b) => b ? "on" : "off";

        public static string Armed(bool impactTransforms, bool deployBridge, bool activeClips) =>
            RuntimePrefix + $"armed: impact transforms {OnOff(impactTransforms)}, deploy bridge {OnOff(deployBridge)}, active clips {OnOff(activeClips)}";

        public static string MemberNotFound(string member, string feature) =>
            RuntimePrefix + $"{member} not found, {feature} off (Redux's module left as is)";

        public static string DrillProbeFound(string part, string transform, double range) =>
            RuntimePrefix + $"{part}: drill probe '{transform}' found, {Inv(range)} m along +Z";

        public static string DrillProbeNotFound(string part, string transform) =>
            RuntimePrefix + $"{part}: drill probe '{transform}' not found on the part, the drill will report TooHigh";

        public static string DeployFollows(string part, string nativeModule, string entryKey) =>
            RuntimePrefix + $"{part}: {nativeModule} deploy follows {entryKey}";

        public static string AlwaysExtended(string part, string nativeModule, ExtendedReason reason) =>
            RuntimePrefix + $"{part}: {nativeModule} always extended (" +
            (reason == ExtendedReason.NoDeployClip ? "no deploy clip" : reason == ExtendedReason.AnimationsOff ? "animations off" : "entry gated by B9") + ")";

        public static string Drilling(string part, string body, string resource, double unitsPerSecond, double density, double groundMetres, double ec) =>
            RuntimePrefix + $"{part}: drilling on {body}: {resource} {Inv(unitsPerSecond)} units/s (density {Inv(density)}), " +
            $"ground at {Inv(groundMetres)} m, EC {Inv(ec)}/s";

        public static string ActiveClipFollows(string part, string entryKey, string nativeModule) =>
            AnimPrefix + $"{part} group '{entryKey}' active clip follows {nativeModule} running";

        public static string PatchFailed(string error) => RuntimePrefix + "patch failed, stopped for this session: " + error;
    }
}
