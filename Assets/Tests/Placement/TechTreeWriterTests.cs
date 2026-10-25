using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Game;
using KSP1PartMod.Placement;
using NUnit.Framework;
using static KSP1PartMod.Tests.Placement.PlacementPlanTests;

namespace KSP1PartMod.Tests.Placement
{
    public class TechTreeWriterTests
    {
        private sealed class FakeStore : ITechTreeStore
        {
            public readonly Dictionary<string, (float x, float y)> Nodes = new Dictionary<string, (float x, float y)>();
            public readonly Dictionary<string, (string anchor, string[] requires, string name, string desc)> Added =
                new Dictionary<string, (string, string[], string, string)>();
            public readonly List<(string, string)> Parts = new List<(string, string)>();

            public bool HasNode(string id) => Nodes.ContainsKey(id);
            public (float x, float y) Position(string id) => Nodes[id];
            public IReadOnlyCollection<(float x, float y)> AllPositions() => Nodes.Values.ToList();
            /// <summary>Node -> tier; a node without an entry is in tier 1.</summary>
            public readonly Dictionary<string, int> Tiers = new Dictionary<string, int>();
            private int TierOf(string id) => Tiers.TryGetValue(id, out var t) ? t : 1;
            public IReadOnlyCollection<float> TierYs(string id) =>
                Nodes.Where(n => TierOf(n.Key) == TierOf(id)).Select(n => n.Value.y).ToList();
            public void AddNode(string id, string anchor, (float x, float y) position, string[] requires, string nameKey, string descriptionKey)
            { Nodes.Add(id, position); Added[id] = (anchor, requires, nameKey, descriptionKey); }
            public void RemoveNode(string id) { Nodes.Remove(id); Added.Remove(id); }
            /// <summary>Part -> node, as the game's PartIDLookup.</summary>
            public readonly Dictionary<string, string> Lookup = new Dictionary<string, string>();
            public bool AddPart(string nodeId, string part)
            {
                if (Parts.Contains((nodeId, part))) return false;
                if (Lookup.TryGetValue(part, out var holder) && holder != nodeId) return false;
                Parts.Add((nodeId, part));
                Lookup[part] = nodeId;
                return true;
            }
            public void RemovePart(string nodeId, string part)
            {
                Parts.Remove((nodeId, part));
                if (Lookup.TryGetValue(part, out var holder) && holder == nodeId) Lookup.Remove(part);
            }
        }

        private FakeStore fake;
        private TechTreeWriter w;

        [SetUp]
        public void SetUp()
        {
            fake = new FakeStore();
            fake.Nodes["tNode_1v_start"] = (0f, 0f);
            fake.Nodes["tNode_1v_rockets_01"] = (500f, 0f);
            fake.Nodes["tNode_2v_rockets_01"] = (1000f, 0f);
            fake.Nodes["tNode_x"] = (1500f, 0f);
            // The tree's vertical extent: new nodes must stay inside [-600, 600].
            fake.Nodes["tNode_low"] = (2000f, -600f);
            fake.Nodes["tNode_high"] = (2000f, 600f);
            w = new TechTreeWriter(fake);
        }

        [Test] public void Apply_CreatesNodesThenParts() { w.Apply(PlanWithRobotics); Assert.IsTrue(fake.HasNode("tNode_ksp1_robotics_01")); CollectionAssert.Contains(fake.Parts, ("tNode_ksp1_robotics_01", "r")); }
        [Test] public void Apply_TwiceWithSamePlan_IsIdempotent() { w.Apply(Plan); w.Apply(Plan); Assert.AreEqual(1, fake.Parts.Count(x => x == ("tNode_1v_start", "s"))); }
        [Test] public void Revert_RemovesOnlyOwnEntries()
        { fake.AddPart("tNode_1v_start", "stock"); w.Apply(PlanWithRobotics); w.Revert();
          CollectionAssert.AreEqual(new[] { ("tNode_1v_start", "stock") }, fake.Parts); Assert.IsFalse(fake.HasNode("tNode_ksp1_robotics_01")); }

