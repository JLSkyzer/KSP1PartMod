using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Game;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 <c>ModuleActiveRadiator</c>s → one KSP2 cooler per part (<c>ksp1_radiators</c>, radiators plan (h)):
    /// <c>Module_Cooler</c> (one <c>Data_Cooler</c>, Redux's single <c>CreateSerialized</c>), or, when the part also has a
    /// <c>ModuleDeployableRadiator</c>, <c>Module_ActiveRadiator</c> (<c>Data_Cooler</c> + <c>Data_ActiveRadiator</c>, Redux's list
    /// <c>CreateSerialized</c>, <see cref="RadiatorAccess"/>). Values from <see cref="RadiatorMapping"/>; the EC pace from the
    /// game's own coolers (<see cref="Reference"/>).
    /// <list type="bullet">
    /// <item>the first call of a part's import context builds the cooler from every radiator of <c>context.PartNode</c>; the node
    /// it was called with replaces the radiator it matches (the same node, else the one with the most identical values: B9's
    /// scratch re-import), else is appended. Later calls in that context return false (merged into the cooler);</item>
    /// <item>a context that already holds a <c>Data_Cooler</c> (a future Redux importer) gets nothing (noted);</item>
    /// <item>without the list form (or <c>Data_ActiveRadiator</c>), a deployable radiator gets <c>Module_Cooler</c> and is read
    /// as a fixed one (it cools folded), logged once;</item>
    /// <item><c>ModuleDeployableRadiator</c> gets no importer of ours: Redux's <c>unsupported</c> line stays, and our Deploy
    /// entry animates it.</item>
    /// </list>
    /// Any failure leaves the part as Redux 0.10.0 imports it (the modules dropped, nothing of ours left on the part).</summary>
    internal sealed class RadiatorImporter : IKsp1PartModuleImporter
    {
        private const string FlagNotReproduced = "radiator:nr:";
        public const string ExistingCoolerNote = "a Data_Cooler is already on the part, ModuleActiveRadiator not converted";
        /// <summary>The part note naming the <c>ModuleDeployableRadiator</c> fields that have no counterpart.</summary>
        internal static string DeployableNote(IEnumerable<string> fields) =>
            RadiatorMapping.DeployableModule + ": " + string.Join(", ", fields) + " not reproduced (no sun tracking, wind or breaking in KSP2's cooler)";

        private readonly double _fluxScale;
        private readonly bool _ksp1Rates, _autoOn;

        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the conversion is counted (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;
        /// <summary>Tests: the native coolers <c>(part, kW, EC/s)</c> instead of the game's parts.</summary>
        internal static Func<IEnumerable<(string Part, double Kw, double EcPerSecond)>> ReferenceSource;
        /// <summary>Tests: the imported KSP1 part nodes for the passive census instead of Redux's registry.</summary>
        internal static Func<IEnumerable<Ksp1ConfigNode>> PassiveSource;

        private sealed class PartStats
        {
            public double Kw;
            public int Merged, Dropped;
        }

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, object> Seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        private static readonly object SeenMark = new object();
        private static readonly Dictionary<string, PartStats> Stats = new Dictionary<string, PartStats>(StringComparer.Ordinal);
        private static NativeCoolerRef _reference;
        private static bool _activeMissingLogged;

        /// <param name="fluxScale"><c>radiator_flux_scale</c>.</param>
        /// <param name="ksp1Rates"><c>radiator_ksp1_rates</c>: the cfg EC rate (false: KSP2's own EC per kW).</param>
        /// <param name="autoOn"><c>radiator_auto_on</c>.</param>
        public RadiatorImporter(double fluxScale, bool ksp1Rates, bool autoOn)
        {
            _fluxScale = fluxScale;
            _ksp1Rates = ksp1Rates;
            _autoOn = autoOn;
        }

        /// <summary>The members without which no radiator is converted; empty when all are found.</summary>
        internal static List<string> MissingMembers() => new List<string>(RadiatorAccess.Get().MissingCooler);

        private static List<Ksp1ConfigNode> Nodes(Ksp1ConfigNode partNode, string module) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>())
            .Where(n => n != null && string.Equals(n.GetValue("name")?.Trim(), module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        /// <summary>The part's radiators with <paramref name="called"/> in place of its entry: the same node, else the radiator
        /// with the most identical values (ties: the first; B9 edits a copy of one radiator), else appended.</summary>
        internal static List<IKsp1Node> WithCalled(List<Ksp1ConfigNode> raw, Ksp1ConfigNode called)
        {
            var list = raw.Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
            if (called == null || raw.Any(n => ReferenceEquals(n, called))) return list;
            var adapter = new Ksp1NodeAdapter(called);
            var values = Pairs(adapter);
            var best = -1;
            var bestScore = -1;
            for (var i = 0; i < list.Count; i++)
            {
                var score = Pairs(list[i]).Count(values.Contains);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }
            if (best >= 0) list[best] = adapter;
            else list.Add(adapter);
            return list;
        }

        private static HashSet<string> Pairs(IKsp1Node node) =>
            new HashSet<string>(node.AllValues.Select(kv => (kv.Key?.Trim() ?? "") + "=" + IsruCfg.Text(kv.Value)), StringComparer.Ordinal);

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
                    if (Seen.TryGetValue(context, out _)) return false; // merged into the cooler built at the part's first call
                    Seen.Add(context, SeenMark);
                }
                if (context.ModuleData.Any(RadiatorAccess.IsCooler))
                {
                    NativeModules.Note(part, ExistingCoolerNote);
                    return false;
                }

                var access = RadiatorAccess.Get();
                var radiators = WithCalled(Nodes(context.PartNode, RadiatorMapping.Ksp1Module), moduleNode);
                var deployables = Nodes(context.PartNode, RadiatorMapping.DeployableModule);
                var hasDeployable = deployables.Count > 0;
                if (hasDeployable && !access.HasActiveRadiator)
                {
                    LogActiveMissingOnce(access);
                    hasDeployable = false; // Module_Cooler, read as a fixed radiator: it cools folded
                }

                var reference = Reference();
                var r = ConverterImporter.Resources(ConverterImporter.CurrentDatabase(), null);
                var map = RadiatorMapping.From(radiators, hasDeployable, r, reference.KwPerEc, _fluxScale, _ksp1Rates, _autoOn);
                var stats = new PartStats { Kw = map.FluxRemoved, Merged = Math.Max(0, map.Merged - 1), Dropped = map.DroppedResources };
                if (!map.HasModule)
                {
                    lock (Lock) Stats[part] = stats;
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                    return false;
                }

                var cooler = RadiatorAccess.NewCooler(map.FluxRemoved, map.Resources.Select(x => (x.Name, x.Rate)), map.StartsOn);
                added = context.ModuleData.Count;
                if (map.Kind == RadiatorKind.ActiveRadiator)
                    serializedModule = RadiatorAccess.CreateSerialized(access.ModuleActiveRadiator,
                        new[] { cooler, RadiatorAccess.NewActiveRadiator() }, context);
                else
                    serializedModule = NativeModules.CreateSerialized(access.ModuleCooler, cooler, context);

                var deployFields = deployables.SelectMany(d => RadiatorMapping.DeployableNotReproduced(new Ksp1NodeAdapter(d)))
                    .Distinct(StringComparer.Ordinal).ToList();
                var line = RadiatorLines.Part(part, map);
                FaultForTests?.Invoke(part);
                // The conversion is complete: counted, then logged (a failed part leaves no line and no count).
                var record = NativeModules.Record;
                record.AddConversion(part, map.NativeModule);
                foreach (var field in map.NotReproduced.Concat(deployFields)) record.Flag(part, FlagNotReproduced + field);
                lock (Lock) Stats[part] = stats;
                added = -1;
                try
                {
                    if (record.AddNote(part, line)) NativeModules.Info(line);
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                    if (deployFields.Count > 0) NativeModules.Note(part, DeployableNote(deployFields));
                }
                catch (Exception e)
                {
                    // Only the log failed: the module stays.
                    NativeModules.Error(NativeLines.Prefix + part + ": radiator lines not logged: " + e.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, RadiatorMapping.Ksp1Module, e);
                return false;
            }
        }

        private static void LogActiveMissingOnce(RadiatorAccess.Targets access)
        {
            lock (Lock)
            {
                if (_activeMissingLogged) return;
                _activeMissingLogged = true;
            }
            if (!access.HasListForm) NativeModules.Warn(RadiatorLines.ListFormMissing);
            foreach (var m in access.MissingActive.Where(m => !m.StartsWith("Ksp1PartModuleImporterRegistry.", StringComparison.Ordinal)))
                NativeModules.Warn(NativeLines.MemberNotFound(m, RadiatorMapping.NativeActiveRadiator + " (deployable radiators get Module_Cooler, they cool folded)"));
        }

        // ------------------------------------------------------------------ the native reference

        /// <summary>The native cooler pace, read and logged once per session before the first KSP1 radiator: every
        /// <c>Data_Cooler</c> with an EC resource on the game's KSP2 parts. A read that throws logs one line and uses the fallback.</summary>
        internal static NativeCoolerRef Reference()
        {
            lock (Lock)
            {
                if (_reference != null) return _reference;
                List<(string, double, double)> coolers;
                try
                {
                    coolers = (ReferenceSource ?? GameCoolers)().ToList();
                }
                catch (Exception e)
                {
                    NativeModules.Error(NativeLines.Prefix + "cooler reference not read, fallback used: " + e);
                    coolers = new List<(string, double, double)>();
                }
                _reference = NativeCoolerRef.From(coolers);
                NativeModules.Info(RadiatorLines.CoolerReference(_reference));
                return _reference;
            }
        }

        private static IEnumerable<(string, double, double)> GameCoolers()
        {
            var parts = GameManager.Instance?.Game?.Parts?.PartData;
            return parts == null ? Enumerable.Empty<(string, double, double)>() : CoolersOf(parts.ToList());
        }

        /// <summary>The coolers of <paramref name="parts"/> (part name → <c>PartCore</c>) that use EC: <c>(part, kW, EC/s)</c>; the
        /// KSP1 imports and the variants with '+' left out. Tests: real <c>PartCore</c>s.</summary>
        internal static IEnumerable<(string Part, double Kw, double EcPerSecond)> CoolersOf(IEnumerable<KeyValuePair<string, PartCore>> parts)
        {
            foreach (var kv in parts)
            {
                var core = kv.Value;
                if (kv.Key == null || kv.Key.Contains('+') || core?.data == null) continue;
                if (Ksp1Tags.IsKsp1Import(core.data.tags)) continue;
                var part = core.data.partName ?? kv.Key;
                foreach (var data in IsruReference.DataOf(core))
                {
                    if (!RadiatorAccess.IsCooler(data)) continue;
                    var (kw, ec) = RadiatorAccess.PaceOf(data);
                    if (ec > 0) yield return (part, kw, ec);
                }
            }
        }

        // ------------------------------------------------------------------ summary

        /// <summary>A KSP1 part that radiates passively: <c>emissiveConstant</c> ≥ 0.8 and no <c>ModuleActiveRadiator</c>.</summary>
        internal static bool IsPassive(Ksp1ConfigNode partNode)
        {
            if (partNode == null) return false;
            if (!IsruCfg.TryNumber(partNode.GetValue("emissiveConstant")?.Trim(), out var emissive) || !(emissive >= 0.8)) return false;
            return Nodes(partNode, RadiatorMapping.Ksp1Module).Count == 0;
        }

        private static IEnumerable<Ksp1ConfigNode> ImportedNodes() =>
            Ksp1ImportedPartRegistry.ImportedParts.Where(e => e != null).Select(e => e.PartNode);

        /// <summary>The <c>native: radiators:</c> line, then the passive census.</summary>
        internal static IEnumerable<string> SummaryLines()
        {
            var r = NativeModules.Record;
            double kw;
            int merged, dropped;
            lock (Lock)
            {
                kw = Stats.Where(kv => r.ConvertedModules(kv.Key).Any(m => m == RadiatorMapping.NativeCooler || m == RadiatorMapping.NativeActiveRadiator))
                    .Sum(kv => kv.Value.Kw);
                merged = Stats.Values.Sum(s => s.Merged);
                dropped = Stats.Values.Sum(s => s.Dropped);
            }
            yield return RadiatorLines.Summary(r.PartsWith(RadiatorMapping.NativeCooler), r.PartsWith(RadiatorMapping.NativeActiveRadiator), kw,
                merged, dropped, r.FlagsWithPrefix(FlagNotReproduced));
            var passive = PassiveCount();
            if (passive >= 0) yield return RadiatorLines.Passive(passive);
        }

        /// <summary>The passive census; -1 when the registry could not be read (logged).</summary>
        private static int PassiveCount()
        {
            try
            {
                return (PassiveSource ?? ImportedNodes)().Count(IsPassive);
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "radiators: passive census failed: " + e.Message);
                return -1;
            }
        }

        internal static void ResetSession()
        {
            lock (Lock)
            {
                Stats.Clear();
                _reference = null;
                _activeMissingLogged = false;
            }
            FaultForTests = null;
            ReferenceSource = null;
            PassiveSource = null;
        }
    }
}
