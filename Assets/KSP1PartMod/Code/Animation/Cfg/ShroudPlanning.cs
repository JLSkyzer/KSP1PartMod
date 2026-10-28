using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Which shroud is shown in which variant (0.7.0 engine shrouds). KSP1 shows the first <c>jettisonName</c> whose
    /// GameObject the active variant leaves active. Redux leaves the shroud names out of its variants, so the choice is
    /// made here from the cfg: a name a variant set mentions is off unless the variant turns it on (stock
    /// <c>GAMEOBJECTS { name = true }</c>, B9 <c>transform = name</c>), the rule Redux applies to every other transform.
    /// A name no set mentions is always enabled.</summary>
    public static class ShroudVariantPlanner
    {
        /// <summary>For each name, one <see cref="B9Gate"/> per variant set that mentions it, with
        /// <see cref="B9Gate.InactiveIn"/> the variants that do not turn it on. The result is parallel to
        /// <paramref name="names"/>; a name no set mentions has an empty array. A set Redux dropped keeps its gate with a
        /// null <see cref="B9Gate.SetId"/>, which stays at the default variant.</summary>
        public static B9Gate[][] Gates(IReadOnlyList<IKsp1Node> modules, IReadOnlyList<string> names)
        {
            if (names == null) return new B9Gate[0][];
            var sets = VariantSwitches.All(modules);
            var result = new B9Gate[names.Count][];
            for (var i = 0; i < names.Count; i++)
            {
                var gates = new List<B9Gate>();
                var name = names[i]?.Trim();
                if (!string.IsNullOrEmpty(name))
                    foreach (var set in sets)
                    {
                        var mentioned = false;
                        var inactive = new List<string>();
                        for (var v = 0; v < set.Subtypes.Count; v++)
                        {
                            var state = State(set, set.Subtypes[v], name);
                            if (state != Mention.None) mentioned = true;
                            if (state != Mention.On) inactive.Add(set.VariantIds[v]);
                        }
                        if (!mentioned) continue;
                        gates.Add(new B9Gate
                        {
                            SetId = set.SetId, InactiveIn = inactive.ToArray(), DefaultSubtype = set.DefaultSubtype,
                            SubtypeIds = set.VariantIds.ToArray(),
                        });
                    }
                result[i] = gates.ToArray();
            }
            return result;
        }

        private enum Mention { None, Off, On }

        private static Mention State(VariantSwitch set, IKsp1Node variant, string name)
        {
            if (set.IsStock)
            {
                var found = Mention.None;
                foreach (var p in VariantSwitches.StockMentions(variant))
                {
                    if (!p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (VariantSwitches.IsTruthy(p.Value)) return Mention.On;
                    found = Mention.Off;
                }
                return found;
            }
            return VariantSwitches.B9Enabled(variant).Any(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)) ? Mention.On : Mention.None;
        }
    }

    /// <summary>Who owns a transform when two modules name it (0.7.0). The shroud entry owns its meshes: a Bluedog_DB mesh
    /// hider that names the same transform loses that name.</summary>
    public static class ShroudOwnership
    {
        /// <summary>Drops the names of the <see cref="EntryKind.Shroud"/> entries (compared ignoring case) from every
        /// <see cref="EntryKind.MeshHider"/>'s <see cref="AnimEntryDef.TransformNames"/>, removes a hider left with no name, and
        /// returns one note per dropped name.</summary>
        public static IReadOnlyList<string> TrimMeshHiders(List<AnimEntryDef> entries)
        {
            var notes = new List<string>();
            if (entries == null) return notes;
            var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in entries.Where(e => e != null && e.Kind == EntryKind.Shroud && e.TransformNames != null))
                foreach (var n in s.TransformNames)
                    if (!owners.ContainsKey(n)) owners[n] = s.ModuleIndex;
            if (owners.Count == 0) return notes;
            foreach (var h in entries.Where(e => e != null && e.Kind == EntryKind.MeshHider && e.TransformNames != null).ToList())
            {
                var kept = new List<string>();
                foreach (var n in h.TransformNames)
                {
                    if (owners.TryGetValue(n, out var owner))
                        notes.Add($"MODULE[{h.ModuleIndex}] ModuleBDBDeployableMeshHider: transform '{n}' is the shroud of MODULE[{owner}] ModuleJettison, left to the shroud entry");
                    else kept.Add(n);
                }
                if (kept.Count == h.TransformNames.Length) continue;
                h.TransformNames = kept.ToArray();
                if (kept.Count == 0) entries.Remove(h);
            }
            return notes;
        }
    }

    /// <summary>Which transforms of a KSP1 prefab the import hides (0.7.0): the outermost transform of each shroud name.</summary>
    public static class ShroudImportRule
    {
        /// <summary>From the transform paths of a prefab (<c>/</c>-separated, each transform once), the paths whose last
        /// segment is one of <paramref name="convertibleNames"/> (whole segment, ignoring case) and that have no matching
        /// segment above them: a shroud inside another shroud is hidden with it. Input order, each path once.</summary>
        public static IReadOnlyList<string> RootsToHide(IEnumerable<string> convertibleNames, IEnumerable<string> pathsOfMatches)
        {
            var roots = new List<string>();
            if (convertibleNames == null || pathsOfMatches == null) return roots;
            var names = new HashSet<string>(convertibleNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return roots;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in pathsOfMatches)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var segments = path.Split('/');
                if (!names.Contains(segments[segments.Length - 1])) continue;
                var under = false;
                for (var i = 0; i < segments.Length - 1 && !under; i++) under = names.Contains(segments[i]);
                if (!under && seen.Add(path)) roots.Add(path);
            }
            return roots;
        }
    }
}
