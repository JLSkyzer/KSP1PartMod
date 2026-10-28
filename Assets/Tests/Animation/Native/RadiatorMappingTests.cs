using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="RadiatorMapping"/>: KSP1 <c>ModuleActiveRadiator</c> to <c>Module_Cooler</c> or
    /// <c>Module_ActiveRadiator</c> (plan (h)). Cfg values written for the tests, in the shape of the stock files.</summary>
    public class RadiatorMappingTests
    {
        private const double KwPerEc = 100;

        private const string RadPanelLg = @"
PART
{
    name = testRadPanelLg
    MODULE
    {
        name = ModuleActiveRadiator
        maxEnergyTransfer = 10000
        overcoolFactor = 0.25
        isCoreRadiator = true
        parentCoolingOnly = true
        RESOURCE
        {
            name = ElectricCharge
            rate = 0.025
        }
    }
}";

        private const string FoldingRadLarge = @"
PART
{
    name = testFoldingRadLarge
    MODULE
    {
        name = ModuleDeployableRadiator
        animationName = Deploy
        pivotName = Pivot
        windResistance = 2.5
        trackingSpeed = 0.1
    }
    MODULE
    {
        name = ModuleActiveRadiator
        maxEnergyTransfer = 50000
        overcoolFactor = 0.25
        isCoreRadiator = true
        RESOURCE
        {
            name = ElectricCharge
            rate = 0.5
        }
    }
}";

        private static List<IKsp1Node> Radiators(string cfg) => IsruFakes.Modules(cfg, RadiatorMapping.Ksp1Module);

        private static IKsp1Node One(string body) =>
            Radiators("PART\n{\nname = p\nMODULE\n{\nname = ModuleActiveRadiator\n" + body + "\n}\n}\n")[0];

        private static string Res(string name, double rate) =>
            $"RESOURCE\n{{\nname = {name}\nrate = {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n}}";

        private static RadiatorMapping Map(IReadOnlyList<IKsp1Node> nodes, bool deployable = false, double scale = 1,
            bool ksp1Rates = false, bool autoOn = true, double kwPerEc = KwPerEc) =>
            RadiatorMapping.From(nodes, deployable, IsruFakes.Table(), kwPerEc, scale, ksp1Rates, autoOn);

        [Test] public void RadPanelLg_Cooler_200kW_NativeEc2()
        {
            var m = Map(Radiators(RadPanelLg));
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(RadiatorKind.Cooler, m.Kind);
            Assert.AreEqual(RadiatorMapping.NativeCooler, m.NativeModule);
            Assert.AreEqual(200, m.FluxRemoved, 1e-9);
            Assert.AreEqual(10000, m.MaxEnergyTransfer, 1e-9);
            var ec = m.Resources.Single();
            Assert.AreEqual("ElectricCharge", ec.Name);
            Assert.AreEqual(2, ec.Rate, 1e-12, "200 kW at 100 kW per EC/s");
            Assert.AreEqual(0.025, ec.Ksp1Rate, 1e-12);
            Assert.IsTrue(m.StartsOn, "a fixed radiator starts on");
            Assert.IsFalse(m.TurnsOnWhenExtended);
            Assert.AreEqual(1, m.Merged);
            CollectionAssert.IsSubsetOf(new[] { "parentCoolingOnly", "overcoolFactor", "isCoreRadiator", "load-proportional EC" }, m.NotReproduced);
            Assert.That(m.Notes, Has.Some.Contain("parentCoolingOnly = true"));
        }

        [Test] public void RadPanelLg_Ksp1Rates_KeepsTheCfgEc()
        {
            var m = Map(Radiators(RadPanelLg), ksp1Rates: true);
            Assert.AreEqual(200, m.FluxRemoved, 1e-9);
            Assert.AreEqual(0.025, m.Resources.Single().Rate, 1e-12);
        }

        [Test] public void FoldingRadLarge_ActiveRadiator_1000kW_Ec10()
        {
            var m = Map(Radiators(FoldingRadLarge), deployable: true);
            Assert.AreEqual(RadiatorKind.ActiveRadiator, m.Kind);
            Assert.AreEqual(RadiatorMapping.NativeActiveRadiator, m.NativeModule);
            Assert.AreEqual(1000, m.FluxRemoved, 1e-9);
            Assert.AreEqual(10, m.Resources.Single().Rate, 1e-12);
            Assert.AreEqual(0.5, m.Resources.Single().Ksp1Rate, 1e-12);
            Assert.IsFalse(m.StartsOn, "a deployable radiator starts off, the bridge turns it on");
            Assert.IsTrue(m.TurnsOnWhenExtended);
            CollectionAssert.DoesNotContain(m.NotReproduced, "parentCoolingOnly");
        }

        [Test] public void BdbServiceModule_100_Is2kW()
        {
            var m = Map(new[] { One("maxEnergyTransfer = 100\novercoolFactor = 0.25\nisCoreRadiator = true\n" + Res("ElectricCharge", 0.0005)) });
            Assert.AreEqual(2, m.FluxRemoved, 1e-12);
            Assert.AreEqual(0.02, m.Resources.Single().Rate, 1e-12);
        }

        [Test] public void TwoRadiators_Merged_FluxAndEcSummed()
        {
            var a = One("maxEnergyTransfer = 10000\n" + Res("ElectricCharge", 0.025));
            var b = One("maxEnergyTransfer = 5000\n" + Res("ElectricCharge", 0.01));
            var m = Map(new[] { a, b });
            Assert.AreEqual(300, m.FluxRemoved, 1e-9);
            Assert.AreEqual(15000, m.MaxEnergyTransfer, 1e-9);
            Assert.AreEqual(3, m.Resources.Single().Rate, 1e-12);
            Assert.AreEqual(0.035, m.Resources.Single().Ksp1Rate, 1e-12);
            Assert.AreEqual(2, m.Merged);
            var kept = Map(new[] { a, b }, ksp1Rates: true);
            Assert.AreEqual(300, kept.FluxRemoved, 1e-9);
            Assert.AreEqual(0.035, kept.Resources.Single().Rate, 1e-12);
        }

        [Test] public void UnknownResource_Dropped_CoolerStays()
        {
            var m = Map(new[] { One("maxEnergyTransfer = 10000\n" + Res("ElectricCharge", 0.025) + "\n" + Res("IntakeAirX", 0.1)) });
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(1, m.DroppedResources);
            Assert.AreEqual("ElectricCharge", m.Resources.Single().Name);
            Assert.That(m.Notes, Has.Some.Contain("IntakeAirX"));
        }

        [Test] public void XenonGas_KeepsItsCfgRate()
        {
            var m = Map(new[] { One("maxEnergyTransfer = 10000\n" + Res("ElectricCharge", 0.025) + "\n" + Res("XenonGas", 0.4)) });
            Assert.AreEqual(2, m.Resources.Count);
            var xenon = m.Resources.Single(r => r.Ksp1Name == "XenonGas");
            Assert.AreEqual("Xenon", xenon.Name);
            Assert.AreEqual(0.4, xenon.Ksp1Rate, 1e-12);
            Assert.AreEqual(0.4, xenon.Rate, 1e-12, "converted as the containers are (factor 1 in the fake table), never native-paced");
            Assert.AreEqual(2, m.Resources.Single(r => r.Name == "ElectricCharge").Rate, 1e-12);
        }

        [Test] public void NoEnergyTransfer_NoModule()
        {
            foreach (var body in new[] { "maxEnergyTransfer = 0", "overcoolFactor = 0.25", "maxEnergyTransfer = -5", "maxEnergyTransfer = abc" })
            {
                var m = Map(new[] { One(body + "\n" + Res("ElectricCharge", 0.025)) });
                Assert.IsFalse(m.HasModule, body);
                Assert.That(m.Notes, Has.Some.Contain("maxEnergyTransfer"), body);
            }
            Assert.IsFalse(Map(new IKsp1Node[0]).HasModule);
            Assert.IsFalse(Map(null).HasModule);
        }

        [Test] public void FluxScale_Half()
        {
            var m = Map(Radiators(RadPanelLg), scale: 0.5);
            Assert.AreEqual(100, m.FluxRemoved, 1e-9);
            Assert.AreEqual(1, m.Resources.Single().Rate, 1e-12, "the native pace follows the flux removed");
            Assert.AreEqual(0.5, m.FluxScale);
        }

        [Test] public void AutoOnFalse_StartsOff_NothingTurnsOn()
        {
            var fixedRad = Map(Radiators(RadPanelLg), autoOn: false);
            Assert.IsFalse(fixedRad.StartsOn);
            Assert.IsFalse(fixedRad.TurnsOnWhenExtended);
            var folding = Map(Radiators(FoldingRadLarge), deployable: true, autoOn: false);
            Assert.IsFalse(folding.StartsOn);
            Assert.IsFalse(folding.TurnsOnWhenExtended);
        }

        [Test] public void BadParameters_FallBack()
        {
            var m = Map(Radiators(RadPanelLg), scale: -3, kwPerEc: 0);
            Assert.AreEqual(1, m.FluxScale);
            Assert.AreEqual(200, m.FluxRemoved, 1e-9);
            Assert.AreEqual(2, m.Resources.Single().Rate, 1e-12, "the 26w42c fallback of 100 kW per EC/s");
        }

        [Test] public void OtherKeys_AndNodes_AreNotReproduced()
        {
            var m = Map(new[] { One("maxEnergyTransfer = 1000\nweirdKey = 3\nSOMENODE\n{\na = 1\n}\n" + Res("ElectricCharge", 1)) });
            CollectionAssert.Contains(m.NotReproduced, "weirdKey");
            CollectionAssert.Contains(m.NotReproduced, "node SOMENODE");
        }

        [Test] public void DeployableFields_NotReproduced()
        {
            var deployable = IsruFakes.Modules(FoldingRadLarge, RadiatorMapping.DeployableModule)[0];
            var fields = RadiatorMapping.DeployableNotReproduced(deployable);
            CollectionAssert.AreEquivalent(new[] { "pivotName", "windResistance", "trackingSpeed" }, fields);
            Assert.AreEqual(0, RadiatorMapping.DeployableNotReproduced(null).Count);
        }
    }
}
