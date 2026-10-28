using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="ConverterPace"/> and its use by <see cref="ConverterMapping"/>: the final review's I1 ruling, the
    /// converters follow the drills' per-EC rule (a KSP1 mode consumes per EC/s what the KSP2 converter consumes).</summary>
    public class ConverterPaceTests
    {
        private const double HeatPerEc = 10.5;
        private static readonly NativeIsruFormula[] Native = NativeIsruRefTests.NativeParts().Where(f => f.Kind == NativeIsruKind.Converter).ToArray();

        private static IKsp1Node Mode(string body) =>
            IsruFakes.Modules("PART\n{\nname = p\nMODULE\n{\nname = ModuleResourceConverter\n" + body + "\n}\n}\n", ConverterMapping.Ksp1Module)[0];

        private static string In(string r, string ratio) => "INPUT_RESOURCE\n{\nResourceName = " + r + "\nRatio = " + ratio + "\n}\n";
        private static string Out(string r, string ratio) => "OUTPUT_RESOURCE\n{\nResourceName = " + r + "\nRatio = " + ratio + "\n}\n";

        private static readonly string LfOx = "ConverterName = Lf+Ox\nGeneratesHeat = true\n" + In("Ore", "0.5") + In("ElectricCharge", "30") +
                                              Out("LiquidFuel", "0.45") + Out("Oxidizer", "0.55");

        private static IsruFormula Paced(string body) =>
            ConverterMapping.From(new[] { Mode(body) }, 0, null, IsruFakes.Table(), HeatPerEc, 1, Native).Formulas.Single();

        [Test] public void StockLfOx_ScaledToMethaloxPerEc_RatiosEcHeatKept()
        {
            var raw = ConverterMapping.From(new[] { Mode(LfOx) }, 0, null, IsruFakes.Table(), HeatPerEc, 1).Formulas.Single();
            Assert.IsNull(raw.Pace, "no native list, no reason: no pace");
            var f = Paced(LfOx);
            var p = f.Pace;
            Assert.IsTrue(p.Scaled);
            Assert.AreEqual(ConverterPace.Tonnes, p.Unit);
            Assert.AreEqual("converter_2v_inline 'MethaloxISRU'", p.NativeFrom, "the native mode that consumes CarbonRegolith");
            Assert.IsFalse(p.FromAll);
            Assert.AreEqual(0.025 / 30, p.Ksp1PerEc, 1e-15, "0.025 t/s of CarbonRegolith for 30 EC/s");
            Assert.AreEqual(3.333e-5 / 20, p.NativePerEc, 1e-15);
            Assert.AreEqual(p.NativePerEc / p.Ksp1PerEc, p.Factor, 1e-15);
            TestContext.WriteLine($"Lf+Ox pace x{p.Factor} ({1 / p.Factor:0} times slower than KSP1)");
            Assert.AreEqual(30 * 3.333e-5 / 20, f.Inputs[0].Rate, 1e-15, "CarbonRegolith per EC/s = KSP2's");
            Assert.AreEqual(0.5, f.Inputs[0].Ksp1Ratio, "the KSP1 ratio is kept for the record");
            Assert.AreEqual(30, f.Inputs[1].Rate, "EC unchanged");
            Assert.AreEqual(raw.Outputs[0].Rate * p.Factor, f.Outputs[0].Rate, 1e-15);
            Assert.AreEqual(raw.Outputs[1].Rate * p.Factor, f.Outputs[1].Rate, 1e-15);
            Assert.AreEqual(raw.FluxGenerated, f.FluxGenerated, "heat follows the EC");
            Assert.AreEqual(raw.MassIn * p.Factor, f.MassIn, 1e-15, "totals recomputed");
            Assert.AreEqual(raw.MassOut * p.Factor, f.MassOut, 1e-15);
        }

        [Test] public void DrillAndConverter_BackNearKsp2sBalance()
        {
            // KSP1 RadialDrill under the drill ruling: 15 EC/s x the KSP2 drill's CarbonRegolith per EC/s, at density 1.
            var drill = 15 * 5.556e-5 / 20;
            var f = Paced(LfOx);
            var ratio = f.Inputs[0].Rate / drill;
            TestContext.WriteLine($"ISRU Lf+Ox eats {ratio:0.##} RadialDrill(s) of CarbonRegolith (was {0.025 / drill:0})");
            Assert.That(ratio, Is.InRange(0.5, 2.0), "about one drill per ISRU, as the KSP2 pair (0.6)");
        }

        [Test] public void NoEcInput_FuelCell_KeepsKsp1Rates()
        {
            var f = Paced("ConverterName = Fuel Cell\n" + In("MonoPropellant", "0.0046875") + Out("ElectricCharge", "0.75"));
            Assert.IsFalse(f.Pace.Scaled);
            Assert.AreEqual("no ElectricCharge input", f.Pace.KeptReason);
            Assert.AreEqual(0.75, f.Outputs.Single().Rate);
            StringAssert.EndsWith("; pace: KSP1 rates (no ElectricCharge input)", IsruLines.ConverterMode("p", f, 0));
        }

        [Test] public void OnlyEcConsumed_KeepsKsp1Rates()
        {
            var f = Paced("ConverterName = Gen\n" + In("ElectricCharge", "10") + Out("MonoPropellant", "1"));
            Assert.IsFalse(f.Pace.Scaled);
            Assert.AreEqual("no consumed resource", f.Pace.KeptReason);
        }

        [Test] public void RequiredOnly_NotConsumption()
        {
            var f = Paced("ConverterName = R\nREQUIRED_RESOURCE\n{\nResourceName = Ore\nRatio = 10\n}\n" + In("ElectricCharge", "5") + Out("MonoPropellant", "1"));
            Assert.AreEqual("no consumed resource", f.Pace.KeptReason);
        }

        [Test] public void NoNativeMatch_MedianOfEveryNativeMode_ByMass()
        {
            // Xenon is consumed by no native converter: the median of 1.67e-6, 1.67e-6 and 3.61e-6 t/s per EC/s.
            var f = Paced("ConverterName = X\n" + In("XenonGas", "1") + In("ElectricCharge", "10") + Out("MonoPropellant", "1"));
            var p = f.Pace;
            Assert.IsTrue(p.Scaled);
            Assert.IsTrue(p.FromAll);
            var perEc = new[] { 2e-5 / 12, 3.333e-5 / 20, 2.167e-5 / 6 }.OrderBy(v => v).ToArray();
            Assert.AreEqual(perEc[1], p.NativePerEc, 1e-15);
            StringAssert.Contains("median of every KSP2 converter mode", IsruLines.Pace(p));
        }

        [Test] public void UnknownMass_SameResource_ComparedInUnits_ElseKept()
        {
            var table = new IsruResources(n => n, (s, t, a) => a, n => true, null, n => double.NaN);
            var same = ConverterMapping.From(new[] { Mode("ConverterName = W\n" + In("Water", "0.1") + In("ElectricCharge", "12") + Out("Hydrogen", "1")) },
                0, null, table, HeatPerEc, 1, Native).Formulas.Single().Pace;
            Assert.IsTrue(same.Scaled);
            Assert.AreEqual(ConverterPace.Units, same.Unit);
            Assert.AreEqual((2e-5 / 12) / (0.1 / 12), same.Factor, 1e-12);
            var other = ConverterMapping.From(new[] { Mode("ConverterName = K\n" + In("Karbonite", "1") + In("ElectricCharge", "12") + Out("Hydrogen", "1")) },
                0, null, table, HeatPerEc, 1, Native).Formulas.Single().Pace;
            Assert.IsFalse(other.Scaled);
            StringAssert.StartsWith("a unit mass is unknown", other.KeptReason);
        }

        [Test] public void NoNativeConverter_Kept()
        {
            var f = ConverterMapping.From(new[] { Mode(LfOx) }, 0, null, IsruFakes.Table(), HeatPerEc, 1, new NativeIsruFormula[0]).Formulas.Single();
            Assert.AreEqual("no KSP2 converter to compare with", f.Pace.KeptReason);
            Assert.AreEqual(0.025, f.Inputs[0].Rate, 1e-15);
        }

        [Test] public void SwitchOn_KeptWithTheReason()
        {
            var f = ConverterMapping.From(new[] { Mode(LfOx) }, 0, null, IsruFakes.Table(), HeatPerEc, 1, null, "isru_ksp1_rates").Formulas.Single();
            Assert.IsFalse(f.Pace.Scaled);
            Assert.AreEqual(0.025, f.Inputs[0].Rate, 1e-15);
            Assert.AreEqual("; pace: KSP1 rates (isru_ksp1_rates)", IsruLines.Pace(f.Pace));
        }

        [Test] public void Line_FactorAndBothFiguresSideBySide()
        {
            var p = Paced(LfOx).Pace;
            Assert.AreEqual($"; pace x{IsruCfg.Inv(p.Factor)} (KSP2's rate per EC): consumes {IsruCfg.Inv(0.025 / 30)} t/s per EC/s in KSP1, " +
                            $"{IsruCfg.Inv(3.333e-5 / 20)} in KSP2 (converter_2v_inline 'MethaloxISRU')", IsruLines.Pace(p));
            Assert.AreEqual("", IsruLines.Pace(null));
        }

        [Test] public void NativeRef_ConverterFormulas_ReadOrFallback()
        {
            var read = NativeIsruRef.From(NativeIsruRefTests.NativeParts());
            Assert.IsFalse(read.ConverterFormulasAreFallback);
            CollectionAssert.AreEqual(new[] { "HydrogenISRU", "MethaloxISRU", "HydrazineISRU" }, read.ConverterFormulas.Select(f => f.Name));
            var none = NativeIsruRef.From(new NativeIsruFormula[0]);
            Assert.IsTrue(none.ConverterFormulasAreFallback);
            Assert.AreSame(NativeIsruRef.FallbackConverters, none.ConverterFormulas);
            Assert.IsTrue(NativeIsruRef.Fallback.ConverterFormulasAreFallback);
            var noEc = NativeIsruRef.From(new[] { NativeIsruFormula.Converter("x", "y", new[] { ("Water", 1.0) }, new[] { ("Hydrogen", 1.0) }, 10) });
            Assert.IsTrue(noEc.ConverterFormulasAreFallback, "a native mode without EC is no pace");
        }

        [Test] public void Ksp1Tags_LastTokenOnly()
        {
            Assert.IsTrue(Ksp1Tags.IsKsp1Import("KSP1"));
            Assert.IsTrue(Ksp1Tags.IsKsp1Import("drill Squad Utility KSP1"));
            Assert.IsTrue(Ksp1Tags.IsKsp1Import("ore tank KSP1 "));
            Assert.IsFalse(Ksp1Tags.IsKsp1Import("KSP1 drill"), "a KSP2 or mod part whose tags merely contain it");
            Assert.IsFalse(Ksp1Tags.IsKsp1Import("myKSP1"));
            Assert.IsFalse(Ksp1Tags.IsKsp1Import("ksp1"), "Redux writes it upper case");
            Assert.IsFalse(Ksp1Tags.IsKsp1Import(null));
            Assert.IsFalse(Ksp1Tags.IsKsp1Import(""));
        }
    }
}
