using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class EntryGateTests
    {
        private static readonly string[] Subtypes = { "x", "y", "z" };

        private static B9Gate G(string setId, params string[] off) =>
            new B9Gate { SetId = setId, SubtypeIds = Subtypes, InactiveIn = off, DefaultSubtype = "x" };

        private static AnimEntryDef E(params B9Gate[] g) => new AnimEntryDef { Clip = "nozzle", Gates = g };

        [Test] public void Gate_NoSet_KeepsDefaultSubtype()
        {
            var g = new EntryGate(new[] { G(null, "x") }, new[] { -1 });
            Assert.IsFalse(g.Active, "default 'x' is inactive");
            Assert.IsFalse(g.OnVariant(-1, "y"));
            Assert.IsFalse(g.Active);
        }

        [Test] public void Gate_FollowsItsSetOnly()
        {
            var g = new EntryGate(new[] { G("sw", "y") }, new[] { 2 });
            Assert.IsTrue(g.Follows(2));
            Assert.IsFalse(g.Follows(0));
            Assert.IsFalse(g.OnVariant(0, "y"), "another set");
            Assert.IsTrue(g.Active);
            Assert.IsTrue(g.OnVariant(2, "y"));
            Assert.IsFalse(g.Active);
            Assert.IsTrue(g.OnVariant(2, null), "no selection: back to the default subtype");
            Assert.IsTrue(g.Active);
        }

        [Test] public void Gate_TwoSwitches_InactiveWhenEitherSaysSo()
        {
            var g = new EntryGate(new[] { G("a", "y"), G("b", "z") }, new[] { 0, 1 });
            Assert.IsTrue(g.Active);
            g.OnVariant(1, "z");
            Assert.IsFalse(g.Active);
            g.OnVariant(0, "y");
            g.OnVariant(1, "x");
            Assert.IsFalse(g.Active);
            g.OnVariant(0, "x");
            Assert.IsTrue(g.Active);
        }

        /// <summary>Controller ruling (Task 5 review): two entries the planner keeps on one clip because their gates keep
        /// them apart never both drive it, through any sequence of picks (the module steps only active entries; an inactive
        /// one holds its rest pose).</summary>
        [Test] public void TwoEntriesOnOneClip_GatedApart_NeverBothDrive()
        {
            var a = E(G("sw", "y", "z"));
            var b = E(G("sw", "x"));
            Assert.IsTrue(B9GatePlanner.GatedApart(new[] { a, b }), "the planner's condition for keeping both");
            var gates = new[] { new EntryGate(a.Gates, new[] { 0 }), new EntryGate(b.Gates, new[] { 0 }) };
            int Drivers() => gates.Count(g => g.Active);
            Assert.AreEqual(1, Drivers(), "at bind, default 'x'");
            foreach (var pick in new[] { "y", "x", "z", "z", null, "y", "x" })
            {
                foreach (var g in gates) g.OnVariant(0, pick);
                Assert.LessOrEqual(Drivers(), 1, $"after '{pick}'");
            }
            // A part without the set: both stay on the default subtype, still apart.
            var none = new[] { new EntryGate(a.Gates, new[] { -1 }), new EntryGate(b.Gates, new[] { -1 }) };
            Assert.AreEqual(1, none.Count(g => g.Active));
        }

        [Test] public void Gate_OnVariant_NoAllocation()
        {
            var g = new EntryGate(new[] { G("sw", "y") }, new[] { 0 });
            void Frames()
            {
                for (var i = 0; i < 10000; i++) g.OnVariant(0, i % 2 == 0 ? "y" : "x");
            }

            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }
    }
}
