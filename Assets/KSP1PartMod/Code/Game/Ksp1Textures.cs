using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using KSP.Game;
using KSP.Messages;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Textures;
using KSP1PartMod.Game.Perf;
using KSP1PartMod.Perf;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Model;
using UnityEngine;
using UnityEngine.Profiling;

namespace KSP1PartMod.Game
{
    /// <summary>The textures of Redux's KSP1 import (lines <c>[KSP1PartMod] textures:</c>).
    /// <list type="bullet">
    /// <item><b>Measure</b> (always on): every <c>Ksp1TextureLoader.LoadTexture</c> call is counted (fresh load or from a
    /// cache), and the texture memory is logged at the end of Redux's import and at each VAB or flight entry.</item>
    /// <item><b>Share</b> (<c>share_ksp1_textures</c>): Redux empties its texture cache before and after each part
    /// rebuild, so every rebuilt part loads its own copy of every texture. Once the import is complete,
    /// <c>ClearCache(false)</c> only drops destroyed entries, <c>DestroyPrefabGraph</c> (the temporary icon prefab) keeps
    /// its textures, and <c>LoadTexture</c> never hands out a destroyed texture or one loaded under the other usage
    /// (colour against normal map, see <see cref="TextureShareRule"/>). <c>ReleaseResidentPrefabs</c> and the destroying
    /// <c>ClearCache(true)</c> (Redux's <c>Clear</c>) still free everything. All four patches or none.</item>
    /// <item><b>Quality</b> (<c>ksp1_textures_follow_quality</c>): a freshly loaded texture that is still readable gets
    /// <c>ignoreMipmapLimit = false</c>. Unity applies the global mipmap limit to a runtime texture only while it is
    /// readable, so the non-readable ones (most of them) are counted and left alone.</item>
    /// <item><b>Budget</b> (<c>raise_streaming_budget</c>, default off): the streaming budget is raised by the live KSP1
    /// texture memory, capped by the card's memory (<see cref="TextureMemory.RaisedBudgetMb"/>).</item>
    /// </list>
    /// Never throws into the game: a failure in a sharing hook stops sharing for the session (Redux's behaviour from then
    /// on), with one log line.</summary>
    internal static class Ksp1Textures
    {
        public const string MeasureHarmonyId = "KSP1PartMod.Ksp1Textures.Measure";
        public const string ShareHarmonyId = "KSP1PartMod.Ksp1Textures.Share";
        public const string SourceHarmonyId = "KSP1PartMod.Ksp1Textures.Source";
        private const string Prefix = "[KSP1PartMod] textures: ";
        private const int MaxUsageLines = 20;
        private const float SettledSeconds = 5f;

        // Sharing
        private static Dictionary<string, Texture2D> _cache;
        private static Dictionary<string, string> _pathCache;
        private static Func<string, string, string, string> _resolve;
        private static readonly Dictionary<string, TextureUsageClass> UsageByPath =
            new Dictionary<string, TextureUsageClass>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Scratch = new List<string>();
        private static bool _sharingArmed, _broken, _releasing;
        private static string _pendingPath;
        private static TextureUsageClass _pendingClass;

        // Measure and quality. Seen: every texture a fresh load gave (the keys), with its texture name argument and source
        // extension for the census.
        private static readonly Dictionary<EntityId, TextureOrigin> Seen = new Dictionary<EntityId, TextureOrigin>();
        private static string _lastResolved;
        private static bool _measureBroken, _followQuality;
        private static long _calls, _fresh, _fromCache, _shared, _failed, _evictedDestroyed, _evictedOtherUsage;
        private static long _keptClears, _keptGraphs, _qualityApplied, _qualitySkipped;
        private static int _usageLines;

        // Budget
        private static bool _raiseBudget;
        private static float? _lastSetBudget;
        private static float _baseBudget;

        private static bool SharingActive =>
            _sharingArmed && !_broken && TextureShareRule.SharingActive(Ksp1ImportedPartRegistry.IsComplete, _releasing);

