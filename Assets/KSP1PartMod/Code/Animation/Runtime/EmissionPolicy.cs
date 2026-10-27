using System.Text;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>What the glue reads, at bind, from one material of a renderer written through a colour property.</summary>
    public readonly struct MaterialFacts
    {
        /// <summary>The shader actually found on the material at bind (never assumed for a part).</summary>
        public readonly string Shader;
        /// <summary>The material has <c>_EmissionColor</c>.</summary>
        public readonly bool HasEmissionColor;
        /// <summary>The shader's keyword space declares <c>_EMISSION</c>; <see cref="KeywordOn"/>: it is enabled on the
        /// material.</summary>
        public readonly bool KeywordDeclared, KeywordOn;
        /// <summary>The material has <c>_EmissionMap</c>; <see cref="EmissionMapSet"/>: a texture is assigned.</summary>
        public readonly bool HasEmissionMap, EmissionMapSet;

        public MaterialFacts(string shader, bool hasEmissionColor, bool keywordDeclared, bool keywordOn, bool hasEmissionMap,
            bool emissionMapSet)
        {
            Shader = shader;
            HasEmissionColor = hasEmissionColor;
            KeywordDeclared = keywordDeclared;
            KeywordOn = keywordOn;
            HasEmissionMap = hasEmissionMap;
            EmissionMapSet = emissionMapSet;
        }
    }

    /// <summary>What the glue does to one material so that a written <c>_EmissionColor</c> shows.</summary>
    public readonly struct EmissionDecision
    {
        /// <summary>Give the renderer its own material copies, once at bind, through <c>renderer.materials</c> (Unity
        /// tracks them, so Redux's later <c>renderer.materials</c> calls do not instance them again), with the copy's
        /// <c>_EmissionColor</c> black so nothing shows until a value is written. Destroyed on shutdown and rebind.</summary>
        public readonly bool CopyMaterial;
        /// <summary>Enable <c>_EMISSION</c> on the copy.</summary>
        public readonly bool EnableKeyword;
        /// <summary>Set <c>Texture2D.whiteTexture</c> as <c>_EmissionMap</c> in the renderer's block.</summary>
        public readonly bool WhiteMap;
        /// <summary>For the log: <c>keyword _EMISSION &lt;declared|absent&gt; &lt;on|off&gt;, _EmissionMap
        /// &lt;set|none|absent&gt; -&gt; &lt;action&gt;</c>. The glue prefixes
        /// <c>anim: emission on '&lt;shader&gt;' material '&lt;name&gt;': </c>.</summary>
        public readonly string Action;

        public EmissionDecision(bool copyMaterial, bool enableKeyword, bool whiteMap, string action)
        {
            CopyMaterial = copyMaterial;
            EnableKeyword = enableKeyword;
            WhiteMap = whiteMap;
            Action = action;
        }
    }

    /// <summary>Spec 2a.6, per material, from the shader found at bind: what makes a written <c>_EmissionColor</c>
    /// visible. Runs at bind only (it builds a string).</summary>
    public static class EmissionPolicy
    {
        public const string EmissionColor = "_EmissionColor";

        /// <summary>Acts only when <paramref name="pickedProperty"/> is <c>_EmissionColor</c>, the material has it and
        /// <paramref name="emissionSetup"/> is on:
        /// <list type="bullet">
        /// <item>keyword declared and off: copy and keyword on;</item>
        /// <item>keyword absent from the shader: nothing for the keyword (the shader does not gate emission on it);</item>
        /// <item><c>_EmissionMap</c> present and unset: white map in the block, whatever the keyword.</item>
        /// </list>
        /// Every other shader or property: no action.</summary>
        public static EmissionDecision Decide(in MaterialFacts f, string pickedProperty, bool emissionSetup)
        {
            var facts = Facts(f);
            if (pickedProperty != EmissionColor)
                return new EmissionDecision(false, false, false, $"{facts} -> no action (property {pickedProperty ?? "none"})");
            if (!f.HasEmissionColor)
                return new EmissionDecision(false, false, false, $"{facts} -> no action (no {EmissionColor})");
            if (!emissionSetup)
                return new EmissionDecision(false, false, false, $"{facts} -> no action (emission_setup false)");

            var copy = f.KeywordDeclared && !f.KeywordOn;
            var white = f.HasEmissionMap && !f.EmissionMapSet;
            var action = new StringBuilder();
            if (copy) action.Append("own material copy with keyword on");
            else if (!f.KeywordDeclared) action.Append("keyword left alone (absent)");
            if (white) action.Append(action.Length > 0 ? ", " : "").Append("white _EmissionMap");
            if (action.Length == 0) action.Append("no action");
            return new EmissionDecision(copy, copy, white, $"{facts} -> {action}");
        }

        private static string Facts(in MaterialFacts f) =>
            $"keyword _EMISSION {(f.KeywordDeclared ? "declared" : "absent")} {(f.KeywordOn ? "on" : "off")}, " +
            $"_EmissionMap {(!f.HasEmissionMap ? "absent" : f.EmissionMapSet ? "set" : "none")}";
    }
}
