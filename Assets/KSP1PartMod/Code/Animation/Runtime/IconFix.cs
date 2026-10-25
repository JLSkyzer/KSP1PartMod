using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>Redux 26w42c renders a KSP1 icon with <c>IconRenderProfile.Ksp1Import</c>, whose only difference from the
    /// KSP2 profile is <c>hotswapPartShaders = false</c> (<c>RuntimePartIconRenderer.RenderIcon</c>). KSP1 materials are
    /// made with <c>KSP2/Parts/Paintable</c> when that shader exists (<c>Ksp1ModelImporter.FindBestShader</c>); the OAB
    /// swaps it for <c>KSP2/Scenery/Standard (Opaque)</c>, the icon studio then does not, and the subject comes out
    /// black under the studio's own lights. The fix turns the swap on for KSP1 parts.</summary>
    public static class IconFix
    {
        public const string SourceShader = "KSP2/Parts/Paintable";
        public const string TargetShader = "KSP2/Scenery/Standard (Opaque)";

        public static bool ShouldSwap(bool enabled, bool isKsp1Part, bool swapAlready) => enabled && isKsp1Part && !swapAlready;

        /// <summary>1, 10, 100, then every 500.</summary>
        public static bool ShouldLog(int count) => count == 1 || count == 10 || count == 100 || (count > 0 && count % 500 == 0);

        public static string Line(int count, string part, string census) =>
            $"[KSP1PartMod] icons: rendered {count} KSP1 icon(s) with hotswapPartShaders=true ({SourceShader} -> {TargetShader}); " +
            $"last '{part}' had [{census}]";

        public static string ShaderCensus(IEnumerable<string> shaderNames) =>
            string.Join(", ", (shaderNames ?? Enumerable.Empty<string>()).Where(n => n != null)
                .GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"));
    }
}
