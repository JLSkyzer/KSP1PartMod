using System.Linq;
using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class FloatCurveTests
    {
        private static FloatCurve Curve(params (float t, float v, float i, float o)[] keys) =>
            new FloatCurve(keys.SelectMany(k => new[] { k.t, k.v, k.i, k.o }).ToArray());

        private const float N = float.NaN;

        [Test] public void FloatCurve_Hermite_MatchesUnityReference() =>
            Assert.AreEqual(0.5f, Curve((0, 0, 0, 0), (1, 1, 0, 0)).Evaluate(0.5f), 1e-4f);

        // Hermite on 0..1 with out tangent 3 at the first key and in tangent 0 at the second, sampled at 0.25:
        // h10 * 3 + h01 * 1 = 0.140625 * 3 + 0.15625 = 0.578125.
        [Test] public void FloatCurve_Hermite_UsesTangents() =>
            Assert.AreEqual(0.578125f, Curve((0, 0, 0, 3), (1, 1, 0, 0)).Evaluate(0.25f), 1e-5f);

        [Test] public void FloatCurve_NaNTangents_AutoSmooth()
        {
            var c = Curve((0, 0, N, N), (1, 1, N, N), (2, 0, N, N));
            Assert.AreEqual(1f, c.Evaluate(1f), 1e-5f);
            Assert.AreEqual(c.Evaluate(0.5f), c.Evaluate(1.5f), 1e-5f);
            Assert.AreEqual(0.625f, c.Evaluate(0.5f), 1e-5f);
        }

        [Test] public void FloatCurve_TwoAutoKeys_AreLinear() =>
            Assert.AreEqual(0.3f, Curve((0, 0, N, N), (1, 1, N, N)).Evaluate(0.3f), 1e-5f);

        [Test] public void FloatCurve_OutsideKeys_Clamped()
        {
            var c = Curve((0, 0.2f, 0, 0), (1, 0.8f, 0, 0));
            Assert.AreEqual((0.2f, 0.8f), (c.Evaluate(-5f), c.Evaluate(7f)));
        }

        [Test] public void FloatCurve_Empty_IsLinear01()
        {
            var c = new FloatCurve(new float[0]);
            Assert.AreEqual((0f, 0.4f, 1f), (c.Evaluate(-1f), c.Evaluate(0.4f), c.Evaluate(3f)));
        }

        [Test] public void FloatCurve_Null_IsLinear01() => Assert.AreEqual(0.7f, new FloatCurve(null).Evaluate(0.7f), 1e-6f);

        [Test] public void FloatCurve_SingleKey_IsConstant() => Assert.AreEqual(0.6f, Curve((2, 0.6f, 0, 0)).Evaluate(-3f));

        [Test] public void FloatCurve_UnsortedKeys_AreSorted() =>
            Assert.AreEqual(0.5f, Curve((1, 1, 0, 0), (0, 0, 0, 0)).Evaluate(0.5f), 1e-4f);

        [Test] public void FloatCurve_DuplicateTime_KeepsFirst() =>
            Assert.AreEqual(1f, Curve((0, 0, 0, 0), (1, 1, 0, 0), (1, 5, 0, 0)).Evaluate(1f));
    }
}
