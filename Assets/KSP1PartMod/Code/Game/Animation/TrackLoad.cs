using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Tracks;
using Redux.Ksp1Import.Assets;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Starts the track worker of the current <see cref="TrackCache"/> generation from the load step. The
    /// snapshots are copied here, on the main thread; the worker's log lines are queued and written by the persistent
    /// coroutine runner, on the main thread, until the cache is ready.</summary>
    internal static class TrackLoad
    {
        private static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
        private static bool _draining;
        private static CoroutineRunner _host;

        /// <summary>Never throws; when the worker cannot start, the generation is marked ready (no tracks: phase 1).</summary>
        public static void Start(IEnumerable<(Ksp1ImportedPartRegistry.Entry entry, PartAnimationPlan plan)> parts)
        {
            var log = KSP1PartModPlugin.Log;
            var generation = TrackCache.Generation;
            try
            {
                var snapshots = new List<PartSnapshot>();
                foreach (var (entry, plan) in parts)
                {
                    try
                    {
                        var file = entry.SourceFile;
                        snapshots.Add(PartSnapshot.From(entry.PartName, file?.Path, file?.Root?.GameDataRoot,
                            entry.PartNode == null ? null : new Ksp1NodeAdapter(entry.PartNode), plan));
                    }
                    catch (Exception e)
                    {
                        log.LogError($"[KSP1PartMod] anim: tracks: {entry?.PartName} no snapshot: {e}");
                    }
                }
                TrackCacheWorker.Start(generation, snapshots, Lines.Enqueue);
                Drain();
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] anim: tracks: not started: " + e);
                TrackCache.MarkReady(generation);
            }
        }

        private static void Drain()
        {
            // A drain still running on a live host also drains this generation's lines.
            if (_draining && _host) return;
            _draining = true;
            try
            {
                _host = TechTreeLoadStep.Runner();
                _host.StartCoroutine(DrainUntilReady());
            }
            catch (Exception e)
            {
                _draining = false;
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] anim: tracks: log drain not started: " + e);
            }
        }

        private static IEnumerator DrainUntilReady()
        {
            try
            {
                while (true)
                {
                    var ready = TrackCache.Ready;
                    Flush();
                    if (ready && Lines.IsEmpty) yield break;
                    yield return null;
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private static void Flush()
        {
            var log = KSP1PartModPlugin.Log;
            while (Lines.TryDequeue(out var line))
            {
                if (line.Contains(" failed")) log.LogWarning(line);
                else log.LogInfo(line);
            }
        }
    }
}
