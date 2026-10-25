using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class ToggleMachineTests
    {
        private static ToggleMachine Toggle(string clip = "a", float len = 1f, bool oneShot = false, float editorMult = 10f) =>
            new ToggleMachine(new AnimEntryDef
            {
                Kind = EntryKind.Toggle, Key = "toggle:" + clip + ":0", Clip = clip, Layer = 1,
                Retractable = !oneShot, EditorSpeedMult = editorMult,
            }, len);

        private static Dictionary<string, float> Saved(float t, bool closed, bool moving) =>
            SavedValues.Of("toggle:a:0", ("t", t), ("closed", SavedValues.B(closed)), ("moving", SavedValues.B(moving)));

        /// <summary>Ticks in 0.02 s steps. The editor speed is chosen by Toggle(inEditor), so `editor` only documents
        /// the scene.</summary>
        private static void Run(ToggleMachine m, float seconds, bool editor = false)
        {
            for (; seconds > 1e-6f; seconds -= 0.02f) m.Tick(seconds < 0.02f ? seconds : 0.02f);
        }

        [Test] public void Toggle_Default_IsClosedAtZero() { var m = Toggle(); Assert.IsTrue(m.Closed); Assert.AreEqual(0f, m.Pose.NormalizedTime); }

        [Test] public void Toggle_InFlight_TakesClipLength()
        {
            var m = Toggle(len: 2f); m.Toggle(false); Run(m, 1.9f); Assert.IsTrue(m.IsMoving);
            Run(m, 0.2f); Assert.AreEqual((1f, false), (m.Pose.NormalizedTime, m.IsMoving));
        }

        [Test] public void Toggle_InEditor_ReachesEndIn0_1s()
        {
            var m = Toggle(len: 4f); m.Toggle(true); Run(m, 0.11f, editor: true); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Toggle_InEditor_NotInstant_UsesFlightSpeed()
        {
            var m = Toggle(len: 4f, editorMult: 0f); m.Toggle(true); Run(m, 1f, editor: true);
            Assert.AreEqual(0.25f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void Toggle_SavedMoving_Resumes()
        {
            var m = Toggle(); m.Load(Saved(t: 0.4f, closed: false, moving: true)); Assert.Greater(m.Pose.Speed, 0f);
        }

        [Test] public void Toggle_SavedMovingClosing_ResumesBackward()
        {
            var m = Toggle(); m.Load(Saved(t: 0.4f, closed: true, moving: true)); Run(m, 1f);
            Assert.AreEqual((0f, false, true), (m.Pose.NormalizedTime, m.IsMoving, m.Closed));
        }

        [Test] public void Toggle_TwoEntries_DoNotShareState()
        {
            var a = Toggle("a"); var b = Toggle("b"); a.Toggle(false); Run(a, 5); Assert.AreEqual(0f, b.Pose.NormalizedTime);
        }

        [Test] public void Toggle_Flips_AnimSwitch_ThenCloses()
        {
            var m = Toggle(); m.Toggle(false); Assert.IsFalse(m.Closed); Run(m, 2);
            Assert.IsTrue(m.Toggle(false)); Assert.IsTrue(m.Closed); Assert.Less(m.Pose.Speed, 0f);
            Run(m, 2); Assert.AreEqual(0f, m.Pose.NormalizedTime);
        }

        [Test] public void Toggle_WhileMoving_Refused()
        {
            var m = Toggle(); m.Toggle(false); Run(m, 0.1f); Assert.IsFalse(m.Toggle(false)); Assert.IsFalse(m.Closed);
        }

        [Test] public void Toggle_OneShot_FixedOnceOpen()
        {
            var m = Toggle(oneShot: true); Assert.IsTrue(m.Toggle(false)); Run(m, 2);
            Assert.IsFalse(m.Toggle(false)); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Toggle_Pose_HoldsWithWeightOne()
        {
            var p = Toggle().Pose; Assert.AreEqual((true, 1f, 0f), (p.Enabled, p.Weight, p.Speed));
        }

        [Test] public void Toggle_SaveLoad_RoundTrips()
        {
            var m = Toggle(len: 2f); m.Toggle(false); Run(m, 0.5f);
            var saved = new Dictionary<string, float>(); m.Save(saved);
            var n = Toggle(len: 2f); n.Load(saved);
            Assert.AreEqual((m.Pose.NormalizedTime, m.Closed, m.IsMoving), (n.Pose.NormalizedTime, n.Closed, n.IsMoving));
            CollectionAssert.AreEquivalent(new[] { "toggle:a:0.t", "toggle:a:0.closed", "toggle:a:0.moving" }, saved.Keys);
        }

        [Test] public void Toggle_LoadEmpty_KeepsDefaults()
        {
            var m = Toggle(); m.Load(new Dictionary<string, float>());
            Assert.AreEqual((true, false, 0f), (m.Closed, m.IsMoving, m.Pose.NormalizedTime));
        }

        [Test] public void Toggle_ZeroLengthClip_EndsInOneTick()
        {
            var m = Toggle(len: 0f); m.Toggle(false); m.Tick(0.02f);
            Assert.AreEqual((1f, false), (m.Pose.NormalizedTime, m.IsMoving));
        }
    }
}
