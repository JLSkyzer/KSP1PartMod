using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class GateStateTests
    {
        private static B9Gate Gate(string[] inactiveIn = null, string @default = "x", string setId = "s") => new B9Gate
        {
            SetId = setId, InactiveIn = inactiveIn ?? new[] { "off" }, DefaultSubtype = @default,
            SubtypeIds = new[] { "x", "off", "a", "b" },
        };

        private static GateState State() => new GateState(new[] { Gate() });

        [Test] public void Gate_DefaultSubtypeInactive_StartsInactive() =>
            Assert.IsFalse(new GateState(new[] { Gate(inactiveIn: new[] { "a" }, @default: "a") }).Active);

        [Test] public void Gate_DefaultSubtypeActive_StartsActive() => Assert.IsTrue(State().Active);

        [Test] public void Gate_NoGates_Active()
        {
            Assert.IsTrue(new GateState(null).Active);
            Assert.IsTrue(new GateState(new B9Gate[0]).Active);
        }

        [Test] public void Gate_SetInactiveSubtype_ReportsChange()
        {
            var s = State();
            Assert.IsTrue(s.Set(0, "off"));
            Assert.IsFalse(s.Active);
            Assert.IsTrue(s.Set(0, "a"));
            Assert.IsTrue(s.Active);
        }

        [Test] public void Gate_Null_MeansDefaultSubtype()
        {
            var s = new GateState(new[] { Gate(inactiveIn: new[] { "a" }, @default: "a") });
            Assert.IsTrue(s.Set(0, "b"));
            Assert.IsTrue(s.Set(0, null));
            Assert.IsFalse(s.Active);
        }

        [Test] public void Gate_ComparesOrdinally() => Assert.IsFalse(State().Set(0, "OFF"));

        [Test] public void Gate_TwoGates_ActiveOnlyWhenBothActive()
        {
            var s = new GateState(new[] { Gate(new[] { "a" }), Gate(new[] { "b" }) });
            Assert.IsTrue(s.Active);
            Assert.IsTrue(s.Set(0, "a"));
            Assert.IsFalse(s.Active);
            Assert.IsFalse(s.Set(1, "b"), "still inactive: no change");
            Assert.IsFalse(s.Set(0, "x"), "gate 1 still holds it");
            Assert.IsFalse(s.Active);
            Assert.IsTrue(s.Set(1, "x"));
            Assert.IsTrue(s.Active);
        }

        [Test] public void Gate_SameValue_NoChangeReported()
        {
            var s = State();
            s.Set(0, "x");
            Assert.IsFalse(s.Set(0, "x"));
        }

        [Test] public void Gate_OutOfRangeIndex_Ignored()
        {
            var s = State();
            Assert.IsFalse(s.Set(-1, "off"));
            Assert.IsFalse(s.Set(5, "off"));
            Assert.IsTrue(s.Active);
        }

        [Test] public void Gate_Check10000Frames_AllocatesZero()
        {
            // Gate 0 flips every frame (each flip changes Active) while gate 1 stays at its default; from frame 5000
            // gate 1 holds "b" (inactive), so no flip changes Active any more.
            var warm = new GateState(new[] { Gate(new[] { "off" }), Gate(new[] { "b" }) });
            var measured = new GateState(new[] { Gate(new[] { "off" }), Gate(new[] { "b" }) });
            var s = warm;
            int changes = 0, inactiveFrames = 0;
            void Frames()
            {
                changes = 0;
                inactiveFrames = 0;
                for (var i = 0; i < 10000; i++)
                {
                    if (s.Set(0, (i & 1) == 0 ? "off" : "x")) changes++;
                    if (s.Set(1, i < 5000 ? null : "b")) changes++;
                    if (!s.Active) inactiveFrames++;
                }
            }

            Frames(); // warm up (JIT)
            s = measured;
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(5000 + 1, changes); // frame 5000 flips gate 0 off before gate 1 takes "b"
            Assert.AreEqual(2500 + 5000, inactiveFrames);
        }
    }
}
