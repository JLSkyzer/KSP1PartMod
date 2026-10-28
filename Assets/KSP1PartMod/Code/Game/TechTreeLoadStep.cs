using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KSP.Game;
using KSP.Game.Science;
using KSP1PartMod.Placement;
using Redux.Ksp1Import.Assets;
using SpaceWarp2.API.Loading;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>At each campaign load, after Redux's KSP1 import, places the imported parts in the tech tree; takes
    /// them back at campaign unload. Never blocks a load: every path ends with done().</summary>
    internal static class TechTreeLoadStep
    {
        public const string StepName = "Place KSP1 parts in the tech tree";
        public const string MappingFile = "tech_mapping.json";
        public const string OverridesFile = "overrides.json";
        private const float TimeoutSeconds = 30f;
        private const float PollSeconds = 0.25f;

        private static CoroutineRunner _runner;
        private static TechTreeWriter _writer;
        // Bumped at each unload, so a step still waiting for the previous campaign stops.
        private static int _generation;

        public const string EarlyStepName = "Place KSP1 parts in the tech tree (before the VAB)";
        /// <summary><c>KSP.Game.Load.LoadScienceManager</c>: builds the <c>ScienceManager</c> and its tech tree store.</summary>
        public const string ScienceStep = "Loading Science Data";

        private static readonly PlacementGate Gate = new PlacementGate();

        /// <summary>Once per session (a second call logs and does nothing). Two steps: an early one right after the
        /// game's "Loading Science Data" (Redux's KSP1 import has run, the tech tree store is new, and the VAB has not
        /// yet built its part list: <c>ObjectAssemblyPartTracker.Initialize</c> filters parts through
        /// <c>TechNodeDataStore.PartIDLookup</c> once, at construction), and the <c>Loaded</c> action, which only places
        /// when the early step could not. In the 2026-10-25 log the <c>Loaded</c> action ran after the VAB was built,
        /// so a game started in the VAB showed no KSP1 part and no KSP1 category until the VAB was entered again.</summary>
        public static void Register()
        {
            if (!Gate.TryRegister())
            {
                KSP1PartModPlugin.Log.LogWarning("[KSP1PartMod] placement steps already registered, second registration ignored");
                return;
            }
            SaveLoad.AddCampaignLoadAction(KSP1PartModPlugin.ModId, StepName, CampaignPhase.Loaded, OnCampaignLoad);
            try
            {
                SaveLoad.AddStepAfter(KSP1PartModPlugin.ModId, EarlyStepName, StockFlow.CampaignLoad, ScienceStep, OnEarlyStep);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] early placement step not registered, placing at campaign loaded only: " + e);
            }
        }

        /// <summary>Synchronous: places when the import and the tree are ready, else leaves it to the Loaded action.
        /// Always resolves.</summary>
        private static void OnEarlyStep(Action resolve, Action<string> reject)
        {
            try
            {
                Gate.Reset();
                if (Ready(out var imported, out var treeReady))
                {
                    Place(TreeStore());
                    Gate.PlacedEarly();
                    KSP1PartModPlugin.Log.LogInfo($"[KSP1PartMod] placed after '{ScienceStep}', before the VAB builds its part list");
                }
                else
                    KSP1PartModPlugin.Log.LogInfo(
                        $"[KSP1PartMod] not ready after '{ScienceStep}' (import complete={Bool(imported)}, tree ready={Bool(treeReady)}), " +
                        "placing at campaign loaded");
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] early placement failed, retried at campaign loaded: " + e);
            }
            finally
            {
                resolve();
            }
        }

        private static void OnCampaignLoad(Action done)
        {
            var finish = new OnceAction(done);
            try
            {
                // SaveLoad.UnloadCampaign clears its list after running it (IL, 2026-10-25): register again at each load.
                // The official doc of OnCampaignUnload says unloads run on quitting to the main menu, before another load
                // replaces the campaign, and when a load fails; the IL reading only found the failed-load caller. To
                // settle in game. Correct either way: Place reverts first and the game rebuilds the store at each load.
                SaveLoad.OnCampaignUnload(OnCampaignUnload);
                if (Gate.ConsumeEarly())
                {
                    KSP1PartModPlugin.Log.LogInfo("[KSP1PartMod] already placed before the VAB, nothing to do at campaign loaded");
                    finish.Invoke();
                    return;
                }
                // A coroutine runs up to its first yield inside StartCoroutine, which returns null when it ended there:
                // the placement was ready at once and already called done().
                if (Runner().StartCoroutine(WaitThenPlace(finish.Invoke, _generation)) == null && !finish.Called)
                {
                    KSP1PartModPlugin.Log.LogError("[KSP1PartMod] could not start the placement coroutine");
                    finish.Invoke();
                }
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] placement failed: " + e);
                finish.Invoke();
            }
        }

        /// <summary>Our own hidden, persistent host: the plugin's GameObject can be inactive on a later load
        /// (seen in game, 2026-10-25), and StartCoroutine on an inactive object returns null. Re-created if destroyed.
        /// Shared with the animation glue.</summary>
        internal static CoroutineRunner Runner()
        {
            if (_runner && _runner.isActiveAndEnabled) return _runner;
            if (_runner) UnityEngine.Object.Destroy(_runner.gameObject);
            var go = new GameObject("KSP1PartMod.CoroutineRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<CoroutineRunner>();
            return _runner;
        }

        private static void OnCampaignUnload()
        {
            _generation++;
            Gate.Reset();
            try
            {
                // The game builds a new TechNodeDataStore at each load, so this store may already be discarded.
                _writer?.Revert();
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] revert failed: " + e);
            }
            _writer = null;
        }

        private static IEnumerator WaitThenPlace(Action done, int generation)
        {
            var waited = Stopwatch.StartNew();
            bool imported, treeReady;
            while (!Ready(out imported, out treeReady))
            {
                if (generation != _generation) { done(); yield break; }
                if (waited.Elapsed.TotalSeconds >= TimeoutSeconds)
                {
                    KSP1PartModPlugin.Log.LogWarning(
                        $"[KSP1PartMod] gave up: import complete={Bool(imported)}, tree ready={Bool(treeReady)}");
                    if (imported)
                    {
                        try
                        {
                            // The import is complete even though the tree is not: the native summary still holds.
                            NativeModules.LogImportSummaryOnce();
                            B9Overrides.LogImportSummaryOnce();
                        }
                        catch (Exception e)
                        {
                            KSP1PartModPlugin.Log.LogError("[KSP1PartMod] native: summary failed: " + e);
                        }
                    }
                    done();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(PollSeconds);
            }

            try
            {
                // Redux's import is complete: once per session, independent of the animation switch.
                NativeModules.LogImportSummaryOnce();
                B9Overrides.LogImportSummaryOnce();
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] native: summary failed: " + e);
            }

            try
            {
                if (generation == _generation) Place(TreeStore());
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] placement failed: " + e);
            }
            finally
            {
                done();
            }
        }

        private static string Bool(bool b) => b ? "true" : "false";

        private static TechNodeDataStore TreeStore() => GameManager.Instance?.Game?.ScienceManager?.TechNodeDataStore;

        private static bool Ready(out bool imported, out bool treeReady)
        {
            imported = treeReady = false;
            try
            {
                imported = Ksp1ImportedPartRegistry.IsComplete;
                treeReady = TreeStore()?.IsInitialized == true;
            }
            catch (Exception)
            {
                // Not ready yet (game objects still being built): poll again.
            }
            return imported && treeReady;
        }

        private static void Place(TechNodeDataStore store)
        {
            var log = KSP1PartModPlugin.Log;
            var clock = Stopwatch.StartNew();

            TechMapping map;
            try
            {
                map = TechMapping.Parse(File.ReadAllText(Path.Combine(KSP1PartModPlugin.ModFolder, MappingFile)));
            }
            catch (Exception e)
            {
                log.LogError($"[KSP1PartMod] {MappingFile} could not be read, no part placed: {e.Message}");
                return;
            }

            var overrides = ReadOverrides();

            _writer?.Revert(); // normally done at unload; harmless on a discarded store
            _writer = null;

            var ksp2Nodes = new HashSet<string>(store.AvailableData.Keys);
            foreach (var message in map.Validate(ksp2Nodes)) log.LogWarning($"[KSP1PartMod] {MappingFile}: {message}");

            // Built before Apply, on a store without our entries: our own parts must not be seen as already unlocked.
            var plan = PlacementPlan.Build(ImportedPartsSource.Read(), new PlacementResolver(map, overrides, ksp2Nodes), map,
                store.PartIDLookup);

            var writer = new TechTreeWriter(new GameTechTreeStore(store));
            _writer = writer;
            writer.Apply(plan);

            foreach (var line in plan.LogLines) log.LogInfo(line);
            foreach (var note in writer.Notes) log.LogInfo(note);
            foreach (var problem in writer.Problems) log.LogWarning(problem);
            var nodes = writer.AddedParts.Select(p => p.node).Distinct().Count();
            KSP1PartMod.Perf.PerfClock.Note(KSP1PartMod.Perf.PerfPhase.Placement, clock);
            log.LogInfo($"[KSP1PartMod] placed {writer.AddedParts.Count} part(s) in {nodes} node(s), " +
                        $"created {writer.AddedNodes.Count} node(s) in {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>A missing file means no override. Bad JSON gives Overrides.Empty (Overrides.Parse never throws);
        /// a bad entry is skipped and the others kept.</summary>
        private static Overrides ReadOverrides()
        {
            var path = Path.Combine(KSP1PartModPlugin.ModFolder, OverridesFile);
            if (!File.Exists(path)) return Overrides.Empty;
            try
            {
                var overrides = Overrides.Parse(File.ReadAllText(path), out var error);
                if (error != null) KSP1PartModPlugin.Log.LogError($"[KSP1PartMod] {OverridesFile}: {error}");
                return overrides;
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError($"[KSP1PartMod] {OverridesFile} could not be read: {e.Message}");
                return Overrides.Empty;
            }
        }
    }

    /// <summary>Empty host for <see cref="TechTreeLoadStep"/>'s coroutines.</summary>
    internal sealed class CoroutineRunner : MonoBehaviour
    {
    }
}
