using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class TimeSourceRuleTests
    {
        [TestCase(ClipOwner.OurEntry, TimeSource.EntryPose)] [TestCase(ClipOwner.ReduxRuntimeWithState, TimeSource.ReduxStateTime)]
        [TestCase(ClipOwner.ReduxRuntimeNoState, TimeSource.ReduxRuntimeNormalizedTime)] [TestCase(ClipOwner.ReduxSingleSourceNoState, TimeSource.FollowerClock)]
        public void TimeSource_PerOwner(ClipOwner o, TimeSource t) => Assert.AreEqual(t, TimeSourceRule.For(o));

        [Test] public void TimeSource_UnknownOwner_None() => Assert.AreEqual(TimeSource.None, TimeSourceRule.For((ClipOwner)99));

        [TestCase(true, false, BindMode.State)] [TestCase(true, true, BindMode.StateAndTracks)] [TestCase(false, true, BindMode.TracksOnly)] [TestCase(false, false, BindMode.NotBound)]
        public void TrackBinding_Modes(bool state, bool tracks, BindMode m) => Assert.AreEqual(m, TrackBindingRule.Mode(state, tracks));

        [Test] public void TrackBinding_StateAndTracks_KeepsStateOwner() => Assert.AreNotEqual(BindMode.TracksOnly, TrackBindingRule.Mode(true, true));
    }
}
