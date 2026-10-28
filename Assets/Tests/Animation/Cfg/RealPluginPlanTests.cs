using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>The planner with the phase 2b plugin modules and B9 gates on every plain <c>PART</c> node under the
    /// game's <c>mods/KSP1</c> (cfgs before ModuleManager patches).</summary>
    public class RealPluginPlanTests
    {
        private sealed class RealPart
        {
            public string Mod, File, Name;
            public IReadOnlyList<IKsp1Node> Modules;
            public override string ToString() => $"{Mod}/{Name} ({File})";
        }

        private static List<RealPart> _parts;

        private static List<RealPart> AllParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<RealPart>();
            foreach (var path in InstalledKsp1.AllCfg())
                foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
                    parts.Add(new RealPart
                    {
                        Mod = InstalledKsp1.TopFolder(path),
                        File = path.Substring(InstalledKsp1.Root.TrimEnd('\\', '/').Length + 1),
                        Name = part.GetValue("name"),
                        Modules = part.GetNodes("MODULE").ToList(),
                    });
            Assume.That(parts.Count, Is.GreaterThan(0), "no PART node found");
            return _parts = parts;
        }

        private static bool HasNativeSolar(RealPart p) =>
            p.Modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));

        private static PartAnimationPlan PlanOf(RealPart p, bool own = false, bool follow = false, bool plugins = true, bool b9 = true) =>
            OwnershipPlanner.Plan(p.Modules, HasNativeSolar(p), new HashSet<string>(), own, follow, null, plugins, b9);

        private static readonly (bool own, bool follow)[] Switches = { (false, false), (false, true), (true, false), (true, true) };

        private static IEnumerable<string> ClipsOf(AnimEntryDef e) =>
            new[] { e.Clip, e.SecondClip, e.ThirdClip }.Where(c => !string.IsNullOrEmpty(c));

        /// <summary>The clips our entries play: GearBridge (Redux plays the clip) and AeroGate (no clip time) own none.</summary>
        private static IEnumerable<(string clip, AnimEntryDef entry)> OurClips(PartAnimationPlan plan) =>
            plan.Entries.Where(e => e.Kind != EntryKind.GearBridge && e.Kind != EntryKind.AeroGate)
                .SelectMany(e => ClipsOf(e).Distinct(StringComparer.OrdinalIgnoreCase).Select(c => (c, e)));

        /// <summary>Written apart from the planner's own rule: some switch gates every entry of the group, and in each of
        /// its subtypes at most one entry is active.</summary>
        private static bool AllGatedApart(IReadOnlyList<AnimEntryDef> group)
        {
            if (group.Any(e => e.Gates == null)) return false;
            foreach (var gate in group[0].Gates)
            {
                var same = group.Select(e => e.Gates.FirstOrDefault(g => g.SetId == gate.SetId
                                                                         && g.SubtypeIds.SequenceEqual(gate.SubtypeIds))).ToList();
                if (same.Any(g => g == null)) continue;
                if (gate.SubtypeIds.All(v => same.Count(g => !g.InactiveIn.Contains(v)) <= 1)) return true;
            }
            return false;
        }

        [Test] public void Plan_NoEntryNamesAReduxClip_AllRealParts()
        {
            var bad = new List<string>();
            foreach (var (own, follow) in Switches)
                bad.AddRange(AllParts().Where(p => PlanOf(p, own, follow).Entries
                        .Where(e => e.Kind != EntryKind.GearBridge && e.Kind != EntryKind.AeroGate
                                    && e.Kind != EntryKind.ReduxFollower && e.Kind != EntryKind.ChuteFollower)
                        .SelectMany(ClipsOf).Intersect(PlanOf(p, own, follow).ReduxClips, StringComparer.OrdinalIgnoreCase).Any())
                    .Select(p => $"own={own} follow={follow} {p}"));
            CollectionAssert.IsEmpty(bad);
        }

        [Test] public void Plan_NoClipOwnedByTwoOfOurEntries_AllRealParts()
        {
            var bad = new List<string>();
            foreach (var (own, follow) in Switches)
                foreach (var p in AllParts())
                {
                    var groups = OurClips(PlanOf(p, own, follow)).GroupBy(x => x.clip, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1 && !AllGatedApart(g.Select(x => x.entry).ToList()));
                    bad.AddRange(groups.Select(g => $"own={own} follow={follow} {p}: '{g.Key}' x{g.Count()}"));
                }
            CollectionAssert.IsEmpty(bad);
        }

        [Test] public void Plan_EntryKeysUniquePerPart_AllRealParts()
        {
            var bad = AllParts().Where(p => Switches.Any(s => PlanOf(p, s.own, s.follow).Entries.GroupBy(e => e.Key).Any(g => g.Count() > 1)))
                .Select(p => p.ToString()).ToList();
            CollectionAssert.IsEmpty(bad);
        }

        /// <summary><c>plugin_modules</c> and <c>b9_module_gates</c> off: the plan of 0.4.0, entry for entry.</summary>
        [Test] public void Plan_PluginModulesAndGatesOff_SameAsPhase1_AllRealParts()
        {
            var bad = AllParts().Where(p => !OwnershipPlanner.Plan(p.Modules, HasNativeSolar(p), new HashSet<string>(), false, true)
                    .Entries.Select(e => e.Key)
                    .SequenceEqual(PlanOf(p, false, true, plugins: false, b9: false).Entries.Select(e => e.Key)))
                .Select(p => p.ToString()).ToList();
            CollectionAssert.IsEmpty(bad);
        }

        /// <summary>Every plugin entry the parser makes is planned, unless a note says why not.</summary>
        [Test] public void Plan_EveryParsedPluginModuleIsPlannedOrNoted_AllRealParts()
        {
            var bad = new List<string>();
            foreach (var p in AllParts())
            {
                var plan = PlanOf(p);
                for (var i = 0; i < p.Modules.Count; i++)
                {
                    if (!PluginEntryParser.Handles(p.Modules[i].GetValue("name")) || PluginEntryParser.Parse(p.Modules[i], i, 0) == null) continue;
                    if (plan.Entries.Any(e => e.ModuleIndex == i)) continue;
                    if (plan.Notes.Any(n => n.StartsWith($"MODULE[{i}] ", StringComparison.Ordinal))) continue;
                    bad.Add($"{p} MODULE[{i}] {p.Modules[i].GetValue("name")}");
                }
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test] public void Plan_RealParts_PluginCountsPerKind() => TestContext.WriteLine(CountsPerKindAndMod());

        private static string CountsPerKindAndMod()
        {
            var kinds = ((EntryKind[])Enum.GetValues(typeof(EntryKind))).Where(k => k > EntryKind.ChuteFollower).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("| Mod | Parts | With plugin entries | " + string.Join(" | ", kinds) + " | Gated entries |");
            sb.AppendLine("|---|---|---|" + string.Concat(kinds.Select(_ => "---|")) + "---|");
            var totals = new int[kinds.Length];
            foreach (var mod in AllParts().GroupBy(p => p.Mod, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            {
                var plans = mod.Select(p => PlanOf(p)).ToList();
                var counts = kinds.Select(k => plans.Sum(pl => pl.Entries.Count(e => e.Kind == k))).ToArray();
                for (var i = 0; i < counts.Length; i++) totals[i] += counts[i];
                sb.AppendLine($"| {mod.Key} | {plans.Count} | {plans.Count(pl => pl.Entries.Any(e => e.Kind > EntryKind.ChuteFollower))} | " +
                              string.Join(" | ", counts) + $" | {plans.Sum(pl => pl.Entries.Count(e => e.Gates != null))} |");
            }
            sb.AppendLine("| total | | | " + string.Join(" | ", totals) + " | |");
            sb.AppendLine("Parsed plugin modules per name (before ownership):");
            foreach (var g in AllParts().SelectMany(p => p.Modules.Select((m, i) => (m, i)))
                         .Where(x => PluginEntryParser.Handles(x.m.GetValue("name")))
                         .GroupBy(x => x.m.GetValue("name").Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
                sb.AppendLine($"  {g.Key}: {g.Count()} module(s), {g.Count(x => PluginEntryParser.Parse(x.m, x.i, 0) != null)} parsed");
            sb.AppendLine("Notes about plugin modules and B9:");
            foreach (var g in AllParts().SelectMany(p => PlanOf(p).Notes.Where(IsPluginNote).Select(n => (p.Mod, Note: NoteShape(n))))
                         .GroupBy(x => x).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Count()} x [{g.Key.Mod}] {g.Key.Note}");
            return sb.ToString();
        }

        private static bool IsPluginNote(string note) =>
            note.StartsWith("B9 ", StringComparison.Ordinal) || PluginEntryParser.ModuleNames.Any(n => note.Contains("] " + n + ":"));

        private static string NoteShape(string note) =>
            Regex.Replace(Regex.Replace(note, @"MODULE\[\d+\] ", "MODULE[n] "), "'[^']*'", "'…'");
    }
}
