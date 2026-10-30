using System;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The Gear bridge gives Redux's gear module a Gear action group action. It stands down when another module
    /// already toggles the same deployable on Gear, since a second Gear toggle would cancel the first on one key press:
    /// <list type="bullet">
    /// <item>Redux's deployable module adds its own toggle action in its <c>DefaultActionGroup</c> (KSP1
    /// <c>defaultActionGroup</c>): when that group is Gear, the bridge adds none;</item>
    /// <item>KSP2's <c>Module_WheelBase</c> adds a Gear toggle of the part's native <c>Module_Deployable</c> (0.14.0): on a part
    /// with our wheel base and a native deployable, the bridge adds none.</item>
    /// </list></summary>
    public static class GearBridgeRule
    {
        /// <param name="reduxDefaultGroup">The name of Redux's <c>Data_Deployable.DefaultActionGroup</c>.</param>
        public static bool NeedsOwnAction(string reduxDefaultGroup) => NeedsOwnAction(reduxDefaultGroup, false);

        /// <param name="reduxDefaultGroup">The name of Redux's <c>Data_Deployable.DefaultActionGroup</c>.</param>
        /// <param name="nativeWheelGear">The part has our <c>Module_WheelBase</c> and a native <c>Module_Deployable</c>.</param>
        public static bool NeedsOwnAction(string reduxDefaultGroup, bool nativeWheelGear) =>
            !nativeWheelGear && !string.Equals(reduxDefaultGroup?.Trim(), "Gear", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether <c>Module_WheelBase</c> gives (or, in the VAB, stands for) the part's Gear toggle, so the bridge stands
        /// down (wheels plan, Task 4). In flight, <c>Module_WheelBase.OnInitialize</c> returns early, before it adds its Gear
        /// toggle, when its wheel transform (only if named) or its collider host is not found by the part's
        /// <c>FindModelTransform</c>; past those, it adds the toggle on a <c>Module_Deployable</c> carrying a
        /// <c>Data_Deployable</c>. The same lookups decide here, so the answer does not depend on which module initializes
        /// first. Redux's legacy <c>Ksp1Module_Deployable</c> does not derive from <c>Module_Deployable</c>: on those legs the
        /// wheel base finds no deployable, and the bridge keeps its own action.</summary>
        /// <param name="ourBase">Our importer converted the part's <c>ModuleWheelBase</c> (the record's base, not just a record).</param>
        /// <param name="wheelBase">The part has a <c>Module_WheelBase</c>.</param>
        /// <param name="nativeDeployable">The part has a <c>Module_Deployable</c> with a <c>Data_Deployable</c>.</param>
        /// <param name="inFlight">Flight; else the VAB, where the bridge stands down too (plan, S).</param>
        /// <param name="transformsFound">In flight: the part's model has the wheel transform (when one is named) and the
        /// collider host, so the wheel base does not return early.</param>
        public static bool NativeWheelGear(bool ourBase, bool wheelBase, bool nativeDeployable, bool inFlight, bool transformsFound) =>
            ourBase && wheelBase && nativeDeployable && (!inFlight || transformsFound);
    }
}
