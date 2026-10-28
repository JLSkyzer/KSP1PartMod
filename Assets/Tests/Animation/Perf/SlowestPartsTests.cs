using System.Linq;
using KSP1PartMod.Perf;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class SlowestPartsTests
    {
        private static string[] Names(SlowestParts s) => s.ToArray().Select(p => p.Name).ToArray();

        [Test] public void KeepsTheLargest_InOrder_TiesFirstSeenWins()
        {
            var s = new SlowestParts(3);
            s.Offer("a", 10, 1, 2);
            s.Offer("b", 30, 0, 0);
            s.Offer("c", 20, 0, 0);
            s.Offer("d", 20, 0, 0);
            CollectionAssert.AreEqual(new[] { "b", "c", "d" }, Names(s));
            Assert.IsFalse(s.Offer("e", 20, 0, 0), "an equal time loses to the parts seen first");
            Assert.IsTrue(s.Offer("f", 25, 5, 6));
            CollectionAssert.AreEqual(new[] { "b", "f", "c" }, Names(s));
            Assert.AreEqual(5, s[1].PrefabTicks);
            Assert.AreEqual(6, s[1].TextureTicks);
        }

        [Test] public void CapacityOne()
        {
            var s = new SlowestParts(1);
            Assert.IsTrue(s.WouldEnter(0));
            s.Offer("a", 5, 0, 0);
            Assert.IsFalse(s.WouldEnter(5));
            Assert.IsTrue(s.WouldEnter(6));
            s.Offer("b", 6, 0, 0);
            CollectionAssert.AreEqual(new[] { "b" }, Names(s));
        }

        [Test] public void CapacityFifty()
        {
            var s = new SlowestParts(50);
            for (var i = 0; i < 200; i++) s.Offer("p" + i, (i * 37) % 200, 0, 0);
            Assert.AreEqual(50, s.Count);
            var totals = s.ToArray().Select(p => p.TotalTicks).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(150, 50).Reverse().Select(x => (long)x).ToArray(), totals);
        }

        [Test] public void Clear_EmptiesTheList_NullNameIsQuestionMark()
        {
            var s = new SlowestParts(2);
            s.Offer(null, 3, 0, 0);
            Assert.AreEqual("?", s[0].Name);
            s.Clear();
            Assert.AreEqual(0, s.Count);
            Assert.IsTrue(s.WouldEnter(0));
        }

        [Test] public void FastParts_AllocateNothing()
        {
            var s = new SlowestParts(10);
            for (var i = 0; i < 10; i++) s.Offer("slow" + i, 1000 + i, 0, 0);
            const string name = "fast";
            void Parts()
            {
                for (var i = 0; i < 10000; i++)
                    if (s.WouldEnter(i % 1000)) s.Offer(name, i % 1000, 0, 0);
                for (var i = 0; i < 10000; i++) s.Offer(name, i % 1000, 0, 0);
            }

            Parts();
            Assert.That(Parts, Is.Not.AllocatingGCMemory());
            Assert.IsTrue(s.ToArray().All(p => p.Name.StartsWith("slow")));
        }
    }
}
