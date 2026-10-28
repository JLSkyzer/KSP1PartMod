namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB's <c>ModuleBDBDeployableMeshHider</c> (notes §3.11): flight only, the objects are
    /// hidden while the deployable is retracted.</summary>
    public static class MeshHiderRule
    {
        public static bool Hidden(bool inFlight, bool retracted) => inFlight && retracted;
    }
}
