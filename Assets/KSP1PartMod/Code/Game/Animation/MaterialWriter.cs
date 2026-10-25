using System.Collections.Generic;
using System.Text;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Writes a Color entry's colour to the part's renderers, as KSP1's ModuleColorChanger does: renderers
    /// selected by <c>includedRenderer</c> / <c>excludedRenderer</c>, the colour set through a
    /// <c>MaterialPropertyBlock</c> (read first, so other values in the block stay). The KSP1 property name goes
    /// through <c>shader_property_map</c>: per renderer, the first candidate its material has. Each shader met is
    /// logged once with its property names, because the emissive property of Redux's shaders is not known.</summary>
    internal sealed class MaterialWriter
    {
        private static readonly HashSet<string> LoggedShaders = new HashSet<string>();

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<int> _propertyIds = new List<int>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        /// <summary>Renderers written to.</summary>
        public int RendererCount => _renderers.Count;

        /// <summary>What was chosen, for the log: <c>-&gt; n renderer(s) via [names]</c>, or why nothing was.</summary>
        public string Note { get; }

        public MaterialWriter(GameObject partRoot, AnimEntryDef e, AnimationSettings s)
        {
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
                used.Add(name);
            }
            Note = _renderers.Count > 0
                ? $"-> {_renderers.Count} renderer(s) via [{string.Join(", ", used)}]"
                : $"no renderer of {selected} selected has any of [{string.Join(", ", candidates)}] (shader_property_map in animations.json)";
        }

        /// <summary>No allocation.</summary>
        public void Write(Rgba c)
        {
            var color = new Color(c.R, c.G, c.B, c.A);
            for (var i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(_propertyIds[i], color);
                r.SetPropertyBlock(_block);
            }
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
