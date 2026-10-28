using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Audit
{
    /// <summary>Which of the native modules this mod converted on a part are really running on the instantiated part
    /// (0.8.0). Pure.</summary>
    public static class AuditNative
    {
        public const string Heatshield = "Module_Heatshield";
        public const string Crossfeed = "Module_ToggleCrossfeed";
        public const string UninitializedTag = ":uninitialized";
        public const string MissingTag = ":missing";

        /// <summary>Every converted module name, tagged <c>:missing</c> when the part has no such module and
        /// <c>:uninitialized</c> when it has one that did not initialize.</summary>
        public static IReadOnlyList<string> Tag(IEnumerable<string> converted, IEnumerable<string> present, IEnumerable<string> uninitialized)
        {
            if (converted == null) return Array.Empty<string>();
            var have = new HashSet<string>(present ?? Array.Empty<string>(), StringComparer.Ordinal);
            var bad = new HashSet<string>(uninitialized ?? Array.Empty<string>(), StringComparer.Ordinal);
            return converted.Select(c => !have.Contains(c) ? c + MissingTag : bad.Contains(c) ? c + UninitializedTag : c).ToArray();
        }

        /// <summary>True when the entry carries a failure tag.</summary>
        public static bool IsFailed(string entry) =>
            entry != null && (entry.EndsWith(UninitializedTag, StringComparison.Ordinal) || entry.EndsWith(MissingTag, StringComparison.Ordinal));
    }
}
