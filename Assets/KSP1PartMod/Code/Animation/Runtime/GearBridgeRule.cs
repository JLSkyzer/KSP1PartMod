using System;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The Gear bridge gives Redux's gear module a Gear action group action. Redux's deployable module
    /// already adds its own toggle action in its <c>DefaultActionGroup</c> (KSP1 <c>defaultActionGroup</c>): when
    /// that group is Gear, a second Gear toggle could cancel it, so the bridge adds none.</summary>
    public static class GearBridgeRule
    {
        /// <param name="reduxDefaultGroup">The name of Redux's <c>Data_Deployable.DefaultActionGroup</c>.</param>
        public static bool NeedsOwnAction(string reduxDefaultGroup) =>
            !string.Equals(reduxDefaultGroup?.Trim(), "Gear", StringComparison.OrdinalIgnoreCase);
    }
}
