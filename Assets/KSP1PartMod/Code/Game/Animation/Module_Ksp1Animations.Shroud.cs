namespace KSP1PartMod.Game.Animation
{
    /// <summary>Engine shrouds (0.7.0): the KSP1 <c>ModuleJettison</c> meshes kept by <see cref="ShroudImport"/>.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>The part's stage was activated (<c>PartBehavior.ActivateAllModules</c>, through
        /// <see cref="ShroudImport"/>). Flight only. The shroud entries latch here once their runtime is bound; until
        /// then the signal is received and ignored.</summary>
        internal void OnPartStaged()
        {
        }
    }
}
