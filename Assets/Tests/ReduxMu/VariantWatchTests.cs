using System;
using System.Collections.Generic;
using KSP1PartMod.Game.Animation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>The poll of <c>Data_PartSwitch.ActiveVariants</c> (spec "The variant-change signal: poll"): a pick replaces an
    /// item, a symmetry sync replaces the list with the same strings, a save gives new strings with the same values.</summary>
    public class VariantWatchTests
    {
        private List<string> _list;

        private VariantWatch Watch(params string[] setIds) => new VariantWatch(setIds, () => _list);

        private static string Fresh(string s) => new string(s.ToCharArray());

        [Test] public void Watch_BindValues_NoChangeAtFirstPoll()
        {
            _list = new List<string> { "a", "x" };
            var w = Watch("s0", "s1");
            Assert.AreEqual("a", w.ActiveOf(0));
            Assert.AreEqual("x", w.ActiveOf(1));
            Assert.IsFalse(w.Poll());
        }

        [Test] public void Watch_PickReplacesItem_ChangeOnThatSetOnly()
        {
            _list = new List<string> { "a", "x" };
            var w = Watch("s0", "s1");
            _list[1] = "y";
            Assert.IsTrue(w.Poll());
            Assert.IsFalse(w.Changed(0));
            Assert.IsTrue(w.Changed(1));
            Assert.AreEqual("y", w.ActiveOf(1));
            Assert.IsFalse(w.Poll(), "seen");
            Assert.IsFalse(w.Changed(1), "flags last one poll");
        }

        [Test] public void Watch_NewStringSameValue_NoChange()
        {
            _list = new List<string> { "a" };
            var w = Watch("s0");
            _list[0] = Fresh("a");
            Assert.IsFalse(w.Poll());
        }

        [Test] public void Watch_ListReplaced_ComparedByItem()
        {
            _list = new List<string> { "a", "x" };
            var w = Watch("s0", "s1");
            _list = new List<string>(_list); // SyncAllSymmetricalData: a new list, the same strings
            Assert.IsFalse(w.Poll());
            _list = new List<string> { "a", Fresh("z") };
            Assert.IsTrue(w.Poll());
            Assert.AreEqual("z", w.ActiveOf(1));
        }

        [Test] public void Watch_NoList_OrShortList_NullSelection()
        {
            _list = new List<string> { "a", "x" };
            var w = Watch("s0", "s1");
            _list = new List<string> { "a" };
            Assert.IsTrue(w.Poll());
            Assert.IsNull(w.ActiveOf(1));
            _list = null;
            Assert.IsTrue(w.Poll());
            Assert.IsNull(w.ActiveOf(0));
        }

        [Test] public void Watch_ReaderThrows_NullSelection()
        {
            var w = new VariantWatch(new[] { "s0" }, () => throw new InvalidOperationException("gone"));
            Assert.IsNull(w.ActiveOf(0));
            Assert.IsFalse(w.Poll());
        }

        [Test] public void Watch_IndexOf_Ordinal()
        {
            _list = new List<string>();
            var w = Watch("Engine", "Tank");
            Assert.AreEqual(1, w.IndexOf("Tank"));
            Assert.AreEqual(-1, w.IndexOf("tank"));
            Assert.AreEqual(-1, w.IndexOf(null));
            Assert.AreEqual("[Engine=none, Tank=none]", w.Describe());
        }

        [Test] public void Watch_Poll_10000Frames_NoGcAlloc()
        {
            _list = new List<string> { "a", "x", "m" };
            var w = Watch("s0", "s1", "s2");
            var same = new List<string>(_list);
            var picked = new List<string> { "a", "y", "m" };
            void Frames()
            {
                for (var i = 0; i < 10000; i++)
                {
                    _list = i % 100 == 0 ? (i % 200 == 0 ? picked : same) : _list;
                    w.Poll();
                }
            }

            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }
    }
}
