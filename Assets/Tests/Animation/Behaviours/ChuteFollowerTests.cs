using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class ChuteFollowerTests
    {
        // semi clip 2 s at 0.5 (4 s), full clip 3 s at 0.25 (12 s)
        private static ChuteFollower Make() => new ChuteFollower(2f, 3f, 0.5f, 0.25f);

        [Test] public void ByteValuesMatchDataParachute() =>
            Assert.AreEqual((0, 1, 2, 3, 4), ((int)ReduxChuteState.Stowed, (int)ReduxChuteState.Armed,
                (int)ReduxChuteState.SemiDeployed, (int)ReduxChuteState.Deployed, (int)ReduxChuteState.Cut));

        [Test] public void Stowed_SemiAtZero_FullOff()
        {
            var c = Make();
            c.Tick(0.1f, ReduxChuteState.Stowed);
            Assert.AreEqual((true, 0f, false), (c.SemiPose.Enabled, c.SemiPose.NormalizedTime, c.FullPose.Enabled));
        }

        [Test] public void Armed_LikeStowed()
        {
            var c = Make();
            c.Tick(0.1f, ReduxChuteState.Armed);
            Assert.AreEqual((true, 0f, false), (c.SemiPose.Enabled, c.SemiPose.NormalizedTime, c.FullPose.Enabled));
        }

        [Test] public void SemiDeployed_PlaysSemiAtItsSpeed()
        {
            var c = Make();
            c.Tick(0f, ReduxChuteState.Armed);
            Assert.IsTrue(c.Tick(1f, ReduxChuteState.SemiDeployed));
            Assert.AreEqual(0.25f, c.SemiPose.NormalizedTime, 1e-5);
            c.Tick(10f, ReduxChuteState.SemiDeployed);
            Assert.AreEqual(1f, c.SemiPose.NormalizedTime);
            Assert.IsFalse(c.FullPose.Enabled);
        }

        [Test] public void Deployed_PlaysFullFromZero_SemiOff()
        {
            var c = Make();
            c.Tick(0f, ReduxChuteState.Armed);
            c.Tick(10f, ReduxChuteState.SemiDeployed);
            c.Tick(3f, ReduxChuteState.Deployed);
            Assert.AreEqual(0.25f, c.FullPose.NormalizedTime, 1e-5);
            Assert.IsFalse(c.SemiPose.Enabled);
        }

        [Test] public void FirstTick_DeployedSnapsToEnd()
        {
            var c = Make();
            c.Tick(0.1f, ReduxChuteState.Deployed);
            Assert.AreEqual((true, 1f), (c.FullPose.Enabled, c.FullPose.NormalizedTime));
        }

        [Test] public void FirstTick_SemiSnapsToEnd()
        {
            var c = Make();
            c.Tick(0.1f, ReduxChuteState.SemiDeployed);
            Assert.AreEqual(1f, c.SemiPose.NormalizedTime);
        }

        [Test] public void Cut_BothOff()
        {
            var c = Make();
            c.Tick(0f, ReduxChuteState.Deployed);
            c.Tick(0.1f, ReduxChuteState.Cut);
            Assert.AreEqual((false, false), (c.SemiPose.Enabled, c.FullPose.Enabled));
        }

        [Test] public void Repacked_BackToStowedPose()
        {
            var c = Make();
            c.Tick(0f, ReduxChuteState.Deployed);
            c.Tick(0.1f, ReduxChuteState.Stowed);
            Assert.AreEqual((true, 0f, false), (c.SemiPose.Enabled, c.SemiPose.NormalizedTime, c.FullPose.Enabled));
        }

        [Test] public void ZeroSpeed_StillEnds()
        {
            var c = new ChuteFollower(1f, 1f, 0f, 0f);
            c.Tick(0f, ReduxChuteState.Armed);
            c.Tick(1e6f, ReduxChuteState.SemiDeployed);
            Assert.Greater(c.SemiPose.NormalizedTime, 0f);
        }
    }
}
