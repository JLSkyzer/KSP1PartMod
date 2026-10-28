using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using Redux.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 planetary <c>ModuleResourceHarvester</c>s → one Redux <c>Module_Mine</c> per part (<c>ksp1_drills</c>,
    /// plan (j)): every <c>HarvesterType 0</c> harvester of the part merged into one mining formula
    /// (<see cref="HarvesterMapping"/>). Names through Redux's <c>MapResourceName</c> (after the Ore remap), rates through
    /// Redux's own <c>ConvertAmount</c>, the outputs kept only when Redux has a density map for them (<see cref="IsruReference"/>).
    /// <list type="bullet">
    /// <item>the first call of a part's import context builds the module from <c>context.PartNode</c>'s harvesters, the
    /// called node in place of its entry (B9's scratch re-import, plan (l)); later calls return false, silently;</item>
    /// <item>a context that already holds a <c>Data_Mine</c> gets nothing (noted);</item>
    /// <item>the impact data (<c>ImpactTransform</c>, <c>ImpactRange</c>) is recorded by part name in
    /// <see cref="NativeModules.Isru"/> for the runtime (Task 4). <c>Module_Mine</c>'s <c>vfxArmTopTransform</c> and
    /// <c>vfxArmBottomTransform</c> are prefab fields this import cannot reach: they stay null here (Redux's
    /// <c>vfxArmTopTransform not found</c> warning), and Task 4 sets them from that record before <c>OnInitialize</c>;</item>
    /// <item><c>ModuleAsteroidDrill</c>/<c>ModuleCometDrill</c> on the part are only counted (no importer of ours: Redux's
    /// <c>unsupported</c> line stays).</item>
    /// </list>
    /// Any failure leaves the part as Redux 0.9.0 imports it (the modules dropped, nothing of ours left on the part).</summary>
    internal sealed class HarvesterImporter : IKsp1PartModuleImporter
    {
        private const string FlagNotReproduced = "drill:nr:";

        private readonly double _multiplier, _heatScale;
        private readonly bool _multiplierIsDefault;

        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the drill is counted (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;

        private sealed class PartStats
        {
            public int Ocean, Atmospheric, Exospheric, NoMap;
            public bool Asteroid;
            public IReadOnlyList<string> Resources = Array.Empty<string>();
        }

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, object> Seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        private static readonly object SeenMark = new object();
        private static readonly Dictionary<string, PartStats> Stats = new Dictionary<string, PartStats>(StringComparer.Ordinal);
        private static bool _multiplierLogged;

        public HarvesterImporter(double multiplier, double heatScale, bool multiplierIsDefault)
        {
            _multiplier = multiplier;
            _heatScale = heatScale;
            _multiplierIsDefault = multiplierIsDefault;
        }

        internal static List<string> MissingMembers() => new List<string>(IsruAccess.Get().MissingDrill);

        private static List<Ksp1ConfigNode> Nodes(Ksp1ConfigNode partNode, string module) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>())
            .Where(n => n != null && string.Equals(n.GetValue("name")?.Trim(), module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        /// <summary>The part's harvesters with <paramref name="called"/> in place of its entry: the same node, else the first
        /// with the same <c>ResourceName</c> and <c>HarvesterType</c>, else appended.</summary>
        private static List<IKsp1Node> WithCalled(List<Ksp1ConfigNode> raw, Ksp1ConfigNode called)
        {
            var list = raw.Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
            var at = raw.FindIndex(n => ReferenceEquals(n, called));
            if (at >= 0) return list;
            var adapter = new Ksp1NodeAdapter(called);
            var key = (IsruCfg.Value(adapter, "ResourceName") ?? "", IsruCfg.Value(adapter, "HarvesterType") ?? "0");
            at = list.FindIndex(n => (IsruCfg.Value(n, "ResourceName") ?? "", IsruCfg.Value(n, "HarvesterType") ?? "0") == key);
            if (at >= 0) list[at] = adapter;
            else list.Add(adapter);
            return list;
        }

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            try
            {
                if (context == null || moduleNode == null) return false;
                lock (Lock)
                {
                    if (Seen.TryGetValue(context, out _)) return false; // the module was built at the part's first call
                    Seen.Add(context, SeenMark);
                }
                OreRemap.EnsureVerified();
                var reference = IsruReference.Get();
                if (context.ModuleData.Any(d => d is Data_Mine))
                {
                    NativeModules.Note(part, IsruLines.ExistingMineNote);
                    return false;
                }

                var harvesters = WithCalled(Nodes(context.PartNode, HarvesterMapping.Ksp1Module), moduleNode);
                var partModules = (context.PartNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>())
                    .Where(n => n != null).Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
                var asteroids = HarvesterMapping.CountAsteroidDrills(partModules);

                var db = ConverterImporter.CurrentDatabase();
                var r = ConverterImporter.Resources(db, IsruReference.Minable());
                var map = HarvesterMapping.From(harvesters, r, reference, _multiplier, reference.Drill.HeatPerEc, _heatScale);
                var stats = new PartStats
                {
                    Ocean = map.SkippedOcean,
                    Atmospheric = map.SkippedAtmospheric,
                    Exospheric = map.SkippedExospheric,
                    NoMap = map.SkippedNoMap,
                    Asteroid = asteroids > 0,
                };
                if (!map.HasModule)
                {
                    lock (Lock) Stats[part] = stats;
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                    if (asteroids > 0) NativeModules.Note(part, IsruLines.AsteroidDrillIgnored(asteroids));
                    return false;
                }
                LogMultiplierOnce(map.Multiplier);

                var f = map.Formula;
                var label = IsruReference.DrillFormulaKey();
                if (string.IsNullOrEmpty(label)) label = IsruCfg.Value(harvesters.First(), "ConverterName") ?? "";
                var data = new Data_Mine
                {
                    MiningFormulaDefinition = new ResourceConverterFormulaDefinition
                    {
                        InternalName = f.Id,
                        FormulaLocalizationKey = label,
                        InputResources = Settings(f.Inputs),
                        OutputResources = Settings(f.Outputs),
                        FluxGenerated = (float)f.FluxGenerated,
                    },
                };
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_Mine), data, context);

                var lines = map.Outputs.Select(o => IsruLines.Drill(part, map, o, reference.Drill)).ToList();
                FaultForTests?.Invoke(part);
                // The conversion is complete: counted and recorded, then logged.
                var record = NativeModules.Record;
                record.AddConversion(part, HarvesterMapping.NativeModule);
                foreach (var field in map.NotReproduced) record.Flag(part, FlagNotReproduced + field);
                stats.Resources = f.Outputs.Select(o => o.Name).ToList();
                NativeModules.Isru.SetDrill(part, new DrillImpact(map.ImpactTransform, map.ImpactRange, stats.Resources));
                lock (Lock) Stats[part] = stats;
                added = -1;
                try
                {
                    foreach (var line in lines)
                        if (record.AddNote(part, line)) NativeModules.Info(line);
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                    if (asteroids > 0) NativeModules.Note(part, IsruLines.AsteroidDrillIgnored(asteroids));
                }
                catch (Exception e)
                {
                    NativeModules.Error(NativeLines.Prefix + part + ": drill lines not logged: " + e.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, HarvesterMapping.Ksp1Module, e);
                return false;
            }
        }

        private void LogMultiplierOnce(double multiplier)
        {
            bool first;
            lock (Lock)
            {
                first = !_multiplierLogged;
                _multiplierLogged = true;
            }
            if (first) NativeModules.Info(IsruLines.HarvestMultiplier(multiplier, _multiplierIsDefault));
        }

        private static List<PartModuleResourceSetting> Settings(IEnumerable<IsruFlow> flows) =>
            flows.Select(x => new PartModuleResourceSetting { ResourceName = x.Name, Rate = (float)x.Rate }).ToList();

        /// <summary>The <c>native: drills:</c> line of the session.</summary>
        internal static string Summary()
        {
            var r = NativeModules.Record;
            List<PartStats> all;
            List<string> resources;
            lock (Lock)
            {
                all = Stats.Values.ToList();
                resources = Stats.Where(kv => r.ConvertedModules(kv.Key).Contains(HarvesterMapping.NativeModule))
                    .SelectMany(kv => kv.Value.Resources).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
            }
            return IsruLines.DrillsSummary(r.PartsWith(HarvesterMapping.NativeModule), resources, all.Sum(s => s.Ocean),
                all.Sum(s => s.Atmospheric), all.Sum(s => s.Exospheric), all.Sum(s => s.NoMap), all.Count(s => s.Asteroid),
                r.FlagsWithPrefix(FlagNotReproduced));
        }

        internal static void ResetSession()
        {
            lock (Lock)
            {
                Stats.Clear();
                _multiplierLogged = false;
            }
            FaultForTests = null;
        }
    }
}
