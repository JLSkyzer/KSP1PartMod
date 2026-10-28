using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Game;
using KSP.Game.Science;
using KSP.IO;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native.Science;
using Newtonsoft.Json.Linq;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using SpaceWarp2.API.Loading;

namespace KSP1PartMod.Game
{
    /// <summary>The KSP1 <c>EXPERIMENT_DEFINITION</c>s as KSP2 experiment definitions <c>KSP1_&lt;id&gt;</c> (science plan, Task 3). Armed at
    /// pre-initialization, after the settings and before <see cref="NativeModules"/>:
    /// <list type="bullet">
    /// <item><b>Read</b> (with <c>ksp1_science</c>): every cfg under Redux's KSP1 import roots; the files naming
    /// <c>EXPERIMENT_DEFINITION</c> or <c>experimentID</c> are parsed with Redux's cfg parser. ModuleManager edits of definitions are
    /// counted, not applied. The record (<see cref="Definitions"/>) is kept for the importer, built or not: a definition that is not
    /// generated still gives the closest KSP2 experiment its situations.</item>
    /// <item>With <c>science_generate_definitions</c> too: one CSV of every term through <c>ReduxLocalizer</c>; then either
    /// <b>route A</b> (<c>science_definitions_via_patch_manager</c>): one Patch Manager duplicate of <c>EnvironmentSurvey</c> per
    /// definition whose <c>data</c> is replaced (feature-gated by Patch Manager), or <b>route B</b>: a prefix on
    /// <c>ScienceExperimentsDataStore.Initialize</c> wraps its callback, which then adds the definitions to the store once it reports
    /// <c>IsInitialized</c> and before the game hears of it, when KSP1PartMod is on in the campaign.</item>
    /// <item><b>Check</b>: after the game's "Loading Science Data", the live store is read: how many <c>KSP1_*</c> ids are there, and
    /// the live KSP2 experiments, which replace the 26w42c list (<see cref="Natives"/>).</item>
    /// </list>
    /// Never throws into the game: every failure is one line, and the import goes on.</summary>
    internal static class ScienceDefinitions
    {
        public const string HarmonyId = "KSP1PartMod.ScienceDefinitions";
        public const string CheckStepName = "Check the KSP1 science definitions";
        /// <summary>The stock asset copied by route A: its asset name equals its id, it covers every situation and both types.</summary>
        public const string DuplicateSource = "EnvironmentSurvey";

        public const string PatchManagerExtensionsType = "PatchManager.Science.SciencePatchExtensions";
        public const string ExperimentUserDataType = "PatchManager.Science.UserData.ExperimentUserData";
        public const string PmScopeType = "PatchManager.CSharpPatching.PmScope";
        public const string LocalizerType = "Redux.ApiImpls.ReduxLocalizer";

        private static readonly IReadOnlyDictionary<string, ScienceDefinitionModel> Empty =
            new Dictionary<string, ScienceDefinitionModel>(StringComparer.Ordinal);

        /// <summary>The KSP1 definitions read, by key (base and DMagic variants), built or not. Empty until read.</summary>
        internal static IReadOnlyDictionary<string, ScienceDefinitionModel> Definitions { get; private set; } = Empty;

        /// <summary>The KSP2 experiments: the live store's once checked, the 26w42c list before.</summary>
        internal static NativeExperiments Natives => _live ?? NativeExperiments.Redux26w42c;

        /// <summary>The definitions queued (built, generation on).</summary>
        internal static IReadOnlyList<ScienceDefinitionModel> Queued => _queued;

        /// <summary>Whether the definitions go to the experiment store (route B).</summary>
        internal static bool ViaStore { get; private set; }

        private static NativeExperiments _live;
        private static List<ScienceDefinitionModel> _queued = new List<ScienceDefinitionModel>();
        private static readonly Dictionary<string, string> Json = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _armed;

        private static void Info(string line) => KSP1PartModPlugin.Log?.LogInfo(line);
        private static void Warn(string line) => KSP1PartModPlugin.Log?.LogWarning(line);
        private static void Error(string line) => KSP1PartModPlugin.Log?.LogError(line);