        /// <param name="sourceExtension"><c>perf_memory_lines</c>: the census prints texture names with their source extension,
        /// read by a postfix on <c>ResolveTexturePath</c>; without the census lines that patch is not made.</param>
        public static void TryApply(bool share, bool followQuality, bool raiseBudget, bool sourceExtension = true)
        {
            var log = KSP1PartModPlugin.Log;
            _followQuality = followQuality;
            _raiseBudget = raiseBudget;
            var loader = typeof(Ksp1TextureLoader);
            var registry = typeof(Ksp1ImportedPartRegistry);
            var load = AccessTools.Method(loader, nameof(Ksp1TextureLoader.LoadTexture),
                new[] { typeof(string), typeof(string), typeof(string), typeof(Ksp1TextureLoader.TextureUsage) });
            if (load == null || load.ReturnType != typeof(Texture2D))
            {
                log.LogWarning(Prefix + "Ksp1TextureLoader.LoadTexture not found, measure, sharing and quality off");
                return;
            }
            Harmony measure;
            try
            {
                // The LoadTexture postfix also records each fresh load's usage (sharing) and applies the quality rule: without
                // it, neither can work.
                measure = new Harmony(MeasureHarmonyId);
                measure.Patch(load, postfix: new HarmonyMethod(typeof(Ksp1Textures), nameof(AfterLoad)));
            }
            catch (Exception e)
            {
                log.LogError(Prefix + "measure not armed, sharing and quality off: " + e);
                return;
            }
            // Memory moments only: on its own, so that its failure never takes sharing or quality down (it did up to 0.12.0).
            MethodInfo completeMethod;
            try
            {
                completeMethod = AccessTools.Method(registry, "MarkImportComplete");
            }
            catch (Exception)
            {
                completeMethod = null; // reported as not found
            }
            var complete = ArmImportComplete(measure, completeMethod, m => log.LogWarning(m));
            log.LogInfo(Prefix + "measure armed on Ksp1TextureLoader.LoadTexture" + (complete ? " and Ksp1ImportedPartRegistry.MarkImportComplete" : ""));
            if (sourceExtension) ArmSourceExtension(loader);
            log.LogInfo(Prefix + (followQuality
                ? "quality armed: readable KSP1 textures follow the game's texture quality"
                : "quality disabled by " + KSP1PartModPlugin.AnimationSettingsFile));
            log.LogInfo(Prefix + (raiseBudget
                ? "streaming budget raise armed (raise_streaming_budget)"
                : "streaming budget left as the game sets it (raise_streaming_budget false)"));
            if (!share)
            {
                log.LogInfo(Prefix + "sharing disabled by " + KSP1PartModPlugin.AnimationSettingsFile + ": one copy per rebuilt part, as in 0.4.0");
                return;
            }
            ArmSharing(load, loader, registry);
        }

        /// <summary>The end-of-import memory moment (a postfix on <c>MarkImportComplete</c>). Never throws: a missing method or a
        /// failed patch gives one <paramref name="warn"/> line and false, and the caller goes on to arm sharing and quality.</summary>
        internal static bool ArmImportComplete(Harmony measure, MethodBase complete, Action<string> warn)
        {
            if (complete == null)
            {
                warn?.Invoke(Prefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no memory line at the end of the import");
                return false;
            }
            try
            {
                measure.Patch(complete, postfix: new HarmonyMethod(typeof(Ksp1Textures), nameof(AfterImportComplete)));
                return true;
            }
            catch (Exception e)
            {
                warn?.Invoke(Prefix + "no memory line at the end of the import (sharing and quality unaffected): " + e);
                return false;
            }
        }

        /// <summary>The source file of each fresh load (its extension, for the census): the path Redux resolved last. Optional
        /// and on its own: a failure here only loses the extension, never the measure, sharing or quality.</summary>
        private static void ArmSourceExtension(Type loader)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                var resolvePath = AccessTools.Method(loader, "ResolveTexturePath", new[] { typeof(string), typeof(string), typeof(string) });
                if (resolvePath == null || resolvePath.ReturnType != typeof(string))
                {
                    log.LogWarning(Prefix + "Ksp1TextureLoader.ResolveTexturePath not found, the census names its textures without the source extension");
                    return;
                }
                new Harmony(SourceHarmonyId).Patch(resolvePath, postfix: new HarmonyMethod(typeof(Ksp1Textures), nameof(AfterResolve)));
            }
            catch (Exception e)
            {
                log.LogWarning(Prefix + "source extension not armed, the census names its textures without it: " + e);
            }
        }

