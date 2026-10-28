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
    }
}
