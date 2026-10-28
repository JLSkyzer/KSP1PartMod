namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The glow input of the RCS modules (<c>FXModuleAnimateRCS</c>, <c>ModuleBdbRCSEmissive</c>): a
    /// thruster's force as a fraction of its maximum thrust, times <c>thrustForceMult</c>.</summary>
    public static class RcsScalar
    {
        /// <summary>At least 0; 0 when <paramref name="maxThrust"/> is not positive.</summary>
        public static float Of(float relativeForceMagnitude, float maxThrust, float thrustForceMult)
        {
            if (!(maxThrust > 0f) || !(relativeForceMagnitude > 0f)) return 0f;
            var v = relativeForceMagnitude / maxThrust * thrustForceMult;
            return v > 0f ? v : 0f;
        }
    }
}
