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

        [Test] public void Toggle_Changed_OnlyWhenASavedValueMoves()
        {
            var m = Toggle(); m.Tick(0.02f); Assert.IsFalse(m.Changed);
            m.Toggle(false); Assert.IsTrue(m.Changed);
            m.Save(new Dictionary<string, float>()); Assert.IsFalse(m.Changed);
            m.Tick(0.02f); Assert.IsTrue(m.Changed);
        }
    
        // ---- science toggle (DMModuleScienceAnimateGeneric): animSpeed, editor multiplier, oneWayAnimation

        private static ToggleMachine Science(float len = 2f, float animSpeed = 2f, float editorSetting = 1f, bool oneWay = false) =>
            new ToggleMachine(new AnimEntryDef
            {
                Kind = EntryKind.ScienceToggle, Key = "sciencetoggle:boom:0", Clip = "boom", EditorSpeedMult = 1f,
            }, len, EditorSpeed.Multiplier(editorSetting, 1f), animSpeed, oneWay);

        [Test] public void Science_Flight_RunsAtAnimSpeed()
        {
            var m = Science(len: 2f, animSpeed: 2f); m.Toggle(false); Run(m, 0.9f); Assert.IsTrue(m.IsMoving);
            Run(m, 0.2f); Assert.AreEqual((1f, false), (m.Pose.NormalizedTime, m.IsMoving));
        }

        [Test] public void Science_Editor_SettingTimesAnimSpeed()
        {
            var m = Science(len: 2f, animSpeed: 2f, editorSetting: 4f); m.Toggle(true); Run(m, 0.2f, editor: true); Assert.IsTrue(m.IsMoving);
            Run(m, 0.1f, editor: true); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Science_Editor_Ksp1Setting_RealSpeed()
        {
            // editor_animation_speed 0 means KSP1's own value, 1 for the science toggle: the flight speed.
            var m = Science(len: 2f, animSpeed: 1f, editorSetting: 0f); m.Toggle(true); Run(m, 1.9f, editor: true); Assert.IsTrue(m.IsMoving);
            Run(m, 0.2f, editor: true); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Science_OneWay_PlaysBackToClosed()
        {
            var m = Science(len: 1f, animSpeed: 1f, oneWay: true); m.Toggle(false); Run(m, 1.05f);
            Assert.IsTrue(m.Closed); Assert.IsTrue(m.IsMoving);
            Run(m, 1.05f); Assert.AreEqual((0f, false, true), (m.Pose.NormalizedTime, m.IsMoving, m.Closed));
            Assert.IsTrue(m.Toggle(false), "a one-way toggle can run again");
        }

        [Test] public void Science_NotOneWay_StaysOpen()
        {
            var m = Science(len: 1f, animSpeed: 1f); m.Toggle(false); Run(m, 1.5f);
            Assert.AreEqual((1f, false, false), (m.Pose.NormalizedTime, m.IsMoving, m.Closed));
        }

        [Test] public void Science_ZeroAnimSpeed_CountsAsOne()
        {
            var m = Science(len: 1f, animSpeed: 0f); m.Toggle(false); Run(m, 1.05f); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Science_ResumesMovingAtAnimSpeed()
        {
            var m = Science(len: 2f, animSpeed: 2f);
            m.Load(SavedValues.Of("sciencetoggle:boom:0", ("t", 0f), ("closed", SavedValues.B(false)), ("moving", SavedValues.B(true))));
            Run(m, 1.05f); Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }
    }
}
