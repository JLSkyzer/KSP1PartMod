using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.B9
{
    /// <summary>The six <c>b9_*</c> switches of <c>animations.json</c>, as the planner reads them. Independent of
    /// <c>enabled</c>. <see cref="ModuleActive"/> needs <see cref="ModuleData"/> (the off forms go through the same
    /// edit route).</summary>
    public sealed class B9Switches
    {
        public bool Mass = true, Resources = true, ModuleData = true, ModuleActive = true, PartScalars = true, NewPartSwitches = true;

        public static B9Switches AllOn => new B9Switches();

        public static B9Switches From(AnimationSettings s)
        {
            s = s ?? AnimationSettings.Defaults;
            return new B9Switches
            {
                Mass = s.B9Mass, Resources = s.B9Resources, ModuleData = s.B9ModuleData,
                ModuleActive = s.B9ModuleActive && s.B9ModuleData, PartScalars = s.B9PartScalars,
                NewPartSwitches = s.B9NewPartSwitches,
            };
        }
    }
}
