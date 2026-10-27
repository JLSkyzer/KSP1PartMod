using System.Collections.Generic;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The renderers and lights of one part instance that channels of its <see cref="ChannelTable"/> target:
    /// one table target per renderer or light, made on first use. This file holds the bind-time half (Task 7 of plan
    /// 2a); the write half (<c>IChannelSink</c>, property blocks, material copies, rebind) is Task 8's.</summary>
    internal sealed class RendererChannels
    {
        private readonly ChannelTable _table;
        private readonly Dictionary<Object, int> _targetOf = new Dictionary<Object, int>();
        private readonly List<Object> _objects = new List<Object>();

        public RendererChannels(ChannelTable table) => _table = table;

        public int RendererCount { get; private set; }
        public int LightCount { get; private set; }

        /// <summary>The table target of <paramref name="r"/>, added on first use. <paramref name="note"/>: null (Task 8
        /// says what it decided for the renderer's materials).</summary>
        public int AddRenderer(Renderer r, AnimationSettings s, out string note)
        {
            note = null;
            if (_targetOf.TryGetValue(r, out var target)) return target;
            RendererCount++;
            return Add(r, TargetKind.Renderer);
        }

        /// <summary>The table target of <paramref name="l"/>, added on first use.</summary>
        public int AddLight(Light l)
        {
            if (_targetOf.TryGetValue(l, out var target)) return target;
            LightCount++;
            return Add(l, TargetKind.Light);
        }

        /// <summary>The renderer or light of a table target; null when unknown or destroyed.</summary>
        public Object TargetObject(int target) => target >= 0 && target < _objects.Count ? _objects[target] : null;

        private int Add(Object o, TargetKind kind)
        {
            var target = _table.AddTarget(kind, false, false);
            while (_objects.Count <= target) _objects.Add(null);
            _objects[target] = o;
            _targetOf[o] = target;
            return target;
        }
    }
}
