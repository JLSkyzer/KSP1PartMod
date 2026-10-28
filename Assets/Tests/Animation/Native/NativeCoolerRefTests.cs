using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="NativeCoolerRef"/>: the native kW removed per EC/s (plan (d)).</summary>
    public class NativeCoolerRefTests
    {
        /// <summary>The two native radiator parts of 26w42c.</summary>
        public static (string, double, double)[] NativeParts() => new[]
        {
            ("radiator_2v_radial_fixed", 200.0, 2.0),
            ("radiator_2v_radial_1x8", 500.0, 5.0),
        };

        [Test] public void NativeParts_100KwPerEc()
        {
            var r = NativeCoolerRef.From(NativeParts());
            Assert.IsFalse(r.IsFallback);
            Assert.AreEqual(100, r.KwPerEc, 1e-9);
            Assert.AreEqual(2, r.Count);
            CollectionAssert.AreEqual(new[] { "radiator_2v_radial_1x8", "radiator_2v_radial_fixed" }, r.Parts);
        }

        [Test] public void Empty_Fallback100()
        {
            foreach (var r in new[] { NativeCoolerRef.From(new (string, double, double)[0]), NativeCoolerRef.From(null), NativeCoolerRef.Fallback })
            {
                Assert.IsTrue(r.IsFallback);
                Assert.AreEqual(100, r.KwPerEc);
                Assert.AreEqual(0, r.Count);
            }
        }

        [Test] public void InvalidEntries_Ignored_MedianLowerOfEven()
        {
            var r = NativeCoolerRef.From(new[] { ("a", 100.0, 2.0), ("b", 300.0, 2.0), ("c", 0.0, 2.0), ("d", 50.0, 0.0), ("", 10.0, 1.0), ("e", double.NaN, 1.0) });
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(50, r.KwPerEc, 1e-9, "the lower one of 50 and 150");
            var three = NativeCoolerRef.From(new[] { ("a", 100.0, 1.0), ("b", 400.0, 1.0), ("c", 200.0, 1.0) });
            Assert.AreEqual(3, three.Count);
            Assert.AreEqual(200, three.KwPerEc, 1e-9);
        }
    }
}
