using System;

namespace KSP1PartMod.Native
{
    /// <summary>The tag Redux's KSP1 import appends to every imported part: <c>BuildTags</c> joins the part's own tags, its
    /// mod name, its category and <c>KSP1</c>, with spaces (Ksp1PartTranslator, 26w42c decompilation). Pure.</summary>
    public static class Ksp1Tags
    {
        public const string Token = "KSP1";

        /// <summary>True when the last space-separated token of <paramref name="tags"/> is <c>KSP1</c> (exact: a part whose
        /// tags merely contain the substring is not a KSP1 import; the final review's M6).</summary>
        public static bool IsKsp1Import(string tags)
        {
            if (string.IsNullOrEmpty(tags)) return false;
            var t = tags.TrimEnd();
            if (!t.EndsWith(Token, StringComparison.Ordinal)) return false;
            var at = t.Length - Token.Length;
            return at == 0 || char.IsWhiteSpace(t[at - 1]);
        }
    }
}
