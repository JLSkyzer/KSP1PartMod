namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The scalar of a <see cref="Cfg.ScalarSource.Ablator"/> Color entry (0.8.0): the part's own stored / capacity of
    /// the shield resource, clamped to 0..1. Fresh shield = 1 = the white end of the stock curves (clean), depleted = 0 =
    /// black (S on KSP1's direction; it matches the stock curves). A capacity of 0 or less, or a NaN, keeps the last value
    /// (1 before any read). Read only and visual only: nothing is written to the game, nothing is saved. Allocation-free.
    /// The caller hands <see cref="Fraction"/> to <see cref="ColorRamp.SetScalar"/>.</summary>
    public sealed class AblatorScalar
    {
        public float Fraction { get; private set; } = 1f;

        /// <summary>Takes this frame's amounts; true when <see cref="Fraction"/> changed.</summary>
        public bool Tick(double stored, double capacity)
        {
            if (!(capacity > 0) || double.IsNaN(stored) || double.IsInfinity(capacity)) return false;
            var f = stored / capacity;
            var v = f <= 0 ? 0f : f >= 1 ? 1f : (float)f;
            if (v == Fraction) return false;
            Fraction = v;
            return true;
        }
    }
}
