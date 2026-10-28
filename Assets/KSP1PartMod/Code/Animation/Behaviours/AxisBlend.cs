namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The <c>axisConstrained</c> / <c>axisScales</c> of <c>ModuleAdvancedLookAtConstraint</c> (notes
    /// §3.2b): each constrained axis of the rotator's local Euler angles (degrees) moves from its original value
    /// toward the look-at one, KSP1 weighting: <c>scale * original + (1 - scale) * lookAt</c> (scale 0..1, shortest way round); the others keep the original. Missing
    /// arrays or entries mean unconstrained, a missing scale 0 (fully look-at).</summary>
    public static class AxisBlend
    {
        public static void Blend(ref float x, ref float y, ref float z, float fx, float fy, float fz,
            bool[] constrained, float[] scales)
        {
            x = One(x, fx, 0, constrained, scales);
            y = One(y, fy, 1, constrained, scales);
            z = One(z, fz, 2, constrained, scales);
        }

        private static float One(float original, float look, int axis, bool[] constrained, float[] scales)
        {
            if (constrained == null || axis >= constrained.Length || !constrained[axis]) return original;
            var s = 1f - (scales != null && axis < scales.Length ? Machine.Clamp01(scales[axis]) : 0f);
            var d = (look - original) % 360f;
            if (d > 180f) d -= 360f;
            else if (d < -180f) d += 360f;
            return original + d * s;
        }
    }
}
