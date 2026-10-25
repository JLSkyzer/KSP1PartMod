using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class ReduxFollowerTests
    {
        private static ReduxFollower Make(float len = 2f, float editor = 10f, bool forward = true, float inF = 1f, float outF = 1f) =>
            new ReduxFollower(new AnimEntryDef
            {
                Kind = EntryKind.ReduxFollower, Key = "reduxfollower:door:0", Clip = "door",
                EditorSpeedMult = editor, DeployForward = forward, SpeedFactorIn = inF, SpeedFactorOut = outF,
            }, len);

        [TestCase(ReduxDeployState.Retracted, false)]
        [TestCase(ReduxDeployState.Extending, true)]
        [TestCase(ReduxDeployState.Extended, true)]
        [TestCase(ReduxDeployState.Retracting, false)]
        public void Target_FromReduxState(ReduxDeployState s, bool extended) => Assert.AreEqual(extended, ReduxFollower.Target(s));

        [Test] public void Target_Broken_Holds() => Assert.IsNull(ReduxFollower.Target(ReduxDeployState.Broken));

        [Test] public void Target_ByteValuesMatchDataDeployable()
        {
            // Data_Deployable.DeployState (Redux 26w42c): Retracted, Extended, Retracting, Extending, Broken.
            Assert.AreEqual((0, 1, 2, 3, 4), ((int)ReduxDeployState.Retracted, (int)ReduxDeployState.Extended,
                (int)ReduxDeployState.Retracting, (int)ReduxDeployState.Extending, (int)ReduxDeployState.Broken));
        }

        [Test] public void FirstTick_SnapsToReduxState()
        {
            var f = Make();
            Assert.IsTrue(f.Tick(0.1f, ReduxDeployState.Extended, false));
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
            Assert.IsTrue(f.Pose.Enabled);
        }

        [Test] public void Extending_AdvancesAtKsp1FlightSpeed()
        {
            var f = Make(len: 2f);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            Assert.IsTrue(f.Tick(0.5f, ReduxDeployState.Extending, false));
            Assert.AreEqual(0.25f, f.Pose.NormalizedTime, 1e-5);
            f.Tick(0.5f, ReduxDeployState.Extending, false);
            Assert.AreEqual(0.5f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void ReduxFlipsStraightToExtended_StillAnimates()
        {
            var f = Make(len: 1f);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            f.Tick(0.25f, ReduxDeployState.Extended, false);
            Assert.AreEqual(0.25f, f.Pose.NormalizedTime, 1e-5);
            f.Tick(5f, ReduxDeployState.Extended, false);
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
            Assert.AreEqual(0f, f.Pose.Speed);
        }

        [Test] public void Retract_ReversesFromCurrentTime()
        {
            var f = Make(len: 1f);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            f.Tick(0.6f, ReduxDeployState.Extending, false);
            f.Tick(0.2f, ReduxDeployState.Retracting, false);
            Assert.AreEqual(0.4f, f.Pose.NormalizedTime, 1e-5);
            Assert.Less(f.Pose.Speed, 0f);
        }

        [Test] public void Editor_UsesEditorMultiplier()
        {
            var f = Make(len: 1f, editor: 10f);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.05f, ReduxDeployState.Extending, true);
            Assert.AreEqual(0.5f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Editor_SnapMultiplier_Instant()
        {
            var f = Make(len: 1f, editor: 1000f);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.001f, ReduxDeployState.Extending, true);
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
        }

        [Test] public void Editor_ZeroMultiplier_FlightSpeed()
        {
            var f = Make(len: 1f, editor: 0f);
            f.Tick(0f, ReduxDeployState.Retracted, true);
            f.Tick(0.1f, ReduxDeployState.Extending, true);
            Assert.AreEqual(0.1f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Reversed_Gear_ExtendedIsTimeZero()
        {
            var f = Make(len: 1f, forward: false);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            Assert.AreEqual(1f, f.Pose.NormalizedTime);
            f.Tick(0.3f, ReduxDeployState.Extending, false);
            Assert.AreEqual(0.7f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void SpeedFactors_InAndOut()
        {
            var f = Make(len: 1f, inF: 2f, outF: 0.5f);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            f.Tick(0.25f, ReduxDeployState.Extending, false);
            Assert.AreEqual(0.5f, f.Pose.NormalizedTime, 1e-5);
            f.Tick(0.2f, ReduxDeployState.Retracting, false);
            Assert.AreEqual(0.4f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Broken_HoldsPose()
        {
            var f = Make(len: 1f);
            f.Tick(0f, ReduxDeployState.Retracted, false);
            f.Tick(0.3f, ReduxDeployState.Extending, false);
            Assert.IsFalse(f.Tick(1f, ReduxDeployState.Broken, false));
            Assert.AreEqual(0.3f, f.Pose.NormalizedTime, 1e-5);
        }

        [Test] public void Broken_First_NoPose()
        {
            var f = Make();
            f.Tick(0f, ReduxDeployState.Broken, false);
            Assert.IsFalse(f.Pose.Enabled);
        }

        [Test] public void NeverSaves()
        {
            var f = Make();
            f.Tick(1f, ReduxDeployState.Extending, false);
            Assert.IsFalse(f.Changed);
            var into = new System.Collections.Generic.Dictionary<string, float>();
            f.Save(into);
            CollectionAssert.IsEmpty(into);
        }
    }
}
