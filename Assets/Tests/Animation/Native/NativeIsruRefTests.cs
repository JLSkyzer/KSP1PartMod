using System.Linq;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="NativeIsruRef"/>: the native drill, converters and scanner as data (plan (e)).</summary>
    public class NativeIsruRefTests
    {
        private static (string, double) R(string name, double rate) => (name, rate);

        /// <summary>The three native ISRU parts of 26w42c, as plan (e) lists them.</summary>
        public static NativeIsruFormula[] NativeParts() => new[]
        {
            NativeIsruFormula.Mine("drill_2v_radial", new[] { R("ElectricCharge", 20) },
                new[] { R("Water", 4.333e-5), R("Ammonia", 6.194e-5), R("CarbonRegolith", 5.556e-5), R("Uranium", 1e-5) }, 350),
            NativeIsruFormula.Converter("converter_2v_inline", "HydrogenISRU", new[] { R("ElectricCharge", 12), R("Water", 2e-5) },
                new[] { R("Hydrogen", 1.944e-6) }, 100),
            NativeIsruFormula.Converter("converter_2v_inline", "MethaloxISRU", new[] { R("ElectricCharge", 20), R("CarbonRegolith", 3.333e-5) },
                new[] { R("Methane", 4.444e-6), R("Oxidizer", 1.778e-5) }, 210),
            NativeIsruFormula.Converter("converter_2v_inline", "HydrazineISRU", new[] { R("ElectricCharge", 6), R("Ammonia", 2.167e-5) },
                new[] { R("MonoPropellant", 6.667e-6) }, 140),
            NativeIsruFormula.Scanner("scanner_1v_orbital", new[] { R("ElectricCharge", 14) },
                new[] { "Water", "CarbonRegolith", "Ammonia", "Uranium" }, 5, 0, 5000000, 80, 100),
        };

        [Test] public void NativeParts_Read()
        {
            var r = NativeIsruRef.From(NativeParts());
            Assert.IsFalse(r.Drill.IsFallback);
            Assert.AreEqual("drill_2v_radial", r.Drill.Part);
            Assert.AreEqual(20, r.Drill.Ec);
            Assert.AreEqual(17.5, r.Drill.HeatPerEc, 1e-12);
            Assert.AreEqual(5.556e-5, r.Drill.RateOf("CarbonRegolith"), 1e-15);
            Assert.AreEqual(0, r.Drill.RateOf("Ore"));
            CollectionAssert.AreEqual(new[] { "Water", "Ammonia", "CarbonRegolith", "Uranium" }, r.Drill.Outputs.Select(o => o.Name));

            Assert.IsFalse(r.ConvertersAreFallback);
            Assert.AreEqual(3, r.ConverterModes);
            Assert.AreEqual(10.5, r.ConverterHeatPerEc, 1e-12, "median of 8.3, 10.5, 23.3");

            Assert.IsFalse(r.Scanner.IsFallback);
            Assert.AreEqual("scanner_1v_orbital", r.Scanner.Part);
            Assert.AreEqual(14, r.Scanner.Ec);
            CollectionAssert.AreEqual(new[] { "Water", "CarbonRegolith", "Ammonia", "Uranium" }, r.Scanner.Resources);
            Assert.AreEqual(5, r.Scanner.TimeToComplete);
            Assert.AreEqual(5000000, r.Scanner.MaxAltitude);
            Assert.AreEqual(80, r.Scanner.MinInclination);
        }

        [Test] public void EmptyInput_Fallback()
        {
            var r = NativeIsruRef.From(new NativeIsruFormula[0]);
            Assert.IsTrue(r.Drill.IsFallback);
            Assert.AreEqual(17.5, r.Drill.HeatPerEc, 1e-12);
            Assert.AreEqual(20, r.Drill.Ec);
            Assert.AreEqual(4, r.Drill.Outputs.Count);
            Assert.IsTrue(r.ConvertersAreFallback);
            Assert.AreEqual(10.5, r.ConverterHeatPerEc);
            Assert.AreEqual(0, r.ConverterModes);
            Assert.IsTrue(r.Scanner.IsFallback);
            Assert.AreSame(NativeScannerRef.Fallback, r.Scanner);
            Assert.AreEqual(14, r.Scanner.Ec);
            CollectionAssert.AreEqual(new[] { "Water", "Ammonia", "CarbonRegolith", "Uranium" }, NativeIsruRef.FallbackMinable);
            Assert.AreSame(NativeIsruRef.Fallback.Scanner, NativeScannerRef.Fallback);
            Assert.IsTrue(NativeIsruRef.From(null).Drill.IsFallback);
        }

        [Test] public void InvalidEntries_Ignored_EvenConverterCount_LowerMedian()
        {
            var r = NativeIsruRef.From(new[]
            {
                NativeIsruFormula.Mine("noEc", new (string, double)[0], new[] { R("Water", 1) }, 100),
                NativeIsruFormula.Converter("a", "x", new[] { R("ElectricCharge", 10) }, new[] { R("Methane", 1) }, 100),
                NativeIsruFormula.Converter("a", "y", new[] { R("ElectricCharge", 10) }, new[] { R("Methane", 1) }, 200),
                NativeIsruFormula.Converter("b", "cold", new[] { R("ElectricCharge", 10) }, new[] { R("Methane", 1) }, 0),
                NativeIsruFormula.Converter("b", "fuel cell", new[] { R("MonoPropellant", 1) }, new[] { R("ElectricCharge", 1) }, 50),
                NativeIsruFormula.Scanner("s", new (string, double)[0], new string[0], 5, 0, 1, 0, 1),
            });
            Assert.IsTrue(r.Drill.IsFallback, "a drill without EC is not a reference");
            Assert.AreEqual(2, r.ConverterModes, "converters with EC in and heat only");
            Assert.AreEqual(10, r.ConverterHeatPerEc, 1e-12, "the lower middle of 10 and 20");
            Assert.IsTrue(r.Scanner.IsFallback, "a scanner without resources is not a reference");
        }

        [Test] public void MinableFromDrill_TheNativeDrillOutputs() =>
            CollectionAssert.AreEqual(new[] { "Water", "Ammonia", "CarbonRegolith", "Uranium" },
                NativeIsruRef.From(NativeParts()).MinableFromDrill);
    }
}
