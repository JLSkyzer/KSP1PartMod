using KSP1PartMod.Placement;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Placement
{
    public class PlacementGateTests
    {
        [Test] public void Once_InvokesInnerOnce()
        {
            var n = 0;
            var once = new OnceAction(() => n++);
            Assert.IsFalse(once.Called);
            once.Invoke();
            once.Invoke();
            Assert.AreEqual((1, true), (n, once.Called));
        }

        [Test] public void Once_NullInner_Safe()
        {
            var once = new OnceAction(null);
            once.Invoke();
            Assert.IsTrue(once.Called);
        }

        [Test] public void Gate_EarlyPlacement_ConsumedOnce()
        {
            var g = new PlacementGate();
            Assert.IsFalse(g.ConsumeEarly());
            g.PlacedEarly();
            Assert.IsTrue(g.ConsumeEarly());
            Assert.IsFalse(g.ConsumeEarly());
        }

        [Test] public void Gate_ResetAtUnload()
        {
            var g = new PlacementGate();
            g.PlacedEarly();
            g.Reset();
            Assert.IsFalse(g.ConsumeEarly());
        }

        [Test] public void Gate_RegisterOnce()
        {
            var g = new PlacementGate();
            Assert.IsTrue(g.TryRegister());
            Assert.IsFalse(g.TryRegister());
        }
    }
}
