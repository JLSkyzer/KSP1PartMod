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
        /// <summary>The three native parts of 26w42c: the radial drill mines CarbonRegolith at 5.556e-5 units/s for 20 EC/s.</summary>
        private static readonly NativeIsruRef Native = NativeIsruRef.From(NativeIsruRefTests.NativeParts());
        /// <summary>The native drill's CarbonRegolith per EC/s.</summary>
        private const double CrPerEc = 5.556e-5 / 20;

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
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            var o = m.Outputs.Single();
            Assert.AreEqual("Ore", o.Ksp1Name);
            Assert.AreEqual("CarbonRegolith", o.Name);
            Assert.AreEqual(1.5, o.Efficiency);
            Assert.AreEqual(15, o.Ec);
            Assert.AreEqual(CrPerEc, o.NativePerEc, 1e-15);
            Assert.AreEqual("drill_2v_radial", o.NativeFrom);
            Assert.AreEqual(1, o.EfficiencyFactor, 1e-12, "1.5 / (0.1 × 15): a stock drill");
            Assert.AreEqual(CrPerEc * 15, o.Rate, 1e-15, "the native drill's yield per EC/s × 15 EC/s");
            Assert.AreEqual(CrPerEc, o.PerEc, 1e-15, "the same yield per EC/s as the native drill");
            Assert.AreEqual(CrPerEc * 15 / 0.05, o.Ksp1Rate, 1e-12, "in KSP1 Ore units (0.05 t) for the log");
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
            var m = HarvesterMapping.From(new[] { One(Drill(0.3, 3)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.AreEqual(1, m.Outputs.Single().EfficiencyFactor, 1e-12, "0.3 / (0.1 × 3): a stock drill");
            Assert.AreEqual(CrPerEc * 3, m.Outputs.Single().Rate, 1e-15);
            Assert.AreEqual(52.5, m.Formula.FluxGenerated, 1e-9);
            Assert.AreEqual(1.08, m.ImpactRange, 1e-12);
        }

        [Test] public void Type2_Skipped_NoModule()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1, 5, "Oxidizer", 2)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.IsFalse(m.HasModule);
            Assert.AreEqual(1, m.SkippedAtmospheric);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: HarvesterType 2 (atmospheric) skipped, Module_Mine only mines the ground"));
            Assert.AreEqual(1, HarvesterMapping.From(new[] { One(Drill(1, 5, "Water", 1)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1).SkippedOcean);
            Assert.AreEqual(1, HarvesterMapping.From(new[] { One(Drill(1, 5, "Water", 3)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1).SkippedExospheric);
        }

        [Test] public void TwoType0_Merged_EcSummed_ImpactOfTheFirst()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1.5, 15, range: "5.42")), One(Drill(0.3, 3)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.AreEqual(18, m.Formula.EcInput);
            Assert.AreEqual(18, m.Formula.Inputs.Single().Rate);
            Assert.AreEqual(2, m.Outputs.Count, "one line per harvester");
            Assert.AreEqual("CarbonRegolith", m.Formula.Outputs.Single().Name, "outputs summed by resource");
            Assert.AreEqual(CrPerEc * 18, m.Formula.Outputs.Single().Rate, 1e-15);
            Assert.AreEqual(17.5 * 18, m.Formula.FluxGenerated, 1e-9);
            Assert.AreEqual(5.42, m.ImpactRange, 1e-12);
            Assert.AreEqual(2, m.Merged);
        }

        [Test] public void ResourceWithoutAMap_Skipped()
        {
            var m = HarvesterMapping.From(new[] { One(Drill(1, 5, "LiquidFuel")), One(Drill(1.5, 15)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(1, m.SkippedNoMap);
            Assert.AreEqual(15, m.Formula.EcInput, "the skipped harvester's EC is not counted");
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: LiquidFuel -> Methane has no resource map in KSP2, harvester skipped"));
        }

        [Test] public void OreAsTestRocks_NotMinable_NoModule()
        {
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table("TestRocks"), Native, 1, HeatPerEc, 1);
            Assert.IsFalse(m.HasModule);
            Assert.AreEqual(1, m.SkippedNoMap);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: Ore -> TestRocks has no resource map in KSP2, harvester skipped"));
        }

        [Test] public void HeatScale0_NoHeat_NoGeneratesHeat_NoHeat()
        {
            Assert.AreEqual(0, HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Native, 1, HeatPerEc, 0).Formula.FluxGenerated);
            var cold = HarvesterMapping.From(new[] { One("ResourceName = Ore\nEfficiency = 1\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 2\n}") },
                IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.AreEqual(0, cold.Formula.FluxGenerated);
            Assert.AreEqual("", cold.ImpactTransform);
            Assert.AreEqual(0, cold.ImpactRange);
        }

        [Test] public void MultiplierScalesTheRate_BadMultiplier_Default()
        {
            var m = HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Native, 2, HeatPerEc, 1);
            Assert.AreEqual(CrPerEc * 30, m.Outputs.Single().Rate, 1e-15);
            Assert.AreEqual(2, m.Multiplier);
            foreach (var bad in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                Assert.AreEqual(1, HarvesterMapping.From(Harvesters(RadialDrill), IsruFakes.Table(), Native, bad, HeatPerEc, 1).Multiplier, bad.ToString());
        }

        /// <summary>Ruling of 2026-10-28: the yield follows the native drill per EC/s, scaled by KSP1's Efficiency per EC/s
        /// against the stock drills' 0.1.</summary>
        [Test] public void EfficiencyPerEc_ScalesAgainstTheStockDrills()
        {
            var twice = HarvesterMapping.From(new[] { One(Drill(3, 15)) }, IsruFakes.Table(), Native, 1, HeatPerEc, 1).Outputs.Single();
            Assert.AreEqual(2, twice.EfficiencyFactor, 1e-12, "3 / (0.1 × 15)");
            Assert.AreEqual(CrPerEc * 15 * 2, twice.Rate, 1e-15);
            Assert.AreEqual(CrPerEc * 2, twice.PerEc, 1e-15);

            var noEfficiency = HarvesterMapping.From(new[] { One("ResourceName = Ore\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 4\n}") },
                IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.AreEqual(1, noEfficiency.Outputs.Single().EfficiencyFactor);
            Assert.AreEqual(CrPerEc * 4, noEfficiency.Outputs.Single().Rate, 1e-15);
            Assert.That(noEfficiency.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: Ore: Efficiency missing, the KSP2 drill's yield per EC/s used as is"));

            var noEc = HarvesterMapping.From(new[] { One("ResourceName = Ore\nEfficiency = 0.5") }, IsruFakes.Table(), Native, 1, HeatPerEc, 1);
            Assert.AreEqual(CrPerEc * 5, noEc.Outputs.Single().Rate, 1e-15, "0.5 / 0.1: as a stock drill of 5 EC/s");
            Assert.AreEqual(0, noEc.Outputs.Single().PerEc);
            Assert.That(noEc.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: Ore: no ElectricCharge input, yield from Efficiency alone (the KSP2 drill's per EC/s x Efficiency / 0.1)"));
        }

        [Test] public void YieldPerEc_AnotherNativeDrill_ThenTheMedian()
        {
            (string, double) R(string n, double v) => (n, v);
            var native = NativeIsruRef.From(new[]
            {
                NativeIsruFormula.Mine("big", new[] { R("ElectricCharge", 20) }, new[] { R("Water", 2e-5), R("Ammonia", 6e-5) }, 350),
                NativeIsruFormula.Mine("small", new[] { R("ElectricCharge", 10) }, new[] { R("CarbonRegolith", 5e-5) }, 100),
                NativeIsruFormula.Mine("tiny", new[] { R("ElectricCharge", 10) }, new[] { R("Uranium", 1e-6) }, 400),
            });
            Assert.AreEqual("big", native.Drill.Part, "median by heat per EC/s: 10, 17.5, 40");
            CollectionAssert.AreEqual(new[] { "Water", "Ammonia", "CarbonRegolith", "Uranium" }, native.MinableFromDrill, "every native drill");
            var cr = HarvesterMapping.From(new[] { One(Drill(1.5, 15)) }, IsruFakes.Table(), native, 1, HeatPerEc, 1).Outputs.Single();
            Assert.AreEqual("small", cr.NativeFrom);
            Assert.AreEqual(5e-6, cr.NativePerEc, 1e-15);
            var noNative = NativeIsruRef.From(new[] { NativeIsruFormula.Mine("big", new[] { R("ElectricCharge", 20) },
                new[] { R("Water", 2e-5), R("Ammonia", 6e-5), R("Uranium", 4e-5) }, 350) });
            var m = HarvesterMapping.From(new[] { One(Drill(1.5, 15)) }, IsruFakes.Table(), noNative, 1, HeatPerEc, 1);
            var median = m.Outputs.Single();
            Assert.IsNull(median.NativeFrom);
            Assert.AreEqual(2e-6, median.NativePerEc, 1e-15, "the median of 1e-6, 2e-6 and 3e-6 per EC/s");
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceHarvester: Ore -> CarbonRegolith: no KSP2 drill mines it, the KSP2 drill's median yield per EC/s used"));
        }

        [Test] public void AsteroidAndCometDrills_CountedFromThePart()
        {
            var part = CfgText.Parse(RadialDrill).GetNodes("PART").First().GetNodes("MODULE").ToList();
            Assert.AreEqual(1, HarvesterMapping.CountAsteroidDrills(part));
        }
    }
}
