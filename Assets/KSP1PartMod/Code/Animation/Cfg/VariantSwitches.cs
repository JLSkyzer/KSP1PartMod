using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>One visual variant set of a KSP1 part as Redux 26w42c's <c>Ksp1B9PartSwitchVariantBuilder</c> sees it: a
    /// <c>ModuleB9PartSwitch</c> (SUBTYPEs) or a stock <c>ModulePartVariants</c> (VARIANTs).</summary>
    public sealed class VariantSwitch
    {
        /// <summary>A stock <c>ModulePartVariants</c>; false for a B9 switch.</summary>
        public bool IsStock;
        /// <summary>The module node.</summary>
        public IKsp1Node Module;
        /// <summary>Redux's V-SwiFT set id (<c>Data_PartSwitch.VariantSets[i].VariantSetId</c>), or null when Redux makes no
        /// set of this switch (no visual effect): its gates then stay at <see cref="DefaultSubtype"/>.</summary>
        public string SetId;
        /// <summary>The id before it was made unique (the set id when none was made).</summary>
        public string RawId;
        /// <summary>The variant id Redux selects by default.</summary>
        public string DefaultSubtype;
        /// <summary>The SUBTYPE (B9) or VARIANT (stock) nodes, in cfg order.</summary>
        public List<IKsp1Node> Subtypes;
        /// <summary>Their variant ids, unique within the set, in the same order.</summary>
        public string[] VariantIds;
    }

    /// <summary>Redux's variant-set ids and variant ids for a part, B9 switches first and then stock
    /// <c>ModulePartVariants</c> modules, with one shared id set (case-insensitive, <c>_2</c>, <c>_3</c> suffixes). Read
    /// from the 26w42c decompilation; no Redux or B9 code is used. <see cref="B9GatePlanner"/> filters the B9 half, so
    /// its results do not change; <see cref="ShroudVariantPlanner"/> uses both.</summary>
    public static class VariantSwitches
    {
        private const string B9Name = "ModuleB9PartSwitch";
        private const string StockName = "ModulePartVariants";

        /// <summary>Every variant set of the part: first the B9 switches in cfg order, then the stock modules in cfg order.
        /// A switch Redux drops stays in the list with a null <see cref="VariantSwitch.SetId"/>. A B9 switch without
        /// visual data and a stock module without VARIANT nodes are Redux-skipped before any id is reserved: a B9 one
        /// stays listed (its variants matter to the gates of B9 overrides), a stock one is left out.</summary>
        public static List<VariantSwitch> All(IReadOnlyList<IKsp1Node> modules)
        {
            var result = new List<VariantSwitch>();
            if (modules == null) return result;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignored = ShroudParser.ConvertibleNames(modules);

            var kept = 0;
            foreach (var m in modules)
            {
                if (m == null || !B9Name.Equals(Trim(m.GetValue("name")), StringComparison.OrdinalIgnoreCase)) continue;
                var subtypes = m.GetNodes("SUBTYPE").ToList();
                var raw = FirstNonBlank(m.GetValue("moduleID"), m.GetValue("switcherDescription"),
                    "B9Switch" + kept.ToString(CultureInfo.InvariantCulture));
                var ids = B9VariantIds(subtypes);
                var sw = new VariantSwitch
                {
                    Module = m, RawId = raw, Subtypes = subtypes, VariantIds = ids,
                    DefaultSubtype = subtypes.Count == 0 ? null : ids[B9DefaultIndex(m, subtypes)],
                };
                result.Add(sw);
                if (subtypes.Count == 0 || !subtypes.Any(HasVisualData)) continue;
                // Built: the id is reserved even if the set is then dropped.
                var id = MakeUnique(raw, used);
                if (!subtypes.Any(st => HasTransformer(st, ignored))) continue;
                sw.SetId = id;
                kept++;
            }

            var stockKept = 0;
            foreach (var m in modules)
            {
                if (m == null || !StockName.Equals(Trim(m.GetValue("name")), StringComparison.OrdinalIgnoreCase)) continue;
                var variants = m.GetNodes("VARIANT").ToList();
                if (variants.Count == 0) continue;
                var raw = FirstNonBlank(m.GetValue("moduleID"), m.GetValue("displayName"), m.GetValue("themeName"),
                    "PartVariant" + stockKept.ToString(CultureInfo.InvariantCulture));
                var id = MakeUnique(raw, used); // reserved before the effects test, as in Redux
                var ids = StockVariantIds(variants);
                var sw = new VariantSwitch
                {
                    IsStock = true, Module = m, RawId = raw, Subtypes = variants, VariantIds = ids,
                    DefaultSubtype = ids[StockDefaultIndex(m, variants)],
                };
                result.Add(sw);
                if (!variants.Any(v => HasStockEffect(v, ignored))) continue;
                sw.SetId = id;
                stockKept++;
            }
            return result;
        }

        // ---- B9 ----

        /// <summary>B9 variant ids: <c>name</c>, else <c>title</c>, else <c>Subtype&lt;i&gt;</c>, made unique within the switch.</summary>
        internal static string[] B9VariantIds(List<IKsp1Node> subtypes) =>
            Ids(subtypes, st => FirstNonBlank(st.GetValue("name"), st.GetValue("title")), "Subtype");

        /// <summary>The default B9 subtype index: <c>currentSubtype</c>, else <c>defaultSubtype</c> (by <c>name</c>, ignoring
        /// case), else the highest <c>defaultSubtypePriority</c> (missing = 0, ties: the first).</summary>
        internal static int B9DefaultIndex(IKsp1Node b9, List<IKsp1Node> subtypes)
        {
            var explicitName = FirstNonBlank(b9.GetValue("currentSubtype"), b9.GetValue("defaultSubtype"));
            if (explicitName.Length > 0)
            {
                var i = subtypes.FindIndex(s => string.Equals(s.GetValue("name"), explicitName, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) return i;
            }
            var best = 0;
            var bestPriority = float.NegativeInfinity;
            for (var i = 0; i < subtypes.Count; i++)
            {
                var raw = subtypes[i].GetValue("defaultSubtypePriority");
                var p = raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                if (p > bestPriority) { best = i; bestPriority = p; }
            }
            return best;
        }

        // ---- stock ----

        /// <summary>Stock variant ids: <c>name</c>, else <c>displayName</c>, else <c>themeName</c>, else
        /// <c>Variant&lt;i&gt;</c>, made unique within the module.</summary>
        private static string[] StockVariantIds(List<IKsp1Node> variants) =>
            Ids(variants, v => FirstNonBlank(v.GetValue("name"), v.GetValue("displayName"), v.GetValue("themeName")), "Variant");

        /// <summary>The default stock variant: <c>baseVariant</c>, else <c>currentVariant</c>, matched by <c>name</c> ignoring
        /// case; the first when none matches.</summary>
        private static int StockDefaultIndex(IKsp1Node module, List<IKsp1Node> variants)
        {
            var explicitName = FirstNonBlank(module.GetValue("baseVariant"), module.GetValue("currentVariant"));
            if (explicitName.Length == 0) return 0;
            var i = variants.FindIndex(v => string.Equals(v.GetValue("name"), explicitName, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : 0;
        }

        /// <summary>The names a stock variant turns on (<c>GAMEOBJECTS</c> entries with a true value: <c>true</c>, <c>1</c>
        /// or <c>yes</c>, ignoring case).</summary>
        internal static IEnumerable<string> StockEnabled(IKsp1Node variant) =>
            StockMentions(variant).Where(p => IsTruthy(p.Value)).Select(p => p.Key);

        /// <summary>Every name a stock variant mentions in a <c>GAMEOBJECTS</c> node, with its raw value.</summary>
        internal static IEnumerable<KeyValuePair<string, string>> StockMentions(IKsp1Node variant)
        {
            foreach (var node in variant.GetNodes("GAMEOBJECTS"))
                foreach (var v in node.AllValues)
                {
                    var name = Trim(v.Key);
                    if (!string.IsNullOrEmpty(name)) yield return new KeyValuePair<string, string>(name, v.Value);
                }
        }

        /// <summary>True when Redux builds a transformer for the stock variant: a non-shroud name turned on, or a texture
        /// switch.</summary>
        private static bool HasStockEffect(IKsp1Node variant, HashSet<string> ignored) =>
            StockEnabled(variant).Any(t => !ignored.Contains(t)) || TextureNodes(variant).Any(HasTextureSwitch);

        // ---- shared rules ----

        private static string[] Ids(List<IKsp1Node> nodes, Func<IKsp1Node, string> name, string fallback)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new string[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                var n = name(nodes[i]);
                ids[i] = MakeUnique(n.Length > 0 ? n : fallback + i.ToString(CultureInfo.InvariantCulture), used);
            }
            return ids;
        }

        private static IEnumerable<string> Transforms(IKsp1Node subtype) =>
            subtype.GetValues("transform").Select(Trim).Where(t => !string.IsNullOrEmpty(t));

        /// <summary>The names a B9 subtype turns on (its <c>transform</c> values).</summary>
        internal static IEnumerable<string> B9Enabled(IKsp1Node subtype) => Transforms(subtype);

        private static IEnumerable<IKsp1Node> TextureNodes(IKsp1Node subtype) => subtype.AllNodes.Where(n =>
            n.Name != null && (n.Name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase)
                               || n.Name.StartsWith("TEXTURE:", StringComparison.OrdinalIgnoreCase)));

        private static bool HasVisualData(IKsp1Node subtype) =>
            Transforms(subtype).Any() || subtype.GetNodes("NODE").Any() || TextureNodes(subtype).Any();

        private static bool HasTextureSwitch(IKsp1Node t) =>
            FirstNonBlank(t.GetValue("texture"), t.GetValue("newTexture"), t.GetValue("mainTextureURL")).Length > 0
            || t.AllValues.Any(v => IsShaderTextureKey(v.Key));

        /// <summary>A transformer Redux would build for the subtype. Its transforms leave out the engine-shroud names
        /// (<paramref name="ignored"/>); the visual-data test above does not, as in Redux.</summary>
        private static bool HasTransformer(IKsp1Node subtype, HashSet<string> ignored) =>
            Transforms(subtype).Any(t => !ignored.Contains(t))
            || subtype.GetNodes("NODE").Any(n => !string.IsNullOrWhiteSpace(n.GetValue("name")) && IsVector3(n.GetValue("position")))
            || TextureNodes(subtype).Any(HasTextureSwitch);

        private static bool IsVector3(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var parts = raw.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 && parts.Take(3).All(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        }

        private static bool IsShaderTextureKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("_", StringComparison.Ordinal)) return false;
            return name.EndsWith("Tex", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Map", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("_Emissive", StringComparison.OrdinalIgnoreCase) || name.Equals("_Emission", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Redux's <c>IsTruthy</c>: <c>true</c>, <c>1</c> or <c>yes</c>, ignoring case.</summary>
        internal static bool IsTruthy(string raw) =>
            raw != null && (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase));

        private static string MakeUnique(string value, HashSet<string> used)
        {
            var baseId = string.IsNullOrWhiteSpace(value) ? "Variant" : value.Trim();
            var id = baseId;
            for (var n = 2; !used.Add(id); n++) id = baseId + "_" + n.ToString(CultureInfo.InvariantCulture);
            return id;
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static string Trim(string s) => s?.Trim();
    }
}
