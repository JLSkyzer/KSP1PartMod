using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>KSP1's FX setter (<c>FXModuleThrottleEffects</c>) and <c>ModuleColorChanger.SetScalar</c>.</summary>
    public class ScalarLinkTests
    {
        private static ScalarLink Link(float response = 0.5f) =>
            new ScalarLink(new AnimEntryDef { Kind = EntryKind.Color, Key = "color:_EmissiveColor:0", ScalarLinked = true, ScalarResponse = response });

        private static AnimEntryDef Def(bool engineState = false, bool output = false, bool throttle = false) =>
            new AnimEntryDef { Kind = EntryKind.Color, ScalarLinked = true, DependOnEngineState = engineState, DependOnOutput = output, DependOnThrottle = throttle };

        [Test] public void ScalarLink_Lerp_MatchesKsp1()
        {
            var s = Link(response: 0.5f);
            s.Tick(0.02f, false, 1f);
            Assert.AreEqual(0.25f, s.State, 1e-5f);
        }

        [Test] public void ScalarLink_LerpFactor_Clamped()
        {
            var s = Link(response: 0.5f);
            s.Tick(1f, false, 0.7f);
            Assert.AreEqual(0.7f, s.State, 1e-6f);
        }

        [Test] public void ScalarLink_SnapsToOneAbove0_99995()
        {
            var s = Link(response: 0.5f);
            // k = 0.5 × 25 × 0.079999 = 0.9999875: one step from 0 lands just above 0.99995, snapped to 1.
            s.Tick(0.079999f, false, 1f);
            Assert.AreEqual(1f, s.State);
        }

        [Test] public void ScalarLink_NoSnapBelow0_99995()
        {
            var s = Link(response: 0.5f);
            s.Tick(0.0799f, false, 1f);
            Assert.Less(s.State, 1f);
        }

        [Test] public void ScalarLink_SnapsToZeroBelow5e5()
        {
            var s = Link(response: 0.5f);
            s.Tick(1f, false, 1f);
            Assert.AreEqual(1f, s.State);
            s.Tick(0.079999f, false, 0f);
            Assert.AreEqual(0f, s.State);
        }

        [Test] public void ScalarLink_NoSnapWhenMovingAway()
        {
            // Rising toward 0.00001 from 0: under 5e-5 but rising, not snapped to 0 by the falling rule.
            var s = Link(response: 0.5f);
            s.Tick(1f, false, 0.00001f);
            Assert.AreEqual(0.00001f, s.State, 1e-9f);
        }

        [Test] public void ScalarLink_Editor_HoldsZero()
        {
            var s = Link();
            s.Tick(1f, true, 1f);
            Assert.AreEqual(0f, s.State);
        }

        [Test] public void ScalarLink_ZeroDt_NoChange()
        {
            var s = Link();
            s.Tick(0f, false, 1f);
            Assert.AreEqual(0f, s.State);
        }

        [Test] public void ScalarLink_NothingSaved()
        {
            var s = Link();
            s.Tick(0.02f, false, 1f);
            Assert.IsFalse(s.Changed);
            var into = new Dictionary<string, float>();
            s.Save(into);
            CollectionAssert.IsEmpty(into);
            s.Load(new Dictionary<string, float> { ["color:_EmissiveColor:0.on"] = 1f });
            Assert.AreEqual(0.25f, s.State, 1e-5f);
        }

        [Test] public void ScalarLink_10000Frames_AllocatesZero()
        {
            var s = Link();
            var def = Def(engineState: true, throttle: true);
            var i = 0;
            void Frames()
            {
                for (var k = 0; k < 10000; k++, i++)
                {
                    var target = ScalarLink.Target(def, (i & 63) / 63f, 0.5f, 1f, (i & 128) == 0);
                    s.Tick(0.02f, false, target);
                    _ = s.State;
                }
            }
            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }

        [Test] public void ScalarLink_AllocationCheck_DetectsAnAllocation()
        {
            object sink = null;
            Assert.That(() => { sink = new float[4]; }, Is.AllocatingGCMemory());
            Assert.IsNotNull(sink);
        }

        // --- target

        [Test] public void Target_DependOnOutput_ReadsOutput() => Assert.AreEqual(0.3f, ScalarLink.Target(Def(output: true, throttle: true), 0.9f, 0.3f, 0.6f, true));
        [Test] public void Target_DependOnThrottle_ReadsEngineThrottle() => Assert.AreEqual(0.9f, ScalarLink.Target(Def(throttle: true), 0.9f, 0.3f, 0.6f, true));
        [Test] public void Target_NoDependency_ReadsMainThrottle() => Assert.AreEqual(0.6f, ScalarLink.Target(Def(), 0.9f, 0.3f, 0.6f, false));
        [Test] public void Target_EngineStateNotOperational_Zero() => Assert.AreEqual(0f, ScalarLink.Target(Def(engineState: true, throttle: true), 0.9f, 0.3f, 0.6f, false));
        [Test] public void Target_EngineStateOperational_Reads() => Assert.AreEqual(0.6f, ScalarLink.Target(Def(engineState: true), 0.9f, 0.3f, 0.6f, true));
        [Test] public void Target_Clamped() => Assert.AreEqual((1f, 0f), (ScalarLink.Target(Def(output: true), 0f, 2f, 0f, true), ScalarLink.Target(Def(throttle: true), -1f, 0f, 0f, true)));
        [Test] public void Target_NaN_Zero() => Assert.AreEqual(0f, ScalarLink.Target(Def(), 0f, 0f, float.NaN, true));

        // --- ColorRamp.SetScalar

        private static ColorRamp Ramp() => new ColorRamp(new AnimEntryDef
        {
            Kind = EntryKind.Color, Key = "c", ScalarLinked = true,
            Curves = new[]
            {
                new float[] { 0, 0, 0, 0, 1, 1, 0, 0 },
                new float[] { 0, 0, float.NaN, float.NaN, 1, 0.5f, float.NaN, float.NaN },
                new float[0],
                new float[] { 0, 1, 0, 0 },
            },
        });

        private static Rgba CurveAt(float t)
        {
            var r = Ramp();
            r.SetScalar(t);
            return r.Color;
        }

        [Test] public void Color_SetScalar_ClampsAndSetsOn()
        {
            var c = Ramp();
            c.SetScalar(2f);
            Assert.AreEqual((true, CurveAt(1f)), (c.On, c.Color));
            c.SetScalar(0.4f);
            Assert.IsFalse(c.On);
        }

        [Test] public void Color_SetScalar_EvaluatesCurvesAtT()
        {
            var c = Ramp();
            c.SetScalar(1f);
            Assert.AreEqual((1f, 0.5f, 0f, 1f), (c.Color.R, c.Color.G, c.Color.B, c.Color.A));
            c.SetScalar(0f);
            Assert.AreEqual((0f, 0f, 0f, 1f), (c.Color.R, c.Color.G, c.Color.B, c.Color.A));
            Assert.AreEqual(0f, c.Level);
        }

        [Test] public void Color_SetScalar_OnAtHalf() { var c = Ramp(); c.SetScalar(0.5f); Assert.IsTrue(c.On); }

        [Test] public void Color_SetScalar_NoRamp()
        {
            var c = Ramp();
            c.SetScalar(1f);
            c.Tick(0.1f);
            Assert.AreEqual(1f, c.Level);
            c.SetScalar(0.2f);
            c.Tick(0.1f);
            Assert.AreEqual(0.2f, c.Level, 1e-6f);
        }

        [Test] public void Color_SetScalar_NaN_Zero() { var c = Ramp(); c.SetScalar(float.NaN); Assert.AreEqual((false, 0f), (c.On, c.Level)); }
    }
}
