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
        /// down (wheels plan, Task 4). <c>Module_WheelBase</c> adds its Gear toggle in flight only, after finding its collider host
        /// (it returns early without it) and a <c>Module_Deployable</c> carrying a <c>Data_Deployable</c>. Redux's legacy
        /// <c>Ksp1Module_Deployable</c> does not derive from <c>Module_Deployable</c>: on those legs the wheel base finds no
        /// deployable, and the bridge keeps its own action.</summary>
        /// <param name="ourBase">Our importer converted the part's <c>ModuleWheelBase</c> (the record's base, not just a record).</param>
        /// <param name="wheelBase">The part has a <c>Module_WheelBase</c>.</param>
        /// <param name="nativeDeployable">The part has a <c>Module_Deployable</c> with a <c>Data_Deployable</c>.</param>
        /// <param name="inFlight">Flight; else the VAB, where the bridge stands down too (plan, S).</param>
        /// <param name="wheelInitialized">In flight: the wheel base's <c>OnInitialize</c> already ran (else it is assumed to succeed).</param>
        /// <param name="hostFound">In flight: the wheel base found its collider host, so it went on to add its Gear toggle.</param>
        public static bool NativeWheelGear(bool ourBase, bool wheelBase, bool nativeDeployable, bool inFlight, bool wheelInitialized,
            bool hostFound) =>
            ourBase && wheelBase && nativeDeployable && (!inFlight || !wheelInitialized || hostFound);
    }
}
