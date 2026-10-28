using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>How a <c>ModuleDeployableRadiator</c> entry meets KSP2's <c>Module_ActiveRadiator</c> (radiators plan, Task 3). Pure.
    /// <list type="bullet">
    /// <item>the deploy bridge follows the Deploy entry whose module is the part's <c>ModuleDeployableRadiator</c>;</item>
    /// <item>its state is the deploy machine's; an entry gated off by B9, or an animations-off part, leaves the radiator free:
    /// <see cref="BridgeDeploy.Extended"/> (it cools, as with no bridge);</item>
    /// <item>with <c>radiator_auto_on</c>, the cooler is switched on at a transition to Extended, once; the player's later
    /// choice is kept.</item>
    /// </list>
    /// No entry key changes.</summary>
    public static class RadiatorAnimRules
    {
        /// <summary>A Deploy entry of the part's <c>ModuleDeployableRadiator</c>, whose KSP1 module index is
        /// <paramref name="deployableRadiatorModuleIndex"/> (negative: the part has none).</summary>
        public static bool CanDriveBridge(AnimEntryDef def, int deployableRadiatorModuleIndex) =>
            def != null && def.Kind == EntryKind.Deploy && deployableRadiatorModuleIndex >= 0 && def.ModuleIndex == deployableRadiatorModuleIndex;

        public static BridgeDeploy StateOf(DeployState state, bool inactive)
        {
            if (inactive) return BridgeDeploy.Extended;
            switch (state)
            {
                case DeployState.Extending: return BridgeDeploy.Extending;
                case DeployState.Extended: return BridgeDeploy.Extended;
                case DeployState.Retracting: return BridgeDeploy.Retracting;
                default: return BridgeDeploy.Retracted;
            }
        }

        /// <summary>True only when the bridge arrives at Extended from something else. <paramref name="previous"/> is null before
        /// the first step, so an entry that is Extended from the start (gated by B9) is a transition once.</summary>
        public static bool ShouldAutoEnable(BridgeDeploy? previous, BridgeDeploy now, bool autoOn) =>
            autoOn && now == BridgeDeploy.Extended && previous != BridgeDeploy.Extended;
    }
}
