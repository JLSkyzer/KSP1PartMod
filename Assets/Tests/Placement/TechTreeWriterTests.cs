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
            public void AddNode(string id, string anchor, (float x, float y) position, string[] requires, string nameKey, string descriptionKey)
            { Nodes.Add(id, position); Added[id] = (anchor, requires, nameKey, descriptionKey); }
            public void RemoveNode(string id) { Nodes.Remove(id); Added.Remove(id); }
            public void AddPart(string nodeId, string part) => Parts.Add((nodeId, part));
            public void RemovePart(string nodeId, string part) => Parts.Remove((nodeId, part));
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
            Assert.AreEqual((0f, 200f), fake.Nodes["tNode_ksp1_robotics_01"]);
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
            Assert.AreEqual((0f, 200f), fake.Nodes["tNode_ksp1_robotics_01"]);
            Assert.AreEqual((0f, 320f), fake.Nodes["tNode_ksp1_robotics_02"]);
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
    }
}
