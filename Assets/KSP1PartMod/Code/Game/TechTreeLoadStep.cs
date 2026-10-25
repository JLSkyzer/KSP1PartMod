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

        private static MonoBehaviour _host;
        private static TechTreeWriter _writer;
        // Bumped at each unload, so a step still waiting for the previous campaign stops.
        private static int _generation;

        public static void Register(MonoBehaviour host)
        {
            _host = host ? host : throw new ArgumentNullException(nameof(host));
            SaveLoad.AddCampaignLoadAction(KSP1PartModPlugin.ModId, StepName, CampaignPhase.Loaded, OnCampaignLoad);
        }

        private static void OnCampaignLoad(Action done)
        {
            try
            {
                // SaveLoad.UnloadCampaign clears its list after running it (IL, 2026-10-25): register again at each load.
                SaveLoad.OnCampaignUnload(OnCampaignUnload);
                _host.StartCoroutine(WaitThenPlace(done, _generation));
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] placement failed: " + e);
                done();
            }
        }

        private static void OnCampaignUnload()
        {
            _generation++;
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
                    done();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(PollSeconds);
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
            foreach (var problem in writer.Problems) log.LogWarning(problem);
            var nodes = writer.AddedParts.Select(p => p.node).Distinct().Count();
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
}
