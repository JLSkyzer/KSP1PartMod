using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KSP1PartMod.Animation.State
{
    /// <summary>Every entry's saved values in one string, <c>key=value;key=value</c>. Keys are sorted (ordinal) so the
    /// text is stable; '\', '=' and ';' in a key are escaped with '\'. Values are floats in the invariant culture,
    /// round-trip format.</summary>
    public static class EntryStateCodec
    {
        public static string Encode(IReadOnlyDictionary<string, float> values)
        {
            if (values == null || values.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (sb.Length > 0) sb.Append(';');
                foreach (var c in kv.Key ?? "")
                {
                    if (c == '\\' || c == '=' || c == ';') sb.Append('\\');
                    sb.Append(c);
                }
                sb.Append('=').Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Never throws. Keys the caller does not know are returned as they are, for the caller to ignore.
        /// A malformed pair (no '=', empty key, a value that is not a float) is dropped and reported in
        /// <paramref name="error"/>; the others are kept. Empty pairs (";;", a trailing ';') are skipped. error is null
        /// when everything was read; a repeated key keeps its last value.</summary>
        public static IReadOnlyDictionary<string, float> Decode(string s, out string error)
        {
            error = null;
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s)) return result;
            var problems = new List<string>();
            var key = new StringBuilder();
            var value = new StringBuilder();
            var inValue = false;
            var escaped = false;

            void EndPair()
            {
                var raw = inValue ? key + "=" + value : key.ToString();
                if (!inValue)
                {
                    if (key.Length > 0) problems.Add($"'{raw}' has no '='");
                }
                else if (key.Length == 0) problems.Add($"'{raw}' has no key");
                else if (!float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    problems.Add($"'{raw}' value is not a number");
                else result[key.ToString()] = f;
                key.Clear();
                value.Clear();
                inValue = false;
            }

            foreach (var c in s)
            {
                if (escaped)
                {
                    (inValue ? value : key).Append(c);
                    escaped = false;
                }
                else if (c == '\\') escaped = true;
                else if (c == ';') EndPair();
                else if (c == '=' && !inValue) inValue = true;
                else (inValue ? value : key).Append(c);
            }
            if (escaped) problems.Add("text ends with a lone '\\'");
            EndPair();
            if (problems.Count > 0) error = "saved animation state: ignored " + string.Join(", ", problems);
            return result;
        }
    }
}
