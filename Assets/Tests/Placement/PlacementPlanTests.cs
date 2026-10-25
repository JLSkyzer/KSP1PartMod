using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Placement;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Placement
{
    public class PlacementPlanTests
    {
        // robotics_02 is declared before robotics_01 on purpose: the plan must still create 01 first.
        internal const string MapJson = @"{
            ""ksp1_nodes"": {
                ""start"": ""tNode_1v_start"",
                ""basicRocketry"": ""tNode_1v_rockets_01"",
                ""robotics"": ""new:robotics_01"",
                ""advRobotics"": ""new:robotics_02""
            },
            ""new_nodes"": {
                ""robotics_02"": { ""anchor"": ""tNode_2v_rockets_01"", ""offset"": { ""x"": 0, ""y"": 200 },
                                  ""requires"": [ ""tNode_ksp1_robotics_01"" ] },
                ""robotics_01"": { ""anchor"": ""tNode_1v_start"", ""offset"": { ""x"": 0, ""y"": 200 }, ""requires"": [ ""tNode_1v_start"" ] },
                ""misc"": { ""anchor"": ""tNode_1v_start"", ""offset"": { ""x"": 200, ""y"": 0 }, ""requires"": [] }
            },
            ""category_fallback"": { ""Engine"": { ""1v"": ""tNode_1v_rockets_01"", ""default"": ""tNode_1v_rockets_01"" } },
            ""catch_all"": ""new:misc""
        }";

        internal static readonly HashSet<string> Tree =
            new HashSet<string> { "tNode_1v_start", "tNode_1v_rockets_01", "tNode_2v_rockets_01", "tNode_x" };

        internal static readonly TechMapping Map = TechMapping.Parse(MapJson);

        internal static Ksp1PartInfo Part(string name, string mod, string tech, string cat = "Engine", string size = "1v",
            bool hidden = false) => new Ksp1PartInfo(name, mod, tech, cat, size, hidden);

        internal static readonly Ksp1PartInfo[] BaseParts =
        {
            Part("p", "modA", "start"),                  // already in the game: kept
            Part("s", "modA", "start"),                  // table
            Part("h", "modA", "start", hidden: true),    // hidden by its KSP1 config
            Part("q", "modB", null),                     // category fallback
        };

        internal static readonly Dictionary<string, string> Existing = new Dictionary<string, string> { ["p"] = "tNode_x" };

        internal static PlacementPlan Build(IEnumerable<Ksp1PartInfo> parts) =>
            PlacementPlan.Build(parts, new PlacementResolver(Map, Overrides.Empty, Tree), Map, Existing);

        internal static PlacementPlan Plan => Build(BaseParts);
        internal static PlacementPlan PlanWithNoRoboticsParts => Plan;
        internal static PlacementPlan PlanWithRobotics => Build(BaseParts.Append(Part("r", "modB", "robotics")));

        [Test] public void Build_PartAlreadyInGame_IsKeptWhereItIs()
        { Assert.IsFalse(Plan.PartsByNode.Values.Any(l => l.Contains("p"))); StringAssert.Contains("p", Plan.LogLines.Single(l => l.Contains("already unlocked by tNode_x"))); }
        [Test] public void Build_KeptLine_HasTheExactFormat() =>
            CollectionAssert.Contains(Plan.LogLines, "[KSP1PartMod] kept: p already unlocked by tNode_x");
        [Test] public void Build_NewNodeWithoutParts_IsNotCreated() => CollectionAssert.IsEmpty(PlanWithNoRoboticsParts.NodesToCreate);
        [Test] public void Build_LogsOneSummaryPerMod() => Assert.AreEqual(2, Plan.LogLines.Count(l => l.StartsWith("[KSP1PartMod] mod ")));
        [Test] public void Build_SummaryLine_CountsEachSource()
        {
            CollectionAssert.Contains(Plan.LogLines, "[KSP1PartMod] mod modA: 1 placed (1 table, 0 override, 0 fallback, 0 catch-all), 1 hidden, 1 kept");
            CollectionAssert.Contains(Plan.LogLines, "[KSP1PartMod] mod modB: 1 placed (0 table, 0 override, 1 fallback, 0 catch-all), 0 hidden, 0 kept");
        }
        [Test] public void Build_LogsEachFallbackPart() => Assert.IsTrue(Plan.LogLines.Any(l => l.Contains("fallback") && l.Contains("q") && l.Contains("modB") && l.Contains("tNode_1v_rockets_01")));
        [Test] public void Build_FallbackLine_HasTheExactFormat() =>
            CollectionAssert.Contains(Plan.LogLines, "[KSP1PartMod] fallback: q (modB, TechRequired=none, Engine/1v) -> tNode_1v_rockets_01");
        [Test] public void Build_CatchAllPart_IsLoggedAsFallback() =>
            CollectionAssert.Contains(Build(new[] { Part("w", "modC", "weird", cat: "Weird", size: null) }).LogLines,
                "[KSP1PartMod] fallback: w (modC, TechRequired=weird, Weird/none) -> tNode_ksp1_misc");
        [Test] public void Build_HiddenPart_IsNotPlaced() => Assert.IsFalse(Plan.PartsByNode.Values.Any(l => l.Contains("h")));
        [Test] public void Build_PlacesPartsInTheirNode()
        {
            CollectionAssert.AreEqual(new[] { "s" }, Plan.PartsByNode["tNode_1v_start"]);
            CollectionAssert.AreEqual(new[] { "q" }, Plan.PartsByNode["tNode_1v_rockets_01"]);
        }
        [Test] public void Build_NewTarget_BecomesTheNodeId()
        {
            var plan = PlanWithRobotics;
            CollectionAssert.AreEqual(new[] { "r" }, plan.PartsByNode["tNode_ksp1_robotics_01"]);
            Assert.IsFalse(plan.PartsByNode.Keys.Any(k => k.StartsWith(TechMapping.NewPrefix)));
            CollectionAssert.AreEqual(new[] { "tNode_ksp1_robotics_01" }, plan.NodesToCreate.Select(n => n.NodeId));
        }
        [Test] public void Build_RequiresUncreatedNewNode_UsesAnchor()
        {
            var plan = Build(new[] { Part("r2", "modB", "advRobotics") });
            var node = plan.NodesToCreate.Single();
            Assert.AreEqual("tNode_ksp1_robotics_02", node.NodeId);
            CollectionAssert.AreEqual(new[] { "tNode_2v_rockets_01" }, node.Requires);
        }
        [Test] public void Build_NodeComesAfterNewNodeItRequires()
        {
            var plan = Build(new[] { Part("r2", "modB", "advRobotics"), Part("r", "modB", "robotics") });
            CollectionAssert.AreEqual(new[] { "tNode_ksp1_robotics_01", "tNode_ksp1_robotics_02" }, plan.NodesToCreate.Select(n => n.NodeId));
            CollectionAssert.AreEqual(new[] { "tNode_ksp1_robotics_01" }, plan.NodesToCreate[1].Requires);
        }
        [Test] public void Build_SamePartTwice_IsPlacedOnce() =>
            CollectionAssert.AreEqual(new[] { "s" }, Build(new[] { Part("s", "modA", "start"), Part("s", "modA", "start") }).PartsByNode["tNode_1v_start"]);
    }
}
