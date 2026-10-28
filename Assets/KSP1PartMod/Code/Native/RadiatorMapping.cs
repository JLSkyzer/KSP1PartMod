using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>The native module a KSP1 radiator becomes: <c>Module_Cooler</c>, or <c>Module_ActiveRadiator</c> (a cooler gated
    /// by the part's deploy state) when the part has a <c>ModuleDeployableRadiator</c>.</summary>
    public enum RadiatorKind { Cooler, ActiveRadiator }

    /// <summary>One <c>requiredResources</c> entry of a native cooler.</summary>
    public sealed class RadiatorResource
    {
        /// <summary>The KSP1 resource name (the first one when several KSP1 flows were summed).</summary>
        public string Ksp1Name { get; }
        /// <summary>The KSP2 resource name.</summary>
        public string Name { get; }
        /// <summary>The cfg <c>rate</c>, KSP1 units/s (summed).</summary>
        public double Ksp1Rate { get; }
        /// <summary>The native <c>Rate</c>, KSP2 units/s.</summary>
        public double Rate { get; }

        public RadiatorResource(string ksp1Name, string name, double ksp1Rate, double rate)
        {
            Ksp1Name = ksp1Name;
            Name = name;
            Ksp1Rate = ksp1Rate;
            Rate = rate;
        }
    }

    /// <summary>The KSP1 <c>ModuleActiveRadiator</c>s of a part read as one native cooler (radiators plan (h)):
    /// <list type="bullet">
    /// <item>module: <c>Module_ActiveRadiator</c> with a <c>ModuleDeployableRadiator</c> on the part, else <c>Module_Cooler</c>;</item>
    /// <item><c>fluxRemoved = maxEnergyTransfer / 50 x radiator_flux_scale</c> kW (the unit is KSP1's public figures: 50000 is a
    /// core heat transfer of 1000 kW). A radiator without a <c>maxEnergyTransfer</c> above 0 is skipped (noted); several
    /// radiators are merged (flux and resource rates summed);</item>
    /// <item>resources: each <c>RESOURCE</c> is named by <see cref="IsruResources.Map"/>. Electric charge is paced by KSP2's own
    /// kW per EC/s (<c>Rate = fluxRemoved / nativeKwPerEc</c>), or keeps the cfg rate with <c>radiator_ksp1_rates</c>; any other
    /// resource keeps its cfg rate. A resource the game does not know is dropped (noted), the cooler stays;</item>
    /// <item>start state (<c>radiator_auto_on</c>): a fixed radiator starts on, a deployable one starts off and is turned on when
    /// extended (<see cref="TurnsOnWhenExtended"/>); with the switch off nothing is on or turned on;</item>
    /// <item>not reproduced, counted by field: <c>parentCoolingOnly</c> (when true), <c>overcoolFactor</c>, <c>isCoreRadiator</c>
    /// (when true), load-proportional EC use, any other key or node; the deployable module's own fields come from
    /// <see cref="DeployableNotReproduced"/>.</item>
    /// </list>
    /// Pure.</summary>
    public sealed class RadiatorMapping
    {
        public const string Ksp1Module = "ModuleActiveRadiator";
        public const string DeployableModule = "ModuleDeployableRadiator";
        public const string NativeCooler = "Module_Cooler";
        public const string NativeActiveRadiator = "Module_ActiveRadiator";
        /// <summary><c>maxEnergyTransfer</c> per kW (D: KSP1's public figures, 50000 for 1000 kW).</summary>
        public const double EnergyTransferPerKw = 50;
        public const string LoadProportionalEc = "load-proportional EC";

        /// <summary>The keys that need no note: names and flags that say nothing of the behaviour.</summary>
        public static readonly IReadOnlyList<string> QuietKeys = new[]
        {
            "name", "maxEnergyTransfer", "moduleIsEnabled", "tag", "overcoolFactor", "isCoreRadiator", "parentCoolingOnly",
        };

        /// <summary>The <c>ModuleDeployableRadiator</c> fields about tracking, wind and breaking, which have no counterpart.</summary>
        public static readonly IReadOnlyList<string> DeployableFields = new[]
        {
            "isTracking", "hasPivot", "pivotName", "secondaryTransformName", "raycastTransformName", "trackingSpeed",
            "sunAlignmentOffset", "windResistance", "breakName", "impactResistance", "trackingDotTransformName",
            "useRaycastForTrackingDot",
        };

        public bool HasModule { get; private set; }
        public RadiatorKind Kind { get; private set; }
        public string NativeModule => Kind == RadiatorKind.ActiveRadiator ? NativeActiveRadiator : NativeCooler;
        /// <summary>kW removed from the vessel pool.</summary>
        public double FluxRemoved { get; private set; }
        /// <summary>The summed cfg <c>maxEnergyTransfer</c>.</summary>
        public double MaxEnergyTransfer { get; private set; }
        /// <summary><c>radiator_flux_scale</c> as used.</summary>
        public double FluxScale { get; private set; } = 1;
        /// <summary>The kW per EC/s the native pace used.</summary>
        public double NativeKwPerEc { get; private set; } = NativeCoolerRef.FallbackKwPerEc;
        public bool Ksp1Rates { get; private set; }
        public IReadOnlyList<RadiatorResource> Resources => _resources;
        /// <summary>The definition starts with <c>coolerEnabled = true</c> (a fixed radiator, <c>radiator_auto_on</c> on).</summary>
        public bool StartsOn { get; private set; }
        /// <summary>The deploy bridge turns the cooler on at each transition to Extended (a deployable radiator,
        /// <c>radiator_auto_on</c> on).</summary>
        public bool TurnsOnWhenExtended { get; private set; }
        /// <summary>The radiators read into this cooler.</summary>
        public int Merged { get; private set; }
        /// <summary>Resources of an unknown name, dropped.</summary>
        public int DroppedResources { get; private set; }
        public IReadOnlyList<string> NotReproduced => _notReproduced;
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<RadiatorResource> _resources = new List<RadiatorResource>();
        private readonly List<string> _notReproduced = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <param name="radiators">The part's <c>ModuleActiveRadiator</c> nodes, in cfg order.</param>
        /// <param name="hasDeployable">The part has a <c>ModuleDeployableRadiator</c>.</param>
        /// <param name="nativeKwPerEc">KSP2's kW removed per EC/s; not above 0 or not finite: the fallback 100.</param>
        /// <param name="fluxScale"><c>radiator_flux_scale</c>; not above 0 or not finite: 1.</param>
        public static RadiatorMapping From(IReadOnlyList<IKsp1Node> radiators, bool hasDeployable, IsruResources r, double nativeKwPerEc,
            double fluxScale, bool ksp1Rates, bool autoOn)
        {
            var m = new RadiatorMapping { Ksp1Rates = ksp1Rates };
            r = r ?? new IsruResources(null, null, null, null);
            if (nativeKwPerEc > 0 && !double.IsInfinity(nativeKwPerEc)) m.NativeKwPerEc = nativeKwPerEc;
            if (fluxScale > 0 && !double.IsInfinity(fluxScale)) m.FluxScale = fluxScale;

            var flows = new List<(string Ksp1, string Name, double Ksp1Rate, double Rate)>();
            foreach (var node in (radiators ?? Array.Empty<IKsp1Node>()).Where(n => n != null))
            {
                var text = IsruCfg.Value(node, "maxEnergyTransfer");
                if (text == null)
                {
                    m._notes.Add($"{Ksp1Module}: maxEnergyTransfer missing, radiator skipped");
                    continue;
                }
                if (!IsruCfg.TryNumber(text, out var energy) || !(energy > 0))
                {
                    m._notes.Add($"{Ksp1Module}: maxEnergyTransfer '{text}' is not a number above 0, radiator skipped");
                    continue;
                }
                m.Merged++;
                m.MaxEnergyTransfer += energy;
                m.ReadKeys(node);
                foreach (var res in node.GetNodes("RESOURCE"))
                {
                    var name = IsruCfg.Value(res, "name");
                    if (string.IsNullOrEmpty(name)) { m._notes.Add($"{Ksp1Module}: RESOURCE without a name ignored"); continue; }
                    var rateText = IsruCfg.Value(res, "rate");
                    if (!IsruCfg.TryNumber(rateText, out var rate) || rate < 0)
                    {
                        m._notes.Add($"{Ksp1Module}: {name}: rate '{rateText}' is not a number of 0 or more, resource ignored");
                        continue;
                    }
                    var (mapped, converted) = r.MappedRate(name, rate);
                    if (!r.IsKnown(mapped))
                    {
                        m.DroppedResources++;
                        m._notes.Add($"{Ksp1Module}: {name} -> {mapped} is not a game resource, resource dropped");
                        continue;
                    }
                    flows.Add((name, mapped, rate, converted));
                }
            }
            if (m.Merged == 0) return m;

            m.HasModule = true;
            m.Kind = hasDeployable ? RadiatorKind.ActiveRadiator : RadiatorKind.Cooler;
            m.FluxRemoved = m.MaxEnergyTransfer / EnergyTransferPerKw * m.FluxScale;
            foreach (var group in flows.GroupBy(f => f.Name, StringComparer.Ordinal))
            {
                var first = group.First();
                var ksp1 = group.Sum(f => f.Ksp1Rate);
                var rate = !ksp1Rates && IsruResources.IsElectricCharge(group.Key)
                    ? m.FluxRemoved / m.NativeKwPerEc
                    : group.Sum(f => f.Rate);
                m._resources.Add(new RadiatorResource(first.Ksp1, group.Key, ksp1, rate));
            }
            m.StartsOn = autoOn && !hasDeployable;
            m.TurnsOnWhenExtended = autoOn && hasDeployable;
            m.NotReproducedField(LoadProportionalEc,
                $"{Ksp1Module}: {LoadProportionalEc} not reproduced (the cooler draws its rate whenever it is on)");
            if (m.Merged > 1) m._notes.Add($"{Ksp1Module}: {m.Merged} radiators merged into one cooler (flux and rates summed)");
            return m;
        }

        /// <summary>The tracking, wind and break fields of a <c>ModuleDeployableRadiator</c> node, none of which is reproduced.</summary>
        public static IReadOnlyList<string> DeployableNotReproduced(IKsp1Node deployable)
        {
            var found = new List<string>();
            if (deployable == null) return found;
            foreach (var kv in deployable.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                if (DeployableFields.Contains(key)) IsruCfg.AddOnce(found, key);
            }
            return found;
        }

        private void ReadKeys(IKsp1Node node)
        {
            foreach (var kv in node.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                var value = IsruCfg.Text(kv.Value);
                switch (key)
                {
                    case "parentCoolingOnly":
                        if (IsruCfg.Bool(node, key) == true)
                            NotReproducedField(key, $"{Ksp1Module}: parentCoolingOnly = {value} not reproduced (the cooler serves the whole vessel)");
                        break;
                    case "overcoolFactor":
                        NotReproducedField(key, $"{Ksp1Module}: overcoolFactor = {value} not reproduced (cooling never goes below ambient)");
                        break;
                    case "isCoreRadiator":
                        if (IsruCfg.Bool(node, key) == true)
                            NotReproducedField(key, $"{Ksp1Module}: isCoreRadiator = {value} not reproduced (no core heat in KSP2)");
                        break;
                    default:
                        if (!QuietKeys.Contains(key)) NotReproducedField(key, $"{Ksp1Module}: {key} not reproduced");
                        break;
                }
            }
            foreach (var child in node.AllNodes)
            {
                var kind = child.Name?.Trim() ?? "";
                if (kind != "RESOURCE") NotReproducedField("node " + kind, $"{Ksp1Module}: node {kind} not reproduced");
            }
        }

        private void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }
    }
}
