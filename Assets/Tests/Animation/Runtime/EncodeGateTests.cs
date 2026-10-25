using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class EncodeGateTests
    {
        [Test]
        public void NothingChanged_NoEncode() => Assert.IsFalse(new EncodeGate(1f).ShouldEncode(false, false, 10f));

        [Test]
        public void FirstChange_Encodes() => Assert.IsTrue(new EncodeGate(1f).ShouldEncode(true, false, 0f));

        [Test]
        public void ContinuousChanges_AtMostOncePerInterval()
        {
            var g = new EncodeGate(1f);
            Assert.IsTrue(g.ShouldEncode(true, false, 5f));
            Assert.IsFalse(g.ShouldEncode(true, false, 5.5f));
            Assert.IsTrue(g.ShouldEncode(true, false, 6.0f));
        }

        [Test]
        public void Urgent_EncodesAtOnce()
        {
            var g = new EncodeGate(1f);
            Assert.IsTrue(g.ShouldEncode(true, false, 5f));
            Assert.IsTrue(g.ShouldEncode(true, true, 5.1f));
        }

        [Test]
        public void Urgent_WithoutChange_StillEncodes() => Assert.IsTrue(new EncodeGate(1f).ShouldEncode(false, true, 0f));

        [Test]
        public void ClockGoingBack_Encodes()
        {
            var g = new EncodeGate(1f);
            Assert.IsTrue(g.ShouldEncode(true, false, 100f));
            Assert.IsTrue(g.ShouldEncode(true, false, 2f)); // a new scene restarts the clock
        }
    }
}
