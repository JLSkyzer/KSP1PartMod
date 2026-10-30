using System.Linq;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Wheels
{
    /// <summary>One wheel per part (plan (f)) and the refusal reasons.</summary>
    public class WheelPartRuleTests
    {
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(false, true, true, false)]
        public void ShouldConvertSub(bool baseConverted, bool scratch, bool already, bool expected) =>
            Assert.AreEqual(expected, WheelPartRule.ShouldConvertSub(baseConverted, scratch, already));

        [Test] public void IsFirstBase()
        {
            Assert.IsTrue(WheelPartRule.IsFirstBase(2, 2));
            Assert.IsFalse(WheelPartRule.IsFirstBase(5, 2));
        }

        [Test] public void RefuseSub_EachReason()
        {
            Assert.AreEqual(WheelRefusal.SwitchOff, WheelPartRule.RefuseSub(false, true, false, false));
            Assert.AreEqual(WheelRefusal.NoBase, WheelPartRule.RefuseSub(true, false, false, false));
            Assert.AreEqual(WheelRefusal.Second, WheelPartRule.RefuseSub(true, true, false, true));
            Assert.IsNull(WheelPartRule.RefuseSub(true, true, false, false));
            Assert.IsNull(WheelPartRule.RefuseSub(true, false, true, false), "a B9 scratch context needs no base");
        }

        [Test] public void RefuseBase_EachReason()
        {
            Assert.AreEqual(WheelRefusal.SwitchOff, WheelPartRule.RefuseBase(false, 0, 0));
            Assert.AreEqual(WheelRefusal.Second, WheelPartRule.RefuseBase(true, 3, 0));
            Assert.IsNull(WheelPartRule.RefuseBase(true, 0, 0));
        }

        [Test] public void Labels()
        {
            CollectionAssert.AreEqual(new[] { "no base", "second", "switch off", "mapping", "failed" },
                new[] { WheelRefusal.NoBase, WheelRefusal.Second, WheelRefusal.SwitchOff, WheelRefusal.Mapping, WheelRefusal.Failed }.Select(WheelPartRule.Label));
        }

        [Test] public void PartSummary_ConvertedAndRefused()
        {
            var s = new WheelPartSummary("roverWheel1");
            Assert.IsFalse(s.BaseConverted);
            Assert.AreEqual("none", s.KindsText());
            s.Convert(WheelKind.Base);
            s.Convert(WheelKind.Suspension);
            s.Convert(WheelKind.Suspension);
            s.Refuse(WheelKind.Steering, WheelRefusal.SwitchOff);
            s.Refuse(WheelKind.Motor, WheelRefusal.Mapping);
            Assert.IsTrue(s.BaseConverted);
            Assert.IsTrue(s.Has(WheelKind.Suspension));
            Assert.AreEqual("base, suspension", s.KindsText());
            Assert.AreEqual(2, s.Refused.Count);
            Assert.AreEqual((WheelKind.Motor, WheelRefusal.Mapping), s.Refused[1]);
        }

        /// <summary>Review of Tasks 1+2: KSP2's <c>GetComponent&lt;Module_WheelMotor&gt;</c> finds a <c>Module_WheelMotorSteering</c>
        /// too, so the two KSP1 motors share one slot.</summary>
        [Test] public void MotorAndMotorSteering_ShareOneSlot()
        {
            Assert.AreEqual(WheelKind.Motor, WheelPartRule.Slot(WheelKind.MotorSteering));
            Assert.AreEqual(WheelKind.Motor, WheelPartRule.Slot(WheelKind.Motor));
            Assert.AreEqual(WheelKind.Brakes, WheelPartRule.Slot(WheelKind.Brakes));
            var s = new WheelPartSummary("roverWheel3");
            s.Convert(WheelKind.Base);
            s.Convert(WheelKind.MotorSteering);
            Assert.IsTrue(s.HasSlot(WheelKind.Motor));
            Assert.IsFalse(s.Has(WheelKind.Motor));
            Assert.AreEqual(WheelRefusal.Second, WheelPartRule.RefuseSub(true, true, false, s.HasSlot(WheelKind.Motor)));
            Assert.IsFalse(s.HasSlot(WheelKind.Steering), "the plain steering module is its own slot");
        }

        [Test] public void Tally_CountsKindsTypesReasonsAndFields()
        {
            var t = new WheelTally();
            t.AddBase(WheelTypeName.LEG);
            t.AddBase(WheelTypeName.LEG);
            t.AddBase(WheelTypeName.MOTORIZED);
            t.AddConverted(WheelKind.Lock);
            t.AddRefused(WheelRefusal.NoBase);
            t.AddRefused(WheelRefusal.NoBase);
            t.AddRefused(WheelRefusal.Mapping);
            t.AddNotes(new[] { "TooltipTitle" }, new[] { "springRatio" });
            t.AddNotes(new[] { "TooltipTitle", "steeringRange" }, null);
            Assert.AreEqual(3, t.Converted(WheelKind.Base));
            Assert.AreEqual(2, t.Bases(WheelTypeName.LEG));
            Assert.AreEqual(0, t.Bases(WheelTypeName.FREE));
            Assert.AreEqual(1, t.Converted(WheelKind.Lock));
            Assert.AreEqual(3, t.RefusedTotal);
            CollectionAssert.AreEqual(new[] { ("no base", 2), ("mapping", 1) }, t.RefusedByReason().ToList());
            CollectionAssert.AreEqual(new[] { ("TooltipTitle", 2), ("steeringRange", 1) }, WheelTally.ByCount(t.NotReproduced).ToList());
            Assert.AreEqual(1, t.UnusedBySolver["springRatio"]);
        }
    }
}
