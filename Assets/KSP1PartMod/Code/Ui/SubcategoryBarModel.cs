using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Placement;

namespace KSP1PartMod.Ui
{
    /// <summary>What the bar shows for one KSP1 mod category: the types of the parts the stock filters keep (tech
    /// level, vessel type, hidden parts), so no type opens on an empty grid.</summary>
    public sealed class SubcategoryBarModel
    {
        public struct TypeCount
        {
            public Ksp1Subcategory Type;
            public int Count;
        }

        private readonly int[] _counts;

        /// <summary>Types with at least one visible part, in the KSP1 editor order.</summary>
        public IReadOnlyList<TypeCount> Types { get; }
        public int Total { get; }
        /// <summary>The bar is worth showing only with two types or more.</summary>
        public bool Show => Types.Count >= 2;
        /// <summary>"Pods 1, Engine 2": equal for equal counts.</summary>
        public string Signature { get; }

        private SubcategoryBarModel(int[] counts)
        {
            _counts = counts;
            Types = Ksp1Subcategories.Ordered.Where(t => counts[(int)t] > 0)
                .Select(t => new TypeCount { Type = t, Count = counts[(int)t] }).ToList();
            Total = counts.Sum();
            Signature = string.Join(", ", Types.Select(t => t.Type + " " + t.Count));
        }

        /// <param name="visibleParts">The type of each part the stock filters keep; null gives an empty model.</param>
        public static SubcategoryBarModel Build(IEnumerable<Ksp1Subcategory> visibleParts)
        {
            var counts = new int[Ksp1Subcategories.Ordered.Count];
            if (visibleParts != null)
                foreach (var sub in visibleParts) counts[(int)sub]++;
            return new SubcategoryBarModel(counts);
        }

        public int CountOf(Ksp1Subcategory sub) => _counts[(int)sub];
        public bool Contains(Ksp1Subcategory sub) => _counts[(int)sub] > 0;
    }

    /// <summary>The type chosen in each KSP1 mod category, for the session. Keyed by the mod's display name (Redux
    /// renumbers its category values when the import runs again), case-insensitive like Redux's registry. null means
    /// "All".</summary>
    public sealed class SubcategorySelection
    {
        private readonly Dictionary<string, Ksp1Subcategory> _byMod = new Dictionary<string, Ksp1Subcategory>(StringComparer.OrdinalIgnoreCase);

        public Ksp1Subcategory? Get(string mod) =>
            mod != null && _byMod.TryGetValue(mod, out var sub) ? sub : (Ksp1Subcategory?)null;

        public void Set(string mod, Ksp1Subcategory? sub)
        {
            if (mod == null) return;
            if (sub.HasValue) _byMod[mod] = sub.Value;
            else _byMod.Remove(mod);
        }

        /// <summary>The remembered type when the bar is shown and that type still has a visible part, else All. The
        /// memory is kept either way, so the type comes back when its parts do.</summary>
        public Ksp1Subcategory? Effective(string mod, SubcategoryBarModel model)
        {
            var sub = Get(mod);
            return sub.HasValue && model != null && model.Show && model.Contains(sub.Value) ? sub : null;
        }

        public static bool Keep(Ksp1Subcategory partType, Ksp1Subcategory? selected) =>
            !selected.HasValue || partType == selected.Value;

        /// <summary>The items of the selected type, in their order; all of them for All.</summary>
        public static List<T> Filter<T>(IEnumerable<T> items, Func<T, Ksp1Subcategory> typeOf, Ksp1Subcategory? selected) =>
            items.Where(i => Keep(typeOf(i), selected)).ToList();
    }

    /// <summary>Part name to type, built from Redux's imported part registry.</summary>
    public sealed class SubcategoryIndex
    {
        private readonly Dictionary<string, Ksp1Subcategory> _byPart;

        public int Count => _byPart.Count;
        /// <summary>Types that hold at least one imported part, in the editor order.</summary>
        public IReadOnlyList<Ksp1Subcategory> TypesPresent { get; }

        private SubcategoryIndex(Dictionary<string, Ksp1Subcategory> byPart)
        {
            _byPart = byPart;
            var present = new HashSet<Ksp1Subcategory>(byPart.Values);
            TypesPresent = Ksp1Subcategories.Ordered.Where(present.Contains).ToList();
        }

        /// <summary>Entries without a name are skipped; the first entry of a name wins.</summary>
        public static SubcategoryIndex Build(IEnumerable<Ksp1PartInfo> parts)
        {
            var map = new Dictionary<string, Ksp1Subcategory>(StringComparer.Ordinal);
            if (parts != null)
                foreach (var p in parts)
                    if (p?.PartName != null && !map.ContainsKey(p.PartName))
                        map[p.PartName] = Ksp1Subcategories.Normalize(p.Category);
            return new SubcategoryIndex(map);
        }

        /// <summary>The indexed type, else the one read from Redux's tags.</summary>
        public Ksp1Subcategory Get(string partName, string tags) =>
            partName != null && _byPart.TryGetValue(partName, out var sub) ? sub : Ksp1Subcategories.FromTags(tags);
    }

    /// <summary>The <c>[KSP1PartMod] ui:</c> log lines.</summary>
    public static class SubcategoryLog
    {
        private const string Prefix = "[KSP1PartMod] ui: ";

        public static string TypesLine(string mod, SubcategoryBarModel model) =>
            $"{Prefix}{mod}: {model.Total} visible part(s) in {model.Types.Count} type(s): {model.Signature}; " +
            (model.Show ? "bar shown" : "bar hidden (fewer than 2 types)");

        public static string FilterLine(string mod, Ksp1Subcategory? selected, SubcategoryBarModel model) =>
            $"{Prefix}{mod}: filter {(selected.HasValue ? selected.Value.ToString() : "All")} keeps " +
            $"{(selected.HasValue ? model.CountOf(selected.Value) : model.Total)} of {model.Total} part(s)";

        public static string IndexLine(SubcategoryIndex index) =>
            $"{Prefix}indexed {index.Count} KSP1 part(s) in {index.TypesPresent.Count} type(s): {string.Join(", ", index.TypesPresent)}";
    }
}
