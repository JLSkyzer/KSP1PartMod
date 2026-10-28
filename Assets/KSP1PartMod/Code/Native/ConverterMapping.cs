using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>How the node a B9 re-import called the importer with found its place among the part's converters.</summary>
    public enum CalledMatch { None, Reference, ConverterName, Appended }

    /// <summary>The KSP1 <c>ModuleResourceConverter</c>s of a part read as one native <c>Module_ResourceConverter</c>
    /// (plan (j)): one formula per KSP1 converter, in cfg order.
    /// <list type="bullet">
    /// <item>names through <see cref="IsruResources.Map"/>, rates through its converter (units/s, as the containers);</item>
    /// <item>pace (the final review's I1 ruling, the drills' per-EC rule): with the native converter formulas given, each
    /// mode's non-EC rates are scaled so that its consumption per EC/s equals the native converter's
    /// (<see cref="ConverterPace"/>); without them (<c>isru_ksp1_rates</c>), the KSP1 rates;</item>
    /// <item><c>REQUIRED_RESOURCE</c>: an input at rate 0 (present, never consumed); its amount is not reproduced;</item>
    /// <item><c>FluxGenerated = GeneratesHeat ? heatPerEc × EC input × heatScale : 0</c>; a formula that generates heat
    /// without an EC input gets 0, noted;</item>
    /// <item>a formula with a resource the game does not know, or a ratio that is not a number of 0 or more, is dropped
    /// (noted); no formula left: no module;</item>
    /// <item>module fields: <c>ResourceAutoShutdown</c> = the first kept formula's <c>AutoShutdown</c>,
    /// <c>conversionRate</c> 1 (KSP1 runs at full rate), <c>AutoShutdownTemperatureRatio</c> 0.9, <c>ConvertByMass</c> false;</item>
    /// <item>not reproduced, counted once per part by field (<see cref="NotReproduced"/>): <c>DumpExcess</c> (when true),
    /// <c>FillAmount</c> (when below 1), <c>FlowMode</c> (when not vessel-wide: <see cref="IsVesselWide"/>), <c>ThermalEfficiency</c>, <c>TemperatureModifier</c>,
    /// <c>DefaultShutoffTemp</c>, the specialist bonus, <c>AlwaysActive</c> (when true), <c>EfficiencyBonus</c> (when not 1),
    /// the <c>REQUIRED_RESOURCE</c> amount, several modes at once, and any other key or node.</item>
    /// </list>
    /// B9 (plan (l)): the node the importer was <paramref name="called"/> with replaces its entry (same reference, else the
    /// first with the same <c>ConverterName</c>), else it is appended. Only the first importer call of a part builds the
    /// module (<c>callIndex</c> 0); later calls build nothing and note nothing. Pure.</summary>
    public sealed class ConverterMapping
    {
        public const string Ksp1Module = "ModuleResourceConverter";
        public const string NativeModule = "Module_ResourceConverter";
        public const double NativeAutoShutdownTemperatureRatio = 0.9;

        /// <summary>The keys read, or covered by the rules below; any other key is noted.</summary>
        public static readonly IReadOnlyList<string> KnownKeys = new[]
        {
            "name", "ConverterName", "StartActionName", "StopActionName", "ToggleActionName", "AutoShutdown", "GeneratesHeat",
            "resourceOutputName", "ExperienceEffect", "UseSpecialistBonus", "UseSpecialistHeatBonus", "SpecialistEfficiencyFactor",
            "SpecialistBonusBase", "SpecialistHeatFactor", "EfficiencyBonus", "FillAmount", "AlwaysActive", "DefaultShutoffTemp",
            "moduleIsEnabled", "tag",
        };

        public const string SeveralModes = "several modes at once";
        public const string RequiredAmount = "REQUIRED_RESOURCE amount";
        public const string SpecialistBonus = "specialist bonus";

        /// <summary>The part already has a <c>Data_ResourceConverter</c> (a Redux importer of its own).</summary>
        public const string ExistingModuleNote = Ksp1Module + " not converted: the part already has a " + NativeModule;

        /// <summary>KSP1 flow modes that draw from the whole vessel, as KSP2 does: not noted.</summary>
        public static bool IsVesselWide(string flowMode) =>
            string.Equals(flowMode, "ALL_VESSEL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(flowMode, "ALL_VESSEL_BALANCE", StringComparison.OrdinalIgnoreCase);

        /// <summary>The localization term of a mode's name.</summary>
        public static string LabelKey(string part, int index) => $"Parts/Ksp1Converter/{part}/{index}";

        public bool LaterCall { get; private set; }
        public bool HasModule => !LaterCall && _formulas.Count > 0;
        public IReadOnlyList<IsruFormula> Formulas => _formulas;
        /// <summary>The KSP1 converters read (the called node included).</summary>
        public int SourceCount { get; private set; }
        public int DroppedFormulas { get; private set; }
        /// <summary>KSP1 converters whose cfg omits <c>AutoShutdown</c> / <c>GeneratesHeat</c> (read as false, S: KSP1's
        /// defaults are not proven), for the census.</summary>
        public int OmittedAutoShutdown { get; private set; }
        public int OmittedGeneratesHeat { get; private set; }
        public int CalledIndex { get; private set; } = -1;
        public CalledMatch CalledMatch { get; private set; }

        public bool ResourceAutoShutdown { get; private set; }
        public double ConversionRate => 1.0;
        public double AutoShutdownTemperatureRatio => NativeAutoShutdownTemperatureRatio;
        public bool ConvertByMass => false;

        /// <summary>Field names not reproduced, each once, in first-seen order.</summary>
        public IReadOnlyList<string> NotReproduced => _notReproduced;
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<IsruFormula> _formulas = new List<IsruFormula>();
        private readonly List<string> _notReproduced = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <param name="converters">The part's <c>ModuleResourceConverter</c> nodes (<c>context.PartNode</c>), in cfg order.</param>
        /// <param name="callIndex">The importer call of this part (0 for the first).</param>
        /// <param name="called">The node the importer was called with (null: none).</param>
        /// <param name="heatPerEc">kW per EC/s (the native converters' median); not above 0 or not finite: the fallback.</param>
        /// <param name="heatScale"><c>isru_heat_scale</c>; negative or not finite: 1.</param>
        /// <param name="nativeConverters">The native converter formulas each mode's pace is taken from
        /// (<see cref="ConverterPace"/>, the drills' per-EC rule); null: every mode keeps its KSP1 rates, with
        /// <paramref name="keptReason"/> as its pace.</param>
        /// <param name="keptReason">Why the rates are KSP1's when <paramref name="nativeConverters"/> is null (null: no pace at
        /// all, the modes carry none).</param>
        public static ConverterMapping From(IReadOnlyList<IKsp1Node> converters, int callIndex, IKsp1Node called, IsruResources r,
            double heatPerEc, double heatScale, IReadOnlyList<NativeIsruFormula> nativeConverters = null, string keptReason = null)
        {
            var m = new ConverterMapping();
            if (callIndex > 0)
            {
                m.LaterCall = true;
                return m;
            }
            r = r ?? new IsruResources(null, null, null, null);
            if (!(heatPerEc > 0) || double.IsInfinity(heatPerEc)) heatPerEc = NativeIsruRef.FallbackConverterHeatPerEc;
            if (!(heatScale >= 0) || double.IsInfinity(heatScale)) heatScale = 1;

            var list = (converters ?? Array.Empty<IKsp1Node>()).Where(n => n != null).ToList();
            if (called != null)
            {
                var at = list.FindIndex(n => ReferenceEquals(n, called));
                var match = CalledMatch.Reference;
                if (at < 0)
                {
                    var name = IsruCfg.Value(called, "ConverterName");
                    at = name == null ? -1 : list.FindIndex(n => IsruCfg.Value(n, "ConverterName") == name);
                    match = CalledMatch.ConverterName;
                }
                if (at >= 0) list[at] = called;
                else
                {
                    list.Add(called);
                    at = list.Count - 1;
                    match = CalledMatch.Appended;
                }
                m.CalledIndex = at;
                m.CalledMatch = match;
            }
            m.SourceCount = list.Count;

            bool? autoShutdown = null;
            for (var i = 0; i < list.Count; i++)
            {
                var f = m.Formula(list[i], i, r, heatPerEc, heatScale);
                if (f == null)
                {
                    m.DroppedFormulas++;
                    continue;
                }
                if (nativeConverters != null) Pace(f, ConverterPace.Of(f, nativeConverters, r.MassPerUnit), r);
                else if (keptReason != null) f.Pace = ConverterPace.Kept(keptReason);
                m._formulas.Add(f);
                if (autoShutdown == null) autoShutdown = f.AutoShutdown;
                else if (autoShutdown != f.AutoShutdown)
                    IsruCfg.AddOnce(m._notes, $"{Ksp1Module}: AutoShutdown differs between converters, the first mode's ({(autoShutdown.Value ? "true" : "false")}) used for all");
            }
            m.ResourceAutoShutdown = autoShutdown ?? false;
            if (list.Count > 1)
                m.NotReproducedField(SeveralModes, $"{Ksp1Module}: {list.Count} converters ran together in KSP1, {SeveralModes} not reproduced (KSP2 runs one mode at a time)");
            if (m._formulas.Count == 0 && list.Count > 0)
                m._notes.Add($"{Ksp1Module}: no mode left, no {NativeModule}");
            return m;
        }

        /// <summary>Applies <paramref name="pace"/> to the mode: every non-EC rate times its factor (the KSP1 ratios, the EC
        /// rate and the heat, which follows the EC, kept), then the mass totals again.</summary>
        private static void Pace(IsruFormula f, ConverterPace pace, IsruResources r)
        {
            f.Pace = pace;
            if (!pace.Scaled) return;
            IsruFlow Scale(IsruFlow x) => IsruResources.IsElectricCharge(x.Name) ? x : x.Scaled(pace.Factor);
            f.Inputs = f.Inputs.Select(Scale).ToList();
            f.Outputs = f.Outputs.Select(Scale).ToList();
            f.ComputeTotals(r);
        }

        private void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }

        private IsruFormula Formula(IKsp1Node node, int index, IsruResources r, double heatPerEc, double heatScale)
        {
            var name = IsruCfg.Value(node, "ConverterName");
            var label = string.IsNullOrEmpty(name) ? "Converter " + (index + 1) : name;
            var who = $"{Ksp1Module} '{label}'";
            if (IsruCfg.Value(node, "AutoShutdown") == null) OmittedAutoShutdown++;
            if (IsruCfg.Value(node, "GeneratesHeat") == null) OmittedGeneratesHeat++;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in node.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                if (!seen.Add(key)) continue;
                var value = IsruCfg.Text(kv.Value);
                switch (key)
                {
                    case "FillAmount":
                        if (!IsruCfg.TryNumber(value, out var fill) || fill < 1)
                            NotReproducedField("FillAmount", $"{Ksp1Module}: FillAmount not reproduced (KSP2 runs until an output is full)");
                        break;
                    case "AlwaysActive":
                        if (bool.TryParse(value, out var always) && always)
                            NotReproducedField("AlwaysActive", $"{Ksp1Module}: AlwaysActive not reproduced (the player starts the mode)");
                        break;
                    case "UseSpecialistBonus":
                    case "UseSpecialistHeatBonus":
                        if (bool.TryParse(value, out var bonus) && bonus)
                            NotReproducedField(SpecialistBonus, $"{Ksp1Module}: {SpecialistBonus} not reproduced (no crew bonus in KSP2)");
                        break;
                    case "DefaultShutoffTemp":
                        NotReproducedField("DefaultShutoffTemp", $"{Ksp1Module}: DefaultShutoffTemp not reproduced (KSP2 shuts down at {IsruCfg.Inv(NativeAutoShutdownTemperatureRatio)} of the part's max temperature)");
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

            var inputs = new List<IsruFlow>();
            var outputs = new List<IsruFlow>();
            var required = new List<IsruFlow>();
            string drop = null;
            foreach (var child in node.AllNodes)
            {
                var kind = child.Name?.Trim() ?? "";
                switch (kind)
                {
                    case "INPUT_RESOURCE":
                    case "OUTPUT_RESOURCE":
                    case "REQUIRED_RESOURCE":
                        var flow = Flow(child, kind, who, r, ref drop);
                        if (flow == null) break;
                        (kind == "INPUT_RESOURCE" ? inputs : kind == "OUTPUT_RESOURCE" ? outputs : required).Add(flow);
                        break;
                    case "ThermalEfficiency":
                    case "TemperatureModifier":
                        NotReproducedField(kind, $"{Ksp1Module}: {kind} not reproduced (no core temperature in KSP2, full rate)");
                        break;
                    default:
                        NotReproducedField("node " + kind, $"{Ksp1Module}: node {kind} not reproduced");
                        break;
                }
            }
            if (drop != null)
            {
                _notes.Add($"{who}: {drop}, mode dropped");
                return null;
            }
            if (inputs.Count + outputs.Count + required.Count == 0)
            {
                _notes.Add($"{who}: no resource, mode dropped");
                return null;
            }

            // A required resource that is also consumed is checked by its consumption.
            var consumed = new HashSet<string>(inputs.Select(f => f.Name), StringComparer.Ordinal);
            var f2 = new IsruFormula
            {
                Index = index,
                Id = "ksp1_" + index,
                LabelSource = label,
                Inputs = IsruCfg.Merge(inputs.Concat(required.Where(q => !consumed.Contains(q.Name)))),
                Outputs = IsruCfg.Merge(outputs),
                AutoShutdown = IsruCfg.Bool(node, "AutoShutdown") ?? false,
            };
            f2.ComputeTotals(r);
            if (IsruCfg.Bool(node, "GeneratesHeat") == true)
            {
                if (f2.EcInput > 0) f2.FluxGenerated = heatPerEc * f2.EcInput * heatScale;
                else _notes.Add($"{who}: GeneratesHeat without an ElectricCharge input, no heat (KSP2's heat is per EC/s)");
            }
            return f2;
        }

        private IsruFlow Flow(IKsp1Node node, string kind, string who, IsruResources r, ref string drop)
        {
            var name = IsruCfg.Value(node, "ResourceName");
            if (string.IsNullOrEmpty(name))
            {
                _notes.Add($"{who}: {kind} without ResourceName ignored");
                return null;
            }
            var ratioText = IsruCfg.Value(node, "Ratio");
            if (!IsruCfg.TryNumber(ratioText, out var ratio) || ratio < 0)
            {
                drop = drop ?? $"{name} Ratio '{ratioText}' is not a number of 0 or more";
                return null;
            }
            var flowMode = IsruCfg.Value(node, "FlowMode");
            if (flowMode != null && !IsVesselWide(flowMode))
                NotReproducedField("FlowMode", $"{Ksp1Module}: FlowMode not reproduced (KSP2 draws from the whole vessel)");
            if (kind == "OUTPUT_RESOURCE" && IsruCfg.Bool(node, "DumpExcess") == true)
                NotReproducedField("DumpExcess", $"{Ksp1Module}: DumpExcess not reproduced (KSP2 stops the mode when an output is full)");
            var isRequired = kind == "REQUIRED_RESOURCE";
            if (isRequired && ratio > 0)
                NotReproducedField(RequiredAmount, $"{Ksp1Module}: {RequiredAmount} not reproduced (the resource must be present, the amount is not checked)");
            var (mapped, rate) = r.MappedRate(name, isRequired ? 0 : ratio);
            if (!r.IsKnown(mapped))
            {
                drop = drop ?? $"{name} -> {mapped} is not a game resource";
                return null;
            }
            return new IsruFlow(name, mapped, isRequired ? 0 : ratio, isRequired ? 0 : rate, isRequired);
        }
    }
}
