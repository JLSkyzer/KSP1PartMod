using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Science;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 experiments → one KSP2 <c>Module_ScienceExperiment</c> per part, and KSP1 science parts → report storage
    /// (<c>ksp1_science</c>, science plan (h), Task 4). Registered under <c>ModuleScienceExperiment</c>,
    /// <c>DMModuleScienceAnimate</c>, <c>DMModuleScienceAnimateGeneric</c> (experiments) and <c>ModuleScienceContainer</c> (storage only).
    /// <list type="bullet">
    /// <item>the first experiment call of a part's import context reads every experiment and container module of
    /// <c>context.PartNode</c>, resolves them (<see cref="ScienceMapping"/>, over the definitions of <see cref="ScienceDefinitions"/>),
    /// builds one <c>Data_ScienceExperiment</c> with Redux's <c>CreateSerialized</c>, and sets <c>PartData.HasReportStorage</c> when the
    /// rule says so. The node it was called with replaces the module it matches (the same node, else the module of the same name
    /// with the most identical values: B9's scratch re-import of an edited copy, which is built but not recorded nor logged).
    /// Later experiment calls in that context return false (merged);</item>
    /// <item>no resolved experiment: no module, storage still follows the rule (a container);</item>
    /// <item>a container call sets storage and returns false;</item>
    /// <item>a context that already holds a <c>Data_ScienceExperiment</c> (a future Redux importer) gets nothing (noted).</item>
    /// </list>
    /// Any failure removes the data added and restores <c>HasReportStorage</c>: the part is then imported as 0.11.0 does.</summary>
    internal sealed class ScienceImporter : IKsp1PartModuleImporter
    {
        private const string FlagNotReproduced = "science:nr:";
        private const string FlagSkipped = "science:skip:";
        private const string FlagStorage = "science:storage";
        private const string FlagContainer = "science:container";
        /// <summary>The "native" side of the container converter's pair.</summary>
        public const string StorageTarget = "report storage";
        /// <summary>The switches of the container converter (its off line).</summary>
        public const string ContainerSwitches = "ksp1_science or science_report_storage";
        public const string ExistingModuleNote = "a Data_ScienceExperiment is already on the part, KSP1 experiments not converted";

        private readonly AnimationSettings _settings;
        private readonly bool _container;

        /// <summary>Tests: the KSP1 definitions (null: <see cref="ScienceDefinitions.Definitions"/>).</summary>
        internal static Func<IReadOnlyDictionary<string, ScienceDefinitionModel>> DefinitionsSource;
        /// <summary>Tests: the KSP2 experiments (null: <see cref="ScienceDefinitions.Natives"/>).</summary>
        internal static Func<NativeExperiments> NativesSource;
        /// <summary>Tests: called after the module is built and storage set, before the part is counted (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;
        /// <summary>Tests: the imported KSP1 part nodes for the "not imported" census instead of Redux's registry.</summary>
        internal static Func<IEnumerable<Ksp1ConfigNode>> ImportedSource;

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, object> Seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        private static readonly object SeenMark = new object();
        /// <summary>By part name: the resolution of the part's science (read by the runtime and the audit).</summary>
        private static readonly Dictionary<string, SciencePart> Parts = new Dictionary<string, SciencePart>(StringComparer.Ordinal);
        private static bool _summaryLogged;

        /// <param name="settings">The arming's settings (null: defaults).</param>
        /// <param name="container">The <c>ModuleScienceContainer</c> importer (storage only).</param>
        public ScienceImporter(AnimationSettings settings, bool container)
        {
            _settings = settings ?? AnimationSettings.Defaults;
            _container = container;
        }

        /// <summary>The members the experiment importers need; empty when all are found.</summary>
        internal static List<string> MissingMembers() => new List<string>(ScienceAccess.Get().MissingExperiment);

        /// <summary>The members the container importer needs; empty when all are found.</summary>
        internal static List<string> MissingStorageMembers() => new List<string>(ScienceAccess.Get().MissingStorage);

        /// <summary>The recorded science of <paramref name="part"/> (configurations, resolutions, clip targets); null when none.</summary>
        internal static SciencePart PartOf(string part)
        {
            if (part == null) return null;
            lock (Lock) return Parts.TryGetValue(part, out var p) ? p : null;
        }

        /// <summary>Tests: records <paramref name="p"/> under <paramref name="part"/> as an import would (the runtime reads it).</summary>
        internal static void RecordForTests(string part, SciencePart p)
        {
            lock (Lock) Parts[part] = p;
        }

        /// <summary>The recorded parts that got <c>Module_ScienceExperiment</c>.</summary>
        internal static IReadOnlyList<string> PartsWithModule()
        {
            var r = NativeModules.Record;
            lock (Lock) return Parts.Keys.Where(k => r.ConvertedModules(k).Contains(ScienceMapping.NativeModule)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private static IReadOnlyDictionary<string, ScienceDefinitionModel> Definitions() => DefinitionsSource?.Invoke() ?? ScienceDefinitions.Definitions;
        private static NativeExperiments Natives() => NativesSource?.Invoke() ?? ScienceDefinitions.Natives;

        private static List<Ksp1ConfigNode> RawModules(Ksp1ConfigNode partNode) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>()).Where(n => n != null).ToList(); // as the planner numbers them

        /// <summary>The part's modules as the mapping reads them, with <paramref name="called"/> in place of the module it matches: the
        /// same node, else the module of the same name with the most identical values (ties: the first), else appended.
        /// <paramref name="copy"/>: <paramref name="called"/> is not one of the part's nodes (B9's edited copy).</summary>
        internal static List<IKsp1Node> WithCalled(List<Ksp1ConfigNode> raw, Ksp1ConfigNode called, out bool copy)
        {
            var list = raw.Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
            copy = called != null && !raw.Any(n => ReferenceEquals(n, called));
            if (!copy) return list;
            var adapter = new Ksp1NodeAdapter(called);
            var name = called.GetValue("name")?.Trim();
            var values = Pairs(adapter);
            var best = -1;
            var bestScore = -1;
            for (var i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].GetValue("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
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

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule) =>
            _container ? ImportContainer(moduleNode, context, out serializedModule) : ImportExperiments(moduleNode, context, out serializedModule);

        private bool ImportExperiments(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            bool? storageBefore = null;
            try
            {
                if (context == null || moduleNode == null) return false;
                lock (Lock)
                {
                    if (Seen.TryGetValue(context, out _)) return false; // merged into the module built at the part's first call
                    // Marked before the work: if this first call fails, the part's other experiment modules are dropped too.
                    Seen.Add(context, SeenMark);
                }
                if (context.ModuleData.Any(ScienceAccess.IsExperiment))
                {
                    NativeModules.Note(part, ExistingModuleNote);
                    return false;
                }

                var modules = WithCalled(RawModules(context.PartNode), moduleNode, out var copy);
                var p = ScienceMapping.Resolve(modules, Definitions(), Natives(), _settings);
                var access = ScienceAccess.Get();
                var storage = p.ReportStorage && access.HasStorage;
                if (storage)
                {
                    storageBefore = ScienceAccess.StorageOf(context.PartData);
                    ScienceAccess.SetStorage(context.PartData, true);
                }
                string line = null;
                if (p.HasModule)
                {
                    var data = ScienceAccess.NewExperiment(p);
                    added = context.ModuleData.Count;
                    serializedModule = NativeModules.CreateSerialized(access.Module, data, context);
                    line = ScienceLines.Part(part, p.Configurations.Select(c => (c.Ksp1Id, c.ExperimentDefinitionId, c.Kind)), p.CrewRequired, storage);
                }
                FaultForTests?.Invoke(part);
                // Complete: counted, then logged (a failed part leaves no line and no count). B9's edited copies are neither.
                if (!copy) Record(part, p, storage);
                added = -1;
                storageBefore = null;
                if (!copy) Log(part, p, line);
                return p.HasModule;
            }
            catch (Exception e)
            {
                // Redux's own result: the modules dropped, nothing of ours left on the part.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                if (storageBefore.HasValue) TryRestore(context, storageBefore.Value);
                serializedModule = default;
                NativeModules.ImporterFailed(part, moduleNode?.GetValue("name")?.Trim() ?? ScienceMapping.StockModule, e);
                return false;
            }
        }

        private bool ImportContainer(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            bool? storageBefore = null;
            try
            {
                if (context == null || moduleNode == null) return false;
                var modules = WithCalled(RawModules(context.PartNode), moduleNode, out var copy);
                var p = ScienceMapping.Resolve(modules, Definitions(), Natives(), _settings);
                var storage = p.ReportStorage && ScienceAccess.Get().HasStorage;
                if (storage)
                {
                    storageBefore = ScienceAccess.StorageOf(context.PartData);
                    ScienceAccess.SetStorage(context.PartData, true);
                }
                FaultForTests?.Invoke(part);
                storageBefore = null;
                // A part with experiments is recorded and logged by its experiment call; a container-only part here.
                if (!copy && p.ExperimentModules == 0)
                {
                    Record(part, p, storage);
                    Log(part, p, null);
                }
                return false;
            }
            catch (Exception e)
            {
                if (storageBefore.HasValue) TryRestore(context, storageBefore.Value);
                NativeModules.ImporterFailed(part, ScienceMapping.ContainerModule, e);
                return false;
            }
        }

        private static void TryRestore(Ksp1PartImportContext context, bool value)
        {
            try { ScienceAccess.SetStorage(context?.PartData, value); }
            catch (Exception) { /* nothing more to undo */ }
        }

        private static void Record(string part, SciencePart p, bool storage)
        {
            var record = NativeModules.Record;
            if (p.HasModule) record.AddConversion(part, ScienceMapping.NativeModule);
            if (storage)
            {
                record.Flag(part, FlagStorage);
                if (p.HasContainer) record.Flag(part, FlagContainer);
            }
            foreach (var r in p.Resolutions.Where(r => r.Kind == ScienceResolutionKind.Skipped)) record.Flag(part, FlagSkipped + r.Ksp1Id);
            foreach (var field in p.NotReproduced) record.Flag(part, FlagNotReproduced + field);
            lock (Lock) Parts[part] = p;
        }

        private static void Log(string part, SciencePart p, string line)
        {
            try
            {
                var record = NativeModules.Record;
                if (line != null && record.AddNote(part, line)) NativeModules.Info(line);
                foreach (var r in p.Resolutions.Where(r => r.Kind == ScienceResolutionKind.Skipped))
                {
                    var skipped = ScienceLines.Skipped(part, r.Ksp1Id, r.SkipReason);
                    if (record.AddNote(part, skipped)) NativeModules.Info(skipped);
                }
                foreach (var note in p.Notes) NativeModules.Note(part, note);
            }
            catch (Exception e)
            {
                // Only the log failed: the module stays.
                NativeModules.Error(NativeLines.Prefix + part + ": science lines not logged: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ summary

        /// <summary>The <c>native: science:</c> line, once for the four converters (the first armed asks).</summary>
        internal static IEnumerable<string> SummaryLines()
        {
            var r = NativeModules.Record;
            int parts, generated = 0, identical = 0, closest = 0, merged = 0;
            lock (Lock)
            {
                if (_summaryLogged) yield break;
                _summaryLogged = true;
                var converted = Parts.Where(kv => kv.Value.HasModule && r.ConvertedModules(kv.Key).Contains(ScienceMapping.NativeModule)).Select(kv => kv.Value).ToList();
                parts = converted.Count;
                foreach (var p in converted)
                {
                    merged += Math.Max(0, p.ExperimentModules - 1);
                    foreach (var x in p.Resolutions)
                    {
                        if (x.Kind == ScienceResolutionKind.Generated) generated++;
                        else if (x.Kind == ScienceResolutionKind.Identical) identical++;
                        else if (x.Kind == ScienceResolutionKind.Closest) closest++;
                    }
                }
            }
            var scope = NotImported();
            int Of(string k) => scope != null && scope.TryGetValue(k, out var n) ? n : 0;
            yield return ScienceLines.Summary(parts, generated, identical, closest, r.CountFlag(FlagStorage), r.CountFlag(FlagContainer), merged,
                r.FlagsWithPrefix(FlagSkipped).Select(x => (x.Flag, x.Parts)), r.FlagsWithPrefix(FlagNotReproduced).Select(x => (x.Flag, x.Parts)),
                Of("ModuleScienceLab"), Of("ModuleScienceConverter"), Of("ModuleTestSubject"), Of("ground science"), Of("specialised DMagic"));
        }

        /// <summary>The imported KSP1 parts per out-of-scope category (<see cref="ScienceMapping.OutOfScope"/>), each part once per
        /// category; null when the registry could not be read (logged).</summary>
        internal static Dictionary<string, int> NotImported()
        {
            try
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var node in (ImportedSource ?? ImportedNodes)())
                foreach (var scope in RawModules(node).Select(m => ScienceMapping.OutOfScope(m.GetValue("name"))).Where(s => s != null).Distinct())
                    counts[scope] = counts.TryGetValue(scope, out var n) ? n + 1 : 1;
                return counts;
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "science: not-imported census failed: " + e.Message);
                return null;
            }
        }

        private static IEnumerable<Ksp1ConfigNode> ImportedNodes() =>
            Ksp1ImportedPartRegistry.ImportedParts.Where(e => e != null).Select(e => e.PartNode);

        internal static void ResetSession()
        {
            lock (Lock)
            {
                Parts.Clear();
                _summaryLogged = false;
            }
            DefinitionsSource = null;
            NativesSource = null;
            FaultForTests = null;
            ImportedSource = null;
        }
    }
}
