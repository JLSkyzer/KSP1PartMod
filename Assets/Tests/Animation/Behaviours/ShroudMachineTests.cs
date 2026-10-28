using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Engine shrouds (0.7.0, Task 3): the per-entry state the runtime drives each frame and saves.</summary>
    public class ShroudMachineTests
    {
        private static ShroudMachine M(bool isFairing = true, bool ignoreNodes = false) =>
            new ShroudMachine(new AnimEntryDef { Kind = EntryKind.Shroud, Key = "shroud:j:0", IsFairing = isFairing, IgnoreNodes = ignoreNodes });

        // ---- editor ----

        [Test] public void Editor_ShowsTheSelectedNameWhileAttached()
        {
            var m = M();
            Assert.AreEqual(1, m.Step(true, true, true, 1));
            Assert.AreEqual(-1, m.Step(true, false, true, 1), "nothing attached below");
            Assert.AreEqual(-1, m.Step(true, true, false, 1), "B9 gate closed");
            Assert.AreEqual(-1, m.Step(true, true, true, -1), "no name enabled by the variants");
        }

        [Test] public void Editor_IgnoreNodes_AlwaysShown()
        {
            var m = M(ignoreNodes: true);
            Assert.AreEqual(0, m.Step(true, false, true, 0));
        }

        [Test] public void HideOverride_HidesAndIsSaved()
        {
            var m = M();
            Assert.IsTrue(m.SetHideOverride(true));
            Assert.IsFalse(m.SetHideOverride(true), "no change");
            Assert.IsTrue(m.Changed);
            Assert.AreEqual(-1, m.Step(true, true, true, 0));
            var saved = new Dictionary<string, float>();
            m.Save(saved);
            Assert.IsFalse(m.Changed);
            Assert.AreEqual(1f, saved["shroud:j:0.hide_override"]);
            Assert.AreEqual(0f, saved["shroud:j:0.jettisoned"]);
        }

        // ---- flight ----

        [Test] public void Flight_DetachAfterAttach_JettisonsOnce()
        {
            var m = M();
            Assert.AreEqual(0, m.Step(false, true, true, 0));
            for (var i = 0; i < ShroudMachine.DetachDebounce; i++) Assert.AreEqual(-1, m.Step(false, false, true, 0));
            Assert.IsTrue(m.Latch.Jettisoned);
            Assert.AreEqual(ShroudReason.BottomDetached, m.Latch.Reason);
            Assert.IsTrue(m.TakeJettisonEvent());
            Assert.IsFalse(m.TakeJettisonEvent(), "one event");
            Assert.IsTrue(m.Changed, "the jettison is saved");
            Assert.AreEqual(-1, m.Step(false, true, true, 0), "gone even if something docks below");
        }

        [Test] public void Flight_InactiveEntry_DoesNotLatch()
        {
            var m = M();
            m.Step(false, true, false, 0);
            for (var i = 0; i < 5; i++) m.Step(false, false, false, 0);
            Assert.IsFalse(m.Latch.Jettisoned);
        }

        [Test] public void Flight_StagedNonFairing_JettisonsWithReasonStaged()
        {
            var m = M(isFairing: false);
            Assert.AreEqual(0, m.Step(false, true, true, 0));
            m.OnStaged(true);
            Assert.IsTrue(m.TakeJettisonEvent());
            Assert.AreEqual(ShroudReason.Staged, m.Latch.Reason);
            Assert.AreEqual(-1, m.Step(false, true, true, 0));
        }

        [Test] public void Flight_StagedFairing_Stays()
        {
            var m = M();
            m.OnStaged(true);
            Assert.IsFalse(m.TakeJettisonEvent());
            Assert.AreEqual(0, m.Step(false, true, true, 0));
        }

        [Test] public void Load_Jettisoned_HidesFromTheFirstFlightStep_WithoutEvent()
        {
            var m = M();
            m.Load(new Dictionary<string, float> { ["shroud:j:0.jettisoned"] = 1f, ["shroud:j:0.hide_override"] = 0f });
            Assert.IsFalse(m.Changed);
            Assert.AreEqual(-1, m.Step(false, true, true, 0));
            Assert.IsFalse(m.TakeJettisonEvent(), "restored, not jettisoned now");
            Assert.AreEqual(ShroudReason.Restored, m.Latch.Reason);
            Assert.AreEqual(0, m.Step(true, true, true, 0), "the editor never hides a jettisoned shroud");
        }

        [Test] public void Load_Missing_KeepsDefaults()
        {
            var m = M();
            m.Load(new Dictionary<string, float>());
            Assert.IsFalse(m.HideOverride);
            Assert.IsFalse(m.Latch.Jettisoned);
        }

        // ---- rules used by the glue ----

        [TestCase(false, true, 1, 0, true)]
        [TestCase(true, true, 1, 0, false)]   // particles
        [TestCase(false, false, 1, 0, false)] // already off
        [TestCase(false, true, 2, 2, false)]  // every slot null: RenderFixes hides it
        [TestCase(false, true, 2, 1, true)]
        [TestCase(false, true, 0, 0, true)]
        public void OwnsRenderer(bool particle, bool enabled, int slots, int nulls, bool expected) =>
            Assert.AreEqual(expected, ShroudRule.OwnsRenderer(particle, enabled, slots, nulls));

        [TestCase(2, 2, true, false, true, false, false)]
        [TestCase(1, 2, true, false, true, true, false)]
        [TestCase(0, 2, false, false, false, true, true)]
        [TestCase(1, 1, false, true, true, false, false)] // ignoreNodes needs no node
        public void AuditNotes(int found, int names, bool node, bool ignore, bool kept, bool missing, bool noNode)
        {
            ShroudRule.AuditNotes(found, names, node, ignore, out var k, out var m, out var n);
            Assert.AreEqual(kept, k);
            Assert.AreEqual(missing, m);
            Assert.AreEqual(noNode, n);
        }

        // ---- allocation ----

        [Test] public void Step_10000Frames_DoesNotAllocate()
        {
            var m = M(isFairing: false);
            var shown = 0;
            void Frames()
            {
                for (var i = 0; i < 10000; i++)
                {
                    var attached = (i / 7 & 1) == 0;
                    if (m.Step((i & 3) == 0, attached, (i / 11 & 1) == 0, (i / 13 & 1) == 0 ? 0 : 1) >= 0) shown++;
                    if (ShroudRule.Visible(false, attached, false, m.Latch.Jettisoned, m.HideOverride, true)) shown++;
                    m.TakeJettisonEvent();
                }
            }

            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
            Assert.Greater(shown, 0);
        }
    }
}
