using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>How a <c>ModuleDeployableAntenna</c> entry meets KSP2's native <c>Module_DataTransmitter</c> (antenna gating,
    /// 0.18.0). Pure.
    /// <list type="bullet">
    /// <item>KSP1: a deployable antenna transmits only once extended. KSP2's transmitter gates itself on a
    /// <c>PartComponentModule_Deployable</c> of the part; an imported KSP1 antenna has none (Redux has no importer for
    /// <c>ModuleDeployableAntenna</c>), so it transmitted retracted too;</item>
    /// <item>the bridge follows the Deploy entry of the part's <c>ModuleDeployableAntenna</c>
    /// (<see cref="AnimEntryDef.GatesTransmitter"/>, set by the planner when the part also has a <c>ModuleDataTransmitter</c>);
    /// an entry gated off by B9, or an animations-off part, leaves the antenna free: <see cref="BridgeDeploy.Extended"/>.</item>
    /// </list>
    /// No entry key changes.</summary>
    public static class AntennaRules
    {
        /// <summary>A Deploy entry the planner marked as the gate of the part's transmitter.</summary>
        public static bool CanDriveBridge(AnimEntryDef def) => def != null && def.Kind == EntryKind.Deploy && def.GatesTransmitter;

        /// <summary>The bridge state of the entry's deploy machine state (same mapping as the radiator's).</summary>
        public static BridgeDeploy StateOf(DeployState state, bool inactive) => RadiatorAnimRules.StateOf(state, inactive);

        /// <summary>True when the planner marks the module: a <c>ModuleDeployableAntenna</c> on a part that has a transmitter.</summary>
        public static bool MarksModule(string moduleName, bool partHasTransmitter) =>
            partHasTransmitter && "ModuleDeployableAntenna".Equals(moduleName?.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}
