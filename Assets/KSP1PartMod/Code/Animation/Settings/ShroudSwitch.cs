namespace KSP1PartMod.Animation.Settings
{
    /// <summary>What <see cref="ShroudSwitch.Arm"/> decides about the import patches of the engine shrouds (0.7.0).</summary>
    public enum ShroudArming
    {
        /// <summary>Patch Redux's import: no <c>Module_Fairing</c> on KSP1 shroud engines, meshes kept and hidden.</summary>
        Armed,
        /// <summary><c>ksp1_engine_shrouds</c> is false: 0.6.0 behaviour.</summary>
        OffBySwitch,
        /// <summary>The animations are disabled (<c>enabled</c> false): no runtime would ever show the kept meshes, so
        /// Redux is left alone.</summary>
        OffAnimationsDisabled,
    }

    /// <summary>The arming rule: the import patches act only when the shroud runtime can run.</summary>
    public static class ShroudSwitch
    {
        /// <param name="enabled">Settings <c>enabled</c> (the master switch of the animations).</param>
        /// <param name="shrouds">Settings <c>ksp1_engine_shrouds</c>.</param>
        /// <remarks>When both are false the player's own switch is reported.</remarks>
        public static ShroudArming Arm(bool enabled, bool shrouds)
        {
            if (!shrouds) return ShroudArming.OffBySwitch;
            return enabled ? ShroudArming.Armed : ShroudArming.OffAnimationsDisabled;
        }
    }
}
