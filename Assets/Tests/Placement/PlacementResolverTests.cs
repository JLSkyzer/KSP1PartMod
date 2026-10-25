using System.Collections.Generic;
using KSP1PartMod.Placement;
using NUnit.Framework;
using PlacementResult = KSP1PartMod.Placement.Placement;

namespace KSP1PartMod.Tests.Placement
{
    public class PlacementResolverTests
    {
        private const string MapJson = @"{
            ""ksp1_nodes"": { ""basicRocketry"": ""tNode_1v_rockets_01"", ""mappedToGone"": ""tNode_gone"" },
            ""new_nodes"": { ""misc"": { ""anchor"": ""tNode_1v_rockets_01"", ""offset"": { ""x"": 0, ""y"": 200 }, ""requires"": [] } },
            ""category_fallback"": {
                ""Engine"": { ""1v"": ""tNode_1v_rockets_01"", ""2v"": ""tNode_2v_rockets_01"", ""default"": ""tNode_1v_rockets_01"" }
            },
            ""catch_all"": ""new:misc""
        }";

        private static readonly HashSet<string> Tree =
            new HashSet<string> { "tNode_1v_rockets_01", "tNode_2v_rockets_01", "tNode_2v_aero_01" };

        private static Ksp1PartInfo Part(string name = "part", string tech = "basicRocketry", string cat = "Engine",
            string size = "1v", bool hidden = false) =>
            new Ksp1PartInfo(name, "mod", tech, cat, size, hidden);

        private static PlacementResult R(Ksp1PartInfo part, (string, string)? partOverride = null,
            (string, string)? nodeOverride = null)
        {
            var parts = partOverride.HasValue ? $@"""{partOverride.Value.Item1}"": ""{partOverride.Value.Item2}""" : "";
            var nodes = nodeOverride.HasValue ? $@"""{nodeOverride.Value.Item1}"": ""{nodeOverride.Value.Item2}""" : "";
            var overrides = Overrides.Parse($@"{{ ""parts"": {{ {parts} }}, ""ksp1_nodes"": {{ {nodes} }} }}", out var error);
            Assert.IsNull(error);
            return new PlacementResolver(TechMapping.Parse(MapJson), overrides, Tree).Resolve(part);
        }

        [Test] public void Resolve_Hidden_HasNoTarget()
        { var r = R(Part(hidden: true)); Assert.AreEqual(PlacementSource.Hidden, r.Source); Assert.IsNull(r.Target); }
        [Test] public void Resolve_PartOverrideWins() =>
            Assert.AreEqual("tNode_2v_aero_01", R(Part("p", tech: "basicRocketry"), partOverride: ("p", "tNode_2v_aero_01")).Target);
        [Test] public void Resolve_PartOverrideKey_IsCaseSensitive() =>
            Assert.AreEqual(PlacementSource.Table, R(Part("P"), partOverride: ("p", "tNode_2v_aero_01")).Source);
        [Test] public void Resolve_Ksp1NodeOverrideBeatsTable() =>
            Assert.AreEqual(PlacementSource.Override, R(Part(tech: "basicRocketry"), nodeOverride: ("basicRocketry", "tNode_2v_rockets_01")).Source);
        [Test] public void Resolve_Ksp1NodeOverrideKey_IsCaseInsensitive() =>
            Assert.AreEqual("tNode_2v_rockets_01", R(Part(tech: "basicRocketry"), nodeOverride: ("BasicRocketry", "tNode_2v_rockets_01")).Target);
        [Test] public void Resolve_Table()
        { var r = R(Part()); Assert.AreEqual(PlacementSource.Table, r.Source); Assert.AreEqual("tNode_1v_rockets_01", r.Target); }
        [Test] public void Resolve_NodeIdIsCaseInsensitive() =>
            Assert.AreEqual(PlacementSource.Table, R(Part(tech: "BASICROCKETRY")).Source);
        [Test] public void Resolve_UnknownNode_UsesCategoryAndSize() =>
            Assert.AreEqual("tNode_2v_rockets_01", R(Part(tech: "someModNode", cat: "Engine", size: "2v")).Target);
        [Test] public void Resolve_UnknownNode_NoteNamesIt() =>
            StringAssert.Contains("someModNode", R(Part(tech: "someModNode")).Note);
        [Test] public void Resolve_CategoryIsCaseInsensitive() =>
            Assert.AreEqual(PlacementSource.CategoryFallback, R(Part(tech: null, cat: "engine", size: "2v")).Source);
        [Test] public void Resolve_UnknownSize_UsesCategoryDefault() =>
            Assert.AreEqual("tNode_1v_rockets_01", R(Part(tech: null, cat: "Engine", size: "srf")).Target);
        [Test] public void Resolve_NullSize_UsesCategoryDefault() =>
            Assert.AreEqual(PlacementSource.CategoryFallback, R(Part(tech: null, cat: "Engine", size: null)).Source);
        [Test] public void Resolve_NothingMatches_GoesToCatchAll() =>
            Assert.AreEqual(PlacementSource.CatchAll, R(Part(tech: null, cat: "Weird")).Source);
        [Test] public void Resolve_NullCategory_GoesToCatchAll()
        { var r = R(Part(tech: null, cat: null)); Assert.AreEqual(PlacementSource.CatchAll, r.Source); Assert.AreEqual("new:misc", r.Target); }
        [Test] public void Resolve_OverrideToMissingNode_FallsThrough()
        {
            var r = R(Part("p", tech: "basicRocketry"), partOverride: ("p", "tNode_gone"));
            Assert.AreEqual(PlacementSource.Table, r.Source);
            StringAssert.Contains("tNode_gone", r.Note);
        }
        [Test] public void Resolve_TableTargetMissingFromTree_FallsBack() =>
            Assert.AreEqual(PlacementSource.CategoryFallback, R(Part(tech: "mappedToGone", cat: "Engine", size: "1v")).Source);
        [Test] public void Resolve_NewNodeWithMissingAnchor_IsSkipped()
        {
            var map = TechMapping.Parse(@"{ ""ksp1_nodes"": { ""a"": ""new:x"" },
                ""new_nodes"": { ""x"": { ""anchor"": ""tNode_gone"", ""offset"": { ""x"": 0, ""y"": 0 }, ""requires"": [] } },
                ""category_fallback"": { ""Engine"": { ""default"": ""tNode_1v_rockets_01"" } }, ""catch_all"": ""new:x"" }");
            var r = new PlacementResolver(map, Overrides.Empty, Tree).Resolve(Part(tech: "a"));
            Assert.AreEqual(PlacementSource.CategoryFallback, r.Source);
        }

        [Test] public void Overrides_BadJson_ReportsAndIsEmpty()
        { var o = Overrides.Parse("{ nope", out var e); Assert.IsNotNull(e); Assert.AreEqual(0, o.Parts.Count); }
        [Test] public void Overrides_ShippedShape_ParsesEmptyIgnoringHelp()
        {
            var o = Overrides.Parse(@"{ ""_help"": ""text"", ""parts"": {}, ""ksp1_nodes"": {} }", out var e);
            Assert.IsNull(e); Assert.AreEqual(0, o.Parts.Count); Assert.AreEqual(0, o.Ksp1Nodes.Count);
        }
        [Test] public void Overrides_NonStringValue_IsSkippedAndReported()
        {
            var o = Overrides.Parse(@"{ ""parts"": { ""a"": 3, ""b"": ""tNode_x"" } }", out var e);
            Assert.IsNotNull(e); Assert.AreEqual(1, o.Parts.Count);
        }
    }
}
