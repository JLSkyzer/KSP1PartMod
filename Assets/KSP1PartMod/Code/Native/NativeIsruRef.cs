using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    public enum NativeIsruKind { Mine, Converter, Scanner }

    /// <summary>One formula of a native KSP2 part, as the glue reads it from the game's part data: a <c>Data_Mine</c>
    /// mining formula, one <c>Data_ResourceConverter</c> formula, or a <c>Data_ResourceScanner</c> (inputs = required
    /// resources, outputs = scannable resources with rate 0). Pure.</summary>
    public sealed class NativeIsruFormula
    {
        public NativeIsruKind Kind { get; private set; }
        public string Part { get; private set; }
        public string Name { get; private set; }
        public IReadOnlyList<(string Name, double Rate)> Inputs { get; private set; }
        public IReadOnlyList<(string Name, double Rate)> Outputs { get; private set; }
        public double FluxGenerated { get; private set; }
        public double TimeToComplete { get; private set; }
        public double MinAltitude { get; private set; }
        public double MaxAltitude { get; private set; }
        public double MinInclination { get; private set; }
        public double MaxInclination { get; private set; }

        private static IReadOnlyList<(string, double)> List(IEnumerable<(string, double)> flows) =>
            (flows ?? Enumerable.Empty<(string, double)>()).Where(f => !string.IsNullOrEmpty(f.Item1)).ToList();

        public static NativeIsruFormula Mine(string part, IEnumerable<(string Name, double Rate)> inputs,
            IEnumerable<(string Name, double Rate)> outputs, double flux) =>
            new NativeIsruFormula { Kind = NativeIsruKind.Mine, Part = part, Name = "", Inputs = List(inputs), Outputs = List(outputs), FluxGenerated = flux };

        public static NativeIsruFormula Converter(string part, string name, IEnumerable<(string Name, double Rate)> inputs,
            IEnumerable<(string Name, double Rate)> outputs, double flux) =>
            new NativeIsruFormula { Kind = NativeIsruKind.Converter, Part = part, Name = name ?? "", Inputs = List(inputs), Outputs = List(outputs), FluxGenerated = flux };

        public static NativeIsruFormula Scanner(string part, IEnumerable<(string Name, double Rate)> required, IEnumerable<string> scannable,
            double timeToComplete, double minAltitude, double maxAltitude, double minInclination, double maxInclination) =>
            new NativeIsruFormula
            {
                Kind = NativeIsruKind.Scanner, Part = part, Name = "", Inputs = List(required),
                Outputs = List((scannable ?? Enumerable.Empty<string>()).Select(s => (s, 0.0))),
                TimeToComplete = timeToComplete, MinAltitude = minAltitude, MaxAltitude = maxAltitude,
                MinInclination = minInclination, MaxInclination = maxInclination,
            };

        internal double Ec => Inputs.Where(f => IsruResources.IsElectricCharge(f.Name)).Sum(f => f.Rate);
    }

    /// <summary>The native drill a KSP1 drill is compared with: its EC, heat per EC/s and outputs at density 1.</summary>
    public sealed class NativeDrillRef
    {
        public string Part { get; }
        public double Ec { get; }
        public double HeatPerEc { get; }
        public IReadOnlyList<(string Name, double Rate)> Outputs { get; }
        public bool IsFallback { get; }

        public NativeDrillRef(string part, double ec, double heatPerEc, IReadOnlyList<(string Name, double Rate)> outputs, bool isFallback)
        {
            Part = part;
            Ec = ec;
            HeatPerEc = heatPerEc;
            Outputs = outputs ?? Array.Empty<(string, double)>();
            IsFallback = isFallback;
        }

        /// <summary>The drill's rate of <paramref name="resource"/> at density 1, 0 when it does not mine it.</summary>
        public double RateOf(string resource) => Outputs.Where(o => o.Name == resource).Sum(o => o.Rate);
    }

    /// <summary>The native scanner whose fields a KSP1 scanner takes.</summary>
    public sealed class NativeScannerRef
    {
        /// <summary>26w42c, <c>scanner_1v_orbital</c> (plan (e), V).</summary>
        public static readonly NativeScannerRef Fallback = new NativeScannerRef("scanner_1v_orbital", 14,
            new[] { "Water", "CarbonRegolith", "Ammonia", "Uranium" }, 5, 0, 5000000, 80, 100, true);

        public string Part { get; }
        /// <summary>EC/s while scanning.</summary>
        public double Ec { get; }
        public IReadOnlyList<string> Resources { get; }
        public double TimeToComplete { get; }
        public double MinAltitude { get; }
        public double MaxAltitude { get; }
        public double MinInclination { get; }
        public double MaxInclination { get; }
        public bool IsFallback { get; }

        public NativeScannerRef(string part, double ec, IReadOnlyList<string> resources, double timeToComplete, double minAltitude,
            double maxAltitude, double minInclination, double maxInclination, bool isFallback = false)
        {
            Part = part;
            Ec = ec;
            Resources = resources ?? Array.Empty<string>();
            TimeToComplete = timeToComplete;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            MinInclination = minInclination;
            MaxInclination = maxInclination;
            IsFallback = isFallback;
        }
    }

    /// <summary>The native ISRU references (plan (e)), read from the game's KSP2 parts by the glue:
    /// <list type="bullet">
    /// <item>the drill: among the <c>Data_Mine</c> formulas with an EC input, some heat and an output, the median by heat per
    /// EC/s (the lower one for an even count);</item>
    /// <item>the converters' heat per EC/s: the median over the converter formulas with an EC input and some heat;</item>
    /// <item>the scanner: the first scanner with resources, by part name.</item>
    /// </list>
    /// Each falls back to the 26w42c values of plan (e) when none is valid. Pure.</summary>
    public sealed class NativeIsruRef
    {
        public const double FallbackConverterHeatPerEc = 10.5;

        public static readonly NativeDrillRef FallbackDrill = new NativeDrillRef("drill_2v_radial", 20, 17.5,
            new[] { ("Water", 4.333e-5), ("Ammonia", 6.194e-5), ("CarbonRegolith", 5.556e-5), ("Uranium", 1e-5) }, true);

        /// <summary>The minable set when neither the density maps nor the native drill can be read (26w42c).</summary>
        public static readonly IReadOnlyList<string> FallbackMinable = new[] { "Water", "Ammonia", "CarbonRegolith", "Uranium" };

        public static readonly NativeIsruRef Fallback = new NativeIsruRef(FallbackDrill, FallbackConverterHeatPerEc, 0, NativeScannerRef.Fallback);

        public NativeDrillRef Drill { get; }
        public double ConverterHeatPerEc { get; }
        /// <summary>The converter formulas the median was taken over; 0 for the fallback.</summary>
        public int ConverterModes { get; }
        public bool ConvertersAreFallback => ConverterModes == 0;
        public NativeScannerRef Scanner { get; }

        /// <summary>The native drill's outputs, the second source of the minable set.</summary>
        public IReadOnlyList<string> MinableFromDrill => Drill.Outputs.Select(o => o.Name).Distinct(StringComparer.Ordinal).ToList();

        private NativeIsruRef(NativeDrillRef drill, double converterHeatPerEc, int converterModes, NativeScannerRef scanner)
        {
            Drill = drill;
            ConverterHeatPerEc = converterHeatPerEc;
            ConverterModes = converterModes;
            Scanner = scanner;
        }

        private static bool Positive(double v) => v > 0 && !double.IsInfinity(v) && !double.IsNaN(v);

        public static NativeIsruRef From(IEnumerable<NativeIsruFormula> formulas)
        {
            var all = (formulas ?? Enumerable.Empty<NativeIsruFormula>()).Where(f => f != null).ToList();

            var drills = all.Where(f => f.Kind == NativeIsruKind.Mine && Positive(f.Ec) && Positive(f.FluxGenerated) && f.Outputs.Count > 0)
                .OrderBy(f => f.FluxGenerated / f.Ec).ThenBy(f => f.Part, StringComparer.Ordinal).ToList();
            var drill = FallbackDrill;
            if (drills.Count > 0)
            {
                var d = drills[(drills.Count - 1) / 2];
                drill = new NativeDrillRef(d.Part, d.Ec, d.FluxGenerated / d.Ec, d.Outputs, false);
            }

            var heats = all.Where(f => f.Kind == NativeIsruKind.Converter && Positive(f.Ec) && Positive(f.FluxGenerated))
                .Select(f => f.FluxGenerated / f.Ec).OrderBy(h => h).ToList();
            var converterHeat = heats.Count > 0 ? heats[(heats.Count - 1) / 2] : FallbackConverterHeatPerEc;

            var scanner = all.Where(f => f.Kind == NativeIsruKind.Scanner && f.Outputs.Count > 0)
                .OrderBy(f => f.Part, StringComparer.Ordinal).FirstOrDefault();
            var scannerRef = scanner == null
                ? NativeScannerRef.Fallback
                : new NativeScannerRef(scanner.Part, scanner.Ec, scanner.Outputs.Select(o => o.Name).ToList(), scanner.TimeToComplete,
                    scanner.MinAltitude, scanner.MaxAltitude, scanner.MinInclination, scanner.MaxInclination);

            return new NativeIsruRef(drill, converterHeat, heats.Count, scannerRef);
        }
    }
}
