using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="HarvesterMapping"/>: planetary <c>ModuleResourceHarvester</c>s → one <c>Module_Mine</c> formula
    /// (plan (j)). Cfg values written for the tests, in the shape of the stock files.</summary>
    public class HarvesterMappingTests
    {
        private const double HeatPerEc = 17.5;
        private const double Abundance = 0.05;

        private const string RadialDrill = @"
PART
{
    name = testRadialDrill
    MODULE
    {
        name = ModuleResourceHarvester
        HarvesterType = 0
        Efficiency = 1.5
        ResourceName = Ore
        ConverterName = Surface Harvester
        ImpactTransform = ImpactTransform
        ImpactRange = 5.42
        AutoShutdown = true
        GeneratesHeat = true
        INPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 15
        }
        UseSpecialistBonus = true
        SpecialistEfficiencyFactor = 0.2
        EfficiencyBonus = 1
        TemperatureModifier
        {
            key = 0 100000
        }
        ThermalEfficiency
        {
            key = 250 0.9 0 0
        }
    }
    MODULE
    {
        name = ModuleAsteroidDrill
        Efficiency = 5
    }
}";

        private static List<IKsp1Node> Harvesters(string cfg) => IsruFakes.Modules(cfg, HarvesterMapping.Ksp1Module);

        private static IKsp1Node One(string body) =>
            Harvesters("PART\n{\nname = p\nMODULE\n{\nname = ModuleResourceHarvester\n" + body + "\n}\n}\n")[0];

        private static string Drill(double efficiency, double ec, string resource = "Ore", int type = 0, string range = "1.08") =>
            $"HarvesterType = {type}\nEfficiency = {efficiency.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" +
            $"ResourceName = {resource}\nImpactTransform = ImpactTransform\nImpactRange = {range}\nGeneratesHeat = true\n" +
            $"INPUT_RESOURCE\n{{\nResourceName = ElectricCharge\nRatio = {ec.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n}}";

        [Test] public void RadialDrill_OreToCarbonRegolith_Ec15_Heat262_5()
        {
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            var o = m.Outputs.Single();
            Assert.AreEqual("Ore", o.Ksp1Name);
            Assert.AreEqual("CarbonRegolith", o.Name);
            Assert.AreEqual(1.5, o.Efficiency);
            Assert.AreEqual(0.075, o.Ksp1Rate, 1e-12, "1.5 × 0.05 KSP1 units/s at full density");
            Assert.AreEqual(0.00375, o.Rate, 1e-12, "0.075 Ore at 0.05 t → 0.00375 CarbonRegolith of 1 t");
            var f = m.Formula;
            Assert.AreEqual("ksp1_mine", f.Id);
            Assert.AreEqual("ElectricCharge", f.Inputs.Single().Name);
            Assert.AreEqual(15, f.Inputs.Single().Rate);
            Assert.AreEqual(15, f.EcInput);
            Assert.AreEqual("CarbonRegolith", f.Outputs.Single().Name);
            Assert.AreEqual(262.5, f.FluxGenerated, 1e-9);
            Assert.AreEqual("ImpactTransform", m.ImpactTransform);
            Assert.AreEqual(5.42, m.ImpactRange, 1e-12);
            CollectionAssert.IsSubsetOf(new[] { "ThermalEfficiency", "TemperatureModifier", "specialist bonus" }, m.NotReproduced);
            Assert.AreEqual(0, m.SkippedOcean + m.SkippedAtmospheric + m.SkippedExospheric + m.SkippedNoMap + m.SkippedOther);
        }

        [Test] public void MiniDrill_Efficiency0_3()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(0.3, 3)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.AreEqual(0.015, m.Outputs.Single().Ksp1Rate, 1e-12);
            Assert.AreEqual(0.00075, m.Outputs.Single().Rate, 1e-12);
            Assert.AreEqual(52.5, m.Formula.FluxGenerated, 1e-9);
            Assert.AreEqual(1.08, m.ImpactRange, 1e-12);
        }

        [Test] public void Type2_Skipped_NoModule()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1, 5, "Oxidizer", 2)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.IsFalse(m.HasModule);
            Assert.AreEqual(1, m.SkippedAtmospheric);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: HarvesterType 2 (atmospheric) skipped, Module_Mine only mines the ground"));
            Assert.AreEqual(1, HarvesterMapping.From(new[] { One(Drill(1, 5, "Water", 1)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1).SkippedOcean);
            Assert.AreEqual(1, HarvesterMapping.From(new[] { One(Drill(1, 5, "Water", 3)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1).SkippedExospheric);
        }

        [Test] public void TwoType0_Merged_EcSummed_ImpactOfTheFirst()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1.5, 15, range: "5.42")), One(Drill(0.3, 3)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.AreEqual(18, m.Formula.EcInput);
            Assert.AreEqual(18, m.Formula.Inputs.Single().Rate);
            Assert.AreEqual(2, m.Outputs.Count, "one line per harvester");
            Assert.AreEqual("CarbonRegolith", m.Formula.Outputs.Single().Name, "outputs summed by resource");
            Assert.AreEqual(0.00375 + 0.00075, m.Formula.Outputs.Single().Rate, 1e-12);
            Assert.AreEqual(17.5 * 18, m.Formula.FluxGenerated, 1e-9);
            Assert.AreEqual(5.42, m.ImpactRange, 1e-12);
            Assert.AreEqual(2, m.Merged);
        }

        [Test] public void ResourceWithoutAMap_Skipped()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1, 5, "LiquidFuel")), One(Drill(1.5, 15)) }, IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(1, m.SkippedNoMap);
            Assert.AreEqual(15, m.Formula.EcInput, "the skipped harvester's EC is not counted");
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: LiquidFuel -> Methane has no resource map in KSP2, harvester skipped"));
        }

        [Test] public void OreAsTestRocks_NotMinable_NoModule()
        {
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table("TestRocks"), Abundance, HeatPerEc, 1);
            Assert.IsFalse(m.HasModule);
            Assert.AreEqual(1, m.SkippedNoMap);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: Ore -> TestRocks has no resource map in KSP2, harvester skipped"));
        }

        [Test] public void HeatScale0_NoHeat_NoGeneratesHeat_NoHeat()
        {
            Assert.AreEqual(0, HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Abundance, HeatPerEc, 0).Formula.FluxGenerated);
            var cold = HarvesterMapping.From(new[] { One("ResourceName = Ore\nEfficiency = 1\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 2\n}") },
                IsruFakes.Table(), Abundance, HeatPerEc, 1);
            Assert.AreEqual(0, cold.Formula.FluxGenerated);
            Assert.AreEqual("", cold.ImpactTransform);
            Assert.AreEqual(0, cold.ImpactRange);
        }

        [Test] public void AbundanceScalesTheRate()
        {
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), 0.1, HeatPerEc, 1);
            Assert.AreEqual(0.0075, m.Outputs.Single().Rate, 1e-12);
            Assert.AreEqual(0.1, m.Abundance);
        }

        [Test] public void AsteroidAndCometDrills_CountedFromThePart()
        {
            var part = CfgText.Parse(RadialDrill).GetNodes("PART").First().GetNodes("MODULE").ToList();
            Assert.AreEqual(1, HarvesterMapping.CountAsteroidDrills(part));
        }
    }
}
