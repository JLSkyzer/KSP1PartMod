namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The glow input of the RCS modules (<c>FXModuleAnimateRCS</c>, <c>ModuleBdbRCSEmissive</c>): a
    /// thruster's force as a fraction of its maximum thrust, times <c>thrustForceMult</c>, clamped to 0..1 (the clip's
    /// normalized time and the <c>alphaCurve</c> input).</summary>
    public static class RcsScalar
    {
        /// <summary>In 0..1; 0 when <paramref name="maxThrust"/> is not positive.</summary>
        public static float Of(float relativeForceMagnitude, float maxThrust, float thrustForceMult)
        {
            if (!(maxThrust > 0f) || !(relativeForceMagnitude > 0f)) return 0f;
            var v = relativeForceMagnitude / maxThrust * thrustForceMult;
            return v > 0f ? (v < 1f ? v : 1f) : 0f;
        }
    }
}
