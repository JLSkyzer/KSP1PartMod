using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Rescale;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Rescale
{
    /// <summary><see cref="RescaleRule"/> (0.16.0, Task 1), on invented cfg text in the shape of KSP1's files.</summary>
    public class RescaleRuleTests
    {
        private static IKsp1Node PartOf(string cfg) => CfgText.Parse(cfg).GetNodes("PART").First();

        /// <summary>The KSP1 LV-T30's shape: the legacy <c>mesh</c>, <c>scale = 0.1</c>, then the nodes; no rescaleFactor.</summary>
        private const string Lvt30 = "PART\n{\nname = liquidEngine\nmodule = Part\nauthor = test\nmesh = model.mu\nscale = 0.1\n" +
                                     "node_stack_top = 0.0, 7.21461, 0.0, 0.0, 1.0, 0.0\nnode_stack_bottom = 0.0, -7.27403, 0.0, 0.0, 1.0, 0.0\n" +
                                     "node_attach = 0.0, 0.0, 0.0, 0.0, -1.0, 0.0\nmass = 1.25\n}\n";

        [Test] public void Lvt30_DefaultRescale_AndNodeScale()
        {
            var part = PartOf(Lvt30);
            Assert.AreEqual(1.25f, RescaleRule.MissingDefault(part));
            var scale = RescaleRule.NodeScale(part);
            CollectionAssert.AreEqual(new[] { "node_stack_top", "node_stack_bottom", "node_attach" }, scale.Select(p => p.Key).ToArray());
            CollectionAssert.AreEqual(new[] { 0.1f, 0.1f, 0.1f }, scale.Select(p => p.Value).ToArray());
            Assert.IsTrue(RescaleRule.HasNodeScale(part));
        }

        [Test] public void AScaleAfterTheNodes_GivesOne()
        {
            var part = PartOf("PART\n{\nname = a\nnode_stack_top = 0, 1, 0, 0, 1, 0\nscale = 0.1\nnode_attach = 0, 0, 0, 0, -1, 0\n}\n");
            CollectionAssert.AreEqual(new[] { 1f, 0.1f }, RescaleRule.NodeScale(part).Select(p => p.Value).ToArray());
        }

        [Test] public void ABadScale_GivesOne()
        {
            var part = PartOf("PART\n{\nname = a\nscale = 0.1\nscale = -2\nnode_stack_top = 0, 1, 0, 0, 1, 0\nscale = x\nnode_stack_b = 0, 1, 0, 0, 1, 0\n}\n");
            CollectionAssert.AreEqual(new[] { 1f, 1f }, RescaleRule.NodeScale(part).Select(p => p.Value).ToArray());
            Assert.IsFalse(RescaleRule.HasNodeScale(part));
        }

        [Test] public void RescaleFactorOne_GetsNothing() =>
            Assert.IsNull(RescaleRule.MissingDefault(PartOf("PART\n{\nname = a\nrescaleFactor = 1\n}\n")));

        [Test] public void KerbalEva_AndFlag_AreExcluded()
        {
            Assert.IsNull(RescaleRule.MissingDefault(PartOf("PART\n{\nname = kerbalEVA\nmodule = KerbalEVA\n}\n")));
            Assert.IsNull(RescaleRule.MissingDefault(PartOf("PART\n{\nname = flag\nmodule = FlagSite\n}\n")));
            Assert.IsTrue(RescaleRule.IsExcluded(PartOf("PART\n{\nname = kerbalEVA\nmodule = kerbaleva\n}\n")));
        }

        [Test] public void StockPrebuiltOverlays_AreExcluded()
        {
            // KSP1's stock kerbalEVA*.cfg and flag.cfg have no module value (they complete prefabs).
            Assert.IsTrue(RescaleRule.IsExcluded(PartOf("PART\n{\nname = kerbalEVAfemaleVintage\nMODULE\n{\nname = ModuleScienceExperiment\n}\n}\n")));
            Assert.IsTrue(RescaleRule.IsExcluded(PartOf("PART\n{\nname = flag\nMODULE\n{\n__OVERLOAD = True\nname = FlagSite\n}\n}\n")));
            Assert.IsFalse(RescaleRule.IsExcluded(PartOf("PART\n{\nname = liquidEngine\nmodule = Part\n}\n")));
            Assert.IsFalse(RescaleRule.IsExcluded(PartOf("PART\n{\nname = evaTank\n}\n")));
        }

        [Test] public void ModelPositionFactor()
        {
            Assert.AreEqual(0.625f, RescaleRule.ModelPositionFactor(0.625f));
            Assert.AreEqual(1f, RescaleRule.ModelPositionFactor(0f));
            Assert.AreEqual(1f, RescaleRule.ModelPositionFactor(float.NaN));
        }

        [Test] public void WheelRadius_FollowsTheFactor_UnlessFittedToTheMesh()
        {
            Assert.AreEqual(0.4725f, RescaleRule.WheelRadius(0.378f, 1.25f, false), 1e-6f);
            Assert.AreEqual(0.378f, RescaleRule.WheelRadius(0.378f, 1.25f, true), "KSP2 measures the scaled mesh");
            Assert.AreEqual(0.378f, RescaleRule.WheelRadius(0.378f, 1f, false));
            Assert.AreEqual(0.378f, RescaleRule.WheelRadius(0.378f, float.NaN, false));
            Assert.AreEqual(0.378f, RescaleRule.WheelRadius(0.378f, -2f, false));
            Assert.AreEqual(0.175f, RescaleRule.WheelRadius(0.28f, 0.625f, false), 1e-6f);
            Assert.AreEqual("ModuleWheelBase: radius 0.378 x rescaleFactor 1.25 = 0.4725 m (KSP2 sizes the wheel in metres, not with the part)",
                EngineSizeLines.WheelRadiusNote(0.378f, 1.25f, 0.4725f));
        }

        [Test] public void NodeKeys_AnyCase_AsRedux()
        {
            Assert.IsTrue(RescaleRule.IsNodeKey("NODE_STACK_top"));
            Assert.IsTrue(RescaleRule.IsNodeKey("Node_Attach"));
            Assert.IsFalse(RescaleRule.IsNodeKey("node_attachX"));
        }

        [Test] public void Lines()
        {
            var t = new EngineSizeLines.RescaleTally { Defaulted = 19, Excluded = 7, NodeScaled = 14, ModelPositions = 3 };
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] rescale: 19 part(s) without rescaleFactor set to 1.25, 7 excluded (KerbalEVA, FlagSite)",
                "[KSP1PartMod] rescale: node scale on 14 part(s)",
                "[KSP1PartMod] rescale: MODEL position x rescaleFactor on 3 model(s)",
            }, EngineSizeLines.RescaleSummary(t));
        }
    }
}
