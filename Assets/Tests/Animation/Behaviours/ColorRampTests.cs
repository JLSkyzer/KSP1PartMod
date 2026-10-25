using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class ColorRampTests
    {
        private const string Key = "color:_EmissiveColor:0";

        private static ColorRamp ColorRamp(float rate = 1f, bool useRate = true, float[][] curves = null) =>
            new ColorRamp(new AnimEntryDef
            {
                Kind = EntryKind.Color, Key = Key, ShaderProperty = "_EmissiveColor", Rate = rate, UseRate = useRate,
                Curves = curves ?? new[]
                {
                    new float[] { 0, 0, 0, 0, 1, 1, 0, 0 },          // red 0 -> 1
                    new float[] { 0, 0, float.NaN, float.NaN, 1, 0.5f, float.NaN, float.NaN }, // green 0 -> 0.5
                    new float[0],                                     // blue: no curve
                    new float[] { 0, 1, 0, 0 },                       // alpha constant 1
                },
            });

        [Test] public void Color_UseRate_RampsAtRate()
        {
            var c = ColorRamp(rate: 0.8f); c.Toggle(); c.Tick(0.5f); Assert.AreEqual(0.4f, c.Level, 1e-4f);
        }

        [Test] public void Color_NoRate_Snaps()
        {
            var c = ColorRamp(useRate: false); c.Toggle(); c.Tick(0.01f); Assert.AreEqual(1f, c.Level);
        }

        [Test] public void Color_NoRate_SnapsOnToggle()
        {
            var c = ColorRamp(useRate: false); c.Toggle(); Assert.AreEqual(1f, c.Level);
        }

        [Test] public void Color_Ramp_ClampsAtEnds()
        {
            var c = ColorRamp(); c.Toggle(); c.Tick(5f); Assert.AreEqual(1f, c.Level);
            c.Toggle(); c.Tick(0.25f); Assert.AreEqual(0.75f, c.Level, 1e-5f); c.Tick(5f); Assert.AreEqual(0f, c.Level);
        }

        [Test] public void Color_Default_Off()
        {
            var c = ColorRamp(); Assert.AreEqual((false, 0f), (c.On, c.Level));
        }

        [Test] public void Color_EvaluatesCurvesAtLevel()
        {
            var c = ColorRamp(); c.Toggle(); c.Tick(1f);
            Assert.AreEqual((1f, 0.5f, 0f, 1f), (c.Color.R, c.Color.G, c.Color.B, c.Color.A));
        }

        [Test] public void Color_MissingChannel_IsZero()
        {
            var c = ColorRamp(curves: new[] { new float[] { 0, 0.3f, 0, 0 } }); c.Toggle(); c.Tick(1f);
            Assert.AreEqual((0.3f, 0f, 0f, 0f), (c.Color.R, c.Color.G, c.Color.B, c.Color.A));
        }

        [Test] public void Color_LoadOn_StartsAtOne()
        {
            var c = ColorRamp(); c.Load(SavedValues.Of(Key, ("on", 1f)));
            Assert.AreEqual((true, 1f, 1f), (c.On, c.Level, c.Color.R));
        }

        [Test] public void Color_SaveLoad_RoundTrips()
        {
            var c = ColorRamp(); c.Toggle(); var d = new Dictionary<string, float>(); c.Save(d);
            CollectionAssert.AreEquivalent(new[] { Key + ".on" }, d.Keys);
            var n = ColorRamp(); n.Load(d); Assert.IsTrue(n.On);
        }
    }
}
