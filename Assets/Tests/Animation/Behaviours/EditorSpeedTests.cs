using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>The VAB speed (<c>editor_animation_speed</c>) and the Redux follower's own clock.</summary>
    public class EditorSpeedTests
    {
        private const float Real = EditorSpeed.RealTime;

        private static AnimEntryDef Gear(float inF = 1f) => new AnimEntryDef
        {
            Kind = EntryKind.Gear, Key = "gear:leg:0", Clip = "leg", EditorSpeedMult = EditorSpeed.Snap, SpeedFactorIn = inF,
        };

        private static AnimEntryDef Follower(float editor = EditorSpeed.Snap, float inF = 1f) => new AnimEntryDef
        {
            Kind = EntryKind.ReduxFollower, Key = "reduxfollower:XDeploy:0", Clip = "XDeploy", EditorSpeedMult = editor,
            SpeedFactorIn = inF, SpeedFactorOut = 1f,
        };

        /// <summary>Ticks until the pose reaches <paramref name="end"/>; the elapsed seconds.</summary>
        private static float TimeToReach(System.Func<float, float> tick, float end, float dt = 0.01f)
        {
            var elapsed = 0f;
            for (var i = 0; i < 100000; i++)
            {
                elapsed += dt;
                if (System.Math.Abs(tick(dt) - end) < 1e-6f) return elapsed;
            }
            return float.PositiveInfinity;
        }

        [Test] public void EditorSpeed_Default_IsRealTime()
        {
            Assert.AreEqual(1f, AnimationSettings.Defaults.EditorAnimationSpeed);
            Assert.AreEqual(1f, AnimationSettings.Parse("{}", out _).EditorAnimationSpeed);
        }

        [Test] public void EditorSpeed_Parse_ReadsNumber()
        {
            Assert.AreEqual(10f, AnimationSettings.Parse("{\"editor_animation_speed\": 10}", out var e1).EditorAnimationSpeed);
            Assert.IsNull(e1);
            Assert.AreEqual(0f, AnimationSettings.Parse("{\"editor_animation_speed\": 0}", out _).EditorAnimationSpeed);
            Assert.AreEqual(2.5f, AnimationSettings.Parse("{\"editor_animation_speed\": 2.5}", out _).EditorAnimationSpeed);
        }

        [TestCase("-1")] [TestCase("\"fast\"")] [TestCase("true")]
        public void EditorSpeed_Parse_BadValue_KeepsDefault(string value)
        {
            var s = AnimationSettings.Parse("{\"editor_animation_speed\": " + value + "}", out var error);
            Assert.AreEqual(1f, s.EditorAnimationSpeed);
            StringAssert.Contains("editor_animation_speed", error);
        }

        [Test] public void Multiplier_PositiveSettingWins_ZeroKeepsKsp1()
        {
            Assert.AreEqual(1f, EditorSpeed.Multiplier(1f, 1000f));
            Assert.AreEqual(1000f, EditorSpeed.Multiplier(0f, 1000f));
            Assert.AreEqual(10f, EditorSpeed.Multiplier(-2f, 10f));
        }

        [Test] public void Describe()
        {
            Assert.AreEqual("×1 of the flight speed", EditorSpeed.Describe(1f));
            Assert.AreEqual("instant", EditorSpeed.Describe(1000f));
            StringAssert.StartsWith("KSP1", EditorSpeed.Describe(0f));
        }

        // ---------------------------------------------------------------- Redux follower

        [Test] public void Follower_ReduxSnapsToExtended_StillTakesClipLength()
        {
            // Redux's empty Mecanim copy goes Extending -> Extended in one frame; the leg still takes its 4.125 s.
            var f = new ReduxFollower(Follower(), 4.125f, Real);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.01f, ReduxDeployState.Extending, true);
            var t = 0.01f + TimeToReach(dt => { f.Tick(dt, ReduxDeployState.Extended, true); return f.Pose.NormalizedTime; }, 1f);
            Assert.AreEqual(4.125f, t, 0.02f);
            Assert.AreEqual(0f, f.Pose.Speed);
        }

        [Test] public void Follower_ReduxSnapsToExtended_InFlight_TakesClipLengthTimesFactor()
        {
            var f = new ReduxFollower(Follower(inF: 0.5f), 2f, Real);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            var t = TimeToReach(dt => { f.Tick(dt, ReduxDeployState.Extended, false); return f.Pose.NormalizedTime; }, 1f);
            Assert.AreEqual(4f, t, 0.02f);
        }

        [Test] public void Follower_ReduxSnapsBackToRetracted_ReversesSmoothly()
        {
            var f = new ReduxFollower(Follower(), 1f, Real);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.6f, ReduxDeployState.Extended, true);
            f.Tick(0.1f, ReduxDeployState.Retracted, true);
            Assert.AreEqual(0.5f, f.Pose.NormalizedTime, 1e-5);
            Assert.Less(f.Pose.Speed, 0f);
        }

        [Test] public void Follower_LoadedExtended_SnapsWithoutAnimating()
        {
            var f = new ReduxFollower(Follower(), 4.125f, Real);
            Assert.IsTrue(f.Tick(0.02f, ReduxDeployState.Extended, true));
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
            Assert.AreEqual(0f, f.Pose.Speed);
            f.Tick(0.02f, ReduxDeployState.Extended, true);
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
        }

        [Test] public void Follower_Ksp1EditorSetting_KeepsSnap()
        {
            var f = new ReduxFollower(Follower(), 4.125f, EditorSpeed.Ksp1);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.01f, ReduxDeployState.Extending, true);
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
        }

        [Test] public void Follower_SettingTen_TenTimesFaster()
        {
            var f = new ReduxFollower(Follower(), 1f, 10f);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.05f, ReduxDeployState.Extended, true);
            Assert.AreEqual(0.5f, f.Pose.NormalizedTime, 1e-5);
        }

        // ---------------------------------------------------------------- our own entries

        [Test] public void Gear_Editor_DefaultSetting_RealTime()
        {
            var m = new DeployMachine(Gear(inF: 2f), 1f, Real);
            Assert.IsTrue(m.Extend(true));
            Assert.AreEqual(DeployState.Extending, m.State);
            m.Tick(0.25f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Gear_Editor_Ksp1Setting_Snaps()
        {
            var m = new DeployMachine(Gear(), 1f, EditorSpeed.Ksp1);
            m.Extend(true);
            Assert.AreEqual(DeployState.Extended, m.State);
        }

        [Test] public void Deploy_Editor_DefaultSetting_RealTime()
        {
            var def = new AnimEntryDef { Kind = EntryKind.Deploy, Key = "deploy:panel:0", Clip = "panel", EditorSpeedMult = 10f };
            var m = new DeployMachine(def, 2f, Real);
            m.Extend(true);
            m.Tick(1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Toggle_Editor_DefaultSetting_RealTime()
        {
            var def = new AnimEntryDef { Kind = EntryKind.Toggle, Key = "toggle:door:0", Clip = "door", EditorSpeedMult = 10f };
            var m = new ToggleMachine(def, 2f, Real);
            m.Toggle(true);
            m.Tick(1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-5);
            Assert.IsTrue(m.IsMoving);
        }

        [Test] public void Toggle_Editor_Ksp1Setting_TenthOfASecond()
        {
            var def = new AnimEntryDef { Kind = EntryKind.Toggle, Key = "toggle:door:0", Clip = "door", EditorSpeedMult = 10f };
            var m = new ToggleMachine(def, 2f, EditorSpeed.Ksp1);
            m.Toggle(true);
            m.Tick(0.05f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Group_Editor_DefaultSetting_RealTime()
        {
            var def = new AnimEntryDef { Kind = EntryKind.Group, Key = "group:dep:0", Clip = "dep", EditorSpeedMult = EditorSpeed.Snap };
            var m = new GroupMachine(def, 2f, 0f, Real);
            m.Deploy(true);
            m.Tick(1f, true);
            Assert.AreEqual(0.5f, m.DeployPose.NormalizedTime, 1e-5);
        }

        [Test] public void Flight_IgnoresEditorSetting()
        {
            var m = new DeployMachine(Gear(), 1f, 1000f);
            m.Extend(false);
            m.Tick(0.5f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-5);
        }
    }
}
