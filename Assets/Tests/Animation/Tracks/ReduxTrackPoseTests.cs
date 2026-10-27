using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class ReduxTrackPoseTests
    {
        [Test]
        public void ReduxPose_EnabledState_StateTimeAndWeight()
        {
            var p = ReduxTrackPose.For(true, 0.3f, 0.5f, 1f);
            Assert.AreEqual((0.3f, 0.5f, true), (p.NormalizedTime, p.Weight, p.Enabled));
        }

        /// <summary>After Sample(extended) the state is disabled with the transforms extended: the tracks follow the
        /// runtime's NormalizedTime (1), not nothing.</summary>
        [Test]
        public void ReduxPose_DisabledState_RuntimeTimeFullWeightEnabled()
        {
            var p = ReduxTrackPose.For(false, 0f, 0f, 1f);
            Assert.AreEqual((1f, 1f, true), (p.NormalizedTime, p.Weight, p.Enabled));
        }

        [Test]
        public void ReduxPose_NoState_RuntimeTime()
        {
            var p = ReduxTrackPose.For(0.25f);
            Assert.AreEqual((0.25f, 1f, true), (p.NormalizedTime, p.Weight, p.Enabled));
        }

        [TestCase(0, false, TrackBindTime.Never)]
        [TestCase(0, true, TrackBindTime.Never)]
        [TestCase(3, true, TrackBindTime.Now)]
        [TestCase(3, false, TrackBindTime.Pending)]
        public void BindTiming(int generation, bool ready, TrackBindTime expected) =>
            Assert.AreEqual(expected, TrackBindTiming.For(generation, ready));
    }
}
