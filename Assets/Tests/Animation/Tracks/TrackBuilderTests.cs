using System.Linq;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class TrackBuilderTests
    {
        private static MuAnimClip Clip(params (string path, string prop, int type)[] curves) =>
            new MuAnimClip("clip", 0, curves.Select(c => new MuAnimCurve(c.path, c.prop, c.type, 0, 0,
                new[] { new MuKey(0f, 0f, 0f, 0f, 0), new MuKey(1f, 1f, 0f, 0f, 0) })).ToArray());

        private static MuAnimClip Keys(params float[] times) =>
            new MuAnimClip("clip", 0, new[]
            {
                new MuAnimCurve("", "_Opacity", 1, 0, 0, times.Select(t => new MuKey(t, 0f, 0f, 0f, 0)).ToArray()),
            });

        private static TrackSet Build(params (string path, string prop, int type)[] curves) =>
            TrackBuilder.FromClip(Clip(curves), "", 0, out _);

        private static TrackSet Build(MuAnimClip clip) => TrackBuilder.FromClip(clip, "", 0, out _);

        private static FloatCurve Linear01() => new FloatCurve(new[] { 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f });

        [Test] public void Build_ColorChannels_GroupIntoOneTrack() { var s = Build(("Nozzle", "_EmissiveColor.r", 1), ("Nozzle", "_EmissiveColor.a", 1)); var t = s.Tracks.Single(); Assert.AreEqual((TrackKind.MaterialColor, "_EmissiveColor"), (t.Kind, t.Property)); Assert.IsNotNull(t.Channels[0]); Assert.IsNull(t.Channels[1]); Assert.IsNotNull(t.Channels[3]); }
        [Test] public void Build_VectorSuffix_IsMaterialVector() => Assert.AreEqual(TrackKind.MaterialVector, Build(("", "_BumpMap_ST.z", 1)).Tracks.Single().Kind);
        [Test] public void Build_NoSuffix_IsMaterialFloat() => Assert.AreEqual(TrackKind.MaterialFloat, Build(("", "_Opacity", 1)).Tracks.Single().Kind);
        [TestCase("m_Intensity", TrackKind.LightIntensity)] [TestCase("m_Color.g", TrackKind.LightColor)] [TestCase("m_Range", TrackKind.LightRange)] [TestCase("m_SpotAngle", TrackKind.LightSpotAngle)] [TestCase("m_Enabled", TrackKind.LightEnabled)]
        public void Build_LightProperties(string p, TrackKind k) { var t = Build(("L", p, 2)).Tracks.Single(); Assert.AreEqual(k, t.Kind); Assert.IsTrue(t.IsLight); }
        [Test] public void Build_TransformHaloAudioParticle_DroppedAndCounted() { var s = TrackBuilder.FromClip(Clip(("", "m_LocalPosition.x", 0), ("L", "m_DrawHalo", 2), ("", "volume", 3), ("", "x", 4)), "", 0, out var d); Assert.IsNull(s); Assert.AreEqual((1, 1, 1, 1), (d.Transform, d.DrawHalo, d.Audio, d.Particle)); }
        [Test] public void Build_MixedClip_KeepsOnlyNonTransform() => Assert.AreEqual(1, Build(("", "m_LocalRotation.x", 0), ("", "_EmissiveColor.r", 1)).Tracks.Count);
        [Test] public void Build_Length_IsClipLength() => Assert.AreEqual(2.5f, Build(Keys(0f, 2.5f)).Length);
        [Test] public void Build_UnknownLightProperty_CountedUnknown() { TrackBuilder.FromClip(Clip(("L", "m_Shadows", 2)), "", 0, out var d); Assert.AreEqual(1, d.Unknown); }
        [Test] public void Build_HasState_FromTheBindingRule() => Assert.AreEqual((true, false), (Build(("", "m_LocalRotation.x", 0), ("", "_EmissiveColor.r", 1)).HasState, Build(("", "_EmissiveColor.r", 1)).HasState));
        [Test] public void Build_CarriesHostAndModel() { var s = TrackBuilder.FromClip(Clip(("", "_Opacity", 1)), "root/host", 3, out _); Assert.AreEqual(("clip", "root/host", 3), (s.Clip, s.HostPath, s.ModelIndex)); }
        [Test]
        public void FromClip_KeylessCurve_LeavesChannelNull()
        {
            var clip = new MuAnimClip("clip", 0, new[]
            {
                new MuAnimCurve("", "_EmissiveColor.r", 1, 0, 0, new MuKey[0]),
                new MuAnimCurve("", "_EmissiveColor.g", 1, 0, 0, new[] { new MuKey(0f, 0.5f, 0f, 0f, 0) }),
            });
            var t = Build(clip).Tracks.Single();
            Assert.IsNull(t.Channels[0]);
            Assert.IsNotNull(t.Channels[1]);
        }
        [Test] public void FromClip_OnlyKeylessCurves_NoSet() => Assert.IsNull(Build(new MuAnimClip("clip", 0, new[] { new MuAnimCurve("", "_Opacity", 1, 0, 0, new MuKey[0]) })));
        [Test] public void FloatCurve_InfiniteOutTangent_HoldsUntilNextKey() { var c = new FloatCurve(new[] { 0f, 0f, 0f, float.PositiveInfinity, 1f, 1f, float.PositiveInfinity, 0f }); Assert.AreEqual(0f, c.Evaluate(0.99f)); Assert.AreEqual(1f, c.Evaluate(1f)); }
        [Test] public void FloatCurve_Looped_Repeats() => Assert.AreEqual(Linear01().Evaluate(0.25f), Linear01().EvaluateLooped(1.25f, 1f), 1e-5f);
    }
}
