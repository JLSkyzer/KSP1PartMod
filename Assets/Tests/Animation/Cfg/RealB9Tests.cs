using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>The B9 gate planner on the Bluedog_DB parts under the game's <c>mods/KSP1</c> (cfgs before
    /// ModuleManager patches).</summary>
    public class RealB9Tests
    {
        private sealed class Part
        {
            public string Name, File;
            public IReadOnlyList<IKsp1Node> Modules;
            public override string ToString() => $"{Name} ({File})";
        }

        private sealed class B9Override
        {
            public Part Part;
            public string Set, Subtype;
            public IKsp1Node Node, Identifier;
            public int Matches;
            public override string ToString() =>
                $"{Part.Name} '{Set}' '{Subtype}': {Identifier?.GetValue("name")} [{string.Join(", ", Identifier?.AllValues.Select(v => v.Key + "=" + v.Value) ?? new string[0])}] matches {Matches}";
        }

        private static List<Part> _parts;

        private static List<Part> BdbParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<Part>();
            foreach (var path in InstalledKsp1.AllCfg().Where(p => InstalledKsp1.TopFolder(p).Equals("Bluedog_DB", StringComparison.OrdinalIgnoreCase)))
                foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
                    parts.Add(new Part
                    {
                        Name = part.GetValue("name"), File = path.Substring(InstalledKsp1.Root.TrimEnd('\\', '/').Length + 1),
                        Modules = part.GetNodes("MODULE").ToList(),
                    });
            if (parts.Count == 0) Assert.Ignore("Bluedog_DB not found under " + InstalledKsp1.Root);
            return _parts = parts;
        }

        private static bool IsB9(IKsp1Node m) => "ModuleB9PartSwitch".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<B9Override> Overrides()
        {
            foreach (var p in BdbParts())
            {
                var b9s = p.Modules.Where(IsB9).ToList();
                var ids = B9GatePlanner.SetIds(p.Modules);
                for (var j = 0; j < b9s.Count; j++)
                {
                    var variants = B9GatePlanner.VariantIds(p.Modules)[j];
                    var subtypes = b9s[j].GetNodes("SUBTYPE").ToList();
                    for (var s = 0; s < subtypes.Count; s++)
                        foreach (var o in subtypes[s].GetNodes("MODULE"))
                        {
                            var id = o.GetNodes("IDENTIFIER").FirstOrDefault();
                            yield return new B9Override
                            {
                                Part = p, Set = ids[j] ?? "(not a set)", Subtype = variants[s], Node = o, Identifier = id,
                                Matches = id == null ? 0 : B9GatePlanner.Match(p.Modules, id).Count,
                            };
                        }
                }
            }
        }

        private static List<B9Override> BdbOverrides(string moduleName) => Overrides()
            .Where(o => moduleName.Equals(o.Identifier?.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        // Scan of 2026-10-27: 25 overrides name EngineAnimationName (RL10 14, J-2 11), 4 are bare (AJ10-118F 2,
        // MB-60 1, RL-20 1), each on a part with a single ModuleDeployableEngine.
        [Test] public void B9_EveryBdbDeployableEngineOverride_MatchesExactlyOneModule()
        {
            var o = BdbOverrides("ModuleDeployableEngine");
            foreach (var bare in o.Where(x => x.Identifier.GetValue("EngineAnimationName") == null)) TestContext.WriteLine("bare: " + bare);
            Assert.AreEqual(29, o.Count);
            Assert.AreEqual(4, o.Count(x => x.Identifier.GetValue("EngineAnimationName") == null));
            CollectionAssert.IsEmpty(o.Where(x => x.Matches != 1).Select(x => x.ToString()));
        }

        /// <summary>The plan with the plugin modules and the B9 gates (the 0.5.0 defaults).</summary>
        private static List<AnimEntryDef> Gated(Part p, List<string> notes = null)
        {
            var plan = OwnershipPlanner.Plan(p.Modules, false, new HashSet<string>(), false, false, null, true, true);
            notes?.AddRange(plan.Notes.Where(x => x.StartsWith("B9 ", StringComparison.Ordinal)).Select(x => p.Name + ": " + x));
            return plan.Entries.ToList();
        }

        private static IEnumerable<string> ActiveNozzles(List<AnimEntryDef> entries, string subtype) => entries
            .Where(e => e.Kind == EntryKind.EngineDeploy)
            .Where(e => e.Gates == null || e.Gates.All(g => !g.InactiveIn.Contains(subtype, StringComparer.Ordinal)))
            .Select(e => e.Clip);

        // The fixed-nozzle subtypes switch both extensions off; the two extending ones leave theirs. (The brief said
        // "exactly one per subtype": the cfg gives none for the six fixed nozzles.)
        [Test] public void B9_Rl10_EachSubtypeLeavesItsNozzleActive()
        {
            var part = BdbParts().SingleOrDefault(p => p.Name == "bluedog_CentaurD_RL10");
            if (part == null) Assert.Ignore("bluedog_CentaurD_RL10 not found");
            var plan = Gated(part);
            var gate = plan.First(e => e.Kind == EntryKind.EngineDeploy).Gates.Single();
            Assert.AreEqual("engineSwitch", gate.SetId);
            Assert.AreEqual("RL10-B2", gate.DefaultSubtype, "no currentSubtype: highest defaultSubtypePriority (6)");
            var expected = new Dictionary<string, string[]>
            {
                ["RL10A-4-1N"] = new[] { "A4Ndeploy" }, ["RL10-B2"] = new[] { "B2deploy" },
            };
            foreach (var subtype in gate.SubtypeIds)
            {
                var active = ActiveNozzles(plan, subtype).ToList();
                TestContext.WriteLine($"{subtype}: [{string.Join(", ", active)}]");
                CollectionAssert.AreEqual(expected.TryGetValue(subtype, out var x) ? x : new string[0], active, subtype);
            }
            Assert.AreEqual(8, gate.SubtypeIds.Length);
        }

        [Test] public void B9_Rl10_OldSingleNozzleParts_NotGated()
        {
            foreach (var p in BdbParts().Where(p => p.Name == "bluedog_CentaurD_RL10A4N" || p.Name == "bluedog_CentaurD_RL10B2"))
                CollectionAssert.AreEqual(new[] { (string)null }, Gated(p).Where(e => e.Kind == EntryKind.EngineDeploy).Select(e => e.Gates?.Length.ToString()).ToList(), p.ToString());
        }

        [Test] public void B9_EveryBdbSubtype_LeavesAtMostOneNozzleActive()
        {
            var bad = new List<string>();
            foreach (var p in BdbParts().Where(p => p.Modules.Any(IsB9)))
            {
                var plan = Gated(p);
                foreach (var g in plan.Where(e => e.Gates != null).SelectMany(e => e.Gates).GroupBy(g => g.SetId).Select(x => x.First()))
                    foreach (var s in g.SubtypeIds)
                        if (ActiveNozzles(plan, s).Count() > 1) bad.Add($"{p} {s}");
            }
            CollectionAssert.IsEmpty(bad);
        }

        private static bool RenamesClip(B9Override o) => o.Node.GetNodes("DATA")
            .Any(d => d.AllValues.Any(v => v.Key.IndexOf("AnimationName", StringComparison.OrdinalIgnoreCase) >= 0
                                         || v.Key.Equals("animationStateName", StringComparison.OrdinalIgnoreCase)));

        [Test] public void B9_NoDataOverrideRenamesAClip_Bdb() =>
            CollectionAssert.IsEmpty(Overrides().Where(o => o.Node.GetNodes("DATA").Any()).Where(RenamesClip).Select(o => o.ToString()));

        [Test] public void B9_Census_Bdb()
        {
            var gated = 0;
            var notes = new List<string>();
            foreach (var p in BdbParts().Where(p => p.Modules.Any(IsB9)))
            {
                gated += Gated(p, notes).Count(e => e.Gates != null);
            }
            TestContext.WriteLine($"gated entries {gated}, notes {notes.Count}");
            foreach (var n in notes) TestContext.WriteLine(n);
            Assert.Greater(gated, 0);
        }
    }
}
