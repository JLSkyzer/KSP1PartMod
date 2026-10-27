using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Writes the channel table of every KSP1 part instance once per frame, after the game's writers (spec
    /// "Material writers and shader timing", flush order): one behaviour, created on the first registration and kept
    /// across scenes. <c>[DefaultExecutionOrder(32000)]</c> is above every order in the Redux decompile (max 10000); whether
    /// Unity applies it to a type of a mod assembly is not provable, so the <c>mpb check</c> line confirms it at run time.
    /// If that check reads another value than ours, <see cref="EnableRenderTimeFlush"/> flushes again when the frame
    /// renders (only targets rewritten every frame write then). The flush allocates nothing.</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class ChannelFlush : MonoBehaviour
    {
        private static ChannelTable[] _tables = new ChannelTable[64];
        private static RendererChannels[] _sinks = new RendererChannels[64];
        private static int _count;
        private static ChannelFlush _instance;
        private static bool _renderTimeFlush;

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

        private void LateUpdate() => FlushAll();

        /// <summary>Every registered table, in registration order (swap-removal aside). A table whose part is gone, or
        /// whose flush throws, is dropped (the throw logged once with its stack).</summary>
        internal static void FlushAll()
        {
            var diagnostics = KSP1PartModPlugin.Animations?.Diagnostics ?? false;
            var start = diagnostics ? Stopwatch.GetTimestamp() : 0L;
            var writes = 0;
            for (var i = 0; i < _count; i++)
            {
                var sink = _sinks[i];
                if (sink.Orphaned)
                {
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
            if (!diagnostics) return;
            var now = Stopwatch.GetTimestamp();
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

        /// <summary>Fallback of the spec's flush order: flush again from <c>RenderPipelineManager.beginContextRendering</c>,
        /// which runs after every <c>LateUpdate</c>. Turned on, for the session, by an <c>mpb check</c> that read another
        /// value than ours.</summary>
        public static void EnableRenderTimeFlush(string part)
        {
            if (_renderTimeFlush) return;
            _renderTimeFlush = true;
            RenderPipelineManager.beginContextRendering += OnRender;
            KSP1PartModPlugin.Log?.LogWarning(
                $"[KSP1PartMod] anim: {part} mpb check read another value: renderers rewritten every frame are flushed again at render time");
        }

        private static void OnRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            for (var i = 0; i < _count; i++)
            {
                try
                {
                    _tables[i].Flush(_sinks[i]); // dirty targets are clean after LateUpdate: only every-frame targets write
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