        private static void ArmSharing(MethodInfo load, Type loader, Type registry)
        {
            var log = KSP1PartModPlugin.Log;
            var cacheField = AccessTools.Field(loader, "TextureCache");
            var pathField = AccessTools.Field(loader, "RecursivePathCache");
            var resolve = AccessTools.Method(loader, "ResolveTexturePath", new[] { typeof(string), typeof(string), typeof(string) });
            var clear = AccessTools.Method(loader, nameof(Ksp1TextureLoader.ClearCache), new[] { typeof(bool) });
            var graph = AccessTools.Method(registry, "DestroyPrefabGraph", new[] { typeof(GameObject), typeof(bool) });
            var release = AccessTools.Method(registry, "ReleaseResidentPrefabs", Type.EmptyTypes);
            var missing = new List<string>();
            if (cacheField == null || cacheField.FieldType != typeof(Dictionary<string, Texture2D>)) missing.Add("Ksp1TextureLoader.TextureCache");
            if (pathField == null || pathField.FieldType != typeof(Dictionary<string, string>)) missing.Add("Ksp1TextureLoader.RecursivePathCache");
            if (resolve == null || resolve.ReturnType != typeof(string)) missing.Add("Ksp1TextureLoader.ResolveTexturePath");
            if (clear == null) missing.Add("Ksp1TextureLoader.ClearCache(bool)");
            if (graph == null || graph.GetParameters()[1].Name != "destroyTextures") missing.Add("Ksp1ImportedPartRegistry.DestroyPrefabGraph");
            if (release == null) missing.Add("Ksp1ImportedPartRegistry.ReleaseResidentPrefabs");
            if (missing.Count > 0)
            {
                log.LogWarning(Prefix + string.Join(", ", missing) + " not found, sharing off (one copy per rebuilt part, as in 0.4.0)");
                return;
            }
            var harmony = new Harmony(ShareHarmonyId);
            try
            {
                _cache = (Dictionary<string, Texture2D>)cacheField.GetValue(null);
                _pathCache = (Dictionary<string, string>)pathField.GetValue(null);
                _resolve = AccessTools.MethodDelegate<Func<string, string, string, string>>(resolve);
                if (_cache == null || _pathCache == null || _resolve == null)
                    throw new InvalidOperationException("Redux's texture cache could not be read");
                var self = typeof(Ksp1Textures);
                harmony.Patch(load, prefix: new HarmonyMethod(self, nameof(BeforeLoad)));
                harmony.Patch(clear, prefix: new HarmonyMethod(self, nameof(BeforeClearCache)));
                harmony.Patch(graph, prefix: new HarmonyMethod(self, nameof(BeforeDestroyGraph)));
                harmony.Patch(release, prefix: new HarmonyMethod(self, nameof(BeforeRelease)),
                    finalizer: new HarmonyMethod(self, nameof(AfterRelease)));
                _sharingArmed = true;
                log.LogInfo(Prefix + "sharing armed on Ksp1TextureLoader.LoadTexture/ClearCache and " +
                            "Ksp1ImportedPartRegistry.DestroyPrefabGraph/ReleaseResidentPrefabs: one copy of each texture file " +
                            "for every rebuilt part once the import is complete");
            }
            catch (Exception e)
            {
                _sharingArmed = false;
                try
                {
                    harmony.UnpatchAll(ShareHarmonyId);
                }
                catch (Exception u)
                {
                    log.LogError(Prefix + "could not take the sharing patches back: " + u);
                }
                log.LogError(Prefix + "sharing not armed, one copy per rebuilt part as in 0.4.0: " + e);
            }
        }

