using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Perf;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Writes the channel table of every KSP1 part instance once per frame, after the game's writers (spec
    /// "Material writers and shader timing", flush order): one behaviour, created on the first registration and kept
    /// across scenes. <c>[DefaultExecutionOrder(32000)]</c> is above every order in the Redux decompile (max 10000); whether
    /// Unity applies it to a type of a mod assembly is not provable, so the <c>mpb check</c> lines confirm it at run time
    /// (<see cref="MpbCheck"/>: per part type, and at the first reentry of a part type). If a check reads another value
    /// than ours, <see cref="EnableRenderTimeFlush"/> flushes again when the frame renders: every written target of every
    /// table after a part-type check, the targets rewritten every frame (active reentry hulls) after a reentry check. The
    /// flush allocates nothing.</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed partial class ChannelFlush : MonoBehaviour
    {
        private static ChannelTable[] _tables = new ChannelTable[64];
        private static RendererChannels[] _sinks = new RendererChannels[64];
        private static int _count;
        private static ChannelFlush _instance;
        private static bool _renderTimeFlush, _renderRewriteAll;

        // Perf (diagnostics): Drive time and counts of this frame, fed by the modules.
        private static PerfCounter _perf;
        private static long _driveTicks;
        private static int _driveModules, _driveEntries;

        /// <summary>Registered tables.</summary>
        public static int Count => _count;

        /// <summary>Adds a part's table and its sink; a table already registered is not added twice.</summary>
        public static void Register(ChannelTable table, RendererChannels sink)
        {
            if (table == null || sink == null || IndexOf(table) >= 0) return;
            if (_count == _tables.Length)
            {
                Array.Resize(ref _tables, _count * 2);
                Array.Resize(ref _sinks, _count * 2);
            }
            _tables[_count] = table;
            _sinks[_count] = sink;
            _count++;
            EnsureInstance();
        }

        public static void Unregister(ChannelTable table)
        {
            var i = IndexOf(table);
            if (i >= 0) RemoveAt(i);
        }

        /// <summary>One module's <c>Drive</c> of this frame (with <c>diagnostics</c>).</summary>
        public static void NoteDrive(long ticks, int entries)
        {
            _driveTicks += ticks;
            _driveModules++;
            _driveEntries += entries;
        }

        /// <summary>The flush, then (with <c>perf_counter</c>) the end of the frame cost's frame.</summary>
        private void LateUpdate()
        {
            FlushAll();
            if (PerfOn) EndFrame(Time.unscaledDeltaTime);
        }

        /// <summary>Every registered table, in registration order (swap-removal aside). A table whose part is gone, or
        /// whose flush throws, is dropped (the throw logged once with its stack).</summary>
        internal static void FlushAll()
        {
            var perf = PerfOn;
            var start = perf ? Stopwatch.GetTimestamp() : 0L;
            var writes = 0;
            for (var i = 0; i < _count; i++)
            {
                var sink = _sinks[i];
                if (sink.Orphaned)
                {
                    try
                    {
                        sink.ReleaseAll(); // the part is gone: its material copies go too
                    }
                    catch (Exception e)
                    {
                        KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: releasing the copies of a removed part failed: {e}");
                    }
                    RemoveAt(i--);
                    continue;
                }
                try
                {
                    sink.BeforeFlush();
                    _tables[i].Flush(sink);
                    writes += _tables[i].WritesLastFlush;
                }
                catch (Exception e)
                {
                    KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: channel flush failed, part dropped from the flush: {e}");
                    RemoveAt(i--);
                }
            }
            if (!perf) return;
            var now = Stopwatch.GetTimestamp();
            FrameWindowOrCreate()?.Add(PerfFeature.Flush, now - start);
            if (_perf == null)
            {
                _perf = new PerfCounter(Stopwatch.Frequency);
                _perf.Start(now);
            }
            _perf.Add(_driveTicks + (now - start), _driveModules, _driveEntries, writes);
            _driveTicks = 0;
            _driveModules = _driveEntries = 0;
            if (_perf.TryReport(now, out var line)) KSP1PartModPlugin.Log?.LogInfo(line);
        }

        /// <summary>Fallback of the spec's flush order, after an <c>mpb check</c> read another value than ours (another
        /// writer replaced the block after our <c>LateUpdate</c>), for the session, from
        /// <c>RenderPipelineManager.beginContextRendering</c>, which runs after every <c>LateUpdate</c>.
        /// <paramref name="everyTarget"/> (a part-type check, unknown writer): the checked target is rewritten every
        /// frame, and every table rewrites all its written targets at render time. Otherwise (a reentry check: the
        /// writer is the <c>ReentryMesh</c> of an active hull) only the targets rewritten every frame, those under an
        /// active hull, are written again at render time.</summary>
        public static void EnableRenderTimeFlush(string part, ChannelTable table, int target, bool everyTarget = true)
        {
            if (everyTarget) table?.SetRewriteEveryFrame(target, true);
            var widen = everyTarget && !_renderRewriteAll;
            if (_renderTimeFlush && !widen) return;
            if (!_renderTimeFlush)
            {
                _renderTimeFlush = true;
                RenderPipelineManager.beginContextRendering += OnRender;
            }
            if (everyTarget) _renderRewriteAll = true;
            KSP1PartModPlugin.Log?.LogWarning(everyTarget
                ? $"[KSP1PartMod] anim: {part} mpb check read another value: every written renderer is flushed again at render time"
                : $"[KSP1PartMod] anim: {part} mpb check at reentry read another value: the renderers under an active reentry hull are flushed again at render time");
        }

        /// <summary>Tests only: undoes <see cref="EnableRenderTimeFlush"/>.</summary>
        internal static void DisableRenderTimeFlush()
        {
            _renderRewriteAll = false;
            if (!_renderTimeFlush) return;
            _renderTimeFlush = false;
            RenderPipelineManager.beginContextRendering -= OnRender;
        }

        /// <summary>Tests only: the render-time flush is on, and whether it rewrites every written target.</summary>
        internal static (bool on, bool everyTarget) RenderTimeFlushState => (_renderTimeFlush, _renderRewriteAll);

        private static void OnRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!PerfOn)
            {
                FlushAtRender();
                return;
            }
            var start = Stopwatch.GetTimestamp();
            FlushAtRender();
            AddSince(PerfFeature.Flush, start);
        }

        /// <summary>The render-time flush: all the values of every written target of every table, or, after a reentry
        /// check only, of the targets rewritten every frame (and of any target with a change still pending).</summary>
        internal static void FlushAtRender()
        {
            var all = _renderRewriteAll;
            for (var i = 0; i < _count; i++)
            {
                try
                {
                    _tables[i].Flush(_sinks[i], rewriteAll: all);
                }
                catch (Exception e)
                {
                    KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: render-time flush failed, part dropped from the flush: {e}");
                    RemoveAt(i--);
                }
            }
        }

        private static int IndexOf(ChannelTable table)
        {
            for (var i = 0; i < _count; i++)
                if (ReferenceEquals(_tables[i], table)) return i;
            return -1;
        }

        private static void RemoveAt(int i)
        {
            var last = --_count;
            _tables[i] = _tables[last];
            _sinks[i] = _sinks[last];
            _tables[last] = null;
            _sinks[last] = null;
        }

        /// <summary>Play mode only: the tests flush with <see cref="FlushAll"/>.</summary>
        private static void EnsureInstance()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("KSP1PartMod.ChannelFlush");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ChannelFlush>();
        }
    }
}
