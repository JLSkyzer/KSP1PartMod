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
