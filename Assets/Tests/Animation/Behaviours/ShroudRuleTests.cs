using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Engine shrouds (0.7.0, Task 1): which name is shown, when the shroud is visible, and when it is jettisoned,
    /// after KSP1's <c>ModuleJettison</c> (plan finding (b)).</summary>
    public class ShroudRuleTests
    {
        private const int Debounce = 3;

        // ---- ShroudSelection ----

        [Test] public void Selection_Re12Variants()
        {
            // names [Shroud1p5x3, Shroud2x3]: variant Shroud enables the second, Bare the first
            Assert.AreEqual(1, ShroudSelection.Shown(new[] { false, true }));
            Assert.AreEqual(0, ShroudSelection.Shown(new[] { true, false }));
        }

        [Test] public void Selection_NoneEnabled_MinusOne() =>
            Assert.AreEqual(-1, ShroudSelection.Shown(new[] { false, false }));

        [Test] public void Selection_AllEnabled_GivesZero() =>
            Assert.AreEqual(0, ShroudSelection.Shown(new[] { true, true, true }));

        [Test] public void Selection_NullOrEmpty_MinusOne()
        {
            Assert.AreEqual(-1, ShroudSelection.Shown(null));
            Assert.AreEqual(-1, ShroudSelection.Shown(new bool[0]));
        }

        // ---- ShroudRule.Visible: (inEditor, attached, ignoreNodes, jettisoned, hideOverride, entryActive) ----

        [TestCase(true, true, false, false, false, true, ExpectedResult = true)]   // VAB, attached
        [TestCase(true, false, false, false, false, true, ExpectedResult = false)] // VAB, nothing below
        [TestCase(true, false, true, false, false, true, ExpectedResult = true)]   // VAB, ignoreNodes
        [TestCase(true, true, false, false, true, true, ExpectedResult = false)]   // VAB, hide override
        [TestCase(true, true, false, true, false, true, ExpectedResult = true)]    // VAB ignores a saved jettison
        [TestCase(false, true, false, false, false, true, ExpectedResult = true)]  // flight, attached
        [TestCase(false, true, false, true, false, true, ExpectedResult = false)]  // flight, jettisoned
        [TestCase(false, false, true, false, false, true, ExpectedResult = true)]  // flight, ignoreNodes
        [TestCase(false, false, true, true, false, true, ExpectedResult = false)]  // flight, ignoreNodes jettisoned
        [TestCase(false, true, false, false, true, true, ExpectedResult = false)]  // flight, hide override
        [TestCase(true, true, false, false, false, false, ExpectedResult = false)] // B9 gate closed
        [TestCase(false, true, true, false, false, false, ExpectedResult = false)]
        public bool Visible(bool inEditor, bool attached, bool ignoreNodes, bool jettisoned, bool hideOverride, bool entryActive) =>
            ShroudRule.Visible(inEditor, attached, ignoreNodes, jettisoned, hideOverride, entryActive);

        // ---- ShroudLatch: the KSP1 matrix ----

        private static ShroudLatch Latch(bool isFairing = true, bool checkBottomNode = false, bool ignoreNodes = false,
            bool manual = false, bool decouple = true) =>
            new ShroudLatch(isFairing, checkBottomNode, decouple, manual, ignoreNodes);

        /// <summary>Steps attached once, then detached for <paramref name="detachedSteps"/> steps.</summary>
        private static ShroudLatch Detached(ShroudLatch l, int detachedSteps = Debounce)
        {
            l.Step(true, true, Debounce);
            for (var i = 0; i < detachedSteps; i++) l.Step(true, false, Debounce);
            return l;
        }

        [Test] public void Detach_IsFairing_Latches()
        {
            var l = Detached(Latch());
            Assert.IsTrue(l.Jettisoned);
            Assert.AreEqual(ShroudReason.BottomDetached, l.Reason);
        }

        [Test] public void Detach_NotFairing_DecoupleEnabled_Latches() =>
            Assert.IsTrue(Detached(Latch(isFairing: false)).Jettisoned);

        [Test] public void Detach_NotFairing_DecoupleDisabled_DoesNotLatch() =>
            Assert.IsFalse(Detached(Latch(isFairing: false, decouple: false)).Jettisoned);

        [Test] public void Detach_IsFairing_IgnoresDecoupleEnabled() =>
            Assert.IsTrue(Detached(Latch(decouple: false)).Jettisoned);

        [Test] public void Detach_IgnoreNodesWithIsFairing_NeverLatches() =>
            Assert.IsFalse(Detached(Latch(ignoreNodes: true), 50).Jettisoned);

        [Test] public void Detach_IgnoreNodesNotFairing_Latches() =>
            Assert.IsTrue(Detached(Latch(isFairing: false, ignoreNodes: true)).Jettisoned);

        [Test] public void Detach_ManualJettison_StillLatchesOnDetach() =>
            Assert.IsTrue(Detached(Latch(manual: true)).Jettisoned);

        [Test] public void Detach_NeedsThreeConsecutiveSteps()
        {
            var l = Latch();
            l.Step(true, true, Debounce);
            Assert.IsFalse(l.Step(true, false, Debounce));
            Assert.IsFalse(l.Step(true, false, Debounce), "a 2-frame clear");
            Assert.IsTrue(l.Step(true, false, Debounce), "the third");
        }

        [Test] public void Detach_ATransientClearIsForgotten()
        {
            var l = Latch();
            l.Step(true, true, Debounce);
            l.Step(true, false, Debounce);
            l.Step(true, false, Debounce);
            l.Step(true, true, Debounce);
            l.Step(true, false, Debounce);
            l.Step(true, false, Debounce);
            Assert.IsFalse(l.Jettisoned);
            Assert.IsTrue(l.Step(true, false, Debounce));
        }

        [Test] public void Load_StartsDetached_NeverLatches()
        {
            var l = Latch();
            for (var i = 0; i < 100; i++) Assert.IsFalse(l.Step(true, false, Debounce));
            Assert.AreEqual(ShroudReason.None, l.Reason);
        }

        [Test] public void Editor_NeverLatches()
        {
            var l = Latch();
            for (var i = 0; i < 5; i++) l.Step(false, true, Debounce);
            for (var i = 0; i < 10; i++) Assert.IsFalse(l.Step(false, false, Debounce));
            Assert.IsFalse(l.Jettisoned);
        }

        [Test] public void Editor_AttachedIsNotSeenForTheFlightLatch()
        {
            var l = Latch();
            l.Step(false, true, Debounce);
            for (var i = 0; i < 10; i++) l.Step(true, false, Debounce);
            Assert.IsFalse(l.Jettisoned, "attached was never seen in flight");
        }

        [Test] public void Latch_IsOneWay()
        {
            var l = Detached(Latch());
            Assert.IsTrue(l.Step(true, true, Debounce));
            Assert.IsTrue(l.Jettisoned);
        }

        // ---- ShroudLatch.OnStaged: (isFairing, checkBottomNode, manual, attached) ----

        [TestCase(true, false, false, false, ExpectedResult = false)]  // isFairing: staging never hides it
        [TestCase(true, true, false, false, ExpectedResult = false)]
        [TestCase(false, false, false, false, ExpectedResult = true)]  // not a fairing: jettisoned by the stage
        [TestCase(false, false, false, true, ExpectedResult = true)]   // attached, but checkBottomNode is off
        [TestCase(false, true, false, false, ExpectedResult = true)]   // checkBottomNode, nothing attached
        [TestCase(false, true, false, true, ExpectedResult = false)]   // checkBottomNode, a part attached
        [TestCase(false, false, true, false, ExpectedResult = false)]  // manual
        [TestCase(false, true, true, false, ExpectedResult = false)]
        public bool Staged(bool isFairing, bool checkBottomNode, bool manual, bool attached) =>
            Latch(isFairing: isFairing, checkBottomNode: checkBottomNode, manual: manual).OnStaged(attached);

        [Test] public void Staged_RecordsTheReason()
        {
            var l = Latch(isFairing: false);
            Assert.IsTrue(l.OnStaged(true));
            Assert.AreEqual(ShroudReason.Staged, l.Reason);
        }

        [Test] public void Staged_IgnoreNodesNotFairing_Latches() =>
            Assert.IsTrue(Latch(isFairing: false, ignoreNodes: true).OnStaged(true));

        [Test] public void Staged_AfterJettison_KeepsTheFirstReason()
        {
            var l = Detached(Latch(isFairing: false));
            Assert.IsTrue(l.OnStaged(false));
            Assert.AreEqual(ShroudReason.BottomDetached, l.Reason);
        }

        // ---- restored state ----

        [Test] public void Restore_SavedJettison_StaysHiddenFromTheFirstStep()
        {
            var l = Latch();
            l.Restore(true);
            Assert.IsTrue(l.Jettisoned);
            Assert.AreEqual(ShroudReason.Restored, l.Reason);
            Assert.IsTrue(l.Step(true, true, Debounce));
            Assert.IsFalse(ShroudRule.Visible(false, true, false, l.Jettisoned, false, true));
        }

        [Test] public void Restore_NotJettisoned_ChangesNothing()
        {
            var l = Latch();
            l.Restore(false);
            Assert.IsFalse(l.Jettisoned);
        }

        // ---- allocation ----

        [Test] public void Step_AndVisible_DoNotAllocate()
        {
            var l = Latch();
            var shown = 0;
            Assert.That(() =>
            {
                for (var i = 0; i < 10000; i++)
                {
                    var attached = (i / 7 & 1) == 0;
                    l.Step(true, attached, Debounce);
                    if (ShroudRule.Visible(false, attached, false, l.Jettisoned, false, true)) shown++;
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.GreaterOrEqual(shown, 0);
        }
    }
}
