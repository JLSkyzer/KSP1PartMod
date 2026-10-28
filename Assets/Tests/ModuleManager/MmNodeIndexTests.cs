using KSP1PartMod.ModuleManager;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ModuleManager
{
    public class MmNodeIndexTests
    {
        private static void AssertSplit(string childName, bool has, string name, MmIndexKind kind, int value)
        {
            Assert.AreEqual(has, MmNodeIndex.Split(childName, out var n, out var index), "has index");
            Assert.AreEqual(name, n, "name");
            Assert.AreEqual(kind, index.Kind, "kind");
            Assert.AreEqual(value, index.Value, "value");
        }

        [Test] public void Split_SelectorAndIndex() => AssertSplit("@MODULE[X],1", true, "@MODULE[X]", MmIndexKind.N, 1);

        [Test] public void Split_HasWithNestedBrackets() =>
            AssertSplit("@MODULE[X]:HAS[#a[b]],1", true, "@MODULE[X]:HAS[#a[b]]", MmIndexKind.N, 1);

        [Test] public void Split_All() => AssertSplit("!MODULE,*", true, "!MODULE", MmIndexKind.All, 0);

        [Test] public void Split_AllAfterSelector() =>
            AssertSplit("!MODULE[ModuleDataTransmitter],*", true, "!MODULE[ModuleDataTransmitter]", MmIndexKind.All, 0);

        [Test] public void Split_FromEnd() => AssertSplit("@MODULE[X],-1", true, "@MODULE[X]", MmIndexKind.FromEnd, 1);

        [Test] public void Split_CommaInsideBrackets_NotAnIndex() => AssertSplit("@MODULE[a,b]", false, "@MODULE[a,b]", MmIndexKind.None, 0);

        [Test] public void Split_HasWithoutIndex() =>
            AssertSplit("@MODULE[X]:HAS[#name[Y],@Z]", false, "@MODULE[X]:HAS[#name[Y],@Z]", MmIndexKind.None, 0);

        [Test] public void Split_NotAnIndexAfterComma_Unchanged() => AssertSplit("@MODULE,abc", false, "@MODULE,abc", MmIndexKind.None, 0);

        [Test] public void Split_SpacesAroundIndex() => AssertSplit("@MODULE[X] , 2 ", true, "@MODULE[X]", MmIndexKind.N, 2);

        [Test] public void Split_Null() => AssertSplit(null, false, "", MmIndexKind.None, 0);

        [Test] public void Pick_None_AllOrFirst()
        {
            var all = MmNodeIndex.Pick(3, MmIndex.None, false);
            Assert.AreEqual(0, all.Start); Assert.AreEqual(3, all.Count); Assert.IsFalse(all.OutOfRange);
            var first = MmNodeIndex.Pick(3, MmIndex.None, true);
            Assert.AreEqual(0, first.Start); Assert.AreEqual(1, first.Count);
            var empty = MmNodeIndex.Pick(0, MmIndex.None, true);
            Assert.AreEqual(0, empty.Count); Assert.IsFalse(empty.OutOfRange);
        }

        [Test] public void Pick_N()
        {
            var p = MmNodeIndex.Pick(3, new MmIndex(MmIndexKind.N, 2), false);
            Assert.AreEqual(2, p.Start); Assert.AreEqual(1, p.Count);
            var o = MmNodeIndex.Pick(2, new MmIndex(MmIndexKind.N, 2), false);
            Assert.IsTrue(o.OutOfRange); Assert.AreEqual(0, o.Count);
        }

        [Test] public void Pick_FromEnd()
        {
            var p = MmNodeIndex.Pick(3, new MmIndex(MmIndexKind.FromEnd, 1), false);
            Assert.AreEqual(2, p.Start); Assert.AreEqual(1, p.Count);
            var q = MmNodeIndex.Pick(3, new MmIndex(MmIndexKind.FromEnd, 3), false);
            Assert.AreEqual(0, q.Start);
            Assert.IsTrue(MmNodeIndex.Pick(3, new MmIndex(MmIndexKind.FromEnd, 4), false).OutOfRange);
        }

        [Test] public void Pick_All_IgnoresFirstMatch()
        {
            var p = MmNodeIndex.Pick(3, MmIndex.All, true);
            Assert.AreEqual(0, p.Start); Assert.AreEqual(3, p.Count);
        }

        [Test] public void Pick_IndexOnEmpty_OutOfRange() =>
            Assert.IsTrue(MmNodeIndex.Pick(0, new MmIndex(MmIndexKind.N, 0), false).OutOfRange);

        [Test] public void Pick_Contains()
        {
            var p = MmNodeIndex.Pick(5, new MmIndex(MmIndexKind.N, 2), false);
            Assert.IsFalse(p.Contains(1));
            Assert.IsTrue(p.Contains(2));
            Assert.IsFalse(p.Contains(3));
        }
    }
}
