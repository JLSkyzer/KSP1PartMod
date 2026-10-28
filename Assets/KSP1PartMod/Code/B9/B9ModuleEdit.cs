using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>A B9 subtype's <c>MODULE { DATA { … } }</c> written over a KSP1 module node. The wiki says only that DATA
    /// is "data loaded into the target module"; the rule here is <b>inferred</b> (S) as a partial <c>OnLoad</c>: each DATA
    /// value replaces every value of that key (at the first one's place; a new key is appended), and the DATA child nodes
    /// of a name replace <b>all</b> child nodes of that name (an <c>atmosphereCurve</c> replaces the curve, two
    /// <c>PROPELLANT</c> nodes replace the list).</summary>
    public static class B9ModuleEdit
    {
        /// <summary>A deep copy of <paramref name="module"/> with <paramref name="data"/> written over it; the inputs are
        /// never changed. Null data gives a plain copy.</summary>
        public static B9Node EditedCopy(IKsp1Node module, IKsp1Node data)
        {
            var copy = B9Node.Copy(module) ?? new B9Node("MODULE");
            if (data == null) return copy;

            foreach (var group in data.AllValues.Where(v => v.Key != null).GroupBy(v => v.Key.Trim(), StringComparer.Ordinal))
            {
                var key = group.Key;
                var first = copy.Values.FindIndex(v => v.Key?.Trim() == key);
                copy.Values.RemoveAll(v => v.Key?.Trim() == key);
                var items = group.Select(v => new KeyValuePair<string, string>(key, v.Value)).ToList();
                if (first < 0) copy.Values.AddRange(items);
                else copy.Values.InsertRange(Math.Min(first, copy.Values.Count), items);
            }

            foreach (var group in data.AllNodes.Where(n => n?.Name != null).GroupBy(n => n.Name, StringComparer.Ordinal))
            {
                var first = copy.Children.FindIndex(c => c.Name == group.Key);
                copy.Children.RemoveAll(c => c.Name == group.Key);
                var items = group.Select(n => (IKsp1Node)B9Node.Copy(n)).ToList();
                if (first < 0) copy.Children.AddRange(items);
                else copy.Children.InsertRange(Math.Min(first, copy.Children.Count), items);
            }
            return copy;
        }

        /// <summary>The DATA keys and node names, in order, distinct (node names with <c>{}</c> are not marked: the log
        /// lines name them as written).</summary>
        public static List<string> Keys(IKsp1Node data)
        {
            var keys = new List<string>();
            if (data == null) return keys;
            foreach (var v in data.AllValues)
            {
                var k = v.Key?.Trim();
                if (!string.IsNullOrEmpty(k) && !keys.Contains(k)) keys.Add(k);
            }
            foreach (var n in data.AllNodes)
                if (n?.Name != null && !keys.Contains(n.Name)) keys.Add(n.Name);
            return keys;
        }
    }

    /// <summary>The native "off" form of a KSP1 module that a B9 subtype turns off (<c>moduleActive = false</c>): DATA
    /// values written over the module and re-imported by Redux, for the kinds KSP2 can show as off. A curated table
    /// (plan (d)); any other module has none.</summary>
    public static class B9OffForms
    {
        private static readonly Dictionary<string, KeyValuePair<string, string>[]> Table =
            new Dictionary<string, KeyValuePair<string, string>[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleGimbal"] = Zero("gimbalRange", "gimbalRangeXP", "gimbalRangeXN", "gimbalRangeYP", "gimbalRangeYN"),
                ["ModuleRCS"] = Zero("thrusterPower"),
                ["ModuleRCSFX"] = Zero("thrusterPower"),
                ["ModuleDecouple"] = new[] { new KeyValuePair<string, string>("staged", "false") },
                ["ModuleAnchoredDecoupler"] = new[] { new KeyValuePair<string, string>("staged", "false") },
                ["ModuleEngines"] = Zero("maxThrust"),
                ["ModuleEnginesFX"] = Zero("maxThrust"),
            };

        private static KeyValuePair<string, string>[] Zero(params string[] keys) =>
            keys.Select(k => new KeyValuePair<string, string>(k, "0")).ToArray();

        /// <summary>The off form's DATA values, or null when the module has no native off form.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> For(string moduleName)
        {
            var n = B9Values.Trim(moduleName);
            return n != null && Table.TryGetValue(n, out var values) ? values : null;
        }

        /// <summary>The off form as a DATA node for <see cref="B9ModuleEdit.EditedCopy"/>, or null.</summary>
        public static B9Node DataNode(string moduleName)
        {
            var values = For(moduleName);
            if (values == null) return null;
            var node = new B9Node("DATA");
            node.Values.AddRange(values);
            return node;
        }
    }
}
