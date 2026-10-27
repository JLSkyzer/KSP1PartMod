using System.Linq;
using KSP1PartMod.Animation.Mu;
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

        private static MuAnimClip Clip(string name, params (string prop, int type, int keys)[] curves) =>
            new MuAnimClip(name, 0, curves.Select(c => new MuAnimCurve("", c.prop, c.type, 0, 0,
                Enumerable.Range(0, c.keys).Select(k => new MuKey(k, 0f, 0f, 0f, 0)).ToArray())).ToArray());

        [TestCase("m_LocalPosition.x")] [TestCase("m_LocalPosition.y")] [TestCase("m_LocalPosition.z")]
        [TestCase("m_LocalRotation.x")] [TestCase("m_LocalRotation.y")] [TestCase("m_LocalRotation.z")] [TestCase("m_LocalRotation.w")]
        [TestCase("m_LocalScale.x")] [TestCase("m_LocalScale.y")] [TestCase("m_LocalScale.z")]
        [TestCase("localEulerAnglesRaw.x")] [TestCase("localEulerAnglesRaw.y")] [TestCase("localEulerAnglesRaw.z")]
        public void HasState_KeptTransformProperty_True(string prop) => Assert.IsTrue(TrackBindingRule.HasState(Clip("c", (prop, 0, 2))));

        [Test] public void HasState_MaterialOnly_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip("c", ("_EmissiveColor.r", 1, 2), ("m_Intensity", 2, 2))));
        [Test] public void HasState_MaterialAndRotation_True() => Assert.IsTrue(TrackBindingRule.HasState(Clip("c", ("_EmissiveColor.r", 1, 2), ("m_LocalRotation.x", 0, 2))));
        [Test] public void HasState_TransformCurveWithoutKeys_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip("c", ("m_LocalRotation.x", 0, 0))));
        [Test] public void HasState_UnmappedProperty_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip("c", ("m_IsActive", 0, 2), ("localEulerAngles.x", 0, 2))));
        [Test] public void HasState_PropertyIsCaseSensitive_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip("c", ("m_localrotation.x", 0, 2))));
        [Test] public void HasState_BlankName_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip(" ", ("m_LocalRotation.x", 0, 2))));
        [Test] public void HasState_Null_False() => Assert.IsFalse(TrackBindingRule.HasState(null));
        [Test] public void HasState_NoCurves_False() => Assert.IsFalse(TrackBindingRule.HasState(Clip("c")));

        [Test] public void TrackBinding_StateAndTracks_KeepsStateOwner() => Assert.AreNotEqual(BindMode.TracksOnly, TrackBindingRule.Mode(true, true));
    }
}
