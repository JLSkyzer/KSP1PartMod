using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Audit
{
    /// <summary>The audit's <c>b9_overrides</c> cell (B9 overrides, 0.9.0). Pure: the glue reads the part definition's
    /// <c>Data_PartSwitch</c> and the import's tallies, and builds the items here. Items, joined by <c>;</c> in the CSV:
    /// <list type="bullet">
    /// <item>one per set that carries a B9 override transformer in any variant:
    /// <c>&lt;set&gt;=&lt;active variant&gt;: &lt;kinds of that variant, or none&gt;</c>, with the kinds
    /// <c>mass+0.125</c>, <c>res LFO+Ablator</c>, <c>scalar maxTemp</c>, <c>Module_Engine.engineModes</c>;</item>
    /// <item>the import's counts for the part: <c>counts: mass m, tank t, scalars s, module d, off o, notes n</c>;</item>
    /// <item>when the spawned part shows that the default variant did not apply: <c>failed: &lt;reason&gt;</c>.</item>
    /// </list></summary>
    public static class AuditB9
    {
        public const string CountsPrefix = "counts: ";
        public const string FailedPrefix = "failed: ";
        public const string PartSwitch = "Module_PartSwitch";

        public static string Mass(double delta) =>
            "mass" + (delta >= 0 ? "+" : "") + delta.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>The resources a variant's adder sets (KSP2 names), or <c>none</c> when it only removes them.</summary>
        public static string Resources(IEnumerable<string> names)
        {
            var list = (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
            return "res " + (list.Count == 0 ? "none" : string.Join("+", list));
        }

        public static string Scalar(string key) => "scalar " + key;

        public static string Module(string behaviour, string field) => behaviour + "." + field;

        public static string Set(string setId, string variant, IEnumerable<string> kinds)
        {
            var list = (kinds ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrEmpty(k)).ToList();
            return $"{setId}={variant ?? "none"}: {(list.Count == 0 ? "none" : string.Join(", ", list))}";
        }

        public static string Counts(int mass, int tank, int scalars, int module, int off, int notes) =>
            CountsPrefix + $"mass {mass}, tank {tank}, scalars {scalars}, module {module}, off {off}, notes {notes}";

        public static string Failed(string reason) => FailedPrefix + reason;

        /// <summary>Why the default variant did not apply on the spawned part, or null. Only a part with B9 override sets
        /// that was spawned is judged: its <c>Module_PartSwitch</c> (which applies the variants in the VAB) must be there and
        /// initialized, and neither it nor a behaviour a <c>ModuleDefinitionTransformer</c> edits may have thrown or stayed
        /// uninitialized.</summary>
        /// <param name="present">The behaviour type names of the spawned part.</param>
        /// <param name="uninitialized">Its behaviours that did not initialize.</param>
        /// <param name="failingModule">The module in whose frame the spawn threw, or null.</param>
        /// <param name="edited">The behaviours the sets' <c>ModuleDefinitionTransformer</c>s edit.</param>
        public static string Failure(bool hasOverrides, bool spawned, IEnumerable<string> present, IEnumerable<string> uninitialized,
            string failingModule, IEnumerable<string> edited)
        {
            if (!hasOverrides || !spawned) return null;
            var have = new HashSet<string>(present ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var bad = new HashSet<string>(uninitialized ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (!have.Contains(PartSwitch)) return PartSwitch + " missing";
            if (string.Equals(failingModule, PartSwitch, StringComparison.Ordinal)) return PartSwitch + " threw";
            if (bad.Contains(PartSwitch)) return PartSwitch + " uninitialized";
            foreach (var b in (edited ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal))
            {
                if (string.Equals(failingModule, b, StringComparison.Ordinal)) return b + " threw";
                if (bad.Contains(b)) return b + " uninitialized";
            }
            return null;
        }

        /// <summary>The cell's items: the sets, then the counts (when given), then the failure (when any).</summary>
        public static IReadOnlyList<string> Items(IEnumerable<string> sets, string counts, string failure)
        {
            var items = (sets ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (!string.IsNullOrEmpty(counts)) items.Add(counts);
            if (!string.IsNullOrEmpty(failure)) items.Add(Failed(failure));
            return items;
        }

        /// <summary>The part carries at least one B9 override set.</summary>
        public static bool HasOverrides(IReadOnlyList<string> items) =>
            items != null && items.Any(i => i != null && !i.StartsWith(CountsPrefix, StringComparison.Ordinal)
                                                      && !i.StartsWith(FailedPrefix, StringComparison.Ordinal));

        /// <summary>The default variant failed to apply on the part.</summary>
        public static bool IsFailed(IReadOnlyList<string> items) =>
            items != null && items.Any(i => i != null && i.StartsWith(FailedPrefix, StringComparison.Ordinal));
    }
}
