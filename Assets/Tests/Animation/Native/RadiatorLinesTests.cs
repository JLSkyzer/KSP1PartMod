using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>The log lines of the radiators plan, Tasks 2 and 3 (exact text).</summary>
    public class RadiatorLinesTests
    {
        private static RadiatorMapping Map(string cfgBody, bool deployable, bool ksp1Rates = false, bool autoOn = true)
        {
            var node = IsruFakes.Modules("PART\n{\nname = p\nMODULE\n{\nname = ModuleActiveRadiator\n" + cfgBody + "\n}\n}\n", RadiatorMapping.Ksp1Module);
            return RadiatorMapping.From(node, deployable, IsruFakes.Table(), 100, 1, ksp1Rates, autoOn);
        }

        private const string Lg = "maxEnergyTransfer = 10000\nisCoreRadiator = true\nRESOURCE\n{\nname = ElectricCharge\nrate = 0.025\n}";
        private const string Big = "maxEnergyTransfer = 50000\nRESOURCE\n{\nname = ElectricCharge\nrate = 0.5\n}";

        [Test] public void CoolerReference_FromTheGame_AndFallback()
        {
            Assert.AreEqual("[KSP1PartMod] native: cooler reference: 2 KSP2 cooler(s), median 100 kW per EC/s (radiator_2v_radial_1x8, radiator_2v_radial_fixed)",
                RadiatorLines.CoolerReference(NativeCoolerRef.From(NativeCoolerRefTests.NativeParts())));
            Assert.AreEqual("[KSP1PartMod] native: cooler reference: no KSP2 cooler found, fallback 100 kW per EC/s",
                RadiatorLines.CoolerReference(NativeCoolerRef.Fallback));
            Assert.AreEqual("[KSP1PartMod] native: cooler reference: no KSP2 cooler found, fallback 100 kW per EC/s",
                RadiatorLines.CoolerReference(null));
        }

        [Test] public void Part_Cooler_On()
        {
            Assert.AreEqual("[KSP1PartMod] native: radPanelLg: radiator -> Module_Cooler: 200 kW (maxEnergyTransfer 10000 / 50 x 1), " +
                            "ElectricCharge 2 (KSP1 ElectricCharge 0.025), on at start",
                RadiatorLines.Part("radPanelLg", Map(Lg, false)));
        }

        [Test] public void Part_ActiveRadiator_Off_AndKsp1Rates()
        {
            Assert.AreEqual("[KSP1PartMod] native: foldingRadLarge: radiator -> Module_ActiveRadiator: 1000 kW (maxEnergyTransfer 50000 / 50 x 1), " +
                            "ElectricCharge 10 (KSP1 ElectricCharge 0.5), off at start",
                RadiatorLines.Part("foldingRadLarge", Map(Big, true)));
            Assert.AreEqual("[KSP1PartMod] native: foldingRadLarge: radiator -> Module_ActiveRadiator: 1000 kW (maxEnergyTransfer 50000 / 50 x 1), " +
                            "ElectricCharge 0.5 (KSP1 ElectricCharge 0.5), off at start",
                RadiatorLines.Part("foldingRadLarge", Map(Big, true, ksp1Rates: true)));
        }

        [Test] public void Part_NoResource_SaysNone()
        {
            StringAssert.Contains("kW (maxEnergyTransfer 10000 / 50 x 1), none (KSP1 none), on at start",
                RadiatorLines.Part("p", Map("maxEnergyTransfer = 10000", false)));
        }

        [Test] public void Summary()
        {
            Assert.AreEqual("[KSP1PartMod] native: radiators: 17 part(s) got Module_Cooler, 15 Module_ActiveRadiator (5000 kW in total); merged: 0; " +
                            "resources dropped: 0 (unknown resource); not reproduced: isCoreRadiator 32, overcoolFactor 27",
                RadiatorLines.Summary(17, 15, 5000, 0, 0, new[] { ("isCoreRadiator", 32), ("overcoolFactor", 27) }));
            StringAssert.EndsWith("not reproduced: none", RadiatorLines.Summary(0, 0, 0, 0, 0, null));
        }

        [Test] public void ListFormMissing_AndPassive()
        {
            Assert.AreEqual("[KSP1PartMod] native: radiators: the list form of CreateSerialized not found, deployable radiators get Module_Cooler (they cool folded)",
                RadiatorLines.ListFormMissing);
            Assert.AreEqual("[KSP1PartMod] native: radiators: passive: 12 KSP1 part(s) with emissiveConstant >= 0.8 and no radiator module radiate through KSP2's own thermal model (Redux imports emissiveConstant)",
                RadiatorLines.Passive(12));
        }

        [Test] public void Thermal_Armed()
        {
            Assert.AreEqual("[KSP1PartMod] thermal: armed: deploy bridge on, auto on off", RadiatorLines.Armed(true, false));
            Assert.AreEqual("[KSP1PartMod] thermal: armed: deploy bridge off, auto on on", RadiatorLines.Armed(false, true));
        }

        [Test] public void Thermal_MemberNotFound_DeployFollows()
        {
            Assert.AreEqual("[KSP1PartMod] thermal: Module_ActiveRadiator._dataDeployable not found, deploy bridge off (Redux's module left as is)",
                RadiatorLines.MemberNotFound("Module_ActiveRadiator._dataDeployable", "deploy bridge"));
            Assert.AreEqual("[KSP1PartMod] thermal: foldingRadMed: Module_ActiveRadiator deploy follows deploy:Deploy:0",
                RadiatorLines.DeployFollows("foldingRadMed", "deploy:Deploy:0"));
        }

        [Test] public void Thermal_AlwaysExtended_Reasons()
        {
            Assert.AreEqual("[KSP1PartMod] thermal: p: Module_ActiveRadiator always extended (no deploy clip)", RadiatorLines.AlwaysExtended("p", ExtendedReason.NoDeployClip));
            Assert.AreEqual("[KSP1PartMod] thermal: p: Module_ActiveRadiator always extended (animations off)", RadiatorLines.AlwaysExtended("p", ExtendedReason.AnimationsOff));
            Assert.AreEqual("[KSP1PartMod] thermal: p: Module_ActiveRadiator always extended (entry gated by B9)", RadiatorLines.AlwaysExtended("p", ExtendedReason.GatedByB9));
        }

        [Test] public void Thermal_TurnedOn_Repaired_Cooling_PatchFailed()
        {
            Assert.AreEqual("[KSP1PartMod] thermal: p: radiator turned on when extended", RadiatorLines.TurnedOnWhenExtended("p"));
            Assert.AreEqual("[KSP1PartMod] thermal: p: cooler state repaired: on, OFF -> IDLE", RadiatorLines.StateRepaired("p"));
            Assert.AreEqual("[KSP1PartMod] thermal: p: cooling 200 kW, ElectricCharge 2 (vessel pool)",
                RadiatorLines.Cooling("p", 200, new[] { ("ElectricCharge", 2.0) }));
            Assert.AreEqual("[KSP1PartMod] thermal: p: cooling 50 kW, none (vessel pool)", RadiatorLines.Cooling("p", 50, null));
            Assert.AreEqual("[KSP1PartMod] thermal: patch failed, stopped for this session: boom", RadiatorLines.PatchFailed("boom"));
        }
    }
}
