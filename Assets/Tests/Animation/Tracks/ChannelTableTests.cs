using System.Collections.Generic;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class ChannelTableTests
    {
        private const int P = 7;

        /// <summary>Counts calls and keeps the last value written per property. Allocation-free once warmed up.</summary>
        private sealed class RecordingSink : IChannelSink
        {
            private readonly Dictionary<int, Value4> _last = new Dictionary<int, Value4>();
            public int Begins, Writes, Ends;
            public void Begin(int target, TargetKind kind) => Begins++;
            public void Write(int property, ChannelValueType type, in Value4 v) { Writes++; _last[property] = v; }
            public void End(int target, TargetKind kind) => Ends++;
            public Value4 Last(int property) => _last[property];
            public void Clear() { Begins = Writes = Ends = 0; _last.Clear(); }
        }

        private ChannelTable t;
        private RecordingSink Sink;
        private int Renderer;

        [SetUp] public void SetUp()
        {
            t = Table();
            Sink = new RecordingSink();
        }

        private ChannelTable Table()
        {
            var table = new ChannelTable();
            Renderer = table.AddTarget(TargetKind.Renderer, false, false);
            return table;
        }

        private int Reg(ChannelTable table, WriterClass cls, int module, int property = P, int target = -1) =>
            table.Register(target < 0 ? Renderer : target, property, ChannelValueType.Color, cls, module, V(0));

        private static Value4 V(float x) => new Value4(x, x, x, 1f);

        [Test] public void ChannelTable_ColorBeatsTrack_EvenWhenIdle()
        {
            var tr = Reg(t, WriterClass.Track, 0);
            var co = Reg(t, WriterClass.Color, 0);
            t.Submit(co, V(1));
            t.Flush(Sink);
            t.Submit(tr, V(0.5f));
            t.Flush(Sink);
            Assert.AreEqual(1f, Sink.Last(P).X);
        }

        [Test] public void ChannelTable_TrackShownUntilColorSubmits()
        {
            var tr = Reg(t, WriterClass.Track, 0);
            var co = Reg(t, WriterClass.Color, 0);
            t.Submit(tr, V(0.5f));
            t.Flush(Sink);
            Assert.AreEqual(0.5f, Sink.Last(P).X);
            t.Submit(co, V(0.25f));
            t.Flush(Sink);
            Assert.AreEqual(0.25f, Sink.Last(P).X);
        }

        [Test] public void ChannelTable_LaterModuleWins()
        {
            var a = Reg(t, WriterClass.Color, 2);
            var b = Reg(t, WriterClass.Color, 5);
            t.Submit(b, V(0.2f));
            t.Submit(a, V(0.9f));
            t.Flush(Sink);
            Assert.AreEqual(0.2f, Sink.Last(P).X);
        }

        [Test] public void ChannelTable_LaterModuleWins_RegisteredFirst()
        {
            var b = Reg(t, WriterClass.Color, 5);
            var a = Reg(t, WriterClass.Color, 2);
            t.Submit(a, V(0.9f));
            t.Submit(b, V(0.2f));
            t.Flush(Sink);
            Assert.AreEqual(0.2f, Sink.Last(P).X);
        }

        /// <summary>Review M1: a colour entry held at rest by a B9 gate withdraws; the active entry of a lower module then
        /// holds the slot, although the inactive one outranks it.</summary>
        [Test] public void ChannelTable_WithdrawnColor_GivesSlotToOtherColor()
        {
            var active = Reg(t, WriterClass.Color, 2);
            var inactive = Reg(t, WriterClass.Color, 5);
            t.Submit(active, V(0.9f));
            t.Submit(inactive, V(0.2f));
            t.Flush(Sink);
            Assert.AreEqual(0.2f, Sink.Last(P).X);
            t.Withdraw(inactive);
            t.Flush(Sink);
            Assert.AreEqual(0.9f, Sink.Last(P).X, "the other colour channel's last value");
            t.Submit(active, V(0.4f));
            t.Flush(Sink);
            Assert.AreEqual(0.4f, Sink.Last(P).X, "its writes win");
            t.Submit(inactive, V(0.1f));
            t.Flush(Sink);
            Assert.AreEqual(0.1f, Sink.Last(P).X, "a submit after the gate opens counts again");
        }

        [Test] public void ChannelTable_WithdrawnOnlyColor_SlotBackToTracksOrRest()
        {
            var tr = Reg(t, WriterClass.Track, 0);
            var co = Reg(t, WriterClass.Color, 0);
            t.Submit(co, V(1));
            t.Flush(Sink);
            t.Withdraw(co);
            t.Flush(Sink);
            Assert.AreEqual(0f, Sink.Last(P).X, "the slot's rest value");
            t.Submit(tr, V(0.5f));
            t.Flush(Sink);
            Assert.AreEqual(0.5f, Sink.Last(P).X, "a track shows again");
        }

        [Test] public void ChannelTable_WithdrawNotWinner_NoWrite()
        {
            var a = Reg(t, WriterClass.Color, 2);
            var b = Reg(t, WriterClass.Color, 5);
            t.Submit(a, V(0.9f));
            t.Submit(b, V(0.2f));
            t.Flush(Sink);
            Sink.Clear();
            t.Withdraw(a);
            t.Withdraw(a);
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Writes);
        }

        [Test] public void ChannelTable_OneBeginEndPerTargetPerFlush()
        {
            var a = Reg(t, WriterClass.Track, 0, property: 1);
            var b = Reg(t, WriterClass.Track, 0, property: 2);
            t.Submit(a, V(0.3f));
            t.Submit(b, V(0.4f));
            t.Flush(Sink);
            Assert.AreEqual((1, 2, 1), (Sink.Begins, Sink.Writes, Sink.Ends));
            Assert.AreEqual(2, t.WritesLastFlush);
        }

        [Test] public void ChannelTable_TwoWritersOneProperty_OneWrite()
        {
            var a = Reg(t, WriterClass.Track, 0);
            var b = Reg(t, WriterClass.Color, 1);
            t.Submit(a, V(0.3f));
            t.Submit(b, V(0.4f));
            t.Flush(Sink);
            Assert.AreEqual((1, 1, 1), (Sink.Begins, Sink.Writes, Sink.Ends));
        }

        [Test] public void ChannelTable_Unchanged_NoFlush()
        {
            var c = Reg(t, WriterClass.Track, 0);
            t.Submit(c, V(1));
            t.Flush(Sink);
            Sink.Clear();
            t.Submit(c, V(1));
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
            Assert.AreEqual(0, t.WritesLastFlush);
        }

        [Test] public void ChannelTable_OnlyDirtyPropertiesWritten()
        {
            var a = Reg(t, WriterClass.Track, 0, property: 1);
            var b = Reg(t, WriterClass.Track, 0, property: 2);
            t.Submit(a, V(0.3f));
            t.Submit(b, V(0.4f));
            t.Flush(Sink);
            Sink.Clear();
            t.Submit(a, V(0.6f));
            t.Submit(b, V(0.4f));
            t.Flush(Sink);
            Assert.AreEqual((1, 1, 1), (Sink.Begins, Sink.Writes, Sink.Ends));
            Assert.AreEqual(0.6f, Sink.Last(1).X);
        }

        [Test] public void ChannelTable_LoserChange_NoFlush()
        {
            var tr = Reg(t, WriterClass.Track, 0);
            var co = Reg(t, WriterClass.Color, 0);
            t.Submit(co, V(1));
            t.Submit(tr, V(0.1f));
            t.Flush(Sink);
            Sink.Clear();
            t.Submit(tr, V(0.2f));
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
        }

        [Test] public void ChannelTable_UnsubmittedChannel_KeepsLastValue()
        {
            var r = t.AddTarget(TargetKind.Renderer, rewriteEveryFrame: true, excluded: false);
            var c = Reg(t, WriterClass.Track, 0, target: r);
            t.Submit(c, V(0.7f));
            t.Flush(Sink);
            Sink.Clear();
            t.Flush(Sink);
            t.Flush(Sink);
            Assert.AreEqual(2, Sink.Begins);
            Assert.AreEqual(0.7f, Sink.Last(P).X);
        }

        [Test] public void ChannelTable_UnsubmittedChannel_NotResetToRest()
        {
            var c = t.Register(Renderer, P, ChannelValueType.Color, WriterClass.Track, 0, V(0.1f));
            t.Submit(c, V(0.7f));
            t.Flush(Sink);
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Writes);
            Assert.AreEqual(0.7f, t.Submitted(c).X);
            Assert.AreEqual(0.1f, t.Rest(c).X);
        }

        [Test] public void ChannelTable_RewriteEveryFrame_WritesWithoutChange()
        {
            var r = t.AddTarget(TargetKind.Renderer, rewriteEveryFrame: true, excluded: false);
            var c = Reg(t, WriterClass.Track, 0, target: r);
            t.Submit(c, V(1));
            t.Flush(Sink);
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual(1, Sink.Begins);
            Assert.AreEqual(1, Sink.Writes);
        }

        [Test] public void ChannelTable_RewriteEveryFrame_NothingSubmitted_NoWrite()
        {
            var r = t.AddTarget(TargetKind.Renderer, rewriteEveryFrame: true, excluded: false);
            Reg(t, WriterClass.Track, 0, target: r);
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
        }

        /// <summary>Turned on later (a ReentryMesh hull became active): every flush rewrites the target.</summary>
        [Test] public void ChannelTable_SetRewriteEveryFrame_On_RewritesEachFlush()
        {
            var c = Reg(t, WriterClass.Track, 0);
            t.Submit(c, V(1));
            t.Flush(Sink);
            t.SetRewriteEveryFrame(Renderer, true);
            Sink.Clear();
            t.Flush(Sink);
            t.Flush(Sink);
            Assert.AreEqual((2, 2), (Sink.Begins, Sink.Writes));
        }

        /// <summary>Turned off (the hull went inactive): one more full rewrite, then only on change.</summary>
        [Test] public void ChannelTable_SetRewriteEveryFrame_Off_OneMoreRewrite()
        {
            var r = t.AddTarget(TargetKind.Renderer, rewriteEveryFrame: true, excluded: false);
            var a = Reg(t, WriterClass.Track, 0, property: 1, target: r);
            var b = Reg(t, WriterClass.Track, 0, property: 2, target: r);
            t.Submit(a, V(1));
            t.Submit(b, V(2));
            t.Flush(Sink);
            t.SetRewriteEveryFrame(r, false);
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual((1, 2), (Sink.Begins, Sink.Writes));
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
        }

        [Test] public void ChannelTable_SetRewriteEveryFrame_Unchanged_NoExtraRewrite()
        {
            var c = Reg(t, WriterClass.Track, 0);
            t.Submit(c, V(1));
            t.Flush(Sink);
            t.SetRewriteEveryFrame(Renderer, false);
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
        }

        [Test] public void ChannelTable_FlushRewriteAll_WritesCleanTargets_NotExcludedOrUnsubmitted()
        {
            var c = Reg(t, WriterClass.Track, 0);
            Reg(t, WriterClass.Track, 0, property: 2);
            var ex = t.AddTarget(TargetKind.Renderer, false, excluded: true);
            var e = Reg(t, WriterClass.Track, 0, target: ex);
            t.Submit(c, V(1));
            t.Submit(e, V(1));
            t.Flush(Sink);
            Sink.Clear();
            t.Flush(Sink, rewriteAll: true);
            Assert.AreEqual((1, 1), (Sink.Begins, Sink.Writes));
            Sink.Clear();
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins, "a plain flush after it writes nothing");
        }

        [Test] public void ChannelTable_ExcludedTarget_NeverWritten()
        {
            var r = t.AddTarget(TargetKind.Renderer, rewriteEveryFrame: true, excluded: true);
            var c = Reg(t, WriterClass.Color, 0, target: r);
            t.Submit(c, V(1));
            t.Flush(Sink);
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
        }

        [Test] public void ChannelTable_NeverSubmitted_WritesNothing()
        {
            Reg(t, WriterClass.Track, 0);
            Reg(t, WriterClass.Color, 1, property: 2);
            t.Flush(Sink);
            Assert.AreEqual(0, Sink.Begins);
            Assert.AreEqual(0, t.WritesLastFlush);
        }

        [Test] public void ChannelTable_TargetsFlushedSeparately_KindPassed()
        {
            var light = t.AddTarget(TargetKind.Light, false, false);
            var kinds = new List<TargetKind>();
            var a = Reg(t, WriterClass.Track, 0);
            var b = t.Register(light, 0, ChannelValueType.LightIntensity, WriterClass.Track, 0, V(0));
            t.Submit(a, V(1));
            t.Submit(b, V(2));
            var sink = new KindSink(kinds);
            t.Flush(sink);
            CollectionAssert.AreEqual(new[] { TargetKind.Renderer, TargetKind.Light }, kinds);
            Assert.AreEqual(ChannelValueType.LightIntensity, sink.LastType);
        }

        private sealed class KindSink : IChannelSink
        {
            private readonly List<TargetKind> _kinds;
            public ChannelValueType LastType;
            public KindSink(List<TargetKind> kinds) => _kinds = kinds;
            public void Begin(int target, TargetKind kind) => _kinds.Add(kind);
            public void Write(int property, ChannelValueType type, in Value4 v) => LastType = type;
            public void End(int target, TargetKind kind) { }
        }

        [Test] public void ChannelTable_Register_UnknownTarget_Throws() =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => t.Register(5, P, ChannelValueType.Float, WriterClass.Track, 0, V(0)));

        [Test] public void ChannelTable_Register_SameSlotOtherType_Throws()
        {
            Reg(t, WriterClass.Track, 0);
            Assert.Throws<System.ArgumentException>(() => t.Register(Renderer, P, ChannelValueType.Float, WriterClass.Color, 1, V(0)));
        }

        [Test] public void ChannelTable_Register_SameSlotSameType_Shares()
        {
            Reg(t, WriterClass.Track, 0);
            Assert.DoesNotThrow(() => Reg(t, WriterClass.Color, 1));
        }

        [Test] public void ChannelTable_SubmitCount_CountsAndResets()
        {
            var c = Reg(t, WriterClass.Track, 0);
            t.Submit(c, V(1));
            t.Submit(c, V(1));
            Assert.AreEqual(2, t.SubmitCount);
            t.ResetCounts();
            Assert.AreEqual(0, t.SubmitCount);
        }

        // --- several clips on one slot (review I3): rest and weight 0 never hide another clip; active clips blend

        private int Track(int module, int layer = 0, float rest = 0f) =>
            t.Register(Renderer, P, ChannelValueType.Color, WriterClass.Track, module, V(rest), layer);

        [Test] public void Shared_UpperClipAtRest_LowerShows()
        {
            var lower = Track(1);
            var upper = Track(5);
            t.Submit(lower, V(0f), 1f);
            t.Submit(upper, V(0f), 1f); // Setup's rest pose of the later module
            t.Submit(lower, V(0.8f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.8f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_UpperClipAtWeightZero_LowerShows()
        {
            var lower = Track(1);
            var upper = Track(5);
            t.Submit(upper, V(1f), 0f);
            t.Submit(lower, V(0.6f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.6f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_ActiveClipBackToRest_ReleasesTheSlot()
        {
            var lower = Track(1);
            var upper = Track(5);
            t.Submit(lower, V(0.4f), 1f);
            t.Submit(upper, V(0.9f), 1f);
            t.Submit(upper, V(0f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.4f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_AllAtRest_ShowsRest()
        {
            var a = Track(1, rest: 0.2f);
            var b = Track(5, rest: 0.2f);
            t.Submit(a, V(0.7f), 1f);
            t.Flush(Sink);
            t.Submit(a, V(0.2f), 1f);
            t.Submit(b, V(0.9f), 0f);
            t.Flush(Sink);
            Assert.AreEqual(0.2f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_SameLayer_WeightsAbove1_Normalised()
        {
            var a = Track(1);
            var b = Track(5);
            t.Submit(a, V(1f), 1f);
            t.Submit(b, V(0.5f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.75f, Sink.Last(P).X, 1e-6f); // Unity legacy: same layer, weights 1 + 1 -> 0.5 each
        }

        [Test] public void Shared_SameLayer_WeightsBelow1_RestTakesTheRemainder()
        {
            var a = Track(1, rest: 0.1f);
            var b = Track(5, rest: 0.1f);
            t.Submit(a, V(1f), 0.25f);
            t.Submit(b, V(0.5f), 0.25f);
            t.Flush(Sink);
            Assert.AreEqual(0.25f * 1f + 0.25f * 0.5f + 0.5f * 0.1f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_HigherLayerTakesItsWeightFirst()
        {
            var low = Track(9, layer: 0);
            var high = Track(1, layer: 2);
            t.Submit(low, V(1f), 1f);
            t.Submit(high, V(0.5f), 0.75f);
            t.Flush(Sink);
            Assert.AreEqual(0.75f * 0.5f + 0.25f * 1f, Sink.Last(P).X, 1e-6f); // higher layer first, whatever the module
        }

        [Test] public void Shared_HigherLayerAtFullWeight_HidesLower()
        {
            var low = Track(1, layer: 0);
            var high = Track(5, layer: 1);
            t.Submit(low, V(1f), 1f);
            t.Submit(high, V(0.5f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.5f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_OneActiveClip_ShowsItsBlendWithRest()
        {
            var a = Track(1, rest: 0.4f);
            Track(5, rest: 0.4f);
            t.Submit(a, V(1f), 0.5f);
            t.Flush(Sink);
            Assert.AreEqual(0.7f, Sink.Last(P).X, 1e-6f);
            Assert.AreEqual(0.7f, t.Submitted(a).X, 1e-6f);
        }

        [Test] public void Shared_ColorStillBeatsActiveTracks()
        {
            var a = Track(1);
            var b = Track(2);
            var co = Reg(t, WriterClass.Color, 0);
            t.Submit(a, V(1f), 1f);
            t.Submit(co, V(0.3f));
            t.Submit(b, V(0.9f), 1f);
            t.Flush(Sink);
            Assert.AreEqual(0.3f, Sink.Last(P).X, 1e-6f);
        }

        [Test] public void Shared_Blend10000Frames_NoGcAlloc()
        {
            var a = Track(1, layer: 0);
            var b = Track(2, layer: 0);
            var c = Track(3, layer: 1);
            var sink = new CountingSink();
            t.Submit(a, V(0.1f), 1f);
            t.Flush(sink);
            Assert.That(() =>
            {
                for (var i = 1; i <= 10000; i++)
                {
                    var x = i / 10000f;
                    t.Submit(a, V(x), 1f);
                    t.Submit(b, V(1 - x), 0.5f);
                    t.Submit(c, V(x * 0.5f), i % 2 == 0 ? 0.3f : 0f);
                    t.Flush(sink);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.Greater(sink.Writes, 9000);
        }

        /// <summary>Per the phase 2a ruling: the allocation is measured with Unity's recorder
        /// (<c>Is.Not.AllocatingGCMemory()</c>); <c>GC.GetAllocatedBytesForCurrentThread</c> reads 0 under Unity's Mono.</summary>
        [Test] public void ChannelTable_Flush10000_AllocatesZero()
        {
            var every = t.AddTarget(TargetKind.Renderer, true, false);
            var light = t.AddTarget(TargetKind.Light, false, false);
            var a = Reg(t, WriterClass.Track, 0, property: 1);
            var b = Reg(t, WriterClass.Color, 3, property: 1);
            var c = Reg(t, WriterClass.Track, 0, property: 2, target: every);
            var d = t.Register(light, 0, ChannelValueType.LightIntensity, WriterClass.Track, 0, V(0));
            var sink = new CountingSink();
            t.Submit(a, V(0));
            t.Flush(sink);
            Assert.That(() =>
            {
                for (var i = 1; i <= 10000; i++)
                {
                    var x = i / 10000f;
                    t.Submit(a, V(x));
                    if (i % 3 == 0) t.Submit(b, V(1 - x));
                    if (i % 5 != 0) t.Submit(c, V(x));
                    t.Submit(d, V(i % 2));
                    t.Flush(sink);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.Greater(sink.Writes, 10000);
        }

        private sealed class CountingSink : IChannelSink
        {
            public int Writes;
            public void Begin(int target, TargetKind kind) { }
            public void Write(int property, ChannelValueType type, in Value4 v) => Writes++;
            public void End(int target, TargetKind kind) { }
        }
    }
}
