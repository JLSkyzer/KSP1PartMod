using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>One B9PartSwitch switch that turns an entry's module off in some subtypes. Plain fields, serialized
    /// with the entry.</summary>
    public sealed class B9Gate
    {
        /// <summary>Redux's V-SwiFT set id (<c>Data_PartSwitch.VariantSets[i].VariantSetId</c>), or null when Redux
        /// makes no set of this switch (no visual data): the gate then stays at <see cref="DefaultSubtype"/>.</summary>
        public string SetId;
        /// <summary>The variant ids in which the module is inactive.</summary>
        public string[] InactiveIn;
        /// <summary>The variant id Redux selects by default.</summary>
        public string DefaultSubtype;
        /// <summary>Every variant id of the switch, in SUBTYPE order.</summary>
        public string[] SubtypeIds;
    }

    /// <summary>Spec 2b.1 and "Variant application and plugin inputs (settled 2026-10-28)": turns the
    /// <c>SUBTYPE { MODULE { IDENTIFIER { … } moduleActive = false } }</c> overrides of a part's ModuleB9PartSwitch
    /// modules into <see cref="B9Gate"/>s on our entries. Set ids, variant ids and the default subtype follow Redux
    /// 26w42c's B9 variant builder; IDENTIFIER matching follows B9PartSwitch's documented rule (same <c>name</c>, every
    /// other IDENTIFIER value equal to the module's). No B9 code is used.</summary>
    public static class B9GatePlanner
    {
        private const string B9Name = "ModuleB9PartSwitch";

        /// <summary>Applies the B9 gates to <paramref name="entries"/> (replaces <see cref="AnimEntryDef.Gates"/>, so a
        /// second call gives the same result) and returns one note per override left alone for a reason worth logging.
        /// Overrides on modules that are neither ours nor Redux's are skipped silently.</summary>
        /// <param name="reduxOwnedModuleIndices">MODULE indices whose animation stays with Redux: not gated.</param>
        public static IReadOnlyList<string> Apply(IReadOnlyList<IKsp1Node> modules, IReadOnlyList<AnimEntryDef> entries,
            ISet<int> reduxOwnedModuleIndices)
        {
            var notes = new List<string>();
            if (modules == null || entries == null || entries.Count == 0) return notes;
            var redux = reduxOwnedModuleIndices ?? new HashSet<int>();
            var ours = new HashSet<int>(entries.Where(e => e != null).Select(e => e.ModuleIndex));
            foreach (var e in entries)
                if (e != null) e.Gates = null;
            var switches = Switches(modules);
            foreach (var sw in switches)
            {
                var label = sw.SetId ?? sw.RawId;
                // module index -> variant ids in which it is inactive (in subtype order)
                var inactive = new Dictionary<int, List<string>>();
                for (var s = 0; s < sw.Subtypes.Count; s++)
                {
                    var variant = sw.VariantIds[s];
                    foreach (var o in sw.Subtypes[s].GetNodes("MODULE"))
                    {
                        var identifier = o.GetNodes("IDENTIFIER").FirstOrDefault();
                        if (identifier == null) continue;
                        var off = IsFalse(o.GetValue("moduleActive"));
                        var data = o.GetNodes("DATA").Any();
                        if (!off && !data) continue;
                        var name = Trim(identifier.GetValue("name"));
                        // Only overrides that could reach a module of ours or Redux's are worth a note.
                        var concerned = Enumerable.Range(0, modules.Count)
                            .Where(i => SameName(modules[i], name))
                            .Any(i => ours.Contains(i) || redux.Contains(i));
                        if (!concerned) continue;
                        var prefix = $"B9 '{label}' subtype '{variant}': ";
                        var matches = Match(modules, identifier);
                        if (matches.Count != 1)
                        {
                            notes.Add(prefix + $"IDENTIFIER matches {matches.Count} module(s), ignored");
                            continue;
                        }
                        var index = matches[0];
                        var module = $"MODULE[{index}] {Trim(modules[index].GetValue("name"))}";
                        if (redux.Contains(index))
                        {
                            if (off) notes.Add(prefix + module + " left to Redux, not gated");
                            continue;
                        }
                        if (!ours.Contains(index)) continue;
                        if (data) notes.Add(prefix + module + " DATA ignored");
                        if (!off) continue;
                        if (!inactive.TryGetValue(index, out var list)) inactive[index] = list = new List<string>();
                        if (!list.Contains(variant, StringComparer.Ordinal)) list.Add(variant);
                    }
                }

                foreach (var pair in inactive)
                    foreach (var e in entries.Where(e => e != null && e.ModuleIndex == pair.Key))
                    {
                        var gate = new B9Gate
                        {
                            SetId = sw.SetId, InactiveIn = pair.Value.ToArray(), DefaultSubtype = sw.DefaultSubtype,
                            SubtypeIds = sw.VariantIds.ToArray(),
                        };
                        e.Gates = e.Gates == null ? new[] { gate } : e.Gates.Concat(new[] { gate }).ToArray();
                    }
            }
            return notes;
        }

        /// <summary>True when the gates keep <paramref name="entries"/> apart: one switch gates every one of them, and in
        /// each of its subtypes at most one is active. Fewer than two entries are always apart; an ungated entry is
        /// active everywhere.</summary>
        public static bool GatedApart(IReadOnlyList<AnimEntryDef> entries)
        {
            if (entries == null || entries.Count < 2) return true;
            if (entries.Any(e => e?.Gates == null)) return false;
            foreach (var gate in entries[0].Gates)
            {
                if (gate?.SubtypeIds == null) continue;
                var same = entries.Select(e => e.Gates.FirstOrDefault(g => SameSwitch(g, gate))).ToList();
                if (same.Any(g => g == null)) continue;
                if (gate.SubtypeIds.All(v => same.Count(g => g.InactiveIn == null || !g.InactiveIn.Contains(v, StringComparer.Ordinal)) <= 1))
                    return true;
            }
            return false;
        }

        private static bool SameSwitch(B9Gate a, B9Gate b) =>
            a != null && b != null && string.Equals(a.SetId, b.SetId, StringComparison.Ordinal)
            && a.SubtypeIds != null && b.SubtypeIds != null && a.SubtypeIds.SequenceEqual(b.SubtypeIds, StringComparer.Ordinal);

        /// <summary>Redux's V-SwiFT set id of each ModuleB9PartSwitch of the part, in cfg order; null for a switch Redux
        /// makes no set of.</summary>
        public static IReadOnlyList<string> SetIds(IReadOnlyList<IKsp1Node> modules) =>
            Switches(modules).Select(s => s.SetId).ToList();

        /// <summary>The variant ids of each ModuleB9PartSwitch of the part, in cfg order: <c>name</c>, else
        /// <c>title</c>, else <c>Subtype&lt;i&gt;</c>, made unique within the switch.</summary>
        public static IReadOnlyList<string[]> VariantIds(IReadOnlyList<IKsp1Node> modules) =>
            Switches(modules).Select(s => s.VariantIds).ToList();

        /// <summary>The variant id Redux selects by default: <c>currentSubtype</c>, else <c>defaultSubtype</c> (by
        /// <c>name</c>, ignoring case), else the highest <c>defaultSubtypePriority</c> (missing = 0, ties: the first).
        /// Null when the module has no SUBTYPE.</summary>
        public static string DefaultSubtype(IKsp1Node b9)
        {
            if (b9 == null) return null;
            var subtypes = b9.GetNodes("SUBTYPE").ToList();
            if (subtypes.Count == 0) return null;
            return VariantSwitches.B9VariantIds(subtypes)[VariantSwitches.B9DefaultIndex(b9, subtypes)];
        }

        /// <summary>The indices of the modules an IDENTIFIER matches: same <c>name</c> (trimmed, ignoring case), and
        /// every other value of the IDENTIFIER equal (trimmed, ordinal) to the module's value of that key.</summary>
        public static IReadOnlyList<int> Match(IReadOnlyList<IKsp1Node> modules, IKsp1Node identifier)
        {
            var result = new List<int>();
            if (modules == null || identifier == null) return result;
            var name = Trim(identifier.GetValue("name"));
            if (name == null) return result;
            var others = identifier.AllValues.Where(v => !"name".Equals(v.Key?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            for (var i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (m == null || !SameName(m, name)) continue;
                if (others.All(v => string.Equals(Trim(m.GetValue(v.Key?.Trim())), Trim(v.Value), StringComparison.Ordinal)))
                    result.Add(i);
            }
            return result;
        }

        // ---- Redux's rule: see VariantSwitches ----

        /// <summary>The ModuleB9PartSwitch half of <see cref="VariantSwitches.All"/> (B9 sets are listed first, so their ids
        /// and positions do not depend on the stock modules).</summary>
        private static List<VariantSwitch> Switches(IReadOnlyList<IKsp1Node> modules) =>
            VariantSwitches.All(modules).Where(s => !s.IsStock).ToList();

        // ---- helpers ----

        private static string Trim(string s) => s?.Trim();

        private static bool SameName(IKsp1Node module, string name) =>
            module != null && name != null && name.Equals(Trim(module.GetValue("name")), StringComparison.OrdinalIgnoreCase);

        private static bool IsFalse(string raw)
        {
            var v = Trim(raw);
            return v != null && (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase));
        }
    }
}
