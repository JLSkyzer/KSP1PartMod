using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary>Science plan, Task 5, on the installed KSP1 files (raw <c>PART</c> nodes, ModuleManager patches not applied): the clip
    /// each configuration opens, from the entries the planner makes with the default settings. KSP2's behaviours on the part are
    /// unknown here (only a native solar panel is inferred, as the key snapshots do) and B9 gates are taken as active, so the game
    /// can differ for Redux-owned clips and B9 parts. The planner is not changed by the science runtime: no key moves.</summary>
    public class ScienceClipCensusTests
    {
        private static void Add(SortedDictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out var n) ? n + 1 : 1;

        [Test] public void Installed_ClipTargets_PrintedPerMod()
        {
            var settings = AnimationSettings.Defaults;
            var files = new List<(string Mod, IKsp1Node File)>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                try { files.Add((InstalledKsp1.TopFolderUnder(InstalledKsp1.Root, path), CfgText.Parse(File.ReadAllText(path)))); }
                catch (Exception) { /* unreadable cfg: the importer skips it too */ }
            }
            Assume.That(files.Count, Is.GreaterThan(0), "no installed cfg");
            var parts = files.SelectMany(f => f.File.GetNodes("PART").Select(p => (f.Mod, Part: p))).ToList();
            var definitions = ScienceDefinitionBuilder.BuildAll(
                files.SelectMany(f => f.File.AllNodes.Where(n => n.Name?.Trim() == "EXPERIMENT_DEFINITION")),
                parts.Select(p => (IReadOnlyList<IKsp1Node>)p.Part.GetNodes("MODULE").ToList()), settings, null);
            var skip = new HashSet<string>(settings.SkipModules, StringComparer.Ordinal);

            var opens = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var noClip = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var perMod = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // configurations, opens, waits
            var waits = 0;
            var waitsWithoutGate = 0;
            var configurations = 0;
            var failures = new List<string>();
            var opened = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (mod, node) in parts)
            {
                var name = node.GetValue("name")?.Trim() ?? "?";
                try
                {
                    var modules = node.GetNodes("MODULE").ToList();
                    if (!modules.Any(ScienceMapping.IsExperimentModule)) continue;
                    var science = ScienceMapping.Resolve(modules, definitions, NativeExperiments.Redux26w42c, settings);
                    if (!science.HasModule) continue;
                    var solar = modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));
                    var plan = OwnershipPlanner.Plan(modules, solar, new HashSet<string>(), settings.OwnFirstDeployable,
                        settings.FollowReduxDeployables, skip, settings.PluginModules, settings.B9ModuleGates, shrouds: true, ablatorChar: true);
                    if (!perMod.TryGetValue(mod, out var row)) perMod[mod] = row = new int[3];
                    foreach (var c in science.Configurations)
                    {
                        configurations++;
                        row[0]++;
                        var t = ScienceAnimRules.TargetsFor(c, plan.Entries, true);
                        if (t.First == null)
                        {
                            Add(noClip, ScienceLines.NoClipText(t.Reason));
                            if (c.Waits) waitsWithoutGate++;
                            continue;
                        }
                        row[1]++;
                        Add(opens, t.First.Kind.ToString());
                        opened.Add(name);
                        if (c.Waits)
                        {
                            row[2]++;
                            if (t.Entries.Any(e => e.Kind == EntryKind.ScienceToggle || e.Kind == EntryKind.Toggle)) waits++;
                            else waitsWithoutGate++;
                        }
                    }
                }
                catch (Exception e) { failures.Add($"{mod}/{name}: {e}"); }
            }
            string Join(SortedDictionary<string, int> d) => d.Count == 0 ? "none" : string.Join(", ", d.Select(p => p.Key + " " + p.Value));
            TestContext.WriteLine($"configurations {configurations}; opens {opens.Values.Sum()} ({Join(opens)}); no clip {noClip.Values.Sum()} ({Join(noClip)}); " +
                                  $"waiting with a gate {waits}, waiting without one {waitsWithoutGate}");
            foreach (var p in perMod) TestContext.WriteLine($"  {p.Key}: configurations {p.Value[0]}, opens {p.Value[1]}, waits {p.Value[2]}");
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Greater(configurations, 0);
            Assert.AreEqual(configurations, opens.Values.Sum() + noClip.Values.Sum());
            // The three stock parts whose experiment names its clip (FxModules = 0): the goo canister, the materials bay, the
            // magnetometer boom. Each must get a first target.
            foreach (var stock in new[] { "GooExperiment", "science_module", "Magnetometer" })
                if (parts.Any(p => p.Part.GetValue("name")?.Trim() == stock))
                    CollectionAssert.Contains(opened, stock, $"{stock} opens no clip");
                else Assert.Fail($"{stock} is not installed under mods/KSP1");
        }
    }
}
