using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>A plain, editable KSP1 node: the result of <see cref="B9ModuleEdit.EditedCopy"/> and the off-form DATA
    /// nodes. Lookups by key and node name are ordinal, as in <see cref="IKsp1Node"/> implementations.</summary>
    public sealed class B9Node : IKsp1Node
    {
        public string Name { get; }
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<IKsp1Node> Children = new List<IKsp1Node>();

        public B9Node(string name) { Name = name; }

        public string GetValue(string key) => GetValues(key).FirstOrDefault();
        public IEnumerable<string> GetValues(string key) => Values.Where(v => v.Key == key).Select(v => v.Value);
        public IEnumerable<IKsp1Node> GetNodes(string name) => Children.Where(c => c.Name == name);
        public IEnumerable<KeyValuePair<string, string>> AllValues => Values;
        public IEnumerable<IKsp1Node> AllNodes => Children;

        /// <summary>A deep copy of <paramref name="node"/>; null for null.</summary>
        public static B9Node Copy(IKsp1Node node)
        {
            if (node == null) return null;
            var copy = new B9Node(node.Name);
            copy.Values.AddRange(node.AllValues);
            foreach (var child in node.AllNodes) copy.Children.Add(Copy(child));
            return copy;
        }

        public override string ToString() => Name;
    }

    /// <summary>Number and flag reading shared by the B9 rules.</summary>
    internal static class B9Values
    {
        public static string Trim(string s) => s?.Trim();

        /// <summary>True and a number for a plain invariant number; false for anything else (missing included).</summary>
        public static bool TryNumber(string raw, out double value)
        {
            value = 0;
            return raw != null && double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>The number, null when the value is missing; <paramref name="bad"/> is true when present but not a
        /// number.</summary>
        public static double? Optional(string raw, out bool bad)
        {
            bad = false;
            if (raw == null || raw.Trim().Length == 0) return null;
            if (TryNumber(raw, out var v)) return v;
            bad = true;
            return null;
        }

        public static double? Optional(string raw) => Optional(raw, out _);

        public static bool IsFalse(string raw)
        {
            var v = Trim(raw);
            return v != null && (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase));
        }

        public static string Inv(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        public static void Add(ICollection<string> notes, string note)
        {
            if (notes != null && note != null && !notes.Contains(note)) notes.Add(note);
        }

        public static string FirstNonBlank(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }
    }
}