        /// <param name="modId">The mod's Patch Manager id (<c>KerbalMod.PM</c> is <c>Patching.ForMod(SWMetadata.Guid)</c>).</param>
        public static void TryApply(Harmony harmony, AnimationSettings settings, string modId)
        {
            if (_armed) return;
            _armed = true;
            settings = settings ?? AnimationSettings.Defaults;
            if (!settings.Ksp1Science) return;

            var watch = Stopwatch.StartNew();
            var scan = Scan();
            var duplicates = new List<string>();
            var defs = scan.Build(settings, Resolver(scan), duplicates);
            watch.Stop();
            Definitions = defs;
            Info(ScienceLines.DefinitionsRead(scan.DefinitionNodes.Count, scan.DefinitionFiles, scan.CfgFiles, watch.ElapsedMilliseconds, scan.MmEdits));
            foreach (var f in scan.Failures) Warn(ScienceLines.ReadFailed(f, "parse error"));

            RegisterCheck();
            if (!settings.ScienceGenerateDefinitions) return;

            var built = defs.Values.Where(d => d.IsBuilt).OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
            foreach (var d in built)
            {
                Info(ScienceLines.Definition(d));
                Json[d.ExperimentId] = ScienceDefinitionJson.Data(d).ToString(Newtonsoft.Json.Formatting.None);
            }
            _queued = built;
            AddLocalization(built);

            var viaPm = false;
            if (settings.ScienceDefinitionsViaPatchManager)
            {
                if (!PatchManagerPresent()) Warn(ScienceLines.PatchManagerMissing);
                else
                {
                    var queued = 0;
                    try
                    {
                        queued = QueueThroughPatchManager(modId, built);
                        viaPm = queued == built.Count;
                    }
                    catch (Exception e)
                    {
                        Error(ScienceLines.Failed("definitions: Patch Manager queue", e.ToString()));
                    }
                    if (!viaPm) Warn(ScienceLines.PatchManagerMissing);
                }
            }
            if (!viaPm)
            {
                // Also after a partial Patch Manager queue: the store skips the ids already there.
                try
                {
                    var initialize = AccessTools.Method(typeof(ScienceExperimentsDataStore), nameof(ScienceExperimentsDataStore.Initialize), new[] { typeof(Action) });
                    if (initialize == null)
                    {
                        Error(ScienceLines.Failed("definitions: ScienceExperimentsDataStore.Initialize(Action) not found, store route", "no definition added"));
                        return;
                    }
                    harmony.Patch(initialize, prefix: new HarmonyMethod(typeof(ScienceDefinitions), nameof(BeforeStoreInitialize)));
                    ViaStore = true;
                }
                catch (Exception e)
                {
                    Error(ScienceLines.Failed("definitions: store route", e.ToString()));
                    return;
                }
            }
            Info(ScienceLines.DefinitionsQueued(built.Count, viaPm, ScienceDefinitionScan.SkipCounts(defs.Values, duplicates.Count)));
        }

        // ---- read

        private static ScienceDefinitionScan Scan()
        {
            var scan = new ScienceDefinitionScan();
            List<string> roots;
            try { roots = Ksp1ImportSettings.GetImportRoots().ToList(); }
            catch (Exception e)
            {
                Error(ScienceLines.Failed("definitions: import roots", e.Message));
                return scan;
            }
            foreach (var root in roots)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.cfg", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch (Exception e)
                {
                    Error(ScienceLines.Failed("definitions: listing " + root, e.Message));
                    continue;
                }
                foreach (var path in files)
                {
                    string text;
                    try { text = File.ReadAllText(path); }
                    catch (Exception e)
                    {
                        Warn(ScienceLines.ReadFailed(path, e.Message));
                        scan.Add(path, null, null);
                        continue;
                    }
                    scan.Add(path, text, t => new Ksp1NodeAdapter(Ksp1ConfigNode.Parse(t)));
                }
            }
            return scan;
        }

