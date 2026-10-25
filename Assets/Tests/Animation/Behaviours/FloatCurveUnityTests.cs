using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Checks <see cref="FloatCurve"/> against a real Unity curve: KSP1 adds a key given as only time and value
    /// with <c>AnimationCurve.AddKey(time, value)</c>, which our NaN tangents stand for.</summary>
    public class FloatCurveUnityTests
    {
        private const float N = float.NaN;

        [TestCase(new[] { 0f, 0f, 0.3f, 1f, 2f, 0.5f, 2.2f, 2f })]
        [TestCase(new[] { 0f, 0f, 1f, 1f, 2f, 0f })]
        [TestCase(new[] { 0f, 0f, 1f, 1f })]
        [TestCase(new[] { 2f, 0.5f, 0f, 0f, 2.2f, 2f, 0.3f, 1f })] // cfg order is not time order
        public void FloatCurve_NaNTangents_MatchUnityAddKey(float[] timeValue)
        {
            var unity = new AnimationCurve();
            var flat = new float[timeValue.Length * 2];
            for (var i = 0; i < timeValue.Length / 2; i++)
            {
                unity.AddKey(timeValue[2 * i], timeValue[2 * i + 1]);
                flat[4 * i] = timeValue[2 * i];
                flat[4 * i + 1] = timeValue[2 * i + 1];
                flat[4 * i + 2] = N;
                flat[4 * i + 3] = N;
            }
            var ours = new FloatCurve(flat);
            foreach (var t in new[] { -0.5f, 0.1f, 0.3f, 0.75f, 1.2f, 1.9f, 2.1f, 2.5f })
                Assert.AreEqual(unity.Evaluate(t), ours.Evaluate(t), 1e-4f, $"t = {t}");
        }

        [Test] public void FloatCurve_GivenTangents_MatchUnityKeyframes()
        {
            var unity = new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(0.4f, 1f, -2f, 0.5f), new Keyframe(2f, 0.2f, 1f, 1f));
            var ours = new FloatCurve(new[] { 0f, 0f, 0f, 3f, 0.4f, 1f, -2f, 0.5f, 2f, 0.2f, 1f, 1f });
            foreach (var t in new[] { 0.05f, 0.2f, 0.35f, 0.6f, 1f, 1.5f, 1.9f })
                Assert.AreEqual(unity.Evaluate(t), ours.Evaluate(t), 1e-4f, $"t = {t}");
        }
    }
}
