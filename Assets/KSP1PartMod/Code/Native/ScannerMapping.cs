using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    public enum ScannerKind { Surveyor, Resource }

    /// <summary>Why a KSP1 scanner gets no native scanner.</summary>
    public enum ScannerLeftOut { None, RequiresUnlock, ScannerType, NoScannableResource }

    /// <summary>KSP1 scanners read as Redux's <c>Module_ResourceScanner</c> (plan (j)):
    /// <list type="bullet">
    /// <item><c>ModuleOrbitalSurveyor</c> (<see cref="Surveyor"/>), KSP1's body-wide survey: the native scanner's resources,
    /// power and inclination window; <c>TimeToComplete = ScanTime</c>; altitudes from the cfg's
    /// <c>MinimumAltitude</c>/<c>MaximumAltitude</c> when present, else the native ones. <c>SciBonus</c> is not reproduced;</item>
    /// <item><c>ModuleResourceScanner</c> (<see cref="Resource"/>), <c>ScannerType 0</c> with <c>RequiresUnlock = false</c>
    /// only: its mapped resource if minable, from 0 to <c>MaxAbundanceAltitude</c>, any inclination, no power, the native scan
    /// time. KSP2 records the scan for the whole body, while KSP1 read one spot (noted). <c>RequiresUnlock</c> missing is
    /// taken as true (S), noted.</item>
    /// </list>
    /// Pure.</summary>
    public sealed class ScannerMapping
    {
        public const string SurveyorModule = "ModuleOrbitalSurveyor";
        public const string OrbitalScannerModule = "ModuleOrbitalScanner";
        public const string ResourceScannerModule = "ModuleResourceScanner";
        public const string NativeModule = "Module_ResourceScanner";

        public const string RequiresUnlockNote = ResourceScannerModule +
            ": RequiresUnlock, left out (KSP1 shows local abundance after a survey; KSP2's resource window shows the scanned map and has no part readout)";

        public static readonly IReadOnlyList<string> SurveyorKnownKeys = new[] { "name", "ScanTime", "SciBonus", "MinimumAltitude", "MaximumAltitude" };
        public static readonly IReadOnlyList<string> ResourceKnownKeys = new[] { "name", "ScannerType", "ResourceName", "MaxAbundanceAltitude", "RequiresUnlock" };

        public ScannerKind Kind { get; private set; }
        public ScannerLeftOut LeftOut { get; private set; }
        public bool HasModule => LeftOut == ScannerLeftOut.None;
        public IReadOnlyList<string> ScannableResources { get; private set; } = Array.Empty<string>();
        /// <summary>Consumed per second while scanning.</summary>
        public IReadOnlyList<IsruFlow> RequiredResources { get; private set; } = Array.Empty<IsruFlow>();
        public double TimeToComplete { get; private set; }
        public double MinAltitude { get; private set; }
        public double MaxAltitude { get; private set; }
        public double MinInclination { get; private set; }
        public double MaxInclination { get; private set; }
        /// <summary>The KSP1 <c>ResourceName</c> of a resource scanner; null for a surveyor.</summary>
        public string Ksp1Resource { get; private set; }

        public IReadOnlyList<string> NotReproduced => _notReproduced;
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<string> _notReproduced = new List<string>();
        private readonly List<string> _notes = new List<string>();

        public static string TypeName(int type) => HarvesterMapping.TypeName(type);

        public static ScannerMapping Surveyor(IKsp1Node surveyor, NativeScannerRef native)
        {
            native = native ?? NativeScannerRef.Fallback;
            var m = new ScannerMapping
            {
                Kind = ScannerKind.Surveyor,
                ScannableResources = native.Resources.ToList(),
                RequiredResources = native.Ec > 0
                    ? new[] { new IsruFlow(IsruResources.ElectricCharge, IsruResources.ElectricCharge, native.Ec, native.Ec) }
                    : Array.Empty<IsruFlow>(),
                TimeToComplete = native.TimeToComplete,
                MinAltitude = native.MinAltitude,
                MaxAltitude = native.MaxAltitude,
                MinInclination = native.MinInclination,
                MaxInclination = native.MaxInclination,
            };
            var scanTime = IsruCfg.Number(surveyor, "ScanTime");
            if (scanTime > 0) m.TimeToComplete = scanTime.Value;
            else m._notes.Add($"{SurveyorModule}: ScanTime missing or not above 0, the KSP2 scanner's {IsruCfg.Inv(native.TimeToComplete)} s used");
            if (IsruCfg.Number(surveyor, "MinimumAltitude") is double min) m.MinAltitude = min;
            if (IsruCfg.Number(surveyor, "MaximumAltitude") is double max) m.MaxAltitude = max;
            m._notes.Add($"{SurveyorModule}: inclination {IsruCfg.Inv(m.MinInclination)}° to {IsruCfg.Inv(m.MaxInclination)}° from the KSP2 scanner " +
                         "(KSP1's surveyor asks for a polar orbit, S), power from the KSP2 scanner (KSP1's is not in the cfg)");
            m.ReadUnknown(surveyor, SurveyorModule, SurveyorKnownKeys);
            if (IsruCfg.Value(surveyor, "SciBonus") != null)
                m.NotReproducedField("SciBonus", $"{SurveyorModule}: SciBonus not reproduced (no survey science)");
            if (m.ScannableResources.Count == 0)
            {
                m.LeftOut = ScannerLeftOut.NoScannableResource;
                m._notes.Add($"{SurveyorModule}: the KSP2 scanner lists no resource, no {NativeModule}");
            }
            return m;
        }

        public static ScannerMapping Resource(IKsp1Node scanner, IsruResources r, NativeScannerRef native)
        {
            native = native ?? NativeScannerRef.Fallback;
            r = r ?? new IsruResources(null, null, null, null);
            var m = new ScannerMapping
            {
                Kind = ScannerKind.Resource,
                Ksp1Resource = IsruCfg.Value(scanner, "ResourceName"),
                TimeToComplete = native.TimeToComplete,
                MinAltitude = 0,
                MaxAltitude = native.MaxAltitude,
                MinInclination = -180,
                MaxInclination = 180,
            };
            m.ReadUnknown(scanner, ResourceScannerModule, ResourceKnownKeys);

            var typeText = IsruCfg.Value(scanner, "ScannerType");
            if (typeText != null && (!IsruCfg.TryNumber(typeText, out var type) || type != 0))
            {
                m.LeftOut = ScannerLeftOut.ScannerType;
                var name = IsruCfg.TryNumber(typeText, out var t) && t == Math.Floor(t) ? TypeName((int)t) : "unknown";
                m._notes.Add($"{ResourceScannerModule}: ScannerType {typeText} ({name}) left out, KSP2 scans the ground only");
                return m;
            }
            var unlock = IsruCfg.Bool(scanner, "RequiresUnlock");
            if (unlock == null) m._notes.Add($"{ResourceScannerModule}: RequiresUnlock missing, taken as true (S)");
            if (unlock != false)
            {
                m.LeftOut = ScannerLeftOut.RequiresUnlock;
                m._notes.Add(RequiresUnlockNote);
                return m;
            }
            var mapped = string.IsNullOrEmpty(m.Ksp1Resource) ? null : r.Map(m.Ksp1Resource);
            if (mapped == null || !r.IsMinable(mapped))
            {
                m.LeftOut = ScannerLeftOut.NoScannableResource;
                m._notes.Add(mapped == null
                    ? $"{ResourceScannerModule}: no ResourceName, left out"
                    : $"{ResourceScannerModule}: {m.Ksp1Resource} -> {mapped} has no resource map in KSP2, left out");
                return m;
            }
            m.ScannableResources = new[] { mapped };
            if (IsruCfg.Number(scanner, "MaxAbundanceAltitude") is double max && max >= 0) m.MaxAltitude = max;
            else m._notes.Add($"{ResourceScannerModule}: MaxAbundanceAltitude missing, the KSP2 scanner's {IsruCfg.Inv(native.MaxAltitude)} m used");
            m._notes.Add($"{ResourceScannerModule}: KSP2 records the scan for the whole body, KSP1 read the abundance under the part");
            return m;
        }

        private void ReadUnknown(IKsp1Node node, string module, IReadOnlyList<string> known)
        {
            if (node == null) return;
            foreach (var key in node.AllValues.Select(kv => kv.Key?.Trim() ?? "").Distinct(StringComparer.Ordinal))
                if (!known.Contains(key)) NotReproducedField(key, $"{module}: {key} not reproduced");
            foreach (var child in node.AllNodes)
                NotReproducedField("node " + child.Name, $"{module}: node {child.Name} not reproduced");
        }

        private void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }
    }
}
