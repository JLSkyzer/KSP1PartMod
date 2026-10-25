using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class DiagSelectionTests
    {
        [Test] public void Take_NamedPart_OnceAndWithoutUsingAnOtherSlot()
        {
            var s = new DiagSelection(new[] { "Mk2Pod" }, 1);
            Assert.IsTrue(s.Take("Mk2Pod"));
            Assert.IsFalse(s.Take("Mk2Pod"));
            Assert.AreEqual(1, s.Others);
        }

        [Test] public void Take_Others_StopsAfterTheLimit_ButNamedStillPass()
        {
            var s = new DiagSelection(new[] { "liquidEngine" }, 2);
            Assert.IsTrue(s.Take("a"));
            Assert.IsTrue(s.Take("b"));
            Assert.IsFalse(s.Take("c"));
            Assert.IsFalse(s.Take("a"));
            Assert.IsFalse(s.Done);
            Assert.IsTrue(s.Take("liquidEngine"));
            Assert.IsTrue(s.Done);
        }

        [Test] public void Take_NullOrEmpty_IsIgnored()
        {
            var s = new DiagSelection();
            Assert.IsFalse(s.Take(null));
            Assert.IsFalse(s.Take(""));
            Assert.AreEqual(DiagSelection.DefaultOthers, s.Others);
        }
    }
}
