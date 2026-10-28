namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>What the variant watch does this frame when the part's B9 switch had no variant set at bind.</summary>
    public enum VariantRetry { None, Wait, Retry, GiveUp }

    /// <summary>Final review M2: a part with gated entries whose <c>Module_PartSwitch</c> has no data or no variant set
    /// yet at our bind (its own init may run later) keeps looking for the sets, at most every
    /// <see cref="Interval"/> seconds, for <see cref="Limit"/> seconds; then it gives up with one line and the gates keep
    /// their default subtype. A part without a switch, or whose sets were found, never retries.</summary>
    public static class VariantRetryRule
    {
        public const float Limit = 10f;
        public const float Interval = 0.5f;

        /// <param name="gated">The part has an entry with B9 gates.</param>
        /// <param name="hasSwitch">The part has a <c>Module_PartSwitch</c>.</param>
        /// <param name="setCount">Sets of the current watch; 0 when there is none.</param>
        /// <param name="elapsed">Seconds since the bind.</param>
        /// <param name="sinceLastTry">Seconds since the last try (the bind counts as one).</param>
        public static VariantRetry Next(bool gated, bool hasSwitch, int setCount, float elapsed, float sinceLastTry)
        {
            if (!gated || !hasSwitch || setCount > 0) return VariantRetry.None;
            if (elapsed >= Limit) return VariantRetry.GiveUp;
            return sinceLastTry >= Interval ? VariantRetry.Retry : VariantRetry.Wait;
        }

        public static string FoundLine(string part, float elapsed, string sets) =>
            $"{part} variants found {elapsed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} s after bind: {sets}";

        public static string GiveUpLine(string part) =>
            $"{part} B9 gates on their default subtype: Module_PartSwitch had no variant set {Limit.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s after bind";
    }
}
