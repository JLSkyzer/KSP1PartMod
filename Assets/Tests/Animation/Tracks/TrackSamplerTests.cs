using System;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Curve = KSP1PartMod.Animation.Behaviours.FloatCurve;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class TrackSamplerTests
    {
        private const float Len = 2f;
        private ChannelTable Table;

        [SetUp] public void SetUp() => Table = new ChannelTable();

        private static Curve Const(float v) => new Curve(new[] { 0f, v, 0f, 0f, Len, v, 0f, 0f });

        /// <summary>0 at t = 0, 1 at t = 1, 0.5 at t = 2 (linear segments).</summary>
        private static Curve Ramp() => new Curve(new[] { 0f, 0f, 1f, 1f, 1f, 1f, -0.5f, -0.5f, 2f, 0.5f, -0.5f, -0.5f });

        private static ClipPose Pose(float normalized) => new ClipPose(normalized, 1, 1, true);
        private static readonly ClipPose Full = new ClipPose(0.5f, 1, 1, true);

        private static TrackSet Set(Track track, int wrapMode = 0) =>
            new TrackSet { Clip = "c", HostPath = "h", Length = Len, WrapMode = wrapMode, Tracks = new[] { track } };

        private static Track FloatTrack(Curve curve) =>
            new Track { RelativePath = "", Kind = TrackKind.MaterialFloat, Property = "_Opacity", Channels = new[] { curve } };

        private int Channel(float x, float y = 0f, float z = 0f, float w = 0f)
        {
            var target = Table.AddTarget(TargetKind.Renderer, false, false);
            return Table.Register(target, 0, ChannelValueType.Color, WriterClass.Track, 0, new Value4(x, y, z, w));
        }

        private TrackSampler Sampler(float rest = 0f, Curve curve = null, int[] channelOfTrack = null, int wrapMode = 0) =>
            new TrackSampler(Set(FloatTrack(curve ?? Ramp()), wrapMode), Table, channelOfTrack ?? new[] { Channel(rest) });

        private TrackSampler Sampler((float r, float g, float b, float a) colourRest, char only)
        {
            var channels = new Curve[4];
            channels["rgba".IndexOf(only)] = Const(0.9f);
            var track = new Track { RelativePath = "", Kind = TrackKind.MaterialColor, Property = "_EmissiveColor", Channels = channels };
            var (r, g, b, a) = colourRest;
            return new TrackSampler(Set(track), Table, new[] { Channel(r, g, b, a) });
        }

        private Value4 Submitted(int channel) => Table.Submitted(channel);

        [Test] public void Sampler_WeightBlendsFromRest() { var s = Sampler(rest: 0f, curve: Const(1f)); s.Sample(new ClipPose(0.5f, 0, 0.25f, true)); Assert.AreEqual(0.25f, Submitted(0).X, 1e-5f); }

        [Test] public void Sampler_WeightBlend_NonZeroRest() { var s = Sampler(rest: 0.4f, curve: Const(1f)); s.Sample(new ClipPose(0.5f, 0, 0.5f, true)); Assert.AreEqual(0.7f, Submitted(0).X, 1e-5f); }

        [Test] public void Sampler_TimeIsNormalizedTimesLength() { var s = Sampler(); s.Sample(Pose(0.25f)); Assert.AreEqual(Ramp().Evaluate(0.25f * Len), Submitted(0).X, 1e-5f); }

        [Test]
        public void Sampler_ClampsPastEnd_LoopsWhenLooping()
        {
            var curve = Ramp();
            var s = Sampler(curve: curve);
            s.Sample(Pose(1.25f));
            Assert.AreEqual(curve.Evaluate(Len), Submitted(0).X, 1e-5f);
            s.Loop = true;
            s.Sample(Pose(1.25f));
            Assert.AreEqual(curve.Evaluate(0.25f * Len), Submitted(0).X, 1e-5f);
        }

        [Test] public void Sampler_ClampsBeforeStart() { var s = Sampler(); s.Sample(Pose(-0.5f)); Assert.AreEqual(Ramp().Evaluate(0f), Submitted(0).X, 1e-5f); }

        [Test] public void Sampler_LoopWrapMode_LoopsByDefault() { var s = Sampler(wrapMode: 2); Assert.IsTrue(s.Loop); s.Sample(Pose(1.25f)); Assert.AreEqual(Ramp().Evaluate(0.25f * Len), Submitted(0).X, 1e-5f); }

        [TestCase(0)] [TestCase(1)] [TestCase(8)]
        public void Sampler_OtherWrapModes_ClampByDefault(int wrapMode) => Assert.IsFalse(Sampler(wrapMode: wrapMode).Loop);

        [Test]
        public void Sampler_Disabled_SubmitsNothing()
        {
            var s = Sampler();
            Assert.IsFalse(s.Sample(new ClipPose(0.5f, 0, 1, false)));
            Assert.AreEqual(0, Table.SubmitCount);
        }

        [Test]
        public void Sampler_PoseUnchanged_DoesNotSubmit()
        {
            var s = Sampler();
            var p = Pose(0.3f);
            Assert.IsTrue(s.Sample(p));
            Table.ResetCounts();
            Assert.IsFalse(s.Sample(p));
            Assert.AreEqual(0, Table.SubmitCount);
        }

        [Test] public void Sampler_SpeedOnlyChange_DoesNotSubmit() { var s = Sampler(); s.Sample(new ClipPose(0.3f, 1, 1, true)); Table.ResetCounts(); Assert.IsFalse(s.Sample(new ClipPose(0.3f, -1, 1, true))); }

        [Test] public void Sampler_WeightChange_Submits() { var s = Sampler(); s.Sample(Pose(0.3f)); Table.ResetCounts(); Assert.IsTrue(s.Sample(new ClipPose(0.3f, 1, 0.5f, true))); Assert.AreEqual(1, Table.SubmitCount); }

        [Test] public void Sampler_LoopChange_Submits() { var s = Sampler(); s.Sample(Pose(1.25f)); Table.ResetCounts(); s.Loop = true; Assert.IsTrue(s.Sample(Pose(1.25f))); }

        [Test]
        public void Sampler_ReEnabledAtSameTime_Submits()
        {
            var s = Sampler();
            s.Sample(Pose(0.3f));
            s.Sample(new ClipPose(0.3f, 1, 1, false));
            Table.ResetCounts();
            Assert.IsTrue(s.Sample(Pose(0.3f)));
            Assert.AreEqual(1, Table.SubmitCount);
        }

        [Test]
        public void Sampler_MissingChannel_KeepsRest()
        {
            var s = Sampler(colourRest: (0.1f, 0.2f, 0.3f, 1f), only: 'r');
            s.Sample(Full);
            Assert.AreEqual((0.2f, 0.3f, 1f), (Submitted(0).Y, Submitted(0).Z, Submitted(0).W));
            Assert.AreEqual(0.9f, Submitted(0).X, 1e-5f);
        }

        [Test] public void Sampler_UnboundTrack_Skipped() => Assert.DoesNotThrow(() => Sampler(channelOfTrack: new[] { -1 }).Sample(Full));

        [Test] public void Sampler_UnboundTrack_SubmitsNothing() { Sampler(channelOfTrack: new[] { -1 }).Sample(Full); Assert.AreEqual(0, Table.SubmitCount); }

        [Test]
        public void Sampler_OneSubmitPerTrack()
        {
            var a = Channel(0f);
            var b = Channel(0f);
            var set = new TrackSet { Clip = "c", HostPath = "h", Length = Len, Tracks = new[] { FloatTrack(Ramp()), FloatTrack(Const(1f)) } };
            new TrackSampler(set, Table, new[] { a, b }).Sample(Full);
            Assert.AreEqual(2, Table.SubmitCount);
            Assert.AreEqual(1f, Submitted(b).X, 1e-5f);
        }

        [Test]
        public void Sampler_10000Frames_AllocatesZero()
        {
            var s = Sampler();
            s.Sample(Pose(0));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= 10000; i++) s.Sample(Pose(i / 10000f));
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // Mono does not implement GC.GetAllocatedBytesForCurrentThread: it stays at 0 (checked 2026-10-27, a sampler made to
        // allocate passed the two *_AllocatesZero tests of the plan and failed these). The two below use Unity's GC.Alloc
        // recorder, whose detection TickAllocationTests checks.

        [Test]
        public void Sampler_10000Frames_NoGcAlloc()
        {
            var s = Sampler();
            s.Sample(Pose(0));
            Assert.That(() => { for (var i = 1; i <= 10000; i++) s.Sample(Pose(i / 10000f)); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Sampler_Looping10000Frames_NoGcAlloc()
        {
            var s = Sampler(colourRest: (0.1f, 0.2f, 0.3f, 1f), only: 'g');
            s.Loop = true;
            s.Sample(Pose(0));
            Assert.That(() =>
            {
                for (var i = 1; i <= 10000; i++) s.Sample(new ClipPose(i / 1000f, 1, 0.5f, i % 7 != 0));
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Sampler_Looping10000Frames_AllocatesZero()
        {
            var s = Sampler(colourRest: (0.1f, 0.2f, 0.3f, 1f), only: 'g');
            s.Loop = true;
            s.Sample(Pose(0));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= 10000; i++) s.Sample(new ClipPose(i / 1000f, 1, 0.5f, i % 7 != 0));
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
