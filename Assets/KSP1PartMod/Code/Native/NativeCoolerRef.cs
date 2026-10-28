using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The native cooler pace (radiators plan (d)): the kW a KSP2 cooler removes per EC/s it uses, taken as the median over
    /// the KSP2 parts that have a <c>Data_Cooler</c> with an EC resource, read by the glue as <c>(part, kW, EC/s)</c> (the lower
    /// one of an even count). Both stock radiators give 100 (26w42c, V), which is the fallback when none is read. Pure.</summary>
    public sealed class NativeCoolerRef
    {
        /// <summary>26w42c: <c>radiator_2v_radial_fixed</c> 200 kW for 2 EC/s, <c>radiator_2v_radial_1x8</c> 500 kW for 5 EC/s.</summary>
        public const double FallbackKwPerEc = 100;

        public static readonly NativeCoolerRef Fallback = new NativeCoolerRef(FallbackKwPerEc, 0, new string[0]);

        public double KwPerEc { get; }
        /// <summary>The coolers the median was taken over; 0 for the fallback.</summary>
        public int Count { get; }
        /// <summary>Their part names, distinct, ordinal order.</summary>
        public IReadOnlyList<string> Parts { get; }
        public bool IsFallback => Count == 0;

        private NativeCoolerRef(double kwPerEc, int count, IReadOnlyList<string> parts)
        {
            KwPerEc = kwPerEc;
            Count = count;
            Parts = parts;
        }

        private static bool Positive(double v) => v > 0 && !double.IsInfinity(v) && !double.IsNaN(v);

        public static NativeCoolerRef From(IEnumerable<(string Part, double Kw, double EcPerSecond)> coolers)
        {
            var valid = (coolers ?? Enumerable.Empty<(string, double, double)>())
                .Where(c => !string.IsNullOrEmpty(c.Item1) && Positive(c.Item2) && Positive(c.Item3)).ToList();
            if (valid.Count == 0) return Fallback;
            var ratios = valid.Select(c => c.Item2 / c.Item3).OrderBy(x => x).ToList();
            var parts = valid.Select(c => c.Item1).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
            return new NativeCoolerRef(ratios[(ratios.Count - 1) / 2], valid.Count, parts);
        }
    }
}
