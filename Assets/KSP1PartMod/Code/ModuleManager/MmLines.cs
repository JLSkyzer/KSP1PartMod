using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.ModuleManager
{
    /// <summary>The exact <c>mm:</c> log lines of the ModuleManager glue (plan Task 2). Name lists are joined with
    /// ", "; an empty list reads "none".</summary>
    public static class MmLines
    {
        private const string Prefix = "[KSP1PartMod] mm: ";

        private static string OnOff(bool on) => on ? "on" : "off";

        private static string Names(IEnumerable<string> names)
        {
            var list = names?.ToList() ?? new List<string>();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        public static string Armed(bool safetyNet, bool arithmetic, bool indexSelectors, bool firstMatch, bool beforeAfter) =>
            $"{Prefix}armed: plugin safety net {OnOff(safetyNet)}, arithmetic {OnOff(arithmetic)}, index selectors {OnOff(indexSelectors)}, " +
            $"first-match edits {OnOff(firstMatch)}, BEFORE/AFTER need their mod {OnOff(beforeAfter)}";

        public static string MemberMissing(string member, string feature) =>
            $"{Prefix}{member} not found, {feature} off (Redux's patcher left as is)";

        public static string BlockedNames(IEnumerable<string> names, int curated, int automatic, IEnumerable<string> kept) =>
            $"{Prefix}blocked plugin names: {Names(names)} (curated {curated}, automatic {automatic}; kept: {Names(kept)})";

        public static string BlockedName(string name, int needsTerms, int forDropped, int beforeAfterDropped, int folderDropped) =>
            $"{Prefix}{name}: {needsTerms} NEEDS term(s) now false, {forDropped + beforeAfterDropped + folderDropped} patch(es) dropped " +
            $"({forDropped} FOR, {beforeAfterDropped} BEFORE/AFTER, {folderDropped} from its folder)";

        public static string BeforeAfterAbsent(IEnumerable<KeyValuePair<string, int>> droppedByMod)
        {
            var list = droppedByMod?.ToList() ?? new List<KeyValuePair<string, int>>();
            var text = list.Count == 0 ? "none" : string.Join(", ", list.Select(p => $"{p.Key} {p.Value}"));
            return $"{Prefix}BEFORE/AFTER of an absent mod: {list.Sum(p => p.Value)} patch(es) dropped: {text}";
        }

        public static string Arithmetic(MmCounters c) =>
            $"{Prefix}arithmetic: {c.ArithmeticApplied} applied (*= {c.ArithmeticMul}, += {c.ArithmeticAdd}, -= {c.ArithmeticSub}, /= {c.ArithmeticDiv}), " +
            $"skipped: {c.NonNumeric} non-numeric, {c.MissingKey} missing key, {c.DivideByZero} divide by zero; " +
            $"not supported, left out as before: {c.NotSupportedPow} !=, {c.NotSupportedRegex} ^=";

        public static string Index(MmCounters c) =>
            $"{Prefix}index: {c.IndexValueEdits} value edit(s), {c.IndexNodeEdits} node edit(s), {c.IndexOutOfRange} out of range";

        public static string UnindexedMultiMatch(int count, bool firstMatch) =>
            $"{Prefix}unindexed edits that matched more than one value/node: {count} (first-match edits {OnOff(firstMatch)})";

        public static string Variables(int count) =>
            $"{Prefix}{count} value(s) with #$ variables left as text (not supported yet)";

        public static string VectorComponentsSkipped(int count) =>
            $"{Prefix}{count} vector component edit(s) (key[i] op= x) skipped, not supported (Redux would write the operand over every value of the key)";

        public static string PatchFailed(string exception) =>
            $"{Prefix}patch failed, stopped for this session: {exception}";
    }
}