        /// <summary>The <c>#autoLOC</c> resolver: Redux's own KSP1 localization catalog, over the cfg files that hold a
        /// <c>Localization</c> node, built at the first tag. Null for an unknown tag.</summary>
        private static Func<string, string> Resolver(ScienceDefinitionScan scan)
        {
            Ksp1LocalizationCatalog catalog = null;
            var tried = false;
            return tag =>
            {
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        var files = new List<Ksp1ConfigFile>();
                        foreach (var path in scan.LocalizationFiles)
                        {
                            try { files.Add(new Ksp1ConfigFile(null, path, "", Ksp1ConfigNode.Parse(File.ReadAllText(path)))); }
                            catch (Exception) { /* a dictionary that does not parse is left out */ }
                        }
                        catalog = Ksp1LocalizationCatalog.FromFiles(files);
                    }
                    catch (Exception e)
                    {
                        Warn(ScienceLines.Failed("definitions: localization catalog", e.Message));
                    }
                }
                var resolved = catalog?.Resolve(tag);
                return resolved == null || resolved == tag ? null : resolved;
            };
        }

        // ---- localization

        internal static bool LocalizerPresent()
        {
            var t = AccessTools.TypeByName(LocalizerType);
            if (t == null) return false;
            var create = AccessTools.Method(t, "CreateSourceFromCsv", new[] { typeof(string) });
            return create != null && create.IsStatic && AccessTools.Method(t, "AddSource", new[] { create.ReturnType }) != null;
        }

        private static void AddLocalization(List<ScienceDefinitionModel> built)
        {
            try
            {
                if (!LocalizerPresent())
                {
                    Warn(ScienceLines.LocalizationMissing);
                    return;
                }
                var csv = ScienceDefinitionJson.Csv(built, out var terms, out var names, out var flavors);
                AddCsv(csv);
                Info(ScienceLines.Localization(terms, names, flavors));
            }
            catch (Exception e)
            {
                Warn(ScienceLines.LocalizationMissing + " (" + e.Message + ")");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddCsv(string csv) =>
            Redux.ApiImpls.ReduxLocalizer.AddSource(Redux.ApiImpls.ReduxLocalizer.CreateSourceFromCsv(csv));

        // ---- route A

        internal static bool PatchManagerPresent()
        {
            var ext = AccessTools.TypeByName(PatchManagerExtensionsType);
            var scope = AccessTools.TypeByName(PmScopeType);
            var userData = AccessTools.TypeByName(ExperimentUserDataType);
            if (ext == null || scope == null || userData == null) return false;
            var duplicate = AccessTools.Method(ext, "DuplicateExperiment", new[] { scope, typeof(string), typeof(string) });
            var full = AccessTools.Field(userData, "FullToken");
            return duplicate != null && duplicate.IsStatic && full != null && typeof(JToken).IsAssignableFrom(full.FieldType);
        }

        /// <summary>Calls Patch Manager's types directly; kept apart so that a game without them only fails here.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int QueueThroughPatchManager(string modId, List<ScienceDefinitionModel> built)
        {
            var scope = PatchManager.CSharpPatching.Patching.ForMod(modId);
            var queued = 0;
            foreach (var d in built)
            {
                var data = Json[d.ExperimentId];
                PatchManager.Science.SciencePatchExtensions.DuplicateExperiment(scope, DuplicateSource, d.ExperimentId)
                    .Do(asset => ReplaceData(asset, data));
                queued++;
            }
            return queued;
        }

        /// <summary>The <c>.Do</c> of a duplicate: the copy's <c>data</c> becomes the definition; <c>version</c> is kept.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ReplaceData(PatchManager.Science.UserData.ExperimentUserData asset, string dataJson)
        {
            if (asset?.FullToken is JObject full) full["data"] = JObject.Parse(dataJson);
            else Error(ScienceLines.Failed("definitions: Patch Manager duplicate", "the asset is not a JSON object, left as the copy of " + DuplicateSource));
        }

        // ---- route B

        internal static void BeforeStoreInitialize(ScienceExperimentsDataStore __instance, ref Action onInitialized)
        {
            try
            {
                var store = __instance;
                var inner = onInitialized;
                onInitialized = () =>
                {
                    AddToStore(store);
                    inner?.Invoke();
                };
            }
            catch (Exception e)
            {
                Error(ScienceLines.Failed("store", e.ToString()));
            }
        }

        private static void AddToStore(ScienceExperimentsDataStore store)
        {
            try
            {
                if (store == null || !store.IsInitialized) return;
                var plugin = KSP1PartModPlugin.Instance;
                if (plugin != null && !plugin.Enabled)
                {
                    Info(ScienceLines.StoreOff);
                    return;
                }
                int added = 0, existing = 0;
                foreach (var d in _queued)
                {
                    if (store.GetExperimentDefinition(d.ExperimentId) != null)
                    {
                        existing++;
                        continue;
                    }
                    var def = ToDefinition(d);
                    if (def != null && store.AddExperimentDefinition(def)) added++;
                }
                Info(ScienceLines.StoreAdded(added, existing));
            }
            catch (Exception e)
            {
                Error(ScienceLines.Failed("store", e.ToString()));
            }
        }

        /// <summary>A built definition through the game's own JSON path, as the store reads its assets.</summary>
        internal static ExperimentDefinition ToDefinition(ScienceDefinitionModel d) =>
            IOProvider.FromJson<ExperimentCore>(ScienceDefinitionJson.Core(d))?.data;

        // ---- check

        private static void RegisterCheck()
        {
            try
            {
                SaveLoad.AddStepAfter(KSP1PartModPlugin.ModId, CheckStepName, StockFlow.CampaignLoad, TechTreeLoadStep.ScienceStep, OnCheckStep);
            }
            catch (Exception e)
            {
                Error(ScienceLines.Failed("store check step registration", e.Message));
            }
        }

        private static void OnCheckStep(Action resolve, Action<string> reject)
        {
            try { Check(GameManager.Instance?.Game?.ScienceManager?.ScienceExperimentsDataStore); }
            catch (Exception e) { Error(ScienceLines.Failed("store check", e.ToString())); }
            finally { resolve(); }
        }

        internal static void Check(ScienceExperimentsDataStore store)
        {
            if (store == null || !store.IsInitialized)
            {
                Warn(ScienceLines.StoreNotReady);
                return;
            }
            var ids = store.GetAllExperimentIDs();
            var ours = new HashSet<string>(_queued.Select(d => d.ExperimentId), StringComparer.Ordinal);
            var present = ids.Count(ours.Contains);
            var ksp2 = ids.Where(i => !i.StartsWith(ScienceDefinitionBuilder.IdPrefix, StringComparison.Ordinal)).ToList();
            _live = Live(store, ksp2);
            Info(ScienceLines.Store(present, _queued.Count, ksp2));
            var (added, removed, changed) = NativeExperiments.Diff(NativeExperiments.Redux26w42c, _live);
            if (added.Count + removed.Count + changed.Count > 0) Info(ScienceLines.StoreDiffers(added, removed, changed));
        }

        /// <summary>The live KSP2 experiments: the 26w42c table order first (it breaks the last ties), then the others.</summary>
        internal static NativeExperiments Live(ScienceExperimentsDataStore store, IReadOnlyList<string> ksp2Ids)
        {
            var order = NativeExperiments.Redux26w42c.All.Select(e => e.Id).ToList();
            var sorted = ksp2Ids.OrderBy(id => order.IndexOf(id) < 0 ? int.MaxValue : order.IndexOf(id)).ToList();
            var list = new List<NativeExperiment>();
            foreach (var id in sorted)
            {
                var d = store.GetExperimentDefinition(id);
                if (d == null) continue;
                var locations = (d.ValidLocations ?? new List<ResearchLocation>()).Where(l => l != null)
                    .Select(l => new ScienceLocation((Ksp2Situation)(byte)l.ScienceSituation, l.RequiresRegion));
                list.Add(new NativeExperiment(id, locations, (ScienceExperimentKind)(byte)d.ExperimentType, d.RequiresEVA));
            }
            return new NativeExperiments(list);
        }

        /// <summary>Tests: the definitions route B and the check work on.</summary>
        internal static void QueueForTests(IEnumerable<ScienceDefinitionModel> definitions) =>
            _queued = (definitions ?? Enumerable.Empty<ScienceDefinitionModel>()).Where(d => d.IsBuilt).ToList();

        /// <summary>Tests: forget the arming and the record.</summary>
        internal static void ResetForTests()
        {
            _armed = false;
            _live = null;
            _queued = new List<ScienceDefinitionModel>();
            Json.Clear();
            Definitions = Empty;
            ViaStore = false;
        }
    }
}