        /// <summary>Hands out the shared texture, or makes Redux load the file when the entry is destroyed or of the other
        /// usage. False skips Redux's method.</summary>
        private static bool BeforeLoad(string gameDataRoot, string textureName, string localDirectory,
            Ksp1TextureLoader.TextureUsage usage, ref Texture2D __result)
        {
            _pendingPath = null;
            if (!SharingActive || string.IsNullOrWhiteSpace(textureName)) return true;
            try
            {
                var path = _resolve(gameDataRoot, textureName, localDirectory);
                if (path == null) return true;
                var requested = TextureShareRule.Classify(path, usage == Ksp1TextureLoader.TextureUsage.Normal);
                _pendingPath = path;
                _pendingClass = requested;
                var has = _cache.TryGetValue(path, out var cached);
                TextureUsageClass? recorded = UsageByPath.TryGetValue(path, out var r) ? r : (TextureUsageClass?)null;
                switch (TextureShareRule.Lookup(true, has, has && cached == null, recorded, requested))
                {
                    case CacheLookup.Share:
                        _shared++;
                        __result = cached;
                        return false;
                    case CacheLookup.EvictDestroyed:
                        _cache.Remove(path);
                        UsageByPath.Remove(path);
                        _evictedDestroyed++;
                        return true;
                    case CacheLookup.EvictOtherUsage:
                        _cache.Remove(path);
                        UsageByPath.Remove(path);
                        _evictedOtherUsage++;
                        if (_usageLines < MaxUsageLines)
                        {
                            _usageLines++;
                            KSP1PartModPlugin.Log?.LogInfo(Prefix + $"'{System.IO.Path.GetFileName(path)}' asked as {requested}, cached as " +
                                                           recorded + ": not shared, loaded again" +
                                                           (_usageLines == MaxUsageLines ? " (last such line)" : ""));
                        }
                        return true;
                    default:
                        return true;
                }
            }
            catch (Exception e)
            {
                _pendingPath = null;
                Break(e);
                return true;
            }
        }

        /// <summary>Counts, records the usage of a fresh load for sharing, and applies the quality rule. <c>__1</c> is the
        /// texture name argument, bound by position (its type is pinned by the signature looked up in <see cref="TryApply"/>),
        /// so a renamed parameter cannot fail the patch.</summary>
        private static void AfterLoad(Texture2D __result, string __1)
        {
            var textureName = __1;
            var path = _pendingPath;
            var resolved = _lastResolved;
            _pendingPath = null;
            _lastResolved = null;
            if (_measureBroken) return;
            try
            {
                _calls++;
                if (__result == null)
                {
                    _failed++;
                    return;
                }
                var id = __result.GetEntityId();
                if (Seen.ContainsKey(id))
                {
                    _fromCache++;
                    return;
                }
                Seen.Add(id, new TextureOrigin(textureName, Extension(path ?? resolved)));
                _fresh++;
                if (path != null && SharingActive)
                {
                    UsageByPath[path] = _pendingClass;
                    _recorded?.Add(path);
                }
                if (_followQuality) FollowQuality(__result);
            }
            catch (Exception e)
            {
                _measureBroken = true;
                KSP1PartModPlugin.Log?.LogError(Prefix + "counting failed, stopped for this session: " + e);
            }
        }

        /// <summary>Keeps the path Redux resolved last: <c>LoadTexture</c> resolves its path just before it loads (main
        /// thread), so the measure postfix reads the source of this load.</summary>
        private static void AfterResolve(string __result) => _lastResolved = __result;

