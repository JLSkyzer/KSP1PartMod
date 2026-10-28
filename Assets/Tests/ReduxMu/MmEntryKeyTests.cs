using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Real files, real patcher: the installed <c>mods/KSP1</c> set goes through Redux's
    /// <c>Ksp1ConfigFinalizer.Finalize</c> twice, as 0.7.0 saw it (no patch of ours) and as 0.8.0 sees it (every
    /// <c>mm_*</c> switch at its default: safety net, BEFORE/AFTER rule, arithmetic, indices). The animation planner then
    /// runs on each part's MODULE nodes both ways. Every entry key of 0.7.0 must still exist in 0.8.0 on a module of the
    /// same name, or a saved animation state is lost (review M1: the BEFORE/AFTER drops remove modules, for example the
    /// second <c>MultiModeEngine</c> of BDB's <c>ModuleReAdder.cfg</c>). It also prints the in-game <c>mm:</c> summary lines,
    /// which count only the patches that pass NEEDS, once per matched node.</summary>
    public class MmEntryKeyTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            _lines.Clear();
            MmPatches.Reset();
            MmPatches.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            new Harmony(MmPatches.HarmonyId).UnpatchAll(MmPatches.HarmonyId);
            MmPatches.Reset();
            MmPatches.Sink = null;
        }

        /// <summary>Every root <c>PART</c> of the finalized database, by name, in database order.</summary>
        private static Dictionary<string, List<Ksp1ConfigNode>> FinalizedParts()
        {
            var root = InstalledKsp1.Root;
            InstalledKsp1.AllCfg(); // ignores the test when the install is absent
            var report = new Ksp1ImportReport();
            var files = Ksp1PartModDiscovery.LoadConfigFiles(report, true, new[] { new Ksp1ModRoot(root, root) });
            var db = Ksp1ConfigFinalizer.Finalize(files, report);
            var parts = new Dictionary<string, List<Ksp1ConfigNode>>(StringComparer.Ordinal);
            foreach (var part in db.Files.SelectMany(f => f.Node.Nodes).Where(n => n.Name == "PART"))
            {
                var name = part.GetValue("name") ?? "";
                if (!parts.TryGetValue(name, out var list)) parts[name] = list = new List<Ksp1ConfigNode>();
                list.Add(part);
            }
            return parts;
        }

        private static string NameOf(IReadOnlyList<IKsp1Node> modules, int i) =>
            i >= 0 && i < modules.Count ? modules[i]?.GetValue("name")?.Trim() : null;

        /// <summary>Key -> module name, under every planner switch pair that changes keys, with the 0.8.0 defaults on.</summary>
        private static List<Dictionary<string, string>> Keys(Ksp1ConfigNode part)
        {
            var modules = Ksp1NodeAdapter.Modules(part);
            var solar = modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));
            var result = new List<Dictionary<string, string>>();
            foreach (var own in new[] { false, true })
            foreach (var follow in new[] { false, true })
            {
                var plan = OwnershipPlanner.Plan(modules, solar, new HashSet<string>(), own, follow, null, true, true, true, true);
                result.Add(plan.Entries.ToDictionary(e => e.Key, e => NameOf(modules, e.ModuleIndex), StringComparer.Ordinal));
            }
            return result;
        }

        [Test] public void EntryKeys_Of070_SurviveThe080Patches_AllInstalledParts()
        {
            var before = FinalizedParts();
            MmPatches.ImportRootsOverride = () => new[] { InstalledKsp1.Root };
            MmPatches.TryApply(new Harmony(MmPatches.HarmonyId), new MmPatches.Switches());
            var after = FinalizedParts();
            Assert.IsFalse(MmPatches.Broken, string.Join("\n", _lines));

            var lost = new List<string>();
            var changedModules = new List<string>();
            var missingParts = before.Keys.Where(n => !after.ContainsKey(n)).ToList();
            var compared = 0;
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var now)) continue;
                for (var copy = 0; copy < Math.Min(pair.Value.Count, now.Count); copy++)
                {
                    compared++;
                    var oldModules = Ksp1NodeAdapter.Modules(pair.Value[copy]).Select(m => m.GetValue("name")).ToList();
                    var newModules = Ksp1NodeAdapter.Modules(now[copy]).Select(m => m.GetValue("name")).ToList();
                    if (!oldModules.SequenceEqual(newModules))
                        changedModules.Add($"{pair.Key}: {oldModules.Count} -> {newModules.Count} module(s)");
                    var oldKeys = Keys(pair.Value[copy]);
                    var newKeys = Keys(now[copy]);
                    for (var s = 0; s < oldKeys.Count; s++)
                    foreach (var key in oldKeys[s])
                    {
                        if (!newKeys[s].TryGetValue(key.Key, out var module))
                            lost.Add($"{pair.Key} (switch set {s}): key {key.Key} ({key.Value}) lost");
                        else if (!string.Equals(module, key.Value, StringComparison.OrdinalIgnoreCase))
                            lost.Add($"{pair.Key} (switch set {s}): key {key.Key} moved from {key.Value} to {module}");
                    }
                }
            }
            TestContext.WriteLine($"mm keys (installed): {compared} part node(s) compared, {changedModules.Count} with a changed module list, " +
                                  $"{missingParts.Count} part(s) only in 0.7.0, {lost.Distinct().Count()} key problem(s)");
            foreach (var c in changedModules.Take(40)) TestContext.WriteLine("  modules: " + c);
            foreach (var m in missingParts.Take(20)) TestContext.WriteLine("  part only in 0.7.0: " + m);
            foreach (var l in lost.Distinct().Take(40)) TestContext.WriteLine("  key: " + l);
            TestContext.WriteLine("in-game summary lines expected with this install:");
            foreach (var line in _lines.Where(l => l.StartsWith("[KSP1PartMod] mm: ") && !l.Contains("dropped '")))
                TestContext.WriteLine("  " + line);
            // The arithmetic edits that survive NEEDS on this install are few: name them for the checklist.
            foreach (var name in new[] { "bluedog_UpperSolids_Star37BV", "bluedog_UpperSolids_Star37FMV", "bluedog_Burner2", "bluedog_UpperSolids_Star48BV" })
                if (before.TryGetValue(name, out var b) && after.TryGetValue(name, out var n))
                    TestContext.WriteLine($"  rescaleFactor of {name}: 0.7.0 {b[0].GetValue("rescaleFactor")}, 0.8.0 {n[0].GetValue("rescaleFactor")}");
            Assert.Greater(compared, 0);
            var dropped = _lines.Single(l => l.StartsWith("[KSP1PartMod] mm: BEFORE/AFTER of an absent mod: "));
            var total = int.Parse(dropped.Substring("[KSP1PartMod] mm: BEFORE/AFTER of an absent mod: ".Length).Split(' ')[0]);
            Assert.That(total, NUnit.Framework.Is.InRange(110, 134),
                "BEFORE/AFTER drops after NEEDS, pinned at 122 ±12 (2026-10-28): update the CHANGELOG and the checklist if the files changed");
            CollectionAssert.IsEmpty(missingParts, "parts are never removed");
            CollectionAssert.IsEmpty(lost.Distinct().ToList(), "saved animation state would be lost");
        }
    }
}
