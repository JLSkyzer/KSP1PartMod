using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSP1PartMod.ModuleManager
{
    /// <summary>Counts of the ModuleManager edits of one session (the <c>mm:</c> summary lines).</summary>
    public sealed class MmCounters
    {
        public int ArithmeticMul, ArithmeticAdd, ArithmeticSub, ArithmeticDiv;
        public int NonNumeric, MissingKey, DivideByZero;
        public int NotSupportedPow, NotSupportedRegex;
        public int IndexValueEdits, IndexNodeEdits, IndexOutOfRange;
        /// <summary>Unindexed edits that matched more than one value or node (the measurement for
        /// <c>mm_edit_first_match</c>).</summary>
        public int UnindexedMultiMatch;
        public int VariableValues;
        /// <summary>Vector-component edits with an operator (<c>@key[1,,] *= x</c>), skipped.</summary>
        public int VectorComponentSkipped;
        /// <summary>Delete lines whose key holds a wildcard (<c>!key* = delete</c>), once per surviving patch line: left to
        /// Redux, which deletes only a value of that exact name, so they delete nothing.</summary>
        public int DeleteWildcards;

        public int ArithmeticApplied => ArithmeticMul + ArithmeticAdd + ArithmeticSub + ArithmeticDiv;
    }

    /// <summary>Applies one value line to a node's values (plan (c)). Written from the semantics in the plan, not from
    /// Redux's code: plain lines give Redux's observed result (edit every match under the patch's spelling, create-or-edit
    /// appends when missing, add appends, delete removes every match; keys match case-insensitively); an explicit index
    /// picks one match (or all); unindexed edits hit every match, or the first with <c>firstMatchDefault</c>; deletes
    /// without an index always hit all. Arithmetic parses both sides as invariant <c>double</c> (a <c>NaN</c> or an
    /// infinity on either side is counted as non-numeric and left as it is) and writes <c>G15</c>.</summary>
    public static class MmValueEditor
    {
        private const NumberStyles Number = NumberStyles.Float;

        /// <summary>True when the line changed the list.</summary>
        public static bool Apply(List<KeyValuePair<string, string>> values, MmKeyEdit edit, bool firstMatchDefault, MmCounters c)
        {
            if (values == null || c == null || string.IsNullOrWhiteSpace(edit.Key)) return false;
            if (edit.SkippedVectorEdit)
            {
                c.VectorComponentSkipped++;
                return false;
            }
            if (edit.CountedOnly)
            {
                if (edit.Operator == MmOperator.Pow) c.NotSupportedPow++;
                else c.NotSupportedRegex++;
                return false;
            }
            switch (edit.Command)
            {
                case MmCommand.Edit:
                case MmCommand.CreateOrEdit:
                    return Edit(values, edit, firstMatchDefault && !edit.PassedBack, c);
                case MmCommand.Delete:
                    return Delete(values, edit, c);
                default:
                    values.Add(new KeyValuePair<string, string>(edit.Key, edit.Value));
                    return true;
            }
        }

        private static int CountMatches(List<KeyValuePair<string, string>> values, string key)
        {
            var n = 0;
            for (var i = 0; i < values.Count; i++)
                if (Matches(values[i], key)) n++;
            return n;
        }

        private static bool Matches(KeyValuePair<string, string> value, string key) =>
            string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase);

        private static bool Edit(List<KeyValuePair<string, string>> values, MmKeyEdit edit, bool firstMatch, MmCounters c)
        {
            var arithmetic = MmKeyEdit.IsSupported(edit.Operator);
            var matches = CountMatches(values, edit.Key);
            if (matches == 0)
            {
                if (arithmetic) { c.MissingKey++; return false; }
                if (edit.Index.Kind == MmIndexKind.N || edit.Index.Kind == MmIndexKind.FromEnd)
                {
                    c.IndexOutOfRange++;
                    return false;
                }
                if (edit.Command != MmCommand.CreateOrEdit) return false;
                values.Add(new KeyValuePair<string, string>(edit.Key, edit.Value));
                return true;
            }
            if (edit.Index.Kind == MmIndexKind.None && matches > 1) c.UnindexedMultiMatch++;
            var pick = MmNodeIndex.Pick(matches, edit.Index, firstMatch);
            if (pick.OutOfRange)
            {
                c.IndexOutOfRange++;
                return false;
            }
            double operand = 0;
            if (arithmetic)
            {
                if (!TryFinite(edit.Value, out operand))
                {
                    c.NonNumeric++;
                    return false;
                }
                if (edit.Operator == MmOperator.Div && operand == 0)
                {
                    c.DivideByZero++;
                    return false;
                }
            }
            var changed = false;
            var ordinal = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (!Matches(values[i], edit.Key)) continue;
                if (pick.Contains(ordinal))
                {
                    if (!arithmetic) values[i] = new KeyValuePair<string, string>(edit.Key, edit.Value);
                    else if (TryFinite(values[i].Value, out var current))
                    {
                        values[i] = new KeyValuePair<string, string>(edit.Key,
                            Compute(current, edit.Operator, operand).ToString("G15", CultureInfo.InvariantCulture));
                        CountApplied(edit.Operator, c);
                    }
                    else
                    {
                        c.NonNumeric++;
                        ordinal++;
                        continue;
                    }
                    changed = true;
                }
                ordinal++;
            }
            if (changed && edit.Index.Kind != MmIndexKind.None) c.IndexValueEdits++;
            return changed;
        }

        private static bool Delete(List<KeyValuePair<string, string>> values, MmKeyEdit edit, MmCounters c)
        {
            var matches = CountMatches(values, edit.Key);
            var pick = MmNodeIndex.Pick(matches, edit.Index, false);
            if (pick.OutOfRange)
            {
                c.IndexOutOfRange++;
                return false;
            }
            if (pick.Count == 0) return false;
            var ordinal = matches - 1;
            for (var i = values.Count - 1; i >= 0; i--)
            {
                if (!Matches(values[i], edit.Key)) continue;
                if (pick.Contains(ordinal)) values.RemoveAt(i);
                ordinal--;
            }
            if (edit.Index.Kind != MmIndexKind.None) c.IndexValueEdits++;
            return true;
        }

        /// <summary>An invariant number that is neither <c>NaN</c> nor an infinity (<c>double.TryParse</c> accepts both).</summary>
        private static bool TryFinite(string text, out double value) =>
            double.TryParse(text, Number, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Compute(double current, MmOperator op, double operand)
        {
            switch (op)
            {
                case MmOperator.Mul: return current * operand;
                case MmOperator.Add: return current + operand;
                case MmOperator.Sub: return current - operand;
                default: return current / operand;
            }
        }

        private static void CountApplied(MmOperator op, MmCounters c)
        {
            switch (op)
            {
                case MmOperator.Mul: c.ArithmeticMul++; break;
                case MmOperator.Add: c.ArithmeticAdd++; break;
                case MmOperator.Sub: c.ArithmeticSub++; break;
                default: c.ArithmeticDiv++; break;
            }
        }

        /// <summary>An MM variable value: starts with <c>#</c> and holds a <c>$</c> (<c>#$x$</c>,
        /// <c>#1.0 $vol$</c>). These stay text under Redux (out of scope).</summary>
        public static bool HasVariable(string value)
        {
            if (value == null) return false;
            var i = 0;
            while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
            return i < value.Length && value[i] == '#' && value.IndexOf('$', i + 1) >= 0;
        }
    }
}
