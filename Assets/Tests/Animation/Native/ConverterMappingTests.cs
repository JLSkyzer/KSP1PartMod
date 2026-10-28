using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="ConverterMapping"/>: the KSP1 converters of a part → one <c>Module_ResourceConverter</c>, one
    /// formula per converter (plan (j)). Cfg values written for the tests, in the shape of the stock files.</summary>
    public class ConverterMappingTests
    {
        private const double HeatPerEc = 10.5;

        /// <summary>A stock-like ISRU: four converters, the same heat and thermal fields.</summary>
        private const string Isru = @"
PART
{
    name = testIsru
    MODULE
    {
        name = ModuleResourceConverter
        ConverterName = Lf+Ox // comment
        AutoShutdown = true
        GeneratesHeat = true
        DefaultShutoffTemp = .8
        UseSpecialistBonus = true
        SpecialistEfficiencyFactor = 0.2
        EfficiencyBonus = 1
        TemperatureModifier
        {
            key = 0 100000
        }
        ThermalEfficiency
        {
            key = 1000 1.0 0 0
        }
        INPUT_RESOURCE
        {
            ResourceName = Ore
            Ratio = 0.5
            FlowMode = STAGE_PRIORITY_FLOW
        }
        INPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 30
        }
        OUTPUT_RESOURCE
        {
            ResourceName = LiquidFuel
            Ratio = 0.45
            DumpExcess = false
        }
        OUTPUT_RESOURCE
        {
            ResourceName = Oxidizer
            Ratio = 0.55
            DumpExcess = false
        }
    }
    MODULE
    {
        name = ModuleResourceConverter
        ConverterName = Monoprop
        AutoShutdown = true
        GeneratesHeat = true
        INPUT_RESOURCE
        {
            ResourceName = Ore
            Ratio = 0.5
        }
        INPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 30
        }
        OUTPUT_RESOURCE
        {
            ResourceName = MonoPropellant
            Ratio = 1
        }
    }
    MODULE
    {
        name = ModuleResourceConverter
        ConverterName = LiquidFuel
        AutoShutdown = true
        GeneratesHeat = true
        INPUT_RESOURCE
        {
            ResourceName = Ore
            Ratio = 0.45
        }
        INPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 30
        }
        OUTPUT_RESOURCE
        {
            ResourceName = LiquidFuel
            Ratio = 0.9
        }
    }
    MODULE
    {
        name = ModuleResourceConverter
        ConverterName = Oxidizer
        AutoShutdown = true
        GeneratesHeat = true
        INPUT_RESOURCE
        {
            ResourceName = Ore
            Ratio = 0.55
        }
        INPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 30
        }
        OUTPUT_RESOURCE
        {
            ResourceName = Oxidizer
            Ratio = 1.1
        }
    }
}";

        /// <summary>A BDB-like fuel cell: MonoPropellant → EC, no heat.</summary>
        private const string FuelCell = @"
