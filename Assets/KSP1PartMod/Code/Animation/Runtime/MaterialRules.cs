using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>KSP1 ModuleColorChanger's renderer selection, and the KSP1 -> KSP2 shader property choice of
    /// <c>shader_property_map</c>.</summary>
    public static class MaterialRules
    {
        /// <summary>A renderer is kept unless its name is excluded; when an include list is given, it must be in it.
        /// Exclusion wins. Names are compared exactly (ordinal), after trimming the cfg values.</summary>
        public static bool Accepts(string rendererName, string[] included, string[] excluded)
        {
            if (Contains(excluded, rendererName)) return false;
            return !HasAny(included) || Contains(included, rendererName);
        }

        /// <summary>The KSP2 property names to try for a KSP1 one: the map's list, else the KSP1 name itself.</summary>
        public static IReadOnlyList<string> Candidates(string ksp1Property, IReadOnlyDictionary<string, string[]> map)
        {
            if (map != null && ksp1Property != null && map.TryGetValue(ksp1Property, out var names) && names != null && names.Length > 0)
                return names;
            return new[] { ksp1Property };
        }

        /// <summary>The first candidate the material has, or null.</summary>
        public static string Pick(IReadOnlyList<string> candidates, Func<string, bool> materialHas)
        {
            if (candidates == null || materialHas == null) return null;
            foreach (var c in candidates)
                if (!string.IsNullOrEmpty(c) && materialHas(c)) return c;
            return null;
        }

        private static bool HasAny(string[] names)
        {
            if (names == null) return false;
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(n)) return true;
            return false;
        }

        private static bool Contains(string[] names, string name)
        {
            if (names == null || name == null) return false;
            foreach (var n in names)
                if (n != null && string.Equals(n.Trim(), name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
