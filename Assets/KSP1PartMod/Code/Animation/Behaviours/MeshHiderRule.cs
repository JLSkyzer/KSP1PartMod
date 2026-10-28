namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB's <c>ModuleBDBDeployableMeshHider</c> (notes §3.11): flight only, the objects are
    /// hidden while the deployable is retracted.</summary>
    public static class MeshHiderRule
    {
        public static bool Hidden(bool inFlight, bool retracted) => inFlight && retracted;
    }

    /// <summary>What the mesh hider's glue does with its objects' <c>SetActive</c>: called only on a change of
    /// <see cref="MeshHiderRule.Hidden"/>, and never before we hide them (an object some other code hid, a B9 variant
    /// for instance, is not shown by us). <see cref="Restore"/> shows again only what we hid (shutdown, or a B9 gate
    /// closing the entry).</summary>
    public sealed class MeshHiderState
    {
        /// <summary>We hid the objects and have not shown them again.</summary>
        public bool Hiding { get; private set; }

        /// <summary>True when the objects must be set active to <c>!</c><see cref="Hiding"/> now.</summary>
        public bool Step(bool inFlight, bool retracted)
        {
            var hidden = MeshHiderRule.Hidden(inFlight, retracted);
            if (hidden == Hiding) return false;
            Hiding = hidden;
            return true;
        }

        /// <summary>True when the objects must be set active again (we were hiding them).</summary>
        public bool Restore()
        {
            if (!Hiding) return false;
            Hiding = false;
            return true;
        }
    }
}
