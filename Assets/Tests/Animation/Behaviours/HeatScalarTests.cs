using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class HeatScalarTests
    {
        private static HeatScalar Heat(float[] curve = null) => new HeatScalar(new AnimEntryDef
        {
            Kind = EntryKind.Heat, Key = "heat:Cone_Heat:0", Clip = "Cone_Heat", Layer = 1,
            Curves = curve == null ? null : new[] { curve },
        });

        [TestCase(798f, 2000f, 0f)] [TestCase(1399f, 2000f, 0.5f)] [TestCase(500f, 2000f, 0f)]
        public void Heat_Scalar_MatchesKsp1(float t, float max, float expected)
        {
            var h = Heat(); h.Tick(0f, t, max); Assert.AreEqual(expected, h.Pose.NormalizedTime, 1e-3f);
        }

        [Test] public void Heat_AtOrAboveMaxTempPlusDraper_IsOne()
        {
            var h = Heat(); h.Tick(0f, 2798f, 2000f); Assert.AreEqual(1f, h.Scalar);
        }

        [Test] public void Heat_UpperClampQuirk_PassesAboveOne()
        {
            // T - 798 = 1902 < maxTemp 2000, so KSP1 divides instead of clamping: 1902 / 1202.
            var h = Heat(new float[] { 0, 0, 1, 1, 2, 2, 1, 1 }); h.Tick(0f, 2700f, 2000f);
            Assert.AreEqual(1902f / 1202f, h.Scalar, 1e-5f);
            Assert.AreEqual(1902f / 1202f, h.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void Heat_DefaultCurve_ClampsToOne()
        {
            var h = Heat(); h.Tick(0f, 2700f, 2000f); Assert.AreEqual(1f, h.Pose.NormalizedTime, 1e-6f);
        }

        [Test] public void Heat_Curve_Applied()
        {
            var h = Heat(new float[] { 0, 0, 0, 0, 1, 0.2f, 0, 0 }); h.Tick(0f, 1399f, 2000f);
            Assert.AreEqual(0.1f, h.Pose.NormalizedTime, 1e-3f);
        }

        [Test] public void Heat_NaNTemperature_ReadsAsZero()
        {
            var h = Heat(); h.Tick(0f, float.NaN, 2000f); Assert.AreEqual(0f, h.Pose.NormalizedTime);
        }

        [Test] public void Heat_SavesNothing()
        {
            var h = Heat(); h.Tick(0f, 1399f, 2000f); var d = new Dictionary<string, float>(); h.Save(d);
            Assert.AreEqual(0, d.Count);
        }

        [Test] public void Heat_Pose_EnabledAtSpeedZero()
        {
            var p = Heat().Pose; Assert.AreEqual((true, 0f, 1f), (p.Enabled, p.Speed, p.Weight));
        }
    }
}
