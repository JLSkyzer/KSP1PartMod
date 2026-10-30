using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native.Fairings;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Fairings
{
    /// <summary><see cref="FairingMapping"/>, <see cref="SafMeshNames"/>, <see cref="FairingPartRule"/> and
    /// <see cref="FairingRecord"/> (fairings plan, Task 1). Cfg values written for the tests, in the shape of KSP1's files.</summary>
    public class FairingMappingTests
    {
        private static IKsp1Node PartOf(string cfg) => CfgText.Parse(cfg).GetNodes("PART").First();

        private static IKsp1Node Module(IKsp1Node part, string name) =>
            part.GetNodes("MODULE").First(m => m.GetValue("name")?.Trim() == name);

        private static FairingMap Procedural(string cfg)
        {
            var part = PartOf(cfg);
            return FairingMapping.Procedural(Module(part, FairingMapping.ProceduralModule), part);
        }

        private static FairingMap Saf(string cfg)
        {
            var part = PartOf(cfg);
            return FairingMapping.Saf(Module(part, FairingMapping.SafModule), part);
        }

        private const string StockBase = @"
PART
{
    name = testFairingSize1
    node_stack_top = 0.0, 0.1, 0.0, 0.0, 1.0, 0.0, 1
    MODULE
    {
        name = ModuleProceduralFairing
        nSides = 30
        nArcs = 2
        nCollidersPerXSection = 6
        TextureURL = Test/fairing_diff
        CapTextureURL = Test/fairing_cap
        TextureNormalURL = Test/fairing_nrm
        DefaultBaseTextureURL = Test/base
        DefaultBaseNormalsURL = Test/base_nrm
        BaseModelTransformName = Mesh
        panelGrouping = 3
        pivot = 0,0.17,0
        axis = 0,1,0
        baseRadius = 0.625
        maxRadius = 1.5
        capRadius = 0.2
        snapThreshold = 0.1
        xSectionHeightMin = 0.2
        xSectionHeightMax = 2
        edgeSlide = 0.15
        edgeWarp = 0
        noseTip = 0.7
        UnitAreaMass = 0.01
        UnitAreaCost = 6
        stagingToggleEnabledEditor = True
        stagingToggleEnabledFlight = False
    }
}";

        private static string BdbBase(string extra = "", string baseRadius = "baseRadius = 0.56821") => @"
PART
{
    name = testBdbFairingBase
    MODULE
    {
        name = ModuleProceduralFairing
        nSides = 8
        nArcs = 2
        useClamshell = true
        ejectionForce = 300 // comment
        pivot = 0,0.11,0
        axis = 0,0,0
        " + baseRadius + @"
        maxRadius = 0.56821
        capRadius = 0.15
        UnitAreaMass = 0.009
        " + extra + @"
    }
}";

        // ------------------------------------------------------------------ procedural

        [Test] public void Stock_FourClamshells_Ksp2Force_MeshTransform()
        {
            var m = Procedural(StockBase);
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(FairingDeploy.Clamshellx4, v.DefaultDeployType);
            Assert.AreEqual(100f, v.EjectionForce);
            Assert.AreEqual("Mesh", v.BaseModelTransformName);
            Assert.AreEqual(0.625f, v.BaseRadius);
            Assert.AreEqual(1.5f, v.MaxRadius);
            Assert.AreEqual(0.2f, v.CapRadius);
            Assert.AreEqual(0.2f, v.CloseRadius);
            Assert.AreEqual(0.1f, v.SnapThreshold);
            Assert.AreEqual(0.2f, v.CrossSectionHeightMin);
            Assert.AreEqual(2f, v.CrossSectionHeightMax);
            Assert.AreEqual(0.7f, v.NoseTip);
            Assert.AreEqual(0f, v.EdgeWarp);
            Assert.AreEqual(0.17f, v.Pivot.Y);
            Assert.AreEqual(1f, v.LocalUpAxis.Y);
            Assert.AreEqual(30, v.FairingSideCount);
            Assert.AreEqual(0.01f, v.MassAreaRatio);
            Assert.IsTrue(v.StageToggleDefault);
            Assert.IsFalse(v.AllowInterstageNode);
        }

        [Test] public void Bdb_TwoClamshells_ForceWithComment_ZeroAxisGivesUp_SidesKept()
        {
            var m = Procedural(BdbBase());
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(FairingDeploy.Clamshellx2, v.DefaultDeployType);
            Assert.AreEqual(300f, v.EjectionForce);
            Assert.AreEqual("(0, 1, 0)", v.LocalUpAxis.ToString(), "axis 0,0,0 gives up");
            Assert.AreEqual(0.11f, v.Pivot.Y);
            Assert.AreEqual(8, v.FairingSideCount, "kept: KSP2 clamps it in the VAB");
            Assert.AreEqual(FairingValues.Ksp2BaseModelTransformName, v.BaseModelTransformName);
            Assert.AreEqual(0.009f, v.MassAreaRatio);
        }

        [Test] public void Procedural_StagingEnabledFalse_IsTheDefaultToggle()
        {
            Assert.IsFalse(Procedural(BdbBase("stagingEnabled = False")).Values.StageToggleDefault);
        }

        [TestCase("baseRadius = 0")]
        [TestCase("")]
        [TestCase("baseRadius = x")]
        public void NoBaseRadius_NotConverted(string line)
        {
            var m = Procedural(BdbBase(baseRadius: line));
            Assert.IsFalse(m.Converted);
            Assert.IsNotNull(m.Reason);
        }

        [Test] public void Procedural_EveryNotReproducedField_IsNoted()
        {
            var m = Procedural(StockBase);
            CollectionAssert.AreEquivalent(FairingMapping.ProceduralNotReproduced, m.NotReproduced);
        }

        // ------------------------------------------------------------------ SAF

        private static string SafPart(string topSize = ", 0", string extra = "", bool top = true) => @"
PART
{
    name = testSafBase
    " + (top ? "node_stack_top = 0.0, 0.03, 0.0, 0.0, 1.0, 0.0" + topSize : "") + @"
    node_stack_bottom = 0.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0
    MODULE
    {
        name = ModuleSimpleAdjustableFairing
        segmentLength = 0.9
        maxSegments = 8
        numSlices = 2
        deploySpeed = 2.5
        shieldingCenter = 0, 1.2, 0
        shieldingBaseRadius = 0.5
        editorOpenOffset = 7.5, 0, 0
        " + extra + @"
        WALL_BASE
        {
            transformName = Wallbase
            CoM = 0.1, 1.8, 0
            mass = 0.006
            rootOffset = 0, 0.06, 0
        }
        WALL
        {
            transformName = Wall
            mass = 0.003
            rootOffset = 0, 1.9, 0
        }
        CONE
        {
            transformName = Cone
            mass = 0.002
            rootOffset = 0, 1.9, 0
        }
    }
}";

        [Test] public void Saf_TopSize0_Radius03125_TwoClamshells_PivotFromWallBase()
        {
            var m = Saf(SafPart());
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(0.3125f, v.BaseRadius);
            Assert.AreEqual(2.4f * 0.3125f, v.MaxRadius, 1e-6, "2.4 x base beats 0.5");
            Assert.AreEqual(0.32f * 0.3125f, v.CapRadius, 1e-6);
            Assert.AreEqual(v.CapRadius, v.CloseRadius);
            Assert.AreEqual(0.06f, v.Pivot.Y);
            Assert.AreEqual(FairingDeploy.Clamshellx2, v.DefaultDeployType);
            Assert.AreEqual(100f, v.EjectionForce);
            Assert.AreEqual(FairingValues.Ksp2BaseModelTransformName, v.BaseModelTransformName);
            Assert.AreEqual(FairingValues.DefaultMassAreaRatio, v.MassAreaRatio);
            Assert.AreEqual(24, v.FairingSideCount);
        }

        [Test] public void Saf_TopSize2_Radius125()
        {
            Assert.AreEqual(1.25f, Saf(SafPart(", 2")).Values.BaseRadius);
        }

        [Test] public void Saf_TopWithoutSize_IsKsp1DefaultSize1()
        {
            Assert.AreEqual(0.625f, Saf(SafPart("")).Values.BaseRadius);
        }

        [Test] public void Saf_ShieldingRadiusAbove24Base_Wins()
        {
            var m = Saf(SafPart().Replace("shieldingBaseRadius = 0.5", "shieldingBaseRadius = 1.27 //"));
            Assert.AreEqual(1.27f, m.Values.MaxRadius);
        }

        [Test] public void Saf_NoTopNode_OrUnknownSize_NotConverted()
        {
            Assert.IsFalse(Saf(SafPart(top: false)).Converted);
            Assert.IsFalse(Saf(SafPart(", 7")).Converted);
        }

        [TestCase("3", FairingDeploy.Clamshellx3)]
        [TestCase("4", FairingDeploy.Clamshellx4)]
        [TestCase("6", FairingDeploy.Clamshellx4)]
        [TestCase("1", FairingDeploy.Clamshellx2)]
        public void Saf_NumSlices(string slices, FairingDeploy expected)
        {
            var cfg = SafPart().Replace("numSlices = 2", "numSlices = " + slices);
            Assert.AreEqual(expected, Saf(cfg).Values.DefaultDeployType);
        }

        [Test] public void Saf_EveryNotReproducedField_IsNoted()
        {
            CollectionAssert.AreEquivalent(FairingMapping.SafNotReproduced, Saf(SafPart()).NotReproduced);
        }

        // ------------------------------------------------------------------ SAF meshes

        private const string Saf0p625 = @"
PART
{
    name = testFairingBase_0p625m_SAF
    node_stack_top = 0.0, 0.031, 0.0, 0.0, 1.0, 0.0, 0
    MODULE
    {
        name = ModuleSimpleAdjustableFairing
        numSlices = 2
        WALL_BASE
        {
            transformName = Wallbase
        }
        WALL
        {
            transformName = Wall
        }
        CONE
        {
            transformName = Cone
        }
    }
    MODULE
    {
        name = ModuleCargoBay
        DeployModuleIndex = 0
    }
    MODULE
    {
        name = ModuleB9PartSwitch
        moduleID = fairingSwitch
        SUBTYPE
        {
            name = typeA
            transform = TypeAWallbase
            transform = TypeAWall
            transform = TypeACone
        }
        SUBTYPE
        {
            name = typeB
            transform = TypeBWallbase
            transform = TypeBWall
            transform = TypeBCone
            MODULE
            {
                IDENTIFIER
                {
                    name = ModuleSimpleAdjustableFairing
                }
                DATA
                {
                    shieldingBaseRadius = 1.05
                    WALL_BASE
                    {
                        rootOffset = 0,0.06,0
                    }
                }
            }
        }
        SUBTYPE
        {
            name = typeC
            transform = TypeCWallbase
            transform = typeawall
            transform = TypeCCone
            MODULE
            {
                IDENTIFIER
                {
                    name = ModuleSimpleAdjustableFairing
                }
                DATA
                {
                    WALL
                    {
                        transformName = TypeCWallOverride
                    }
                }
            }
        }
    }
    MODULE
    {
        name = ModuleB9PartSwitch
        moduleID = unrelated
        SUBTYPE
        {
            name = plain
            transform = NotAFairing
        }
    }
}";

        [Test] public void SafMeshNames_BaseNames_EverySubtypeTransform_AndDataOverrides()
        {
            var names = SafMeshNames.Collect(PartOf(Saf0p625));
            CollectionAssert.AreEqual(new[]
            {
                "Wallbase", "Wall", "Cone",
                "TypeAWallbase", "TypeAWall", "TypeACone",
                "TypeBWallbase", "TypeBWall", "TypeBCone",
                "TypeCWallbase", "TypeCCone",
                "TypeCWallOverride",
            }, names, "distinct ignoring case ('typeawall' is TypeAWall); the unrelated switch is left out");
        }

        [Test] public void SafMeshNames_NoSaf_Empty()
        {
            CollectionAssert.IsEmpty(SafMeshNames.Collect(PartOf(StockBase)));
        }

        // ------------------------------------------------------------------ rule and record

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        public void TakeOver_OnlyConvertedKsp1Parts(bool ksp1, bool converted, bool expected)
        {
            Assert.AreEqual(expected, FairingPartRule.TakeOver(ksp1, converted));
        }

        [TestCase(true, true, FairingFallback.OabPartTransform)]
        [TestCase(true, false, FairingFallback.OabPartTransform)]
        [TestCase(false, true, FairingFallback.FlightPartTransform)]
        [TestCase(false, false, FairingFallback.None)]
        public void FallbackOf_VabPartTransform_FlightPartTransform(bool inOab, bool hasFlightPart, FairingFallback expected)
        {
            Assert.AreEqual(expected, FairingPartRule.FallbackOf(inOab, hasFlightPart));
        }

        [Test] public void Record_KindAndCounts_AndSummaryLine()
        {
            var r = new FairingRecord();
            Assert.IsTrue(r.Add("a", FairingKind.Procedural, new[] { "nArcs", "edgeSlide" }));
            Assert.IsTrue(r.Add("b", FairingKind.Procedural, new[] { "nArcs" }));
            Assert.IsFalse(r.Add("a", FairingKind.Saf), "first kind kept");
            Assert.IsTrue(r.Add("c", FairingKind.Saf));
            r.Refuse(FairingKind.Procedural, FairingRefusal.Mapping);
            Assert.AreEqual(FairingKind.Procedural, r.KindOf("a"));
            Assert.IsNull(r.KindOf("x"));
            Assert.AreEqual(2, r.Count(FairingKind.Procedural));
            Assert.AreEqual(1, r.Count(FairingKind.Saf));
            Assert.AreEqual(
                "[KSP1PartMod] native: ModuleProceduralFairing -> Module_Fairing: 2 part(s); refused mapping 1; not reproduced nArcs 2, edgeSlide 1",
                FairingLines.Summary(FairingKind.Procedural, r.Count(FairingKind.Procedural), r.Refusals(FairingKind.Procedural),
                    r.NotReproduced(FairingKind.Procedural)));
            Assert.AreEqual("[KSP1PartMod] native: ModuleSimpleAdjustableFairing -> Module_Fairing: 1 part(s)",
                FairingLines.Summary(FairingKind.Saf, 1, r.Refusals(FairingKind.Saf), r.NotReproduced(FairingKind.Saf)));
            r.Clear();
            Assert.IsFalse(r.Contains("a"));
        }

        [Test] public void Lines_Text()
        {
            Assert.AreEqual("[KSP1PartMod] fairing: p: model fallback to p(Clone)", FairingLines.ModelFallback("p", "p(Clone)"));
            Assert.AreEqual("[KSP1PartMod] fairing: p: SAF mesh hidden 3 transform(s), 1 not found (X)",
                FairingLines.SafHidden("p", 3, new[] { "X" }));
            Assert.AreEqual("[KSP1PartMod] fairing: guards armed (_modelTransform, _materialLoaded)", FairingLines.GuardsArmed);
        }
    }
}
