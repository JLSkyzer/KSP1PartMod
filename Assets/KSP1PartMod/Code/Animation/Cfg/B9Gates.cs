using System;
using System.Collections.Generic;
using System.Globalization;
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
            return VariantIdsOf(subtypes)[DefaultIndex(b9, subtypes)];
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

        // ---- Redux's rule ----

        private sealed class Switch
        {
            public string SetId, RawId, DefaultSubtype;
            public List<IKsp1Node> Subtypes;
            public string[] VariantIds;
        }

        private static List<Switch> Switches(IReadOnlyList<IKsp1Node> modules)
        {
            var result = new List<Switch>();
            if (modules == null) return result;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignored = ShroudTransforms(modules);
            var kept = 0;
            foreach (var m in modules)
            {
                if (m == null || !B9Name.Equals(Trim(m.GetValue("name")), StringComparison.OrdinalIgnoreCase)) continue;
                var subtypes = m.GetNodes("SUBTYPE").ToList();
                var raw = FirstNonBlank(m.GetValue("moduleID"), m.GetValue("switcherDescription"),
                    "B9Switch" + kept.ToString(CultureInfo.InvariantCulture));
                var sw = new Switch
                {
                    RawId = raw, Subtypes = subtypes, VariantIds = VariantIdsOf(subtypes),
                    DefaultSubtype = subtypes.Count == 0 ? null : VariantIdsOf(subtypes)[DefaultIndex(m, subtypes)],
                };
                result.Add(sw);
                if (subtypes.Count == 0 || !subtypes.Any(HasVisualData)) continue;
                // Built: the id is reserved even if the set is then dropped.
                var id = MakeUnique(raw, used);
                if (!subtypes.Any(st => HasTransformer(st, ignored))) continue;
                sw.SetId = id;
                kept++;
            }
            return result;
        }

        private static string[] VariantIdsOf(List<IKsp1Node> subtypes)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new string[subtypes.Count];
            for (var i = 0; i < subtypes.Count; i++)
                ids[i] = MakeUnique(FirstNonBlank(subtypes[i].GetValue("name"), subtypes[i].GetValue("title"),
                    "Subtype" + i.ToString(CultureInfo.InvariantCulture)), used);
            return ids;
        }

        private static int DefaultIndex(IKsp1Node b9, List<IKsp1Node> subtypes)
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

        private static IEnumerable<string> Transforms(IKsp1Node subtype) =>
            subtype.GetValues("transform").Select(Trim).Where(t => !string.IsNullOrEmpty(t));

        private static IEnumerable<IKsp1Node> TextureNodes(IKsp1Node subtype) => subtype.AllNodes.Where(n =>
            n.Name != null && (n.Name.Equals("TEXTURE", StringComparison.OrdinalIgnoreCase)
                               || n.Name.StartsWith("TEXTURE:", StringComparison.OrdinalIgnoreCase)));

        private static bool HasVisualData(IKsp1Node subtype) =>
            Transforms(subtype).Any() || subtype.GetNodes("NODE").Any() || TextureNodes(subtype).Any();

        /// <summary>A transformer Redux would build for the subtype. Its transforms leave out the engine-shroud names
        /// (<paramref name="ignored"/>); the visual-data test above does not, as in Redux.</summary>
        private static bool HasTransformer(IKsp1Node subtype, HashSet<string> ignored) =>
            Transforms(subtype).Any(t => !ignored.Contains(t))
            || subtype.GetNodes("NODE").Any(n => !string.IsNullOrWhiteSpace(n.GetValue("name")) && IsVector3(n.GetValue("position")))
            || TextureNodes(subtype).Any(t =>
                FirstNonBlank(t.GetValue("texture"), t.GetValue("newTexture"), t.GetValue("mainTextureURL")).Length > 0
                || t.AllValues.Any(v => IsShaderTextureKey(v.Key)));

        /// <summary>Redux's ignored transforms (<c>Ksp1EngineShroudBuilder.GetFairingShroudTransformNames</c>, read in
        /// the 26w42c decompile): the comma-separated <c>jettisonName</c> of each ModuleJettison, trimmed, compared
        /// ignoring case, unless <c>ignoreNodes</c> parses as true. (<c>bottomNodeName</c> defaults to <c>bottom</c>, so
        /// its other condition always holds.)</summary>
        private static HashSet<string> ShroudTransforms(IReadOnlyList<IKsp1Node> modules)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in modules)
            {
                if (m == null || !"ModuleJettison".Equals(Trim(m.GetValue("name")), StringComparison.OrdinalIgnoreCase)) continue;
                var names = (m.GetValue("jettisonName") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                if (names.Count == 0) continue;
                if (bool.TryParse(m.GetValue("ignoreNodes"), out var ignoreNodes) && ignoreNodes) continue;
                foreach (var n in names) set.Add(n);
            }
            return set;
        }

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

        private static string MakeUnique(string value, HashSet<string> used)
        {
            var baseId = string.IsNullOrWhiteSpace(value) ? "Variant" : value.Trim();
            var id = baseId;
            for (var n = 2; !used.Add(id); n++) id = baseId + "_" + n.ToString(CultureInfo.InvariantCulture);
            return id;
        }

        // ---- helpers ----

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

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
