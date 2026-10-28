using System;
using System.Collections.Generic;
using System.Globalization;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Perf;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Spec 2a.5: is our property block kept until the frame renders? A written renderer is read back from
    /// <c>RenderPipelineManager.beginContextRendering</c> (after every <c>LateUpdate</c> writer):
    /// <c>anim: &lt;part&gt; mpb check '&lt;prop&gt;': wrote &lt;v&gt;, read &lt;v'&gt;</c>. One check at a time, once per key
    /// and session: the first written renderer of each part type, and the first renderer of a part type rewritten under
    /// an active reentry hull (<c>… mpb check '&lt;prop&gt;' at reentry: …</c>), the one writer that replaces our block
    /// each frame (<c>ReentryMesh</c>).
    /// <list type="bullet">
    /// <item>The reference is what the block holds right after our own write, read back at once: in a Linear project a
    /// colour does not survive the block's round trip bit for bit. The render-time value is compared with it within
    /// <see cref="Tolerance"/>.</item>
    /// <item>A different value turns on <see cref="ChannelFlush.EnableRenderTimeFlush"/>: for every written target after
    /// a part-type check, for the targets under an active hull after a reentry check.</item>
    /// </list></summary>
    internal static class MpbCheck
    {
        /// <summary>Absolute, per component.</summary>
        public const float Tolerance = 1e-4f;

        /// <summary>Keys whose check has started: once per key and session.</summary>
        private static readonly HashSet<string> CheckedKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly MaterialPropertyBlock ReadBlock = new MaterialPropertyBlock();

        private static string _part, _name, _label;
        private static ChannelTable _table;
        private static int _target, _property;
        private static Renderer _renderer;
        private static ChannelValueType _type;
        private static Value4 _wrote, _reference;
        private static bool _everyTarget, _subscribed;

        public static bool Pending { get; private set; }

        /// <summary>The key of the reentry check of a part type.</summary>
        public static string ReentryKey(string part) => part + "|reentry";

        /// <summary>The check of <paramref name="key"/> has started this session.</summary>
        public static bool HasRun(string key) => CheckedKeys.Contains(key);

        /// <summary>Starts the check of <paramref name="key"/> unless one is pending or the key already ran. Called just
        /// after <paramref name="r"/>'s <c>SetPropertyBlock</c>: the block is read back now for the reference.</summary>
        /// <param name="label">Inserted after the property in the log line (" at reentry"), or null.</param>
        /// <param name="everyTarget">On a mismatch, every written target is flushed again at render time (part-type
        /// check); otherwise only the targets rewritten every frame (reentry check).</param>
        /// <returns>The check started.</returns>
        public static bool TryStart(string key, string part, string label, ChannelTable table, int target, Renderer r, int property,
            string name, ChannelValueType type, in Value4 wrote, bool everyTarget)
        {
            if (Pending || r == null || CheckedKeys.Contains(key)) return false;
            CheckedKeys.Add(key);
            _part = part;
            _label = label;
            _table = table;
            _target = target;
            _renderer = r;
            _property = property;
            _name = name;
            _type = type;
            _wrote = wrote;
            _everyTarget = everyTarget;
            r.GetPropertyBlock(ReadBlock);
            _reference = Read(ReadBlock, property, type);
            Pending = true;
            if (!_subscribed)
            {
                _subscribed = true;
                RenderPipelineManager.beginContextRendering += OnRender;
            }
            return true;
        }

        /// <summary>The block kept our value: <paramref name="read"/> within <see cref="Tolerance"/> of
        /// <paramref name="reference"/>.</summary>
        public static bool Kept(in Value4 reference, in Value4 read) => read.Near(reference, Tolerance);

        /// <summary>With <c>perf_counter</c>, timed as <see cref="PerfFeature.Flush"/>.</summary>
        private static void OnRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!ChannelFlush.PerfOn)
            {
                RunNow();
                return;
            }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            RunNow();
            ChannelFlush.AddSince(PerfFeature.Flush, start);
        }

        /// <summary>The render-time half of the check (tests call it directly: EditMode does not render). Null when no
        /// check is pending or the renderer is gone, else whether the block kept our value.</summary>
        internal static bool? RunNow()
        {
            Unsubscribe();
            if (!Pending) return null;
            Pending = false;
            try
            {
                if (_renderer == null)
                {
                    Log($"{_part} mpb check '{_name}'{_label}: wrote {Show(_type, _wrote)}, renderer gone before the render");
                    return null;
                }
                _renderer.GetPropertyBlock(ReadBlock);
                var read = Read(ReadBlock, _property, _type);
                var held = _reference.Near(_wrote, Tolerance) ? "" : $" (the block held {Show(_type, _reference)} after our write)";
                Log($"{_part} mpb check '{_name}'{_label}: wrote {Show(_type, _wrote)}, read {Show(_type, read)}{held}");
                var kept = Kept(_reference, read);
                if (!kept) ChannelFlush.EnableRenderTimeFlush(_part, _table, _target, _everyTarget);
                return kept;
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {_part} mpb check failed: {e}");
                return null;
            }
            finally
            {
                _renderer = null;
                _table = null;
            }
        }

        /// <summary>Tests only: no check pending, every key may run again.</summary>
        internal static void ResetForTests()
        {
            Unsubscribe();
            Pending = false;
            _renderer = null;
            _table = null;
            CheckedKeys.Clear();
        }

        private static void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            RenderPipelineManager.beginContextRendering -= OnRender;
        }

        private static Value4 Read(MaterialPropertyBlock b, int id, ChannelValueType type)
        {
            switch (type)
            {
                case ChannelValueType.Color:
                {
                    var c = b.GetColor(id);
                    return new Value4(c.r, c.g, c.b, c.a);
                }
                case ChannelValueType.Vector:
                {
                    var v = b.GetVector(id);
                    return new Value4(v.x, v.y, v.z, v.w);
                }
                default:
                    return new Value4(b.GetFloat(id), 0f, 0f, 0f);
            }
        }

        private static string Show(ChannelValueType type, in Value4 v) =>
            type == ChannelValueType.Float
                ? F(v.X)
                : "(" + F(v.X) + ", " + F(v.Y) + ", " + F(v.Z) + ", " + F(v.W) + ")";

        private static string F(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        private static void Log(string message) => KSP1PartModPlugin.Log?.LogInfo("[KSP1PartMod] anim: " + message);
    }
}
