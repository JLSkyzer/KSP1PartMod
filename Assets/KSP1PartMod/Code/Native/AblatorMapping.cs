using System;
using System.Collections.Generic;
using System.Globalization;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>KSP1 <c>ModuleAblator</c> read as KSP2's native <c>Module_Heatshield</c> (plan (f)):
    /// <list type="bullet">
    /// <item><c>ablativeResource</c> (default <c>Ablator</c>) → the resource, through the injected mapper (Redux's
    /// <c>Ksp1ResourceMapper.MapResourceName</c> in game);</item>
    /// <item><c>ablationTempThresh</c> (default 500 K, S) → <c>AblationTempThreshold</c>;</item>
    /// <item><c>Rate = reference Rate × (capacity / reference capacity) × (6000 / pyrolysisLossFactor) × ablator_rate_scale</c>:
    /// a higher <c>pyrolysisLossFactor</c> removes more heat per unit and burns slower, 6000 being the stock KSP1 value.
    /// The capacity is the part's capacity of that resource in KSP2 units (after Redux's conversion), as the reference's;</item>
    /// <item>fixed: <c>PyrolysisLossFactor</c> 1 (folded into the Rate), <c>ShieldingScale</c> 1, <c>ShieldingDirection</c>
    /// local -Y (KSP1 shields face down the stack, S), <c>DisabledWhenRetracted</c> false, <c>UseChar</c> false (the char is
    /// our Color entry), no char material;</item>
    /// <item>not reproduced: the temperature shape of <c>lossExp</c>/<c>lossConst</c> and <c>reentryConductivity</c> (no KSP1
    /// thermal model; the summary says so), and per part, noted: <see cref="NotReproducedKeys"/>, unknown keys and nodes.</item>
    /// </list>
    /// Pure: no Unity, game or Redux type.</summary>
    public sealed class AblatorMapping
    {
        public const string Ksp1Module = "ModuleAblator";
        public const string NativeModule = "Module_Heatshield";
        public const string DefaultResource = "Ablator";
        /// <summary>KSP1's <c>ablationTempThresh</c> when absent (S).</summary>
        public const double DefaultThreshold = 500;
        /// <summary>The stock KSP1 <c>pyrolysisLossFactor</c>: the reference of the Rate rule; also used when absent (S).</summary>
        public const double StockPyrolysisLossFactor = 6000;

        /// <summary>The keys this mapping reads or the summary covers.</summary>
        public static readonly IReadOnlyList<string> KnownKeys = new[]
        {
            "name", "ablativeResource", "ablationTempThresh", "pyrolysisLossFactor", "lossExp", "lossConst", "reentryConductivity",
            "useChar", "charModuleName",
        };

        /// <summary>Fields of KSP1's module with no counterpart, noted per part when present and counted in the summary.</summary>
        public static readonly IReadOnlyList<string> NotReproducedKeys = new[]
        {
            "outputResource", "charMin", "charMax", "charAlpha", "nominalAmountRecip", "infoTemp",
        };

        /// <summary>The note of a second <c>ModuleAblator</c> on one part (KSP2's module is unique per part).</summary>
        public const string ExtraModuleNote = Ksp1Module + ": a second one on the part is not imported (" + NativeModule + " is one per part)";

        public static string NoResourceNote(string resource) =>
            $"{Ksp1Module}: the part has no {resource} resource, no {NativeModule} (nothing to burn)";

        /// <summary>The KSP1 resource name (<c>ablativeResource</c>).</summary>
        public string Ksp1Resource { get; private set; } = DefaultResource;

        /// <summary>The KSP2 resource the shield burns.</summary>
        public string ResourceName { get; private set; } = DefaultResource;

        /// <summary>Units per second per kW of shielded flux (with <see cref="PyrolysisLossFactor"/> = 1).</summary>
        public double Rate { get; private set; }

        public double AblationTempThreshold { get; private set; } = DefaultThreshold;

        /// <summary>KSP1's <c>pyrolysisLossFactor</c> used in the rule.</summary>
        public double Ksp1PyrolysisLossFactor { get; private set; } = StockPyrolysisLossFactor;

        public double Capacity { get; private set; }
        public double Scale { get; private set; } = 1;
        public NativeShieldRef Reference { get; private set; }

        // The fixed fields of plan (f).
        public double PyrolysisLossFactor => 1;
        public float ShieldingScale => 1f;
        /// <summary>Local shielding direction (x, y, z): -Y.</summary>
        public (float X, float Y, float Z) ShieldingDirection => (0f, -1f, 0f);
        public bool DisabledWhenRetracted => false;
        public bool UseChar => false;
        public string CharMaterialName => "";

        /// <summary>The <see cref="NotReproducedKeys"/>, unknown keys and child nodes present, in the module's order (each
        /// once), for the summary counts.</summary>
        public IReadOnlyList<string> NotReproduced => _notReproduced;

        /// <summary>One line each, in the module's order.</summary>
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<string> _notReproduced = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <param name="module">The KSP1 <c>ModuleAblator</c> node.</param>
        /// <param name="capacity">The part's capacity of the shield resource, in KSP2 units (above 0).</param>
        /// <param name="reference">The native shield (<see cref="NativeShieldRef.Fallback"/> when null).</param>
        /// <param name="scale"><c>ablator_rate_scale</c>; not finite or not above 0: 1.</param>
        /// <param name="mapResource">KSP1 resource name → KSP2 name; null or a null result keeps the KSP1 name.</param>
        public static AblatorMapping From(IKsp1Node module, double capacity, NativeShieldRef reference, double scale,
            Func<string, string> mapResource = null)
        {
            var m = new AblatorMapping
            {
                Reference = reference ?? NativeShieldRef.Fallback,
                Capacity = capacity,
                Scale = scale > 0 && !double.IsInfinity(scale) && !double.IsNaN(scale) ? scale : 1,
            };
            if (module != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in module.AllValues)
                {
                    var key = kv.Key?.Trim() ?? "";
                    var value = StripComment(kv.Value);
                    // KSP1 reads the first value of a key; a repeat is noted, never applied.
                    if (!seen.Add(key))
                    {
                        if (key != "name") m._notes.Add($"{Ksp1Module}: {key} repeated, the first value kept");
                        continue;
                    }
                    switch (key)
                    {
                        case "ablativeResource":
                            if (value.Length > 0) m.Ksp1Resource = value;
                            break;
                        case "ablationTempThresh":
                            if (TryNumber(value, out var t) && t > 0) m.AblationTempThreshold = t;
                            else m._notes.Add($"{Ksp1Module}: ablationTempThresh '{value}' is not a temperature, {Inv(DefaultThreshold)} K kept");
                            break;
                        case "pyrolysisLossFactor":
                            if (TryNumber(value, out var p) && p > 0) m.Ksp1PyrolysisLossFactor = p;
                            else m._notes.Add($"{Ksp1Module}: pyrolysisLossFactor '{value}' is not above 0, {Inv(StockPyrolysisLossFactor)} kept");
                            break;
                        default:
                            if (Contains(KnownKeys, key)) break;
                            m._notReproduced.Add(key);
                            m._notes.Add($"{Ksp1Module}: {key} not reproduced");
                            break;
                    }
                }
                foreach (var child in module.AllNodes)
                {
                    var name = "node " + child.Name;
                    if (m._notReproduced.Contains(name)) continue;
                    m._notReproduced.Add(name);
                    m._notes.Add($"{Ksp1Module}: {name} not reproduced");
                }
            }
            m.ResourceName = mapResource?.Invoke(m.Ksp1Resource) ?? m.Ksp1Resource;
            var r = m.Reference;
            m.Rate = r.Rate * (capacity / r.Capacity) * (StockPyrolysisLossFactor / m.Ksp1PyrolysisLossFactor) * m.Scale;
            return m;
        }

        /// <summary>The values used, logged once per part so they can be settled in game.</summary>
        public string ValuesNote() =>
            $"{Ksp1Module} -> {NativeModule}: {ResourceName} capacity {Inv(Capacity)}, Rate {Inv(Rate)} " +
            $"(reference {Inv(Reference.Rate)} for {Inv(Reference.Capacity)}{(Reference.IsFallback ? ", fallback" : "")}, " +
            $"pyrolysisLossFactor {Inv(Ksp1PyrolysisLossFactor)}, ablator_rate_scale {Inv(Scale)}), " +
            $"ablation from {Inv(AblationTempThreshold)} K, shielding along local -Y";

        /// <summary>A KSP1 number: the text before a <c>//</c> comment, invariant culture.</summary>
        public static bool TryNumber(string value, out double result) =>
            double.TryParse(StripComment(value), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && !double.IsNaN(result) && !double.IsInfinity(result);

        private static string StripComment(string value)
        {
            value = value ?? "";
            var comment = value.IndexOf("//", StringComparison.Ordinal);
            return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
        }

        private static bool Contains(IReadOnlyList<string> keys, string key)
        {
            foreach (var k in keys)
                if (k == key) return true;
            return false;
        }

        public static string Inv(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
    }
}