        [Test] public void Apply_NewNode_UsesLayoutAndKeys()
        {
            w.Apply(PlanWithRobotics);
            Assert.AreEqual((0f, 120f), fake.Nodes["tNode_ksp1_robotics_01"]);
            CollectionAssert.Contains(w.Notes, "[KSP1PartMod] new node tNode_ksp1_robotics_01 at (0,120) next to tNode_1v_start (0,0), " +
                                               "tree y range [-600,600]");
            var added = fake.Added["tNode_ksp1_robotics_01"];
            Assert.AreEqual("tNode_1v_start", added.anchor);
            CollectionAssert.AreEqual(new[] { "tNode_1v_start" }, added.requires);
            Assert.AreEqual("KSP1PartMod/TechNodes/Names/tNode_ksp1_robotics_01", added.name);
            Assert.AreEqual("KSP1PartMod/TechNodes/Descriptions/tNode_ksp1_robotics_01", added.desc);
        }
        [Test] public void Apply_NodesCreatedEarlierInTheSameApply_CountAsTaken()
        {
            // robotics_01 and _02 have the same offset; give their anchors the same spot.
            fake.Nodes["tNode_2v_rockets_01"] = fake.Nodes["tNode_1v_start"];
            w.Apply(Build(new[] { Part("r", "modB", "robotics"), Part("r2", "modB", "advRobotics") }));
            Assert.AreEqual((0f, 120f), fake.Nodes["tNode_ksp1_robotics_01"]);
            Assert.AreEqual((0f, -120f), fake.Nodes["tNode_ksp1_robotics_02"]);
        }
        [Test] public void Apply_NewNode_StaysInsideItsTierRange()
        {
            // Tier 1 spans [0, 100] only; the whole tree's [-600, 600] must not be used.
            foreach (var id in new[] { "tNode_1v_rockets_01", "tNode_2v_rockets_01", "tNode_x", "tNode_low", "tNode_high" })
                fake.Tiers[id] = 2;
            fake.Nodes["tNode_1v_top"] = (900f, 100f);
            w.Apply(PlanWithRobotics);
            Assert.That(fake.Nodes["tNode_ksp1_robotics_01"].y, Is.InRange(0f, 100f));
            Assert.That(w.Notes[0], Does.EndWith("tree y range [0,100]"));
        }
        [Test] public void Apply_NodeAlreadyInStore_IsNotCreatedNorRemoved()
        {
            fake.Nodes["tNode_ksp1_robotics_01"] = (7f, 7f);
            w.Apply(PlanWithRobotics);
            w.Revert();
            Assert.AreEqual((7f, 7f), fake.Nodes["tNode_ksp1_robotics_01"]);
            CollectionAssert.IsEmpty(fake.Parts);
        }
        [Test] public void Apply_MissingAnchor_SkipsNodeAndItsParts()
        {
            fake.Nodes.Remove("tNode_1v_start");
            w.Apply(PlanWithRobotics);
            Assert.IsFalse(fake.HasNode("tNode_ksp1_robotics_01"));
            Assert.IsFalse(fake.Parts.Any(p => p.Item1 == "tNode_ksp1_robotics_01" || p.Item1 == "tNode_1v_start"));
            CollectionAssert.Contains(fake.Parts, ("tNode_1v_rockets_01", "q"));
            Assert.IsTrue(w.Problems.Any(l => l.StartsWith("[KSP1PartMod]") && l.Contains("tNode_ksp1_robotics_01")));
        }
        [Test] public void Apply_PartAlreadyInNode_RevertKeepsIt()
        {
            fake.Parts.Add(("tNode_1v_start", "s")); // in the node's array, not in the plan's lookup
            w.Apply(Plan);
            Assert.AreEqual(1, fake.Parts.Count(x => x == ("tNode_1v_start", "s")));
            w.Revert();
            CollectionAssert.Contains(fake.Parts, ("tNode_1v_start", "s"));
        }
        [Test] public void Apply_PartMappedElsewhere_IsNotStolen()
        {
            fake.Lookup["s"] = "tNode_x"; // the game unlocks s elsewhere, the plan did not know
            w.Apply(Plan);
            CollectionAssert.DoesNotContain(fake.Parts, ("tNode_1v_start", "s"));
            w.Revert();
            Assert.AreEqual("tNode_x", fake.Lookup["s"]);
            CollectionAssert.DoesNotContain(fake.Parts, ("tNode_1v_start", "s"));
        }
        [Test] public void Apply_MissingRequirement_IsReported()
        {
            // robotics_02 requires robotics_01, which is skipped because its anchor is missing.
            fake.Nodes.Remove("tNode_1v_start");
            w.Apply(Build(new[] { Part("r2", "modB", "advRobotics") , Part("r", "modB", "robotics") }));
            Assert.IsTrue(fake.HasNode("tNode_ksp1_robotics_02"));
            CollectionAssert.AreEqual(new[] { "tNode_2v_rockets_01" }, fake.Added["tNode_ksp1_robotics_02"].requires);
            Assert.IsTrue(w.Problems.Any(l => l.StartsWith("[KSP1PartMod]") && l.Contains("tNode_ksp1_robotics_02")
                                              && l.Contains("requirement tNode_ksp1_robotics_01")), string.Join(" | ", w.Problems));
        }
        [Test] public void Apply_MissingStockRequirement_IsReported()
        {
            var map = TechMapping.Parse(@"{ ""ksp1_nodes"": { ""robotics"": ""new:robotics_01"" },
                ""new_nodes"": { ""robotics_01"": { ""anchor"": ""tNode_1v_start"", ""requires"": [ ""tNode_gone"" ] } } }");
            var tree = new HashSet<string>(Tree) { "tNode_gone" };
            var plan = PlacementPlan.Build(new[] { Part("r", "modB", "robotics") }, new PlacementResolver(map, Overrides.Empty, tree), map, Existing);
            w.Apply(plan);
            CollectionAssert.AreEqual(new[] { "tNode_1v_start" }, fake.Added["tNode_ksp1_robotics_01"].requires);
            CollectionAssert.Contains(w.Problems,
                "[KSP1PartMod] node tNode_ksp1_robotics_01: requirement tNode_gone is not in the tree, requires its anchor tNode_1v_start instead");
        }
    }
}
