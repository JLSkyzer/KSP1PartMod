using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>The ablator char's scalar (0.8.0): the part's own fraction of the shield resource, through
    /// <see cref="ColorRamp.SetScalar"/>.</summary>
    public class AblatorScalarTests
    {
        /// <summary>The stock <c>shieldChar</c> curves: black at 0, white at 1, alpha 0.8.</summary>
        private static AnimEntryDef Char() => new AnimEntryDef
        {
            Kind = EntryKind.Color,
            Key = "color:_BurnColor:0",
            ScalarSource = ScalarSource.Ablator,
            ScalarLinked = true,
            UseRate = false,
            Curves = new[]
            {
                new[] { 0f, 0f, float.NaN, float.NaN, 1f, 1f, float.NaN, float.NaN },
                new[] { 0f, 0f, float.NaN, float.NaN, 1f, 1f, float.NaN, float.NaN },
                new[] { 0f, 0f, float.NaN, float.NaN, 1f, 1f, float.NaN, float.NaN },
                new[] { 0f, 0.8f, float.NaN, float.NaN },
            },
        };

        [Test] public void Fraction_StartsFull_FollowsStoredOverCapacity_Clamped()
        {
            var s = new AblatorScalar();
            Assert.AreEqual(1f, s.Fraction, "a fresh shield before any read");
            Assert.IsFalse(s.Tick(0.8, 0.8));
            Assert.IsTrue(s.Tick(0.2, 0.8));
            Assert.AreEqual(0.25f, s.Fraction, 1e-6f);
            Assert.IsTrue(s.Tick(-1, 0.8));
            Assert.AreEqual(0f, s.Fraction);
            s.Tick(2, 0.8);
            Assert.AreEqual(1f, s.Fraction);
        }

        [Test] public void NoCapacityOrNaN_KeepsTheLastValue()
        {
            var s = new AblatorScalar();
            s.Tick(0.4, 0.8);
            Assert.IsFalse(s.Tick(0, 0));
            Assert.IsFalse(s.Tick(1, -1));
            Assert.IsFalse(s.Tick(double.NaN, 1));
            Assert.IsFalse(s.Tick(1, double.NaN));
            Assert.IsFalse(s.Tick(1, double.PositiveInfinity));
            Assert.AreEqual(0.5f, s.Fraction, 1e-6f);
        }

        [Test] public void Colour_FullIsWhite_EmptyIsBlack_NothingSaved()
        {
            var ramp = new ColorRamp(Char());
            var s = new AblatorScalar();
            ramp.SetScalar(s.Fraction);
            AssertColor(1f, 1f, 1f, 0.8f, ramp.Color, "clean");
            s.Tick(0, 0.8);
            ramp.SetScalar(s.Fraction);
            AssertColor(0f, 0f, 0f, 0.8f, ramp.Color, "charred");

            var saved = new System.Collections.Generic.Dictionary<string, float>();
            ramp.Save(saved);
            CollectionAssert.IsEmpty(saved, "the fraction comes from the resource, never from the save");
            ramp.Tick(1f);
            Assert.AreEqual(0f, ramp.Level, "no ramp of its own");
        }

        private static void AssertColor(float r, float g, float b, float a, Rgba c, string what)
        {
            Assert.AreEqual(r, c.R, 1e-4f, what + " r");
            Assert.AreEqual(g, c.G, 1e-4f, what + " g");
            Assert.AreEqual(b, c.B, 1e-4f, what + " b");
            Assert.AreEqual(a, c.A, 1e-4f, what + " a");
        }

        /// <summary>The per-frame step of an Ablator entry: the scalar, then <see cref="ColorRamp.SetScalar"/>.</summary>
        [Test] public void AblatorStep_DoesNotAllocate()
        {
            var ramp = new ColorRamp(Char());
            var s = new AblatorScalar();
            var i = 0;
            void Frames()
            {
                for (var k = 0; k < 10000; k++, i++)
                {
                    s.Tick(0.8 - (i & 1023) * 0.0007, 0.8);
                    ramp.SetScalar(s.Fraction);
                    _ = ramp.Color;
                }
            }
            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }
    }
}
