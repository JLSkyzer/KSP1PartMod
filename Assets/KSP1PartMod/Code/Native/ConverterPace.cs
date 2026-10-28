using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The pace of one KSP1 converter mode against the native KSP2 converters (the final review's I1 ruling: the
    /// converters follow the drills' per-EC rule). A KSP1 mode consumes what it consumes per EC/s in KSP1; the native
    /// converter consumes about 500 times less per EC/s, so a KSP1 ISRU fed by a KSP1 drill (which yields like the KSP2 drill
    /// per EC/s) would idle. The mode's resource rates (inputs and outputs, electric charge excepted) are therefore scaled by
    /// one factor, so that its consumption per EC/s equals the native one; its power use, its heat and its ratios stay KSP1's.
    /// <list type="bullet">
    /// <item>consumption: the consumed inputs other than electric charge (required resources excluded), in tonnes per second
    /// (KSP2 unit masses); in units per second when a mass is unknown and the native formulas consume the same resources;</item>
    /// <item>the native figure: the native converter formulas (with an EC input) that consume one of the mode's resources, else
    /// every native converter formula (mass only); the median by consumption per EC/s (the lower one for an even count);</item>
    /// <item>not scaled (KSP1 rates, with the reason): no EC input (fuel cells, generators), no consumed resource, no native
    /// formula to compare with, an unknown mass against other resources, or a factor that is not a positive number.</item>
    /// </list>
    /// Pure.</summary>
    public sealed class ConverterPace
    {
        public const string Tonnes = "t";
        public const string Units = "units";

        /// <summary>The factor applied to every non-EC rate of the mode (1 when <see cref="Scaled"/> is false).</summary>
        public double Factor { get; private set; } = 1;
        public bool Scaled { get; private set; }
        /// <summary>The KSP1 mode's consumption per EC/s, before scaling (<see cref="Unit"/> per second per EC/s).</summary>
        public double Ksp1PerEc { get; private set; }
        /// <summary>The native converter's consumption per EC/s.</summary>
        public double NativePerEc { get; private set; }
        /// <summary><see cref="Tonnes"/> or <see cref="Units"/>.</summary>
        public string Unit { get; private set; } = Tonnes;
        /// <summary>The native formula the figure comes from: <c>part 'name'</c>.</summary>
        public string NativeFrom { get; private set; }
        /// <summary>The native figure is the median over every native converter formula (none consumes the mode's resources).</summary>
        public bool FromAll { get; private set; }
        /// <summary>Why the mode keeps KSP1's rates (null when <see cref="Scaled"/>).</summary>
        public string KeptReason { get; private set; }

        public static ConverterPace Kept(string reason) => new ConverterPace { KeptReason = reason ?? "KSP1 rates" };

        private static bool Positive(double v) => v > 0 && !double.IsInfinity(v) && !double.IsNaN(v);

        private static List<(string Name, double Rate)> Consumed(IEnumerable<(string Name, double Rate)> inputs) =>
            (inputs ?? Enumerable.Empty<(string, double)>()).Where(i => !IsruResources.IsElectricCharge(i.Name) && i.Rate > 0).ToList();

        /// <summary>The consumption of <paramref name="flows"/> in tonnes (null when a mass is unknown) or units.</summary>
        private static double? Amount(IEnumerable<(string Name, double Rate)> flows, Func<string, double> massPerUnit, bool tonnes)
        {
            var total = 0.0;
            foreach (var f in flows)
            {
                if (!tonnes)
                {
                    total += f.Rate;
                    continue;
                }
                var m = massPerUnit?.Invoke(f.Name) ?? double.NaN;
                if (!(m >= 0) || double.IsInfinity(m)) return null;
                total += f.Rate * m;
            }
            return total;
        }

        /// <param name="f">The mode, with its KSP1 rates converted to KSP2 units.</param>
        /// <param name="native">The native converter formulas (<see cref="NativeIsruRef.ConverterFormulas"/>).</param>
        /// <param name="massPerUnit">KSP2 unit mass (t); NaN when unknown.</param>
        public static ConverterPace Of(IsruFormula f, IEnumerable<NativeIsruFormula> native, Func<string, double> massPerUnit)
        {
            if (f == null) return Kept("no mode");
            if (!Positive(f.EcInput)) return Kept("no ElectricCharge input");
            var mine = Consumed(f.Inputs.Where(i => !i.Required).Select(i => (i.Name, i.Rate)));
            if (mine.Count == 0) return Kept("no consumed resource");

            var candidates = (native ?? Enumerable.Empty<NativeIsruFormula>())
                .Where(n => n != null && n.Kind == NativeIsruKind.Converter && Positive(n.Ec) && Consumed(n.Inputs).Count > 0).ToList();
            if (candidates.Count == 0) return Kept("no KSP2 converter to compare with");
            var names = new HashSet<string>(mine.Select(i => i.Name), StringComparer.Ordinal);
            var same = candidates.Where(n => Consumed(n.Inputs).Any(i => names.Contains(i.Name))).ToList();
            var fromAll = same.Count == 0;
            if (!fromAll) candidates = same;

            var tonnes = Amount(mine, massPerUnit, true) != null && candidates.All(n => Amount(Consumed(n.Inputs), massPerUnit, true) != null);
            if (!tonnes && fromAll) return Kept("a unit mass is unknown and no KSP2 converter consumes the same resource");

            var perEc = candidates
                .Select(n => (Formula: n, PerEc: Amount(Consumed(n.Inputs), massPerUnit, tonnes).Value / n.Ec))
                .Where(x => Positive(x.PerEc))
                .OrderBy(x => x.PerEc).ThenBy(x => x.Formula.Part, StringComparer.Ordinal).ThenBy(x => x.Formula.Name, StringComparer.Ordinal)
                .ToList();
            if (perEc.Count == 0) return Kept("no KSP2 converter to compare with");
            var median = perEc[(perEc.Count - 1) / 2];
            var ksp1 = Amount(mine, massPerUnit, tonnes).Value / f.EcInput;
            var factor = median.PerEc / ksp1;
            var pace = new ConverterPace
            {
                Ksp1PerEc = ksp1,
                NativePerEc = median.PerEc,
                Unit = tonnes ? Tonnes : Units,
                NativeFrom = $"{median.Formula.Part} '{median.Formula.Name}'",
                FromAll = fromAll,
            };
            if (!Positive(factor) || !Positive(ksp1))
            {
                pace.KeptReason = "the factor is not a positive number";
                return pace;
            }
            pace.Factor = factor;
            pace.Scaled = true;
            return pace;
        }
    }
}
