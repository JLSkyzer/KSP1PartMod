using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Science
{
    /// <summary>The log lines of the science plan, Tasks 3 to 5 (exact text). Numbers: G6, invariant.</summary>
    public static class ScienceLines
    {
        public const string Prefix = "[KSP1PartMod] science: ";
        public const string NativePrefix = NativeLines.Prefix;

        private static string Inv(double v) => IsruCfg.Inv(v);
        private static string OnOff(bool on) => on ? "on" : "off";

        private static string Counts(IEnumerable<(string Name, int Count)> items)
        {
            var list = (items ?? Enumerable.Empty<(string, int)>()).Select(i => i.Item1 + " " + i.Item2).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        public static string TypeName(ScienceExperimentKind k) => k == ScienceExperimentKind.Both ? "Both" : k == ScienceExperimentKind.Sample ? "Sample" : "Data";

        public static string KindName(ScienceResolutionKind k) =>
            k == ScienceResolutionKind.Generated ? "generated" : k == ScienceResolutionKind.Identical ? "identical" : k == ScienceResolutionKind.Closest ? "closest" : "skipped";

        public static string SkipText(ScienceSkipReason r) =>
            r == ScienceSkipReason.EvaKerbal ? "EVA kerbal experiment"
            : r == ScienceSkipReason.NoDefinitionNoCounterpart ? "no definition and no counterpart"
            : "no KSP2 counterpart";

        public static string NoClipText(ScienceNoClip r)
        {
            switch (r)
            {
                case ScienceNoClip.NoFxModules: return "no FxModules";
                case ScienceNoClip.GatedByB9: return "entry gated by B9";
                case ScienceNoClip.AnimationsOff: return "animations off";
                case ScienceNoClip.NoExperimentAnimation: return "experimentAnimation false";
                default: return "no entry";
            }
        }

        // ---- Task 3: the definitions

        public static string DefinitionsRead(int definitions, int files, int cfgFiles, long ms, int mmEdits) =>
            Prefix + $"definitions: read {definitions} EXPERIMENT_DEFINITION(s) from {files} of {cfgFiles} cfg file(s) under mods/KSP1 in {ms} ms; " +
            $"{mmEdits} MM edit(s) of EXPERIMENT_DEFINITION not applied";

        public static string Definition(ScienceDefinitionModel d) =>
            Prefix + $"definition {d.ExperimentId} ({d.Title}): {TypeName(d.Type)} data {Inv(d.DataValue)} sample {Inv(d.SampleValue)} " +
            $"(baseValue {Inv(d.BaseValue)} x {Inv(d.ValueScale)}, xmitDataScalar {Inv(d.XmitDataScalar)}), {Inv(d.TransmissionSize)} kbit, " +
            $"{Ksp1Situations.Labels(d.Locations)}, flavor {d.FlavorKept}/{d.FlavorTotal}";

        public static string DefinitionsQueued(int generated, bool viaPatchManager, IEnumerable<(string Reason, int Count)> skipped) =>
            Prefix + $"definitions: {generated} queued through {(viaPatchManager ? "Patch Manager" : "the experiment store")}; skipped: {Counts(skipped)}";

        public const string PatchManagerMissing =
            Prefix + "definitions: Patch Manager's C# science patching not found, definitions go to the experiment store";

        public static string Localization(int terms, int names, int flavors) =>
            Prefix + $"localization: {terms} term(s) added ({names} name(s), {flavors} flavor text(s))";

        public const string LocalizationMissing = Prefix + "localization: ReduxLocalizer not found, generated experiments show no name";

        public static string Store(int present, int generated, IEnumerable<string> ksp2Ids)
        {
            var ids = (ksp2Ids ?? Enumerable.Empty<string>()).ToList();
            return Prefix + $"store: {present} of {generated} KSP1 definition(s) present; KSP2 experiments: {ids.Count} ({string.Join(", ", ids)})";
        }

        public const string StoreOff = Prefix + "store: KSP1PartMod is off in this campaign, no KSP1 definition added";

        // ---- Task 4: the importer

        public static string Part(string part, IEnumerable<(string Ksp1Id, string Ksp2Id, ScienceResolutionKind Kind)> resolved, int crew, bool storage) =>
            NativePrefix + $"{part}: science -> {ScienceMapping.NativeModule}: " +
            string.Join(", ", (resolved ?? Enumerable.Empty<(string, string, ScienceResolutionKind)>()).Select(r => $"{r.Item1} -> {r.Item2} ({KindName(r.Item3)})")) +
            $"; crew {crew}; report storage {OnOff(storage)}";

        /// <summary>The part line from a mapping: its configurations, in order.</summary>
        public static string Part(string part, SciencePart p) =>
            Part(part, p.Configurations.Select(c => (c.Ksp1Id, c.ExperimentDefinitionId, c.Kind)), p.CrewRequired, p.ReportStorage);

        public static string Skipped(string part, string ksp1Id, ScienceSkipReason reason) =>
            NativePrefix + $"{part}: science: {ksp1Id} skipped ({SkipText(reason)})";

        public static string Summary(int parts, int generated, int identical, int closest, int storage, int containers, int merged,
            IEnumerable<(string Id, int Count)> skipped, IEnumerable<(string Field, int Count)> notReproduced,
            int labs, int converters, int testSubjects, int ground, int dmagic) =>
            NativePrefix + $"science: {parts} part(s) got {ScienceMapping.NativeModule} ({generated} generated, {identical} identical, {closest} closest); " +
            $"report storage on {storage} part(s) ({containers} container(s)); merged: {merged}; skipped: {Counts(skipped)}; " +
            $"not reproduced: {Counts(notReproduced)}; not imported: ModuleScienceLab {labs}, ModuleScienceConverter {converters}, " +
            $"ModuleTestSubject {testSubjects}, ground science {ground}, specialised DMagic {dmagic}";

        // ---- Task 5: the runtime

        public static string Armed(bool deployAnimations, bool waitForAnimation) =>
            Prefix + $"armed: deploy animations {OnOff(deployAnimations)}, wait for animation {OnOff(waitForAnimation)}";

        public static string MemberNotFound(string member, string feature) =>
            Prefix + $"{member} not found, {feature} off (Redux's module left as is)";

        public static string Opens(string part, string ksp2Id, string entryKey) => Prefix + $"{part}: {ksp2Id} opens {entryKey}";

        public static string NoClip(string part, string ksp2Id, ScienceNoClip reason) => Prefix + $"{part}: {ksp2Id} has no clip ({NoClipText(reason)})";

        public static string Waits(string part, string ksp2Id, string entryKey, double seconds) =>
            Prefix + $"{part}: {ksp2Id} waits for {entryKey} (KSP2 deploy gate, {Inv(seconds)} s)";

        public static string ReportStored(string part, string ksp2Id, string locationId, double value) =>
            Prefix + $"{part}: report {ksp2Id} stored at {locationId}, value {Inv(value)}";

        public static string LoadedRunning(string part, string ksp2Id) =>
            Prefix + $"{part}: loaded with {ksp2Id} running, the native trigger already fired";

        public static string PatchFailed(string error) => Prefix + "patch failed, stopped for this session: " + error;
    }
}
