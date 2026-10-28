using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.B9
{
    /// <summary>The <c>[KSP1PartMod] b9:</c> log lines of the B9 overrides plan (Tasks 2 to 4), exact text. Numbers are
    /// invariant.</summary>
    public static class B9Lines
    {
        public const string Prefix = "[KSP1PartMod] b9: ";

        private static string OnOff(bool on) => on ? "on" : "off";
        private static string Inv(double v) => B9Values.Inv(v);

        private static string List(IEnumerable<string> items)
        {
            var list = items?.Where(s => !string.IsNullOrEmpty(s)).ToList();
            return list == null || list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // ---- Task 2: mass, resources, part scalars ----

        public static string Armed(bool mass, bool resources, bool moduleData, bool moduleOff, bool partScalars, bool newPartSwitches) =>
            Prefix + $"armed: mass {OnOff(mass)}, resources {OnOff(resources)}, module data {OnOff(moduleData)}, module off {OnOff(moduleOff)}, " +
            $"part scalars {OnOff(partScalars)}, new part switches {OnOff(newPartSwitches)}";

        public static string MemberNotFound(string member) =>
            Prefix + member + " not found, B9 overrides off (Redux's variants left as is)";

        /// <summary>Every switch that applies something (mass, resources, module data, part scalars) is off: nothing is
        /// armed.</summary>
        public const string AllOff = Prefix + "mass, resources, module data and part scalars off, nothing armed (Redux's variants left as is)";

        /// <summary>A member only module data needs is missing: module data and module off are off, the rest applies.</summary>
        public static string ModuleDataMemberNotFound(string member) =>
            Prefix + member + " not found, module data and module off off (mass, resources and part scalars still apply)";

        public static string BuiltInTankTypesNotFound(string member) =>
            Prefix + member + " not found, Redux's built-in tank types unknown (their subtypes get no tank resources)";

        /// <summary>Once per set.</summary>
        public static string SetSummary(string part, string set, int variants, double massMin, double massMax,
            IEnumerable<string> resources, IEnumerable<string> scalars) =>
            Prefix + $"{part}: {set} -> {variants} variant(s): mass {Inv(massMin)}..{Inv(massMax)} t, resources {List(resources)}, scalars {List(scalars)}";

        /// <param name="moduleIdOrIndex">The B9 module's <c>moduleID</c>, or its index (<c>B9 #n</c>).</param>
        public static string NewSet(string part, string id, string moduleIdOrIndex, int subtypes, string defaultId) =>
            Prefix + $"{part}: new set '{id}' for {moduleIdOrIndex} ({subtypes} subtypes, default '{defaultId}')";

        /// <summary>Once per part and note (<see cref="Notes"/>).</summary>
        public static string PartNote(string part, string note) => Prefix + part + ": " + note;

        public static string TransformerDropped(string part, string type, string message) =>
            Prefix + $"{part}: transformer {type} dropped: {message}";

        public static string PartFailed(string part, string exception) =>
            Prefix + $"{part}: failed, Redux's variants kept: {exception}";

        public const string Stopped = Prefix + "stopped for this session after 3 failures";

        /// <summary>Redux's result does not match the plan in a way that cannot be skipped per switch: the part keeps
        /// Redux's sets (not counted as a failure).</summary>
        public static string PlanMismatch(string part, string reason) =>
            Prefix + $"{part}: Redux's sets differ from the plan ({reason}), Redux's variants kept for this part";

        /// <summary>Once per session: no tank type could be read, so no tank resource is changed.</summary>
        public static string TankTypesUnavailable(string reason) =>
            Prefix + $"tank types not read ({reason}), tank resources off for this session (Redux's resources kept)";

        /// <summary>Once at the end of the import.</summary>
        public static string ImportSummary(int parts, int sets, int newSets, int onPartsWithoutSwitch, int massVariants,
            int resourceVariants, int scalarVariants, int costVariants) =>
            Prefix + $"{parts} part(s), {sets} set(s) ({newSets} new, {onPartsWithoutSwitch} on parts that had no part switch): " +
            $"mass on {massVariants} variant(s), resources on {resourceVariants}, scalars on {scalarVariants}; " +
            $"cost computed, not applied, on {costVariants} variant(s) (no KSP2 system reads part cost)";

        // ---- Task 3: module data and moduleActive ----

        private static string Fields(string behaviour, IEnumerable<string> fields) =>
            string.Join(", ", (fields ?? Enumerable.Empty<string>()).Select(f => behaviour + "." + f));

        /// <summary>Once per part and module.</summary>
        public static string ModuleEdit(string part, string set, string variant, string ksp1Module, string behaviour, IReadOnlyCollection<string> fields) =>
            Prefix + $"{part}: {set}/{variant}: {ksp1Module} -> {Fields(behaviour, fields)} ({fields?.Count ?? 0} field(s))";

        public static string ModuleOff(string part, string ksp1Module, string variant, string behaviour, IEnumerable<string> fields) =>
            Prefix + $"{part}: {ksp1Module} off in {variant} -> {Fields(behaviour, fields)}";

        public static string ModuleOffNone(string part, string ksp1Module, string variant) =>
            Prefix + $"{part}: {ksp1Module} off in {variant}: no native off form, left on";

        /// <summary>An engine turned off: no off form (maxThrust 0 would give NaN Δv and burn time), the engine stays on.</summary>
        public static string ModuleOffEngine(string part, string ksp1Module, string variant) =>
            Prefix + $"{part}: {ksp1Module} off in {variant}: engine off form (maxThrust 0) dropped, left on (its Δv and burn time would read NaN)";

        /// <summary>A decoupler turned off on a part that also stages through another module: staging is per part
        /// (<c>CanStage</c>), so the off form would take that module out of staging too.</summary>
        public static string ModuleOffStaging(string part, string ksp1Module, string variant, string other) =>
            Prefix + $"{part}: {ksp1Module} off in {variant}: off form skipped, the part also stages through {other} (staging is per part), left on";

        /// <summary>An off form whose re-import changes no native field.</summary>
        public static string ModuleOffNoChange(string part, string ksp1Module, string variant) =>
            Prefix + $"{part}: {ksp1Module} off in {variant}: the off form changes no native field";

        /// <summary>An off form on a module Redux does not import natively (<paramref name="reason"/>: Redux's, e.g. "not the
        /// part's first gimbal, the only one Redux imports").</summary>
        public static string ModuleOffNotImported(string part, string ksp1Module, string variant, string reason) =>
            Prefix + $"{part}: {ksp1Module} off in {variant}: not imported natively ({reason}), no off form, left on";

        public static string NoNativeEffect(string part, string ksp1Module, IEnumerable<string> keys) =>
            Prefix + $"{part}: {ksp1Module}: no native effect ({List(keys)})";

        public static string BehaviourRepeated(string part, string behaviour, int count) =>
            Prefix + $"{part}: {behaviour} appears {count} times, edits skipped (V-SwiFT edits the first only)";

        public static string FieldConflict(string part, string behaviour, string field, string keptSet) =>
            Prefix + $"{part}: {behaviour}.{field} edited by two sets, kept '{keptSet}'";

        /// <summary>Once per behaviour type, at its first use.</summary>
        public static string ModuleTypeResolves(string behaviour, bool resolves) =>
            Prefix + $"module types: {behaviour} resolves {(resolves ? "true" : "false")}";

        /// <summary>End of the import.</summary>
        public static string ModuleDataSummary(int transformers, int parts, IEnumerable<(string Field, int Count)> byField,
            int offForms, int noOffForm, IEnumerable<string> noOffFormModules, int noNativeEffect, IEnumerable<string> noEffectModules,
            int offNoChange = 0) =>
            Prefix + $"module data: {transformers} transformer(s) on {parts} part(s) ({List(byField?.Select(f => f.Field + " " + f.Count))}); " +
            $"off forms {offForms}{(offNoChange > 0 ? $" ({offNoChange} changing nothing)" : "")}, no off form {noOffForm} ({List(noOffFormModules)}); " +
            $"no native effect {noNativeEffect} ({List(noEffectModules)})";

        // ---- Task 4: gates and audit ----

        /// <summary>Once per part: the 2b gates could not be matched to the sets the import appended.</summary>
        public static string GatesKeptDefault(string part, string set) =>
            Prefix + $"{part}: appended set '{set}' not found when planning the animation gates again, the gates of the part's appended sets stay at their default subtype";

        /// <summary>The audit's summary line (the audit's own <c>audit:</c> prefix, as its other lines).</summary>
        public static string AuditSummary(int partsWithOverrides, int partsFailed) =>
            $"audit: B9 overrides: {partsWithOverrides} part(s) with overrides, {partsFailed} part(s) where the default variant failed to apply";

        /// <summary>The notes of <see cref="B9OverridePlan.Notes"/>, written after <c>b9: &lt;part&gt;: </c>.</summary>
        public static class Notes
        {
            /// <param name="dryMin">The smallest dry mass (<c>tankMass × volume</c>) of its subtypes, in tonnes, or null when
            /// none can be computed (unreadable volume).</param>
            public static string SecondTankSwitch(string switchLabel, double? dryMin, double? dryMax) =>
                $"second tank switch '{switchLabel}' not applied (one tank switch per part): its dry mass, " +
                (dryMin.HasValue && dryMax.HasValue ? $"{Inv(dryMin.Value)}..{Inv(dryMax.Value)} t" : "unknown (volume not readable)") +
                ", left out";

            public static string ChildVolumeAtDefault(string child, string parent, string childDefault) =>
                $"volume of '{parent}' takes '{child}' at its default subtype '{childDefault}' (the choice of '{child}' is not followed)";

            public static string UnparseableVolume(string switchLabel) => $"volume of '{switchLabel}' cannot be read, no tank override";

            public static string PercentFilledSmall(double value) =>
                $"percentFilled {Inv(value)} read as {Inv(value)} % (B9's wiki: a percentage; Redux's own tank catalog reads values up to 1 as a fraction)";

            public static string UnknownTankType(string name) => $"unknown tank type '{name}', no tank resources";

            public static string TankWithoutMass(string name) => $"tank type '{name}' has no tankMass, dry mass 0";

            public static string NoVolumeForDryMass(string subtype, string tank) =>
                $"subtype '{subtype}': no volume for tank type '{tank}', dry mass 0";

            public const string UpgradeRequiredIgnored = "upgradeRequired ignored (no upgrade gate)";

            public const string NodeTransformNotApplied = "node/TRANSFORM not applied";

            /// <summary>The part has a second tank switch: Redux's base holds the default resources of both switches, so
            /// the first switch's Remover/Adder would lose the second's; Redux's resources are kept.</summary>
            public static string ResourcesKeptSecondTank(string tankSwitch) =>
                $"tank resources of '{tankSwitch}' not applied: the part has a second tank switch whose default resources " +
                "are in Redux's base, so Redux's resources are kept (mass and part values still apply)";

            public static string ReduxSetMissing(string set, IEnumerable<string> reduxSets) =>
                $"Redux's set '{set}' not found (Redux built {List(reduxSets)}), its overrides skipped";

            public static string ReduxVariantMissing(string set, string variant) =>
                $"variant '{variant}' not found in Redux's set '{set}', the set's overrides skipped";

            public const string ResourceCapacitiesAdded =
                "no resource in the base definition, Module_ResourceCapacities added for the tank subtypes";

            public static string Unparseable(string subtype, string key, string raw) =>
                $"subtype '{subtype}': {key} '{raw}' cannot be read, ignored";

            public static string IdentifierMatches(string switchLabel, string variant, string module, int count) =>
                $"B9 '{switchLabel}' subtype '{variant}': IDENTIFIER {module} matches {count} module(s), ignored";

            public static string ResourceWithoutSize(string subtype, string resource) =>
                $"resource '{resource}' of subtype '{subtype}' has no unitsPerVolume or maxAmount, left out";
        }
    }
}
