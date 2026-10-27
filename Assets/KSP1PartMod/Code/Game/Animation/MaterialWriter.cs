using System.Collections.Generic;
using System.Text;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>A Color entry's writer in the part's <see cref="ChannelTable"/>, as KSP1's ModuleColorChanger: renderers
    /// selected by <c>includedRenderer</c> / <c>excludedRenderer</c>, the KSP1 property name through
    /// <c>shader_property_map</c> (per renderer, the first candidate its material has). Its channels are of the Color
    /// class, which beats clip tracks; <see cref="Write"/> submits, and the table's flush writes the property block after
    /// the game's writers. Each shader met is logged once with its property names.</summary>
    internal sealed class MaterialWriter
    {
        private static readonly HashSet<string> LoggedShaders = new HashSet<string>();

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<int> _propertyIds = new List<int>();
        private readonly List<string> _propertyNames = new List<string>();
        private readonly AnimEntryDef _def;
        private readonly AnimationSettings _settings;
        private ChannelTable _table;
        private int[] _channels = new int[0];

        /// <summary>Renderers written to.</summary>
        public int RendererCount => _renderers.Count;

        /// <summary>What was chosen, for the log: <c>-&gt; n renderer(s) via [names]</c>, or why nothing was.</summary>
        public string Note { get; }

        public MaterialWriter(GameObject partRoot, AnimEntryDef e, AnimationSettings s)
        {
            _def = e;
            _settings = s;
            var candidates = MaterialRules.Candidates(e.ShaderProperty, s?.ShaderPropertyMap);
            var used = new SortedSet<string>(System.StringComparer.Ordinal);
            var selected = 0;
            foreach (var r in partRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !MaterialRules.Accepts(r.gameObject.name, e.IncludedRenderers, e.ExcludedRenderers)) continue;
                selected++;
                var material = r.sharedMaterial;
                if (material == null) continue;
                LogShader(material.shader);
                var name = MaterialRules.Pick(candidates, material.HasProperty);
                if (name == null) continue;
                _renderers.Add(r);
                _propertyIds.Add(Shader.PropertyToID(name));
                _propertyNames.Add(name);
                used.Add(name);
            }
            Note = _renderers.Count > 0
                ? $"-> {_renderers.Count} renderer(s) via [{string.Join(", ", used)}]"
                : $"no renderer of {selected} selected has any of [{string.Join(", ", candidates)}] (shader_property_map in animations.json)";
        }

        /// <summary>One Color-class channel per selected renderer in <paramref name="table"/> (a new table on each bind
        /// of the part), ranked by the entry's KSP1 module index. A renderer whose property is already registered with
        /// another value type is skipped.</summary>
        public void Register(ChannelTable table, RendererChannels sink)
        {
            _table = table;
            var channels = new List<int>();
            for (var i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                var material = r.sharedMaterial;
                var rest = material != null ? material.GetColor(_propertyIds[i]) : Color.black;
                var target = sink.AddRenderer(r, _settings, out _, _propertyNames[i]);
                try
                {
                    channels.Add(table.Register(target, _propertyIds[i], ChannelValueType.Color, WriterClass.Color, _def.ModuleIndex,
                        new Value4(rest.r, rest.g, rest.b, rest.a)));
                }
                catch (System.ArgumentException)
                {
                    // the slot holds another value type: not ours to write
                }
            }
            _channels = channels.ToArray();
        }

        /// <summary>Submits the colour on every channel; the flush writes it. No allocation.</summary>
        public void Write(Rgba c)
        {
            var table = _table;
            if (table == null) return;
            var v = new Value4(c.R, c.G, c.B, c.A);
            for (var i = 0; i < _channels.Length; i++) table.Submit(_channels[i], v);
        }

        private static void LogShader(Shader shader)
        {
            if (shader == null || !LoggedShaders.Add(shader.name)) return;
            var sb = new StringBuilder();
            var n = shader.GetPropertyCount();
            for (var i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(shader.GetPropertyName(i)).Append(" (").Append(shader.GetPropertyType(i)).Append(')');
            }
            KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] anim: shader '{shader.name}': {sb}");
        }
    }
}
