using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Game;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 <c>ModuleAblator</c> → KSP2's <c>Module_Heatshield</c> (<c>ksp1_heatshields</c>, plan (f)): builds
    /// <c>Data_Heatshield</c> from <see cref="AblatorMapping"/> and returns the module Redux's own <c>CreateSerialized</c>
    /// builds. The capacity is the part's container of the shield resource as Redux imported it (<c>PartData</c>, KSP2 units,
    /// the units the module burns and the reference is read in); a part without one gets no module (noted). The reference is
    /// the game's own heat shields, read once before the first KSP1 shield (<see cref="Reference"/>). A second one on a part,
    /// or a part that already has a <c>Data_Heatshield</c>, gets nothing more (noted). Any failure leaves the part as Redux
    /// 0.7.0 imports it (the module dropped, nothing of ours left on the part).</summary>
    internal sealed class HeatshieldImporter : IKsp1PartModuleImporter
    {
        public const string DataTypeName = "KSP.Modules.Data_Heatshield";
        public const string ModuleTypeName = "KSP.Modules.Module_Heatshield";
        private const string FlagNoResource = "heatshield:noresource";
        private const string FlagNotReproduced = "heatshield:nr:";

        private readonly double _scale;

        /// <summary>Tests: the (Rate × PyrolysisLossFactor, capacity) pairs of the KSP2 shields instead of the game's parts.</summary>
        internal static Func<IEnumerable<(double Rate, double Capacity)>> ReferenceSource;

        private static readonly object ReferenceLock = new object();
        private static NativeShieldRef _reference;

        public HeatshieldImporter(double scale)
        {
            _scale = scale;
        }

        /// <summary>The members this converter reads or writes, resolved by name; empty when all are found.</summary>
        internal static List<string> MissingMembers()
        {
            var missing = new List<string>();
            var data = AccessTools.TypeByName(DataTypeName);
            if (data == null) missing.Add(DataTypeName);
            else
            {
                var fields = new Dictionary<string, Type>
                {
                    ["requiredResources"] = typeof(List<PartModuleResourceSetting>),
                    ["AblationTempThreshold"] = typeof(double),
                    ["PyrolysisLossFactor"] = typeof(double),
                    ["ShieldingScale"] = typeof(float),
                    ["ShieldingDirection"] = typeof(Vector3),
                    ["DisabledWhenRetracted"] = typeof(bool),
                    ["UseChar"] = typeof(bool),
                    ["CharMaterialName"] = typeof(string),
                };
                foreach (var kv in fields)
                {
                    var f = AccessTools.Field(data, kv.Key);
                    if (f == null || f.IsStatic || f.FieldType != kv.Value) missing.Add("Data_Heatshield." + kv.Key);
                }
            }
            if (AccessTools.TypeByName(ModuleTypeName) == null) missing.Add(ModuleTypeName);
            if (AccessTools.Method(typeof(Ksp1ResourceMapper), "MapResourceName", new[] { typeof(string) }) == null)
                missing.Add("Ksp1ResourceMapper.MapResourceName");
            var containers = AccessTools.Field(typeof(PartData), "resourceContainers");
            if (containers == null || containers.IsStatic) missing.Add("PartData.resourceContainers");
            return missing;
        }

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            try
            {
                if (context == null || moduleNode == null) return false;
                if (context.ModuleData.Any(d => d is Data_Heatshield))
                {
                    NativeModules.Note(part, AblatorMapping.ExtraModuleNote);
                    return false;
                }
                var adapter = new Ksp1NodeAdapter(moduleNode);
                var ksp1Resource = adapter.GetValue("ablativeResource")?.Trim();
                if (string.IsNullOrEmpty(ksp1Resource)) ksp1Resource = AblatorMapping.DefaultResource;
                var resource = Ksp1ResourceMapper.MapResourceName(ksp1Resource) ?? ksp1Resource;
                var capacity = Capacity(context.PartData, resource);
                if (!(capacity > 0))
                {
                    // Nothing to burn: Module_Heatshield would divide by a capacity of 0.
                    NativeModules.Record.Flag(part, FlagNoResource);
                    NativeModules.Note(part, AblatorMapping.NoResourceNote(resource));
                    return false;
                }

                var map = AblatorMapping.From(adapter, capacity, Reference(), _scale, Ksp1ResourceMapper.MapResourceName);
                var data = new Data_Heatshield
                {
                    requiredResources = new List<PartModuleResourceSetting>
                    {
                        // AcceptanceThreshold: the struct's default, 0 (S: accepted by the flow request).
                        new PartModuleResourceSetting { ResourceName = map.ResourceName, Rate = (float)map.Rate },
                    },
                    AblationTempThreshold = map.AblationTempThreshold,
                    PyrolysisLossFactor = map.PyrolysisLossFactor,
                    ShieldingScale = map.ShieldingScale,
                    ShieldingDirection = new Vector3(map.ShieldingDirection.X, map.ShieldingDirection.Y, map.ShieldingDirection.Z),
                    DisabledWhenRetracted = map.DisabledWhenRetracted,
                    UseChar = map.UseChar,
                    CharMaterialName = map.CharMaterialName,
                };
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_Heatshield), data, context);

                var record = NativeModules.Record;
                record.AddConversion(part, AblatorMapping.NativeModule);
                foreach (var field in map.NotReproduced) record.Flag(part, FlagNotReproduced + field);
                NativeModules.Note(part, map.ValuesNote());
                foreach (var note in map.Notes) NativeModules.Note(part, note);
                added = -1;
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, AblatorMapping.Ksp1Module, e);
                return false;
            }
        }

        /// <summary>The part's capacity of <paramref name="resource"/> (KSP2 name, ignoring case) in KSP2 units, summed over
        /// its containers; 0 when it has none.</summary>
        private static double Capacity(PartData partData, string resource)
        {
            var containers = partData?.resourceContainers;
            if (containers == null) return 0;
            double capacity = 0;
            foreach (var c in containers)
                if (c != null && string.Equals(c.name, resource, StringComparison.OrdinalIgnoreCase) && c.capacityUnits > 0)
                    capacity += c.capacityUnits;
            return capacity;
        }

        /// <summary>The native reference, read once per session (before the first KSP1 shield) and logged.</summary>
        internal static NativeShieldRef Reference()
        {
            lock (ReferenceLock)
            {
                if (_reference != null) return _reference;
                IEnumerable<(double, double)> shields;
                try
                {
                    var source = ReferenceSource ?? new Func<IEnumerable<(double Rate, double Capacity)>>(GameShields);
                    shields = source().ToList();
                }
                catch (Exception e)
                {
                    NativeModules.Error(NativeLines.Prefix + "heat shield reference not read, fallback used: " + e);
                    shields = Array.Empty<(double, double)>();
                }
                _reference = NativeShieldRef.Median(shields);
                NativeModules.Info(NativeLines.HeatshieldReference(_reference));
                if (!_reference.IsFallback) NativeModules.Info(NativeLines.HeatshieldReferenceDetail(_reference));
                return _reference;
            }
        }

        internal static void ResetReference()
        {
            lock (ReferenceLock) _reference = null;
        }

        /// <summary>The KSP2 parts of the game (not KSP1 imports, not variants with '+') that carry a <c>Data_Heatshield</c>:
        /// its first resource's <c>Rate × PyrolysisLossFactor</c>, and the part's capacity of that resource.</summary>
        private static IEnumerable<(double Rate, double Capacity)> GameShields()
        {
            var parts = GameManager.Instance?.Game?.Parts?.PartData;
            if (parts == null) yield break;
            foreach (var kv in parts.ToList())
            {
                var core = kv.Value;
                if (kv.Key == null || kv.Key.Contains('+') || core?.data == null) continue;
                if (Ksp1Tags.IsKsp1Import(core.data.tags)) continue;
                var shield = ShieldOf(core);
                var settings = shield?.requiredResources;
                if (settings == null || settings.Count == 0 || string.IsNullOrEmpty(settings[0].ResourceName)) continue;
                yield return ((double)settings[0].Rate * shield.PyrolysisLossFactor, Capacity(core.data, settings[0].ResourceName));
            }
        }

        private static Data_Heatshield ShieldOf(PartCore core)
        {
            var direct = core.modules?.OfType<Data_Heatshield>().FirstOrDefault();
            if (direct != null) return direct;
            if (core.data.serializedPartModules == null) return null;
            foreach (var module in core.data.serializedPartModules)
            {
                if (module.ModuleData == null) continue;
                foreach (var data in module.ModuleData)
                    if (data.DataType == typeof(Data_Heatshield) && data.DataObject is Data_Heatshield shield) return shield;
            }
            return null;
        }

        /// <summary>The <c>native: heat shields:</c> line of the session.</summary>
        internal static string Summary()
        {
            var r = NativeModules.Record;
            return NativeLines.HeatshieldSummary(r.PartsWith(AblatorMapping.NativeModule), r.CountFlag(FlagNoResource),
                r.FlagsWithPrefix(FlagNotReproduced));
        }
    }
}
