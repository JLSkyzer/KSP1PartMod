using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>One harvested resource of a KSP1 drill.</summary>
    public sealed class HarvestOutput
    {
        public string Ksp1Name { get; }
        public string Name { get; }
        public double Efficiency { get; }
        /// <summary>The same rate in KSP1 units/s at density 1 (through the containers' conversion), for the log.</summary>
        public double Ksp1Rate { get; }
        /// <summary>KSP2 units/s at density 1: <c>NativePerEc × Ec × EfficiencyFactor × multiplier</c>.</summary>
        public double Rate { get; }
        /// <summary>This harvester's EC/s.</summary>
        public double Ec { get; }
        /// <summary>The native drill's yield of <see cref="Name"/> per EC/s at density 1.</summary>
        public double NativePerEc { get; }
        /// <summary>The native drill <see cref="NativePerEc"/> comes from; null: the reference drill's median (no native drill
        /// mines <see cref="Name"/>).</summary>
        public string NativeFrom { get; }
        /// <summary><c>Efficiency / (0.1 × Ec)</c>: KSP1's efficiency per EC/s against the stock drills'; 1 when not meaningful.</summary>
        public double EfficiencyFactor { get; }

        /// <summary>This drill's yield per EC/s at density 1; 0 without EC.</summary>
        public double PerEc => Ec > 0 ? Rate / Ec : 0;

        public HarvestOutput(string ksp1Name, string name, double efficiency, double ksp1Rate, double rate, double ec = 0,
            double nativePerEc = 0, string nativeFrom = null, double efficiencyFactor = 1)
        {
            Ksp1Name = ksp1Name;
            Name = name;
            Efficiency = efficiency;
            Ksp1Rate = ksp1Rate;
            Rate = rate;
            Ec = ec;
            NativePerEc = nativePerEc;
            NativeFrom = nativeFrom;
            EfficiencyFactor = efficiencyFactor;
        }
    }

    /// <summary>The planetary KSP1 <c>ModuleResourceHarvester</c>s of a part read as one native <c>Module_Mine</c>
    /// (plan (j)): every <c>HarvesterType 0</c> harvester merged into one mining formula.
    /// <list type="bullet">
    /// <item>output per harvested resource, derived from the native drill per unit of electricity (ruling of 2026-10-28):
    /// <c>Rate = nativePerEc(mapped) × EC × Efficiency / (0.1 × EC) × multiplier</c>, in KSP2 units/s at density 1.
    /// <c>nativePerEc</c> is what a native drill mines of that resource per EC/s (<see cref="NativeIsruRef.YieldPerEc"/>);
    /// 0.1 is the stock KSP1 drills' Efficiency per EC/s (V: MiniDrill 0.3 for 3 EC/s, RadialDrill 1.5 for 15 EC/s), so the
    /// stock drills yield exactly the native drill's per EC/s, and a drill that KSP1 makes more efficient per EC/s yields
    /// more. The factor is 1 when <c>Efficiency</c> is missing; without an EC input the yield is
    /// <c>nativePerEc × Efficiency / 0.1 × multiplier</c>. The mapped resource must be minable (else the harvester is
    /// skipped);</item>
    /// <item>inputs summed by resource, converted as the converters' are;</item>
    /// <item><c>FluxGenerated</c>: the sum, over the harvesters with <c>GeneratesHeat</c>, of
    /// <c>heatPerEc × EC input × heatScale</c> (<c>heatPerEc</c>: the native drill's);</item>
    /// <item>the impact data (<c>ImpactTransform</c>, <c>ImpactRange</c>) of the first kept harvester;</item>
    /// <item>skipped and counted: <c>HarvesterType</c> 1 (ocean), 2 (atmospheric), 3 (exospheric), other types, a resource
    /// without a map, a resource the game does not know;</item>
    /// <item>not reproduced, counted once per part by field: <c>HarvestThreshold</c>, <c>ThermalEfficiency</c>,
    /// <c>TemperatureModifier</c>, the specialist bonus, <c>EfficiencyBonus</c> (when not 1), and any other key or node.</item>
    /// </list>
    /// <c>ModuleAsteroidDrill</c>/<c>ModuleCometDrill</c> are counted by <see cref="CountAsteroidDrills"/> only. Pure.</summary>
    public sealed class HarvesterMapping
    {
        public const string Ksp1Module = "ModuleResourceHarvester";
        public const string NativeModule = "Module_Mine";
        public const string AsteroidDrillModule = "ModuleAsteroidDrill";
        public const string CometDrillModule = "ModuleCometDrill";
        /// <summary><c>harvest_multiplier</c> when the setting is unusable: KSP1 drills yield as KSP2's per EC/s.</summary>
        public const double DefaultMultiplier = 1;
        /// <summary>The stock KSP1 drills' <c>Efficiency</c> per EC/s (V, the installed cfgs, 2026-10-28: MiniDrill 0.3 for 3,
        /// RadialDrill 1.5 for 15).</summary>
        public const double Ksp1EfficiencyPerEc = 0.1;

        public static readonly IReadOnlyList<string> KnownKeys = new[]
        {
            "name", "HarvesterType", "Efficiency", "ResourceName", "ConverterName", "StartActionName", "StopActionName",
            "ToggleActionName", "ImpactTransform", "ImpactRange", "AutoShutdown", "GeneratesHeat", "UseSpecialistBonus",
            "UseSpecialistHeatBonus", "SpecialistEfficiencyFactor", "SpecialistBonusBase", "SpecialistHeatFactor",
            "ExperienceEffect", "EfficiencyBonus", "HarvestThreshold", "moduleIsEnabled", "tag",
        };

        public bool HasModule => _outputs.Count > 0;
        /// <summary>The mining formula (outputs merged by resource); empty when <see cref="HasModule"/> is false.</summary>
        public IsruFormula Formula { get; private set; } = new IsruFormula { Id = MineId, LabelSource = "" };
        /// <summary>One per kept harvester, in cfg order.</summary>
        public IReadOnlyList<HarvestOutput> Outputs => _outputs;
        /// <summary>The kept harvesters.</summary>
        public int Merged => _outputs.Count;
        /// <summary><c>harvest_multiplier</c> as used.</summary>
        public double Multiplier { get; private set; } = DefaultMultiplier;
        public string ImpactTransform { get; private set; } = "";
        public double ImpactRange { get; private set; }

        /// <summary>Kept harvesters whose cfg omits <c>AutoShutdown</c> / <c>GeneratesHeat</c> (read as false, S: KSP1's
        /// defaults are not proven), for the census.</summary>
        public int OmittedAutoShutdown { get; private set; }
        public int OmittedGeneratesHeat { get; private set; }
        public int SkippedOcean { get; private set; }
        public int SkippedAtmospheric { get; private set; }
        public int SkippedExospheric { get; private set; }
        public int SkippedNoMap { get; private set; }
        /// <summary>Another <c>HarvesterType</c>, no <c>ResourceName</c>, a bad number, or an input the game does not know.</summary>
        public int SkippedOther { get; private set; }

        public IReadOnlyList<string> NotReproduced => _notReproduced;
        public IReadOnlyList<string> Notes => _notes;

        public const string MineId = "ksp1_mine";

        private readonly List<HarvestOutput> _outputs = new List<HarvestOutput>();
        private readonly List<string> _notReproduced = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <summary>The <c>ModuleAsteroidDrill</c> and <c>ModuleCometDrill</c> modules of a part (no asteroids or comets in KSP2).</summary>
        public static int CountAsteroidDrills(IEnumerable<IKsp1Node> partModules) =>
            (partModules ?? Enumerable.Empty<IKsp1Node>())
            .Count(m => IsruCfg.IsModule(m, AsteroidDrillModule) || IsruCfg.IsModule(m, CometDrillModule));

        public static string TypeName(int type) =>
            type == 0 ? "planetary" : type == 1 ? "ocean" : type == 2 ? "atmospheric" : type == 3 ? "exospheric" : "unknown";

        /// <param name="harvesters">The part's <c>ModuleResourceHarvester</c> nodes, in cfg order.</param>
        /// <param name="native">The native references (yields per EC/s); null: <see cref="NativeIsruRef.Fallback"/>.</param>
        /// <param name="multiplier"><c>harvest_multiplier</c>; not above 0 or not finite: <see cref="DefaultMultiplier"/>.</param>
        /// <param name="heatPerEc">kW per EC/s of the native drill; not above 0 or not finite: the fallback.</param>
        /// <param name="heatScale"><c>isru_heat_scale</c>; negative or not finite: 1.</param>
        public static HarvesterMapping From(IReadOnlyList<IKsp1Node> harvesters, IsruResources r, NativeIsruRef native, double multiplier,
            double heatPerEc, double heatScale)
        {
            var m = new HarvesterMapping();
            r = r ?? new IsruResources(null, null, null, null);
            native = native ?? NativeIsruRef.Fallback;
            if (multiplier > 0 && !double.IsInfinity(multiplier)) m.Multiplier = multiplier;
            if (!(heatPerEc > 0) || double.IsInfinity(heatPerEc)) heatPerEc = NativeIsruRef.FallbackDrill.HeatPerEc;
            if (!(heatScale >= 0) || double.IsInfinity(heatScale)) heatScale = 1;

            var inputs = new List<IsruFlow>();
            var outputs = new List<IsruFlow>();
            var flux = 0.0;
            var impactSet = false;
            foreach (var h in (harvesters ?? Array.Empty<IKsp1Node>()).Where(n => n != null))
            {
                var typeText = IsruCfg.Value(h, "HarvesterType");
                var type = 0;
                if (typeText != null && (!IsruCfg.TryNumber(typeText, out var t) || t != Math.Floor(t) || t < 0 || t > 3))
                {
                    m.SkippedOther++;
                    m._notes.Add($"{Ksp1Module}: HarvesterType '{typeText}' unknown, skipped");
                    continue;
                }
                if (typeText != null) type = (int)double.Parse(typeText, System.Globalization.CultureInfo.InvariantCulture);
                if (type != 0)
                {
                    if (type == 1) m.SkippedOcean++;
                    else if (type == 2) m.SkippedAtmospheric++;
                    else m.SkippedExospheric++;
                    m._notes.Add($"{Ksp1Module}: HarvesterType {type} ({TypeName(type)}) skipped, {NativeModule} only mines the ground");
                    continue;
                }
                var resource = IsruCfg.Value(h, "ResourceName");
                if (string.IsNullOrEmpty(resource))
                {
                    m.SkippedOther++;
                    m._notes.Add($"{Ksp1Module}: no ResourceName, skipped");
                    continue;
                }
                var mapped = r.Map(resource);
                if (!r.IsMinable(mapped))
                {
                    m.SkippedNoMap++;
                    m._notes.Add($"{Ksp1Module}: {resource} -> {mapped} has no resource map in KSP2, harvester skipped");
                    continue;
                }
                var efficiencyText = IsruCfg.Value(h, "Efficiency");
                double efficiency = 1;
                if (efficiencyText == null) m._notes.Add($"{Ksp1Module}: {resource}: Efficiency missing, the KSP2 drill's yield per EC/s used as is");
                else if (!IsruCfg.TryNumber(efficiencyText, out efficiency) || efficiency < 0)
                {
                    m.SkippedOther++;
                    m._notes.Add($"{Ksp1Module}: {resource}: Efficiency '{efficiencyText}' is not a number of 0 or more, skipped");
                    continue;
                }

                var own = new List<IsruFlow>();
                string drop = null;
                foreach (var child in h.AllNodes)
                {
                    var kind = child.Name?.Trim() ?? "";
                    if (kind == "INPUT_RESOURCE")
                    {
                        var name = IsruCfg.Value(child, "ResourceName");
                        var ratioText = IsruCfg.Value(child, "Ratio");
                        if (string.IsNullOrEmpty(name)) { m._notes.Add($"{Ksp1Module}: INPUT_RESOURCE without ResourceName ignored"); continue; }
                        if (!IsruCfg.TryNumber(ratioText, out var ratio) || ratio < 0) { drop = drop ?? $"{name} Ratio '{ratioText}' is not a number of 0 or more"; continue; }
                        var (inName, inRate) = r.MappedRate(name, ratio);
                        if (!r.IsKnown(inName)) { drop = drop ?? $"{name} -> {inName} is not a game resource"; continue; }
                        own.Add(new IsruFlow(name, inName, ratio, inRate));
                    }
                    else if (kind == "ThermalEfficiency" || kind == "TemperatureModifier")
                        m.NotReproducedField(kind, $"{Ksp1Module}: {kind} not reproduced (no core temperature in KSP2, full rate)");
                    else m.NotReproducedField("node " + kind, $"{Ksp1Module}: node {kind} not reproduced");
                }
                if (drop != null)
                {
                    m.SkippedOther++;
                    m._notes.Add($"{Ksp1Module}: {resource}: {drop}, harvester skipped");
                    continue;
                }
                m.ReadKeys(h);

                var ec = own.Where(f => IsruResources.IsElectricCharge(f.Name)).Sum(f => f.Rate);
                var perEc = native.YieldPerEc(mapped, out var from);
                if (!(perEc > 0))
                {
                    perEc = native.MedianYieldPerEc;
                    from = null;
                    m._notes.Add($"{Ksp1Module}: {resource} -> {mapped}: no KSP2 drill mines it, the KSP2 drill's median yield per EC/s used");
                }
                double factor = 1, rate;
                if (ec > 0)
                {
                    if (efficiencyText != null) factor = efficiency / (Ksp1EfficiencyPerEc * ec);
                    rate = perEc * ec * factor * m.Multiplier;
                }
                else
                {
                    rate = perEc * efficiency / Ksp1EfficiencyPerEc * m.Multiplier;
                    m._notes.Add($"{Ksp1Module}: {resource}: no ElectricCharge input, yield from Efficiency alone " +
                                 $"(the KSP2 drill's per EC/s x Efficiency / {IsruCfg.Inv(Ksp1EfficiencyPerEc)})");
                }
                // The KSP1 units of the same rate, through the containers' conversion (for the log only).
                var unit = r.MappedRate(resource, 1).Item2;
                var ksp1Rate = unit > 0 ? rate / unit : rate;
                m._outputs.Add(new HarvestOutput(resource, mapped, efficiency, ksp1Rate, rate, ec, perEc, from, factor));
                outputs.Add(new IsruFlow(resource, mapped, ksp1Rate, rate));
                inputs.AddRange(own);
                if (IsruCfg.Value(h, "AutoShutdown") == null) m.OmittedAutoShutdown++;
                if (IsruCfg.Value(h, "GeneratesHeat") == null) m.OmittedGeneratesHeat++;
                if (IsruCfg.Bool(h, "GeneratesHeat") == true)
                {
                    if (ec > 0) flux += heatPerEc * ec * heatScale;
                    else m._notes.Add($"{Ksp1Module}: {resource}: GeneratesHeat without an ElectricCharge input, no heat (KSP2's heat is per EC/s)");
                }
                if (!impactSet)
                {
                    impactSet = true;
                    m.ImpactTransform = IsruCfg.Value(h, "ImpactTransform") ?? "";
                    m.ImpactRange = IsruCfg.Number(h, "ImpactRange") ?? 0;
                }
                else if ((IsruCfg.Value(h, "ImpactTransform") ?? "") != m.ImpactTransform || (IsruCfg.Number(h, "ImpactRange") ?? 0) != m.ImpactRange)
                    IsruCfg.AddOnce(m._notes, $"{Ksp1Module}: harvesters with another impact transform or range, the first one's used");
            }
            if (m._outputs.Count > 0)
            {
                m.Formula = new IsruFormula
                {
                    Id = MineId,
                    LabelSource = "",
                    Inputs = IsruCfg.Merge(inputs),
                    Outputs = IsruCfg.Merge(outputs),
                    FluxGenerated = flux,
                };
                m.Formula.ComputeTotals(r);
            }
            return m;
        }

        private void ReadKeys(IKsp1Node h)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in h.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                if (!seen.Add(key)) continue;
                var value = IsruCfg.Text(kv.Value);
                switch (key)
                {
                    case "HarvestThreshold":
                        NotReproducedField(key, $"{Ksp1Module}: HarvestThreshold not reproduced (KSP2 mines any density)");
                        break;
                    case "UseSpecialistBonus":
                    case "UseSpecialistHeatBonus":
                        if (bool.TryParse(value, out var bonus) && bonus)
                            NotReproducedField(ConverterMapping.SpecialistBonus, $"{Ksp1Module}: {ConverterMapping.SpecialistBonus} not reproduced (no crew bonus in KSP2)");
                        break;
                    case "EfficiencyBonus":
                        if (!IsruCfg.TryNumber(value, out var eb) || eb != 1)
                            NotReproducedField("EfficiencyBonus", $"{Ksp1Module}: EfficiencyBonus not reproduced (full rate)");
                        break;
                    default:
                        if (!KnownKeys.Contains(key)) NotReproducedField(key, $"{Ksp1Module}: {key} not reproduced");
                        break;
                }
            }
        }

        private void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }
    }
}
