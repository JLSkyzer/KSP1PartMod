using System;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>Text of the one-shot <c>icons: diag</c> lines (first KSP1 icon subject, first KSP2 one): what decides
    /// whether a studio light reaches a renderer (layer, rendering layers) and what its material shows.</summary>
    internal static class IconDiagnostics
    {
        private const string StudioLightPrefix = "part-icon-";
        private static readonly string[] Textures = { "_MainTex", "_BaseMap", "_BumpMap", "_PaintMaskGlossMap" };
        private static readonly string[] Colors = { "_Color", "_BaseColor", "_PaintA", "_PaintB" };

        /// <param name="maskBefore">The renderer's rendering layers as the studio cloned them, before our fix.</param>
        public static string Renderer(Renderer r, Transform root, uint maskBefore)
        {
            var sb = new StringBuilder();
            sb.Append('\'').Append(PathOf(r.transform, root)).Append("' layer ").Append(r.gameObject.layer)
              .Append(" renderingLayerMask 0x").Append(maskBefore.ToString("X", CultureInfo.InvariantCulture));
            if (r.renderingLayerMask != maskBefore)
                sb.Append(" -> 0x").Append(r.renderingLayerMask.ToString("X", CultureInfo.InvariantCulture));
            sb.Append(" enabled ").Append(r.enabled);
            var m = r.sharedMaterial;
            if (m == null) return sb.Append(" material null").ToString();
            sb.Append(" shader '").Append(m.shader != null ? m.shader.name : "null").Append('\'');
            foreach (var t in Textures)
                if (m.HasProperty(t)) sb.Append(' ').Append(t).Append('=').Append(m.GetTexture(t) != null ? m.GetTexture(t).name : "none");
            foreach (var c in Colors)
                if (m.HasProperty(c)) sb.Append(' ').Append(c).Append('=').Append(Rgba(m.GetColor(c)));
            sb.Append(" keywords=[").Append(string.Join(",", m.shaderKeywords ?? new string[0])).Append(']');
            return sb.ToString();
        }

        /// <summary>The studio's lights (named <c>part-icon-…</c> by Redux), after Redux synced them during the
        /// render. One call per diagnosed icon: <c>FindObjectsOfTypeAll</c> is slow.</summary>
        public static string StudioLights()
        {
            var lights = Resources.FindObjectsOfTypeAll<Light>()
                .Where(l => l != null && l.name.StartsWith(StudioLightPrefix, StringComparison.Ordinal)).ToList();
            if (lights.Count == 0) return "no studio light found";
            var groups = lights.GroupBy(l => (l.cullingMask, l.renderingLayerMask))
                .Select(g => $"{g.Count()} x cullingMask 0x{g.Key.cullingMask:X} renderingLayerMask 0x{g.Key.renderingLayerMask:X}");
            return $"{lights.Count} light(s): {string.Join(", ", groups)}; ambient {Rgba(RenderSettings.ambientLight)} " +
                   $"mode {RenderSettings.ambientMode}";
        }

        private static string Rgba(Color c) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###},{3:0.###})", c.r, c.g, c.b, c.a);

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && t != root && p != root.parent; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