        private static string Extension(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var extension = System.IO.Path.GetExtension(path);
                return string.IsNullOrEmpty(extension) ? null : extension.ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Unity applies the global mipmap limit to a texture created at run time only while it keeps its CPU
        /// copy (Unity 6 docs: <c>Texture2D.ignoreMipmapLimit</c> "can only be toggled at runtime if the Texture is
        /// readable"; <c>QualitySettings.globalTextureMipmapLimit</c> excludes "textures created at run time which are not
        /// readable").</summary>
        private static void FollowQuality(Texture2D texture)
        {
            if (!texture.isReadable || texture.mipmapCount <= 1)
            {
                _qualitySkipped++;
                return;
            }
            if (texture.ignoreMipmapLimit) texture.ignoreMipmapLimit = false;
            _qualityApplied++;
        }

        private static bool BeforeClearCache(bool destroyTextures)
        {
            if (_broken) return true;
            try
            {
                if (TextureShareRule.Clear(destroyTextures, SharingActive) == CacheClear.Redux)
                {
                    UsageByPath.Clear();
                    return true;
                }
                Scratch.Clear();
                foreach (var entry in _cache)
                    if (entry.Value == null) Scratch.Add(entry.Key);
                foreach (var key in Scratch)
                {
                    _cache.Remove(key);
                    UsageByPath.Remove(key);
                }
                _evictedDestroyed += Scratch.Count;
                Scratch.Clear();
                _pathCache.Clear();
                _keptClears++;
                return false;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        /// <summary>Not gated on <c>_broken</c>: textures handed out before a stop stay shared by live parts.</summary>
        private static void BeforeDestroyGraph(ref bool destroyTextures)
        {
            try
            {
                if (!TextureShareRule.KeepTexturesOfDestroyedGraph(destroyTextures, _sharingArmed,
                        Ksp1ImportedPartRegistry.IsComplete, _releasing)) return;
                destroyTextures = false;
                _keptGraphs++;
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void BeforeRelease() => _releasing = true;

        private static Exception AfterRelease(Exception __exception)
        {
            _releasing = false;
            return __exception;
        }

        /// <summary>After a failure, Redux's behaviour: its next <c>ClearCache(false)</c> empties the cache, and every
        /// rebuild loads its own copies again.</summary>
        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError(Prefix + "sharing failed, stopped for this session (one copy per rebuilt part from now on): " + e);
        }

        // ---- Audit hooks (remove cache entries only, never destroy) ----

        /// <summary>Sharing was armed at startup: textures handed out by the cache may be used by several parts.</summary>
        internal static bool SharingArmed => _sharingArmed;

        /// <summary>The paths in Redux's texture cache now (empty when sharing is not armed: Redux then empties the cache
        /// around each rebuild itself).</summary>
        internal static List<string> CacheKeys()
        {
            var keys = new List<string>();
            if (_cache != null) keys.AddRange(_cache.Keys);
            return keys;
        }

        /// <summary>Removes these entries from the cache (and their recorded usage). The textures themselves are left
        /// alone: a live part may use them, and Unity frees only the ones nothing references. Returns how many entries
        /// were removed.</summary>
        internal static int Evict(IEnumerable<string> keys)
        {
            if (_cache == null || keys == null) return 0;
            var removed = 0;
            foreach (var key in keys)
            {
                if (key == null) continue;
                if (_cache.Remove(key)) removed++;
                UsageByPath.Remove(key);
            }
            return removed;
        }

        // Paths freshly loaded into the shared cache while the audit records (its own spawns only).
        private static HashSet<string> _recorded;

        /// <summary>Starts (or stops, with null) recording the paths that fresh loads add to the shared cache. The audit
        /// records only around its own spawn, so the player's VAB loads are never evicted.</summary>
        internal static void RecordFreshLoads(HashSet<string> into) => _recorded = into;

        /// <summary>The memory and cache lines (<c>textures: memory at &lt;when&gt;</c>), for the audit's progress: with
        /// <c>perf_memory_lines</c>, the process memory and census lines too. Not kept for the perf report (the audit writes its
        /// own report).</summary>
        internal static void ReportAt(string when) => Moment(when, false, false, false);

        // ---- Memory moments (lines, census, budget, rebuild line, report file) ----

        private static void AfterImportComplete()
        {
            Moment("end of import (prefabs still resident)", false, false, true);
            try
            {
                TechTreeLoadStep.Runner().StartCoroutine(AfterFrames(2, "after the import released its prefabs"));
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(Prefix + "could not schedule the post-import memory line: " + e);
            }
        }

        private static IEnumerator AfterFrames(int frames, string when)
        {
            for (var i = 0; i < frames; i++) yield return null;
            Moment(when, true, false, true);
        }

        private static IEnumerator AfterSeconds(float seconds, string when, bool rebuilds, int generation)
        {
            yield return new WaitForSecondsRealtime(seconds);
            // The scene was left within the 5 s: this moment would claim the next scene's rebuilds and memory.
            if (!StillInScene(generation))
            {
                KSP1PartModPlugin.Log?.LogDebug(Prefix + when + " skipped: the scene was left before");
                yield break;
            }
            Moment(when, false, rebuilds, true);
        }

        private static int _sceneGeneration;

        /// <summary>Counts the scene changes: every game-state change but the map view toggles (flight and its map are one
        /// scene). Returns the generation of the scene now entered.</summary>
        internal static int NoteSceneChange(GameState current, GameState previous)
        {
            if (current == previous || current == GameState.Map3DView || previous == GameState.Map3DView) return _sceneGeneration;
            return ++_sceneGeneration;
        }

        /// <summary>No scene change since <paramref name="generation"/>.</summary>
        internal static bool StillInScene(int generation) => generation == _sceneGeneration;

        /// <summary>From <see cref="KSP1PartModPlugin.OnInitialized"/>: memory moments at each VAB or flight entry, and at
        /// each main menu entry with <c>perf_memory_lines</c>.</summary>
        public static void SubscribeToGameStates(GameInstance game)
        {
            try
            {
                if (game?.Messages == null)
                {
                    KSP1PartModPlugin.Log?.LogWarning(Prefix + "no message center, no memory line at VAB or flight entry");
                    return;
                }
                game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameState);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(Prefix + "no memory line at VAB or flight entry: " + e);
            }
        }

        private static void OnGameState(MessageCenterMessage message)
        {
            try
            {
                if (!(message is GameStateChangedMessage m) || m.CurrentState == m.PreviousState) return;
                var generation = NoteSceneChange(m.CurrentState, m.PreviousState);
                var where = MomentOf(m.CurrentState, m.PreviousState, Settings.PerfMemoryLines);
                if (where == null) return;
                // The rebuild line (load timeline) belongs to the VAB and flight moments, written once per moment.
                var vabOrFlight = where != MainMenu;
                Moment(where + " entry", vabOrFlight, vabOrFlight, true);
                TechTreeLoadStep.Runner().StartCoroutine(AfterSeconds(SettledSeconds, where + " entry + 5 s", vabOrFlight, generation));
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(Prefix + "memory line failed: " + e);
            }
        }

        private const string MainMenu = "main menu";

        /// <summary>The moment of a game state: VAB, flight (not back from the map), main menu (only with
        /// <c>perf_memory_lines</c>, after leaving a campaign too), or none.</summary>
        internal static string MomentOf(GameState current, GameState previous, bool memoryLines)
        {
            if (current == GameState.VehicleAssemblyBuilder) return "VAB";
            if (current == GameState.FlightView && previous != GameState.Map3DView) return "flight";
            if (current == GameState.MainMenu && memoryLines) return MainMenu;
            return null;
        }

        private static AnimationSettings Settings => KSP1PartModPlugin.Animations ?? AnimationSettings.Defaults;

        /// <summary>One memory moment: one census (every live texture scanned once), the 0.12.0 memory and cache lines, the
        /// opt-in budget, then with <c>perf_memory_lines</c> the process memory, census and largest lines, then the rebuild
        /// line and the report file. Never throws.</summary>
        private static void Moment(string when, bool budget, bool rebuilds, bool kept)
        {
            var log = KSP1PartModPlugin.Log;
            if (log == null) return;
            var settings = Settings;
            var census = TakeCensus(when, settings.PerfTopN);
            Report(when, census);
            if (budget) ApplyBudget(when, census);
            if (settings.PerfMemoryLines)
            {
                try
                {
                    var snapshot = Snapshot(when, census);
                    log.LogInfo(PerfLines.Memory(snapshot));
                    if (census != null)
                    {
                        log.LogInfo(PerfLines.Census(census));
                        log.LogInfo(PerfLines.Largest(census));
                    }
                    if (kept) PerfReportWriter.NoteMoment(snapshot, census);
                }
                catch (Exception e)
                {
                    log.LogError(PerfLines.Prefix + "memory line failed at " + when + ": " + e);
                }
            }
            if (rebuilds) LoadTimeline.WriteRebuilds(when);
            if (kept) PerfReportWriter.Write(when);
        }

        private static TextureCensus TakeCensus(string when, int topN)
        {
            try
            {
                var clock = Stopwatch.StartNew();
                var census = Census(when, topN, Resources.FindObjectsOfTypeAll<Texture2D>(), Seen);
                census.CensusMs = clock.Elapsed.TotalMilliseconds;
                return census;
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(Prefix + "census failed at " + when + ": " + e);
                return null;
            }
        }

        private static void Report(string when, TextureCensus census)
        {
            var log = KSP1PartModPlugin.Log;
            if (log == null) return;
            try
            {
                var live = census == null
                    ? "live KSP1 textures n/a"
                    : $"live KSP1 textures {census.All.Count}, ~{TextureMemory.Mb(census.All.Bytes)} MB with their full mipmap chain, {census.Readable.Count} readable";
                log.LogInfo(Prefix + $"memory at {when}: budget {F(QualitySettings.streamingMipmapsMemoryBudget)} MB " +
                            $"(streaming {(QualitySettings.streamingMipmapsActive ? "on" : "off")}, mipmap limit {QualitySettings.globalTextureMipmapLimit}), " +
                            $"current {TextureMemory.Mb(Texture.currentTextureMemory)} MB, desired {TextureMemory.Mb(Texture.desiredTextureMemory)} MB, " +
                            $"non-streaming {TextureMemory.Mb(Texture.nonStreamingTextureMemory)} MB, target {TextureMemory.Mb(Texture.targetTextureMemory)} MB, " +
                            $"total {TextureMemory.Mb(Texture.totalTextureMemory)} MB, graphics driver {TextureMemory.Mb(Profiler.GetAllocatedMemoryForGraphicsDriver())} MB " +
                            $"(0 outside development builds), card {SystemInfo.graphicsMemorySize} MB");
                log.LogInfo(Prefix + $"cache at {when}: {_calls} load call(s): {_fresh} fresh load(s), {_fromCache} from a cache " +
                            $"({_shared} kept across rebuilds by sharing), {_failed} failed; evicted {_evictedDestroyed} destroyed, " +
                            $"{_evictedOtherUsage} of the other usage; {_keptClears} cache clear(s) and {_keptGraphs} icon prefab(s) kept their textures; " +
                            $"sharing {(_broken ? "stopped" : _sharingArmed ? (SharingActive ? "active" : "armed, waiting for the import") : "off")}; " +
                            $"quality applied to {_qualityApplied}, {_qualitySkipped} not readable or without mipmaps; " + live);
            }
            catch (Exception e)
            {
                log.LogError(Prefix + "memory line failed: " + e);
            }
        }

        /// <summary>The source of a texture we saw loaded: Redux's texture name argument and the extension of the file it
        /// resolved to (null when unknown).</summary>
        internal readonly struct TextureOrigin
        {
            public readonly string Name, Extension;

            public TextureOrigin(string name, string extension)
            {
                Name = name;
                Extension = extension;
            }

            /// <summary>The census name: the texture name, with the source extension when the name does not end with it.</summary>
            public string CensusName
            {
                get
                {
                    var name = string.IsNullOrEmpty(Name) ? "?" : Name;
                    return Extension == null || name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name : name + Extension;
                }
            }
        }

        /// <summary>The census of the textures we saw loaded, among <paramref name="candidates"/>. It only reads each texture's
        /// size, format, mipmap count and readable flag: no pixel access, nothing written. In game, the candidates are every
        /// <c>Texture2D</c> (<c>Resources.FindObjectsOfTypeAll</c>), only at the few memory moments.</summary>
        internal static TextureCensus Census(string when, int topN, IEnumerable<Texture2D> candidates,
            IReadOnlyDictionary<EntityId, TextureOrigin> seen)
        {
            var census = new TextureCensus(when, Math.Max(1, topN));
            foreach (var t in candidates)
            {
                if (t == null || !seen.TryGetValue(t.GetEntityId(), out var origin)) continue;
                var format = t.format;
                Bits(format, out var bits, out var block);
                census.Add(origin, NameOfOrigin, t.width, t.height, FormatName(format), bits, block, t.mipmapCount, t.isReadable);
            }
            return census;
        }

        /// <summary>Built only for the textures the largest list keeps.</summary>
        private static readonly Func<TextureOrigin, string> NameOfOrigin = o => o.CensusName;

        private static readonly Dictionary<TextureFormat, string> FormatNames = new Dictionary<TextureFormat, string>();

        private static string FormatName(TextureFormat format)
        {
            if (!FormatNames.TryGetValue(format, out var name)) FormatNames[format] = name = format.ToString();
            return name;
        }

        /// <summary>Process memory now. Each API in its own try: a failing one is null ("n/a").</summary>
        private static MemorySnapshot Snapshot(string when, TextureCensus census)
        {
            var s = new MemorySnapshot { Moment = when, Ksp1Live = census?.All.Bytes };
            s.Allocated = Read(() => Profiler.GetTotalAllocatedMemoryLong());
            s.Reserved = Read(() => Profiler.GetTotalReservedMemoryLong());
            s.MonoUsed = Read(() => Profiler.GetMonoUsedSizeLong());
            s.MonoHeap = Read(() => Profiler.GetMonoHeapSizeLong());
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    s.WorkingSet = Read(() => process.WorkingSet64);
                    s.PrivateBytes = Read(() => process.PrivateMemorySize64);
                }
            }
            catch (Exception)
            {
                // Both "n/a".
            }
            s.SystemRamMb = Read(() => SystemInfo.systemMemorySize);
            s.TextureCurrent = Read(() => (long)Texture.currentTextureMemory);
            s.TextureDesired = Read(() => (long)Texture.desiredTextureMemory);
            s.TextureNonStreaming = Read(() => (long)Texture.nonStreamingTextureMemory);
            s.TextureTotal = Read(() => (long)Texture.totalTextureMemory);
            try
            {
                s.BudgetMb = QualitySettings.streamingMipmapsMemoryBudget;
            }
            catch (Exception)
            {
                // "n/a".
            }
            return s;
        }

        private static long? Read(Func<long> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>GPU bits per pixel. RGB24 is stored as 32 bits: D3D12 has no 24-bit format.</summary>
        private static void Bits(TextureFormat format, out int bits, out bool block)
        {
            block = false;
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.BC4:
                    bits = 4;
                    block = true;
                    return;
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    bits = 8;
                    block = true;
                    return;
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                    bits = 8;
                    return;
                case TextureFormat.RGBAHalf:
                    bits = 64;
                    return;
                case TextureFormat.RGBAFloat:
                    bits = 128;
                    return;
                default:
                    bits = 32;
                    return;
            }
        }

        /// <summary>Opt-in: base + live KSP1 bytes, capped; the base again while the mod's campaign feature is off.</summary>
        private static void ApplyBudget(string when, TextureCensus census)
        {
            if (!_raiseBudget) return;
            var log = KSP1PartModPlugin.Log;
            try
            {
                if (!QualitySettings.streamingMipmapsActive)
                {
                    log?.LogInfo(Prefix + $"budget at {when}: texture streaming is off, budget left alone");
                    return;
                }
                var current = QualitySettings.streamingMipmapsMemoryBudget;
                _baseBudget = TextureMemory.BaseBudgetMb(current, _lastSetBudget, _baseBudget);
                var plugin = KSP1PartModPlugin.Instance;
                var featureOn = plugin == null || plugin.Enabled;
                if (census == null)
                {
                    log?.LogInfo(Prefix + $"budget at {when}: the KSP1 texture size is unknown (census failed), budget left alone");
                    return;
                }
                var bytes = census.All.Bytes;
                var capped = false;
                var target = featureOn
                    ? TextureMemory.RaisedBudgetMb(_baseBudget, bytes, SystemInfo.graphicsMemorySize, out capped)
                    : _baseBudget;
                if (Math.Abs(target - current) >= 0.5f) QualitySettings.streamingMipmapsMemoryBudget = target;
                _lastSetBudget = QualitySettings.streamingMipmapsMemoryBudget;
                log?.LogInfo(Prefix + $"budget at {when}: {F(current)} -> {F(_lastSetBudget.Value)} MB (base {F(_baseBudget)} MB, " +
                             $"KSP1 ~{TextureMemory.Mb(bytes)} MB, card {SystemInfo.graphicsMemorySize} MB, margin " +
                             $"{TextureMemory.MarginMb(SystemInfo.graphicsMemorySize)} MB" + (capped ? ", capped" : "") +
                             (featureOn ? ")" : ", mod off in this campaign: base kept)"));
            }
            catch (Exception e)
            {
                _raiseBudget = false;
                log?.LogError(Prefix + "budget raise failed, stopped for this session: " + e);
            }
        }

        private static string F(float mb) => mb.ToString("0", CultureInfo.InvariantCulture);
    }
}
