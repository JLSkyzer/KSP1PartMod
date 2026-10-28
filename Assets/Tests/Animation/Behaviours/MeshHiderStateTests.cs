using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class MeshHiderStateTests
    {
        /// <summary>Objects as booleans; counts the SetActive calls.</summary>
        private sealed class Fake : IHiderObjects
        {
            public readonly bool[] Active;
            public int Calls;

            public Fake(params bool[] active) => Active = active;

            public int Count => Active.Length;

            public bool ActiveSelf(int i) => Active[i];

            public void SetActive(int i, bool active)
            {
                Calls++;
                Active[i] = active;
            }
        }

        [Test] public void Flight_HidesWhileRetracted_SetActiveOnlyOnChange()
        {
            var o = new Fake(true, true);
            var h = new MeshHiderState(2);
            Assert.IsTrue(h.Step(true, true, o), "retracted at load: hide");
            CollectionAssert.AreEqual(new[] { false, false }, o.Active);
            Assert.AreEqual(2, o.Calls);
            Assert.IsFalse(h.Step(true, true, o), "no change, no SetActive");
            Assert.AreEqual(2, o.Calls);
            Assert.IsTrue(h.Step(true, false, o), "extending: show");
            CollectionAssert.AreEqual(new[] { true, true }, o.Active);
            Assert.IsFalse(h.Step(true, false, o));
            Assert.AreEqual(4, o.Calls);
        }

        [Test] public void B9HidX_RetractThenExtend_XStaysHidden()
        {
            var o = new Fake(false, true); // X (index 0) hidden by a B9 variant
            var h = new MeshHiderState(2);
            h.Step(true, true, o);
            Assert.IsFalse(h.HidByUs(0), "X was already inactive: not ours");
            Assert.IsTrue(h.HidByUs(1));
            h.Step(true, false, o);
            CollectionAssert.AreEqual(new[] { false, true }, o.Active, "X stays hidden, the other shows again");
            Assert.AreEqual(2, o.Calls, "X never touched");
        }

        [Test] public void B9HidX_Restore_XStaysHidden()
        {
            var o = new Fake(false, true);
            var h = new MeshHiderState(2);
            h.Step(true, true, o);
            Assert.IsTrue(h.Restore(o), "shutdown, disable or B9 gate closed");
            CollectionAssert.AreEqual(new[] { false, true }, o.Active);
            Assert.IsFalse(h.Restore(o), "once");
        }

        [Test] public void NeverShowsWhatItDidNotHide()
        {
            var o = new Fake(false);
            var h = new MeshHiderState(1);
            Assert.IsFalse(h.Step(true, false, o), "extended at load: left as is");
            Assert.IsFalse(h.Restore(o));
            Assert.AreEqual(0, o.Calls);
        }

        [Test] public void Editor_NeverHides()
        {
            var o = new Fake(true);
            var h = new MeshHiderState(1);
            Assert.IsFalse(h.Step(false, true, o));
            Assert.IsFalse(h.Hiding);
            Assert.AreEqual(0, o.Calls);
        }

        [Test] public void Restore_ThenHidesAgainOnStep()
        {
            var o = new Fake(true);
            var h = new MeshHiderState(1);
            h.Step(true, true, o);
            h.Restore(o);
            Assert.IsTrue(o.Active[0]);
            Assert.IsTrue(h.Step(true, true, o), "gate opened again while retracted: hide again");
            Assert.IsFalse(o.Active[0]);
        }

        [Test] public void FewerObjectsThanSlots_NoThrow()
        {
            var h = new MeshHiderState(3);
            Assert.DoesNotThrow(() => h.Step(true, true, new Fake(true)));
            Assert.DoesNotThrow(() => h.Restore(null));
        }

        [Test] public void Step_10000Frames_AllocatesZero()
        {
            var o = new Fake(true, false, true);
            var h = new MeshHiderState(3);
            var i = 0;
            Assert.That(() =>
            {
                for (var k = 0; k < 10000; k++, i++) h.Step((i & 1) == 0, (i / 7 & 1) == 0, o);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
