using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The deploy state a deploy bridge shows (Redux's <c>Data_Deployable.DeployState</c> values the native drill
    /// and scanner read).</summary>
    public enum BridgeDeploy { Retracted, Extending, Extended, Retracting }

    /// <summary>How <c>ModuleAnimationGroup</c> entries meet KSP2's native converter, drill and scanner (converters and
    /// drills plan, Task 4, (k)). Pure.
    /// <list type="bullet">
    /// <item>the active clip follows the native module's running state when <c>isru_active_animation</c> is on and the
    /// entry has an active clip and is not <c>AlwaysActive</c> (that one keeps playing as in phase 1);</item>
    /// <item>the deploy bridge follows the part's first Group entry with a deploy clip; none: always extended;</item>
    /// <item>the bridge's state: the machine's <c>Deployed</c> and <c>IsMoving</c>.</item>
    /// </list>
    /// None of this changes an entry key or a saved value.</summary>
    public static class IsruAnimRules
    {
        public static bool WantsActiveInput(AnimEntryDef def, bool isruActiveAnimation) =>
            isruActiveAnimation && def != null && def.Kind == EntryKind.Group && !string.IsNullOrWhiteSpace(def.SecondClip) && !def.AlwaysActive;

        /// <summary>A Group entry the deploy bridge may follow: it has a deploy clip.</summary>
        public static bool CanDriveBridge(AnimEntryDef def) =>
            def != null && def.Kind == EntryKind.Group && !string.IsNullOrWhiteSpace(def.Clip);

        /// <summary>The bridge state of a Group machine; an entry gated off by B9, or disabled, leaves the native module
        /// free: <see cref="BridgeDeploy.Extended"/>.</summary>
        public static BridgeDeploy StateOf(bool deployed, bool moving, bool inactive) =>
            inactive ? BridgeDeploy.Extended
            : deployed ? (moving ? BridgeDeploy.Extending : BridgeDeploy.Extended)
            : moving ? BridgeDeploy.Retracting : BridgeDeploy.Retracted;
    }
}