PART
{
    name = testFuelCell
    MODULE
    {
        name = ModuleResourceConverter
        ConverterName = Fuel Cell
        FillAmount = 0.95
        AutoShutdown = false
        GeneratesHeat = false
        UseSpecialistBonus = false
        INPUT_RESOURCE
        {
            ResourceName = MonoPropellant
            Ratio = 0.0046875
            FlowMode = STAGE_PRIORITY_FLOW
        }
        OUTPUT_RESOURCE
        {
            ResourceName = ElectricCharge
            Ratio = 0.75
            DumpExcess = false
        }
    }
}";

        private static System.Collections.Generic.List<IKsp1Node> Converters(string cfg) =>
            IsruFakes.Modules(cfg, ConverterMapping.Ksp1Module);

        private static IKsp1Node One(string body) =>
            Converters("PART\n{\nname = p\nMODULE\n{\nname = ModuleResourceConverter\n" + body + "\n}\n}\n")[0];

        [Test] public void StockIsru_FourModes_LfOxConvertedByMass()
        {
            var m = ConverterMapping.From(Converters(Isru), 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(4, m.Formulas.Count);
            Assert.AreEqual(4, m.SourceCount);
            CollectionAssert.AreEqual(new[] { "ksp1_0", "ksp1_1", "ksp1_2", "ksp1_3" }, m.Formulas.Select(f => f.Id));
            CollectionAssert.AreEqual(new[] { "Lf+Ox", "Monoprop", "LiquidFuel", "Oxidizer" }, m.Formulas.Select(f => f.LabelSource));

            var lfox = m.Formulas[0];
            CollectionAssert.AreEqual(new[] { "CarbonRegolith", "ElectricCharge" }, lfox.Inputs.Select(f => f.Name));
            Assert.AreEqual(0.025, lfox.Inputs[0].Rate, 1e-12, "Ore 0.5 at 0.05 t → 0.025 CarbonRegolith");
            Assert.AreEqual(30, lfox.Inputs[1].Rate, "EC is never converted");
            CollectionAssert.AreEqual(new[] { "Methane", "Oxidizer" }, lfox.Outputs.Select(f => f.Name));
            Assert.AreEqual(0.9, lfox.Outputs[0].Rate, 1e-12);
            Assert.AreEqual(0.55, lfox.Outputs[1].Rate, 1e-12);
            Assert.AreEqual(30, lfox.EcInput);
            Assert.AreEqual(315, lfox.FluxGenerated, 1e-9, "10.5 kW per EC/s × 30 EC/s");
            Assert.IsTrue(lfox.AutoShutdown);
            Assert.AreEqual(0.025, lfox.MassIn, 1e-12);
            Assert.AreEqual(0.005, lfox.MassOut, 1e-12);
            Assert.IsTrue(lfox.MassKnown);
            TestContext.WriteLine($"Lf+Ox: mass in {lfox.MassIn} t/s, out {lfox.MassOut} t/s");
            TestContext.WriteLine(IsruLines.ConverterMode("testIsru", lfox, 0));

            Assert.IsTrue(m.ResourceAutoShutdown);
            Assert.AreEqual(1.0, m.ConversionRate);
            Assert.AreEqual(0.9, m.AutoShutdownTemperatureRatio);
            Assert.IsFalse(m.ConvertByMass);
            Assert.AreEqual(0, m.DroppedFormulas);
            CollectionAssert.IsSubsetOf(new[]
            {
                "TemperatureModifier", "ThermalEfficiency", "DefaultShutoffTemp", "specialist bonus", "FlowMode",
                "several modes at once",
            }, m.NotReproduced);
            CollectionAssert.DoesNotContain(m.NotReproduced, "DumpExcess", "DumpExcess = false is KSP2's behaviour");
            CollectionAssert.DoesNotContain(m.NotReproduced, "EfficiencyBonus", "1 is the neutral value");
            Assert.AreEqual(m.NotReproduced.Count, m.NotReproduced.Distinct().Count(), "each field once per part");
            Assert.That(m.Notes, Has.Some.Contains("several modes at once"));
        }

        [Test] public void FuelCell_MonoToEc_NoHeat()
        {
            var m = ConverterMapping.From(Converters(FuelCell), 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            var f = m.Formulas.Single();
            Assert.AreEqual("Fuel Cell", f.LabelSource);
            Assert.AreEqual("MonoPropellant", f.Inputs.Single().Name);
            Assert.AreEqual(0.0046875, f.Inputs.Single().Rate, 1e-12);
            Assert.AreEqual("ElectricCharge", f.Outputs.Single().Name);
            Assert.AreEqual(0.75, f.Outputs.Single().Rate);
            Assert.AreEqual(0, f.FluxGenerated);
            Assert.AreEqual(0, f.EcInput);
            Assert.IsFalse(m.ResourceAutoShutdown);
            CollectionAssert.AreEquivalent(new[] { "FillAmount", "FlowMode" }, m.NotReproduced);
            Assert.That(m.Notes, Has.None.Contains("several modes"));
        }

        [Test] public void RequiredResource_IsAZeroRateInput()
        {
            var m = ConverterMapping.From(new[] { One(
                "ConverterName = Reactor\nREQUIRED_RESOURCE\n{\nResourceName = Ore\nRatio = 10\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 5\n}") }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            var f = m.Formulas.Single();
            var required = f.Inputs.Single();
            Assert.AreEqual("CarbonRegolith", required.Name);
            Assert.AreEqual(0, required.Rate);
            Assert.IsTrue(required.Required);
            Assert.AreEqual(0, f.MassIn, "a required resource is not consumed");
            CollectionAssert.Contains(m.NotReproduced, "REQUIRED_RESOURCE amount");
        }

        [Test] public void UnknownResource_DropsThatFormulaOnly()
        {
            var nodes = Converters(FuelCell).Concat(new[] { One(
                "ConverterName = Karbonite\nINPUT_RESOURCE\n{\nResourceName = Karbonite\nRatio = 1\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = LiquidFuel\nRatio = 1\n}") }).ToList();
            var m = ConverterMapping.From(nodes, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsTrue(m.HasModule);
            Assert.AreEqual(1, m.Formulas.Count);
            Assert.AreEqual("ksp1_0", m.Formulas[0].Id);
            Assert.AreEqual(1, m.DroppedFormulas);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceConverter 'Karbonite': Karbonite -> Karbonite is not a game resource, mode dropped"));
        }

        [Test] public void AllFormulasDropped_NoModule()
        {
            var m = ConverterMapping.From(new[] { One(
                "ConverterName = X\nINPUT_RESOURCE\n{\nResourceName = Karbonite\nRatio = 1\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = LiquidFuel\nRatio = 1\n}") }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsFalse(m.HasModule);
            Assert.AreEqual(0, m.Formulas.Count);
            Assert.AreEqual(1, m.DroppedFormulas);
            Assert.That(m.Notes, Has.Some.Contains("no mode left, no " + ConverterMapping.NativeModule));
        }

        [Test] public void LaterCall_NoModule_NoNote()
        {
            var m = ConverterMapping.From(Converters(Isru), 1, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsTrue(m.LaterCall);
            Assert.IsFalse(m.HasModule);
            CollectionAssert.IsEmpty(m.Notes);
            CollectionAssert.IsEmpty(m.Formulas);
        }

        [Test] public void B9_CalledByReference_KeepsItsPlace()
        {
            var nodes = Converters(Isru);
            var m = ConverterMapping.From(nodes, 0, nodes[2], IsruFakes.Table(), HeatPerEc, 1);
            Assert.AreEqual(4, m.Formulas.Count);
            Assert.AreEqual(2, m.CalledIndex);
            Assert.AreEqual(CalledMatch.Reference, m.CalledMatch);
        }

        [Test] public void B9_EditedCopy_ReplacesByConverterName()
        {
            var nodes = Converters(Isru);
            // A B9 edited copy of the Lf+Ox converter: a separate node with the same ConverterName.
            var edited = One("ConverterName = Lf+Ox\nAutoShutdown = true\nGeneratesHeat = true\n" +
                "INPUT_RESOURCE\n{\nResourceName = Ore\nRatio = 0.5\n}\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 30\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = LiquidFuel\nRatio = 0.9\n}\nOUTPUT_RESOURCE\n{\nResourceName = Oxidizer\nRatio = 0.55\n}");
            var m = ConverterMapping.From(nodes, 0, edited, IsruFakes.Table(), HeatPerEc, 1);
            Assert.AreEqual(4, m.Formulas.Count, "the edited node replaces its entry");
            Assert.AreEqual(0, m.CalledIndex);
            Assert.AreEqual(CalledMatch.ConverterName, m.CalledMatch);
            Assert.AreEqual(1.8, m.Formulas[0].Outputs[0].Rate, 1e-12, "LF 0.9 → Methane 1.8");
            Assert.AreEqual(1.8, m.Formulas[2].Outputs[0].Rate, 1e-12, "the LiquidFuel mode is unchanged");
        }

        [Test] public void B9_UnmatchedNode_Appended()
        {
            var nodes = Converters(Isru);
            var extra = Converters(FuelCell)[0];
            var m = ConverterMapping.From(nodes, 0, extra, IsruFakes.Table(), HeatPerEc, 1);
            Assert.AreEqual(5, m.Formulas.Count);
            Assert.AreEqual(4, m.CalledIndex);
            Assert.AreEqual(CalledMatch.Appended, m.CalledMatch);
            Assert.AreEqual("ksp1_4", m.Formulas[4].Id);
        }

        [Test] public void GeneratesHeat_WithoutEc_ZeroAndNoted()
        {
            var m = ConverterMapping.From(new[] { One(
                "ConverterName = Hot\nGeneratesHeat = true\nINPUT_RESOURCE\n{\nResourceName = MonoPropellant\nRatio = 1\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 10\n}") }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.AreEqual(0, m.Formulas.Single().FluxGenerated);
            Assert.That(m.Notes, Has.Some.EqualTo(
                "ModuleResourceConverter 'Hot': GeneratesHeat without an ElectricCharge input, no heat (KSP2's heat is per EC/s)"));
        }

        [Test] public void HeatScale0_NoHeat_Scale2_Double()
        {
            Assert.AreEqual(0, ConverterMapping.From(Converters(Isru), 0, null, IsruFakes.Table(), HeatPerEc, 0).Formulas[0].FluxGenerated);
            Assert.AreEqual(630, ConverterMapping.From(Converters(Isru), 0, null, IsruFakes.Table(), HeatPerEc, 2).Formulas[0].FluxGenerated, 1e-9);
        }

        [Test] public void DumpExcessTrue_AlwaysActive_UnknownKey_Noted()
        {
            var m = ConverterMapping.From(new[] { One(
                "ConverterName = A\nAlwaysActive = true\nmysteryKey = 3\nINPUT_RESOURCE\n{\nResourceName = MonoPropellant\nRatio = 1\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 10\nDumpExcess = true\n}") }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            CollectionAssert.AreEquivalent(new[] { "AlwaysActive", "mysteryKey", "DumpExcess" }, m.NotReproduced);
        }

        [Test] public void BadRatio_DropsTheMode_MissingResourceName_IgnoresTheFlow()
        {
            var m = ConverterMapping.From(new[]
            {
                One("ConverterName = Bad\nINPUT_RESOURCE\n{\nResourceName = MonoPropellant\nRatio = x\n}\nOUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}"),
                One("ConverterName = NoName\nINPUT_RESOURCE\n{\nRatio = 1\n}\nOUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}"),
            }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.AreEqual(1, m.Formulas.Count);
            Assert.AreEqual("NoName", m.Formulas[0].LabelSource);
            CollectionAssert.IsEmpty(m.Formulas[0].Inputs);
            Assert.AreEqual(1, m.DroppedFormulas);
            Assert.That(m.Notes, Has.Some.Contains("Ratio 'x' is not a number of 0 or more, mode dropped"));
            Assert.That(m.Notes, Has.Some.Contains("INPUT_RESOURCE without ResourceName ignored"));
        }

        [Test] public void SameResourceTwice_Summed_MissingConverterName_LabelFromIndex()
        {
            var f = ConverterMapping.From(new[] { One(
                "INPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 2\n}\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 3\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = MonoPropellant\nRatio = 1\n}") }, 0, null, IsruFakes.Table(), HeatPerEc, 1).Formulas.Single();
            Assert.AreEqual(5, f.Inputs.Single().Rate);
            Assert.AreEqual(5, f.EcInput);
            Assert.AreEqual("Converter 1", f.LabelSource);
        }

        [Test] public void LabelKey_IsPerPartAndIndex() =>
            Assert.AreEqual("Parts/Ksp1Converter/ISRU/2", ConverterMapping.LabelKey("ISRU", 2));

        [Test] public void AutoShutdownDiffers_FirstKeptOneUsed_Noted()
        {
            var m = ConverterMapping.From(new[]
            {
                One("ConverterName = A\nAutoShutdown = false\nOUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}"),
                One("ConverterName = B\nAutoShutdown = true\nOUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}"),
            }, 0, null, IsruFakes.Table(), HeatPerEc, 1);
            Assert.IsFalse(m.ResourceAutoShutdown);
            Assert.That(m.Notes, Has.Some.Contains("AutoShutdown differs between converters"));
        }
    }
}
