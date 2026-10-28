using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The log lines of the radiators plan, Tasks 2 and 3 (exact text). Numbers: G6, invariant.</summary>
    public static class RadiatorLines
    {
        public const string Prefix = NativeLines.Prefix;
        public const string ThermalPrefix = "[KSP1PartMod] thermal: ";

        private static string Inv(double v) => IsruCfg.Inv(v);

        private static string Flows(IEnumerable<(string Name, double Rate)> flows)
        {
            var list = (flows ?? Enumerable.Empty<(string, double)>()).Select(f => f.Item1 + " " + Inv(f.Item2)).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // ---- Task 2: the importer ----

        public static string CoolerReference(NativeCoolerRef r) =>
            r == null || r.IsFallback
                ? Prefix + $"cooler reference: no KSP2 cooler found, fallback {Inv(NativeCoolerRef.FallbackKwPerEc)} kW per EC/s"
                : Prefix + $"cooler reference: {r.Count} KSP2 cooler(s), median {Inv(r.KwPerEc)} kW per EC/s ({string.Join(", ", r.Parts)})";

        public static string Part(string part, RadiatorMapping m) =>
            Prefix + $"{part}: radiator -> {m.NativeModule}: {Inv(m.FluxRemoved)} kW " +
            $"(maxEnergyTransfer {Inv(m.MaxEnergyTransfer)} / {Inv(RadiatorMapping.EnergyTransferPerKw)} x {Inv(m.FluxScale)}), " +
            $"{Flows(m.Resources.Select(r => (r.Name, r.Rate)))} (KSP1 {Flows(m.Resources.Select(r => (r.Ksp1Name, r.Ksp1Rate)))}), " +
            (m.StartsOn ? "on" : "off") + " at start";

        public static string Summary(int coolers, int activeRadiators, double totalKw, int merged, int dropped,
            IEnumerable<(string Field, int Parts)> notReproduced)
        {
            var fields = (notReproduced ?? Enumerable.Empty<(string, int)>()).Select(f => f.Item1 + " " + f.Item2).ToList();
            return Prefix + $"radiators: {coolers} part(s) got {RadiatorMapping.NativeCooler}, {activeRadiators} {RadiatorMapping.NativeActiveRadiator} " +
                   $"({Inv(totalKw)} kW in total); merged: {merged}; resources dropped: {dropped} (unknown resource); " +
                   $"not reproduced: {(fields.Count == 0 ? "none" : string.Join(", ", fields))}";
        }

        public const string ListFormMissing =
            Prefix + "radiators: the list form of CreateSerialized not found, deployable radiators get Module_Cooler (they cool folded)";

        public static string Passive(int parts) =>
            Prefix + $"radiators: passive: {parts} KSP1 part(s) with emissiveConstant >= 0.8 and no radiator module radiate through " +
            "KSP2's own thermal model (Redux imports emissiveConstant)";

        // ---- Task 3: the runtime ----

        private static string OnOff(bool on) => on ? "on" : "off";

        public static string Armed(bool deployBridge, bool autoOn) =>
            ThermalPrefix + $"armed: deploy bridge {OnOff(deployBridge)}, auto on {OnOff(autoOn)}";

        public static string MemberNotFound(string member, string feature) =>
            ThermalPrefix + $"{member} not found, {feature} off (Redux's module left as is)";

        public static string DeployFollows(string part, string entryKey) =>
            ThermalPrefix + $"{part}: {RadiatorMapping.NativeActiveRadiator} deploy follows {entryKey}";

        public static string AlwaysExtended(string part, ExtendedReason reason) =>
            ThermalPrefix + $"{part}: {RadiatorMapping.NativeActiveRadiator} always extended (" +
            (reason == ExtendedReason.NoDeployClip ? "no deploy clip" : reason == ExtendedReason.AnimationsOff ? "animations off" : "entry gated by B9") + ")";

        /// <summary>A radiator of ours to which Redux gave its own <c>Data_Deployable</c>: kept, no bridge (once per part per session).</summary>
        public static string KeepsReduxDeployable(string part) =>
            ThermalPrefix + $"{part}: {RadiatorMapping.NativeActiveRadiator} keeps Redux's own Data_Deployable (no deploy bridge)";

        public static string TurnedOnWhenExtended(string part) => ThermalPrefix + $"{part}: radiator turned on when extended";

        public static string StateRepaired(string part) => StateRepaired(part, true, "OFF", "IDLE");

        /// <summary>The repair line for any mismatch between <c>coolerEnabled</c> and the cooler's state (only <c>coolerEnabled</c> is
        /// saved: the state comes back from the definition): on with <c>OFF</c> or <c>RETRACTED</c>, off with <c>IDLE</c>.</summary>
        public static string StateRepaired(string part, bool on, string from, string to) =>
            ThermalPrefix + $"{part}: cooler state repaired: {OnOff(on)}, {from} -> {to}";

        /// <summary>How auto-on starts for one part: a new part (an extended start turns it on), or the saved cooler (only a later
        /// extension turns it on, a cooler the player switched off stays off).</summary>
        public static string AutoOnSeed(string part, bool newPart, bool savedOn) =>
            ThermalPrefix + $"{part}: auto on: " + (newPart
                ? "new part, an extended start turns it on"
                : $"saved cooler {OnOff(savedOn)}, only a later extension turns it on");

        public static string Cooling(string part, double kw, IEnumerable<(string Name, double Rate)> resources) =>
            ThermalPrefix + $"{part}: cooling {Inv(kw)} kW, {Flows(resources)} (vessel pool)";

        public static string PatchFailed(string error) => ThermalPrefix + "patch failed, stopped for this session: " + error;
    }
}
