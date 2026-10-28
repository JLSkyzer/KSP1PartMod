using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The native KSP2 heat shield every KSP1 shield is measured against (plan (f)): its consumption per unit of
    /// shielded flux and its ablator capacity, in KSP2 units.
    /// <list type="bullet">
    /// <item><see cref="Rate"/> is the native shield's <c>Rate × PyrolysisLossFactor</c>: KSP2 burns
    /// <c>flux × PyrolysisLossFactor × Rate</c> units per second, and our shields carry <c>PyrolysisLossFactor = 1</c>, so the
    /// product is what our <c>Rate</c> is compared with.</item>
    /// <item><see cref="Median"/> picks, among the game's shields, the one whose Rate is the median (the lower one for an
    /// even count), so the logged Rate and capacity are one real shield's pair. The detail line lists every pair, to check in
    /// game that native <c>Rate × PyrolysisLossFactor</c> does not grow with capacity.</item>
    /// <item><see cref="Fallback"/> stands in when the game has none: values of the stock 2.5 m class (capacity 0.8 units,
    /// Rate 0.5 × 1.6e-7), read in the KSP2 patch <c>CommunityFixes/patches/inflatable_heatshield.patch</c>, not in Redux's
    /// own parts (S; the checklist calibrates it with <c>ablator_rate_scale</c>).</item>
    /// </list>
    /// Pure.</summary>
    public sealed class NativeShieldRef
    {
        public const double FallbackRate = 0.5 * 1.6e-7;
        public const double FallbackCapacity = 0.8;

        public static readonly NativeShieldRef Fallback = new NativeShieldRef(FallbackRate, FallbackCapacity, 0, Array.Empty<(double, double)>());

        /// <summary>Units per second per kW of shielded flux (the native <c>Rate × PyrolysisLossFactor</c>).</summary>
        public double Rate { get; }

        /// <summary>The native shield's capacity of its resource, in KSP2 units.</summary>
        public double Capacity { get; }

        /// <summary>The number of KSP2 shields read; 0 for <see cref="Fallback"/>.</summary>
        public int Count { get; }

        public bool IsFallback => Count == 0;

        /// <summary>Every valid (rate, capacity) pair read, sorted by rate (for the detail line).</summary>
        public IReadOnlyList<(double Rate, double Capacity)> Shields { get; }

        private NativeShieldRef(double rate, double capacity, int count, IReadOnlyList<(double, double)> shields)
        {
            Rate = rate;
            Capacity = capacity;
            Count = count;
            Shields = shields;
        }

        /// <summary>The median shield of <paramref name="definitions"/> (pairs with a finite rate and capacity above 0; the
        /// others are ignored), or <see cref="Fallback"/> when none is valid.</summary>
        public static NativeShieldRef Median(IEnumerable<(double Rate, double Capacity)> definitions)
        {
            var valid = (definitions ?? Enumerable.Empty<(double, double)>())
                .Where(d => Positive(d.Item1) && Positive(d.Item2))
                .OrderBy(d => d.Item1)
                .ThenBy(d => d.Item2)
                .ToList();
            if (valid.Count == 0) return Fallback;
            var m = valid[(valid.Count - 1) / 2];
            return new NativeShieldRef(m.Item1, m.Item2, valid.Count, valid);
        }

        private static bool Positive(double v) => v > 0 && !double.IsInfinity(v) && !double.IsNaN(v);
    }
}
