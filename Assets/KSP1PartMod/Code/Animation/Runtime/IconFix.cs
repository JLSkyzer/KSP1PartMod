using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>Why KSP1 icons came out black (decompilation of <c>Ksp2Redux.dll</c> 26w42c, 2026-10-25). Redux renders on
    /// URP and emulates the legacy light culling masks with rendering layers (<c>Redux.Rendering.LegacyLightCullingMask</c>):
    /// a light gets <c>ForCullingMask(cullingMask)</c>, a scene renderer <c>ForLayer(gameObject.layer)</c>, layer 0 being
    /// bit 23 (<c>0x800000</c>). The icon studio (<c>PartIconRenderer.RenderTexture2D</c>) moves the subject to layer 31
    /// and its lights cull to <c>1 &lt;&lt; 31</c>, which maps to bit 0 (<c>0x1</c>), but it never touches the subject's
    /// <c>renderingLayerMask</c>: the clone keeps its prefab's. KSP1 prefabs are scene objects (<c>DontDestroyOnLoad</c>,
    /// <c>Ksp1ModelImporter</c>), which Redux's per-frame sweep visits while the import runs over many frames, mapping
    /// their renderers to <c>0x800000</c>; the icons are rendered at the end (<c>MarkImportComplete</c> →
    /// <c>GenerateMissingIcons</c>), and no studio light reaches them: black, geometry right. That the sweep reached a
    /// given prefab first is not proven statically: the <c>icons: diag</c> line logs each mask before the fix. KSP2 fallback icons come from Addressables prefabs, which are
    /// assets, never swept. The fix gives each KSP1 subject renderer the lights' layers. The shader swap of 0.2.2 is kept
    /// but does nothing so far: the in-game census shows the prefabs already in <c>KSP2/Scenery/Standard</c>, as
    /// <c>FindBestShader</c> gives when <c>KSP2/Parts/Paintable</c> is not found at import.</summary>
    public static class IconFix
    {
        public const string SourceShader = "KSP2/Parts/Paintable";
        public const string TargetShader = "KSP2/Scenery/Standard (Opaque)";
        /// <summary>Renderers listed per diagnosed subject.</summary>
        public const int DiagRendererLimit = 4;

        public static bool ShouldSwap(bool enabled, bool isKsp1Part, bool swapAlready) => enabled && isKsp1Part && !swapAlready;

        /// <summary>A renderer is lit by a light only if their rendering layers overlap.</summary>
        public static bool NeedsLayerFix(uint rendererMask, uint lightMask) => (rendererMask & lightMask) == 0;

        /// <summary>1, 10, 100, then every 500.</summary>
        public static bool ShouldLog(int count) => count == 1 || count == 10 || count == 100 || (count > 0 && count % 500 == 0);

        public static string Line(int count, string part, string census, uint lightMask) =>
            $"[KSP1PartMod] icons: rendered {count} KSP1 icon(s), subject rendering layers set to 0x{lightMask.ToString("X", CultureInfo.InvariantCulture)}; " +
            $"last '{part}' had [{census}]";

        public static string ShaderCensus(IEnumerable<string> shaderNames) =>
            string.Join(", ", (shaderNames ?? Enumerable.Empty<string>()).Where(n => n != null)
                .GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"));
    }
}
