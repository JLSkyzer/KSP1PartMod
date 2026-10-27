using System.Globalization;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The VAB speed of our animations, <c>editor_animation_speed</c> in <c>animations.json</c>. A positive
    /// setting multiplies the flight speed of every entry in the editor: 1, the default, plays each clip at its real
    /// speed (gear: times its KSP1 speed factor), 1000 or more snaps. 0 restores KSP1's own editor values, read from
    /// each cfg: ModuleAnimateGeneric 10 × the clip length (0.1 s; flight speed with <c>instantAnimInEditor =
    /// false</c>), the ModuleDeployablePart family <c>editorAnimationSpeedMult</c> (10), gear, Redux-followed
    /// deployables and animation groups an instant snap.</summary>
    public static class EditorSpeed
    {
        public const float RealTime = 1f;
        public const float Ksp1 = 0f;
        public const float Snap = 1000f;

        public static bool UsesKsp1(float setting) => !(setting > 0f);

        /// <summary>The editor multiplier of the flight speed: the setting when positive, else KSP1's
        /// <paramref name="ksp1"/>.</summary>
        public static float Multiplier(float setting, float ksp1) => UsesKsp1(setting) ? ksp1 : setting;

        public static string Describe(float setting) =>
            UsesKsp1(setting) ? "KSP1 editor values (×10, gear instant)"
            : setting >= Snap ? "instant"
            : "×" + setting.ToString("0.###", CultureInfo.InvariantCulture) + " of the flight speed";
    }
}
