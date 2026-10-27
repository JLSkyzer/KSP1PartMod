using System;
using System.Collections.Generic;
using System.Globalization;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Spec 2a.5: is our property block kept until the frame renders? The first written renderer of each part
    /// type is read back from <c>RenderPipelineManager.beginContextRendering</c> (after every <c>LateUpdate</c> writer):
    /// <c>anim: &lt;part&gt; mpb check '&lt;prop&gt;': wrote &lt;v&gt;, read &lt;v'&gt;</c>. One check at a time. A different
    /// value turns on <see cref="ChannelFlush.EnableRenderTimeFlush"/> for that target.</summary>
    internal static class MpbCheck
    {
        /// <summary>Part types whose check has started: once per part type and session.</summary>
        private static readonly HashSet<string> CheckedParts = new HashSet<string>(StringComparer.Ordinal);
        private static readonly MaterialPropertyBlock ReadBlock = new MaterialPropertyBlock();

        private static string _part, _name;
        private static ChannelTable _table;
        private static int _target, _property;
        private static Renderer _renderer;
        private static ChannelValueType _type;
        private static Value4 _wrote;

        public static bool Pending { get; private set; }

        /// <summary>Starts the check of <paramref name="part"/>'s type unless it already ran.</summary>
        public static void TryStart(string part, ChannelTable table, int target, Renderer r, int property, string name,
            ChannelValueType type, in Value4 wrote)
        {
            if (Pending || !CheckedParts.Add(part)) return;
            _part = part;
            _table = table;
            _target = target;
            _renderer = r;
            _property = property;
            _name = name;
            _type = type;
            _wrote = wrote;
            Pending = true;
            RenderPipelineManager.beginContextRendering += OnRender;
        }

        private static void OnRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            RenderPipelineManager.beginContextRendering -= OnRender;
            Pending = false;
            try
            {
                if (_renderer == null)
                {
                    Log($"{_part} mpb check '{_name}': wrote {Show(_type, _wrote)}, renderer gone before the render");
                    return;
                }
                _renderer.GetPropertyBlock(ReadBlock);
                var read = Read(ReadBlock, _property, _type);
                Log($"{_part} mpb check '{_name}': wrote {Show(_type, _wrote)}, read {Show(_type, read)}");
                if (!read.SameAs(_wrote)) ChannelFlush.EnableRenderTimeFlush(_part, _table, _target);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {_part} mpb check failed: {e}");
            }
            finally
            {
                _renderer = null;
                _table = null;
            }
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
