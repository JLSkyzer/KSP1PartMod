using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>One resource of a native formula: the KSP2 name and rate (units/s) the glue writes into a
    /// <c>PartModuleResourceSetting</c>, with the KSP1 name and ratio it came from.</summary>
    public sealed class IsruFlow
    {
        /// <summary>The KSP1 resource name (the first one, when several KSP1 flows were summed).</summary>
        public string Ksp1Name { get; }
        /// <summary>The KSP2 resource name.</summary>
        public string Name { get; }
        /// <summary>KSP1 units/s (summed).</summary>
        public double Ksp1Ratio { get; }
        /// <summary>KSP2 units/s (summed); 0 for a required resource.</summary>
        public double Rate { get; }
        /// <summary>A KSP1 <c>REQUIRED_RESOURCE</c>: present in the input list with rate 0, never consumed.</summary>
        public bool Required { get; }

        public IsruFlow(string ksp1Name, string name, double ksp1Ratio, double rate, bool required = false)
        {
            Ksp1Name = ksp1Name;
            Name = name;
            Ksp1Ratio = ksp1Ratio;
            Rate = rate;
            Required = required;
        }

        /// <summary>The same flow at <paramref name="factor"/> times its KSP2 rate (the KSP1 ratio kept).</summary>
        internal IsruFlow Scaled(double factor) => new IsruFlow(Ksp1Name, Name, Ksp1Ratio, Rate * factor, Required);

        internal IsruFlow Plus(IsruFlow other) =>
            new IsruFlow(Ksp1Name, Name, Ksp1Ratio + other.Ksp1Ratio, Rate + other.Rate, Required && other.Required);
    }

    /// <summary>One native formula (a converter mode, or the mining formula of a drill), pure: the glue copies it into a
    /// <c>ResourceConverterFormulaDefinition</c>.</summary>
    public sealed class IsruFormula
    {
        /// <summary>The position of the KSP1 module it came from among the part's modules of its kind (0-based).</summary>
        public int Index { get; internal set; }
        /// <summary>The native <c>InternalName</c>: <c>ksp1_&lt;index&gt;</c> (stable), or <c>ksp1_mine</c>.</summary>
        public string Id { get; internal set; }
        /// <summary>The text the mode's name comes from: the KSP1 <c>ConverterName</c> (it may be an <c>#autoLOC</c> tag), or
        /// <c>Converter &lt;index+1&gt;</c> when absent.</summary>
        public string LabelSource { get; internal set; }
        /// <summary>Inputs, merged by KSP2 name, in cfg order (required resources at rate 0).</summary>
        public IReadOnlyList<IsruFlow> Inputs { get; internal set; } = Array.Empty<IsruFlow>();
        /// <summary>Outputs, merged by KSP2 name, in cfg order.</summary>
        public IReadOnlyList<IsruFlow> Outputs { get; internal set; } = Array.Empty<IsruFlow>();
        /// <summary>The native <c>FluxGenerated</c> (kW).</summary>
        public double FluxGenerated { get; internal set; }
        /// <summary>The KSP1 <c>AutoShutdown</c> (false when absent).</summary>
        public bool AutoShutdown { get; internal set; }
        /// <summary>Electric charge consumed (units/s, required resources excluded).</summary>
        public double EcInput { get; internal set; }
        /// <summary>Tonnes per second consumed and produced (KSP2 unit masses; an unknown mass counts 0, see
        /// <see cref="MassKnown"/>).</summary>
        public double MassIn { get; internal set; }
        public double MassOut { get; internal set; }
        /// <summary>Every consumed resource has a known unit mass.</summary>
        public bool MassInKnown { get; private set; } = true;
        /// <summary>Every produced resource has a known unit mass.</summary>
        public bool MassOutKnown { get; private set; } = true;
        public bool MassKnown => MassInKnown && MassOutKnown;
        /// <summary>A converter mode's pace against the native converters (null for a drill, or when not computed).</summary>
        public ConverterPace Pace { get; internal set; }

        internal void ComputeTotals(IsruResources r)
        {
            EcInput = Inputs.Where(f => !f.Required && IsruResources.IsElectricCharge(f.Name)).Sum(f => f.Rate);
            MassIn = Mass(Inputs.Where(f => !f.Required), r, out var inKnown);
            MassOut = Mass(Outputs, r, out var outKnown);
            MassInKnown = inKnown;
            MassOutKnown = outKnown;
        }

        private static double Mass(IEnumerable<IsruFlow> flows, IsruResources r, out bool known)
        {
            known = true;
            var total = 0.0;
            foreach (var f in flows)
            {
                var m = r.MassPerUnit(f.Name);
                if (double.IsNaN(m)) known = false;
                else total += f.Rate * m;
            }
            return total;
        }
    }

    /// <summary>Reading KSP1 cfg values for the ISRU rules: the text before a <c>//</c> comment, invariant numbers, KSP1
    /// bools; the first value of a key wins, as in KSP1.</summary>
    public static class IsruCfg
    {
        public static string Text(string value)
        {
            value = value ?? "";
            var comment = value.IndexOf("//", StringComparison.Ordinal);
            return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
        }

        /// <summary>The first value of <paramref name="key"/>, comment stripped; null when absent.</summary>
        public static string Value(IKsp1Node node, string key)
        {
            var v = node?.GetValue(key);
            return v == null ? null : Text(v);
        }

        public static bool TryNumber(string value, out double result) =>
            double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && !double.IsNaN(result) && !double.IsInfinity(result);

        /// <summary>The first value as a number; null when absent or not a number.</summary>
        public static double? Number(IKsp1Node node, string key) => TryNumber(Value(node, key), out var d) ? d : (double?)null;

        /// <summary>The first value as a KSP1 bool (any case); null when absent or not a bool.</summary>
        public static bool? Bool(IKsp1Node node, string key) =>
            bool.TryParse(Value(node, key) ?? "", out var b) ? b : (bool?)null;

        public static bool IsModule(IKsp1Node module, string name) =>
            string.Equals(Value(module, "name"), name, StringComparison.OrdinalIgnoreCase);

        /// <summary>G6, invariant.</summary>
        public static string Inv(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        internal static void AddOnce(List<string> list, string item)
        {
            if (!list.Contains(item)) list.Add(item);
        }

        /// <summary>Merges flows of the same KSP2 name (rates summed, first KSP1 name kept), in first-seen order.</summary>
        internal static List<IsruFlow> Merge(IEnumerable<IsruFlow> flows)
        {
            var merged = new List<IsruFlow>();
            foreach (var f in flows)
            {
                var at = merged.FindIndex(m => m.Name == f.Name);
                if (at < 0) merged.Add(f);
                else merged[at] = merged[at].Plus(f);
            }
            return merged;
        }
    }
}
