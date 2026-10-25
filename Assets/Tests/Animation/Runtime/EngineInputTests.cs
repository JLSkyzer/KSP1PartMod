using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class EngineInputTests
    {
        [Test]
        public void NoEngineName_AnyMode()
        {
            var r = EngineInput.Read(null, "Wet", 0.7f, true);
            Assert.AreEqual(0.7f, r.throttle);
            Assert.IsTrue(r.operational);
        }

        [Test]
        public void ActiveModeMatches_PassesThrough()
        {
            var r = EngineInput.Read("Dry", "dry", 0.4f, true);
            Assert.AreEqual(0.4f, r.throttle);
            Assert.IsTrue(r.operational);
        }

        [Test]
        public void OtherModeActive_ZeroAndNotOperational()
        {
            var r = EngineInput.Read("Wet", "Dry", 0.9f, true);
            Assert.AreEqual(0f, r.throttle);
            Assert.IsFalse(r.operational);
        }

        [Test]
        public void NotOperational_PassedThrough()
        {
            var r = EngineInput.Read("Dry", "Dry", 0.5f, false);
            Assert.AreEqual(0.5f, r.throttle);
            Assert.IsFalse(r.operational);
        }

        [Test]
        public void NaNThrottle_ReadsZero() => Assert.AreEqual(0f, EngineInput.Read(null, null, float.NaN, true).throttle);

        [Test]
        public void HasMode_IgnoresCase()
        {
            Assert.IsTrue(EngineInput.HasMode("wet", new[] { "Dry", "Wet" }));
            Assert.IsFalse(EngineInput.HasMode("Afterburner", new[] { "Dry", "Wet" }));
            Assert.IsTrue(EngineInput.HasMode("", new[] { "Dry" }));
            Assert.IsFalse(EngineInput.HasMode("Dry", null));
        }
    }
}
