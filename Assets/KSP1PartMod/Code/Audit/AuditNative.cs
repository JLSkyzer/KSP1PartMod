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
        /// <summary>Converters, drills and scanners (0.10.0).</summary>
        public const string Converter = "Module_ResourceConverter";
        public const string Drill = "Module_Mine";
        public const string Scanner = "Module_ResourceScanner";
        /// <summary>Radiators (0.11.0): fixed ones are a cooler, deployable ones an active radiator.</summary>
        public const string Cooler = "Module_Cooler";
        public const string ActiveRadiator = "Module_ActiveRadiator";
        /// <summary>Science (0.12.0): the experiment module, and report storage, a flag of the part definition and not a
        /// module.</summary>
        public const string Science = "Module_ScienceExperiment";
        public const string ReportStorage = "report storage";
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

        /// <summary><paramref name="tagged"/> with <see cref="ReportStorage"/> appended when the import gave the part report
        /// storage (<paramref name="recorded"/>), tagged <c>:missing</c> when the part definition does not carry the flag
        /// (<paramref name="onDefinition"/>). Unchanged when the import gave none.</summary>
        public static IReadOnlyList<string> WithStorage(IReadOnlyList<string> tagged, bool recorded, bool onDefinition)
        {
            tagged = tagged ?? Array.Empty<string>();
            if (!recorded) return tagged;
            return tagged.Concat(new[] { onDefinition ? ReportStorage : ReportStorage + MissingTag }).ToArray();
        }

        /// <summary>True when the entry carries a failure tag.</summary>
        public static bool IsFailed(string entry) =>
            entry != null && (entry.EndsWith(UninitializedTag, StringComparison.Ordinal) || entry.EndsWith(MissingTag, StringComparison.Ordinal));
    }
}
