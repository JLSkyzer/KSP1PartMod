using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class MeshHiderStateTests
    {
        [Test] public void Flight_HidesWhileRetracted_SetActiveOnlyOnChange()
        {
            var h = new MeshHiderState();
            Assert.IsTrue(h.Step(true, true), "retracted at load: hide");
            Assert.IsTrue(h.Hiding);
            Assert.IsFalse(h.Step(true, true), "no change, no SetActive");
            Assert.IsTrue(h.Step(true, false), "extending: show");
            Assert.IsFalse(h.Hiding);
            Assert.IsFalse(h.Step(true, false));
            Assert.IsTrue(h.Step(true, true), "retracted again: hide");
        }

        [Test] public void NeverShowsWhatItDidNotHide()
        {
            var h = new MeshHiderState();
            Assert.IsFalse(h.Step(true, false), "extended at load: the objects are left as they are (a B9 variant may hide them)");
            Assert.IsFalse(h.Restore(), "nothing hidden by us: nothing to show");
        }

        [Test] public void Editor_NeverHides()
        {
            var h = new MeshHiderState();
            Assert.IsFalse(h.Step(false, true));
            Assert.IsFalse(h.Hiding);
        }

        [Test] public void Restore_ShowsWhatWeHid_Once_ThenHidesAgainOnStep()
        {
            var h = new MeshHiderState();
            h.Step(true, true);
            Assert.IsTrue(h.Restore(), "shutdown or B9 gate closed: SetActive(true)");
            Assert.IsFalse(h.Hiding);
            Assert.IsFalse(h.Restore());
            Assert.IsTrue(h.Step(true, true), "gate opened again while retracted: hide again");
        }

        [Test] public void Step_10000Frames_AllocatesZero()
        {
            var h = new MeshHiderState();
            var i = 0;
            Assert.That(() =>
            {
                for (var k = 0; k < 10000; k++, i++) h.Step((i & 1) == 0, (i / 7 & 1) == 0);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
