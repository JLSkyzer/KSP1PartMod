using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class PluginMachinesTests
    {
        private const string DKey = "decouplerclip:deploy:0";

        private static DecouplerClipMachine Dec(float len = 2f) =>
            new DecouplerClipMachine(new AnimEntryDef { Kind = EntryKind.DecouplerClip, Key = DKey, Clip = "deploy" }, len);

        private static AccelSpring Spring(float deployed = 0.5f, float stowed = 0.1f, float k = 40f, float c = 12f) =>
            new AccelSpring(new AnimEntryDef
            {
                Kind = EntryKind.AccelSpring, Key = "accelspring:whip:0", SpringK = k, SpringDamping = c,
                AnchorDeployed = deployed, AnchorRetracted = stowed,
            }, 1f);

        [Test] public void Decoupler_PlaysOnceOnDecoupled()
        {
            var m = Dec(); m.Tick(0.5f, false, false, 1f);
            Assert.AreEqual((0f, 0f), (m.Pose.NormalizedTime, m.Pose.Speed));
            m.Tick(1f, false, true, 1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
            m.Tick(5f, false, true, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            m.Tick(5f, false, false, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime, "plays once, never rewinds in flight");
        }

        [Test] public void Decoupler_LoadedDecoupled_ShowsEndPose()
        {
            var m = Dec(); m.Load(SavedValues.Of(DKey, ("t", 0f))); m.Tick(0f, false, decoupled: true, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Decoupler_FirstSeenDecoupled_ShowsEndPose()
        {
            var m = Dec(); m.Tick(0.01f, false, true, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Decoupler_SavedT_ShowsEndPoseEvenIfFlagNotYetRead()
        {
            var m = Dec(); m.Load(SavedValues.Of(DKey, ("t", 0.4f))); m.Tick(0.01f, false, false, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void Decoupler_SaveWritesT_AndClearsChanged()
        {
            var m = Dec(); m.Tick(0f, false, false, 1f); m.Tick(1f, false, true, 1f);
            Assert.IsTrue(m.Changed);
            var d = new Dictionary<string, float>(); m.Save(d);
            Assert.AreEqual(0.5f, d[DKey + ".t"], 1e-4f);
            Assert.IsFalse(m.Changed);
        }

        [Test] public void Decoupler_Preview_UsesEditorSpeed_AndToggles()
        {
            var m = Dec(); Assert.IsTrue(m.TogglePreview()); m.Tick(0.1f, true, false, 1000f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            m.TogglePreview(); m.Tick(1f, true, false, 1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void AccelSpring_SettlesAtAnchor()
        {
            var s = Spring();
            for (var i = 0; i < 400; i++) s.Tick(0.02f, 0f);
            Assert.AreEqual(0.1f, s.Pose.NormalizedTime, 1e-3f);
            for (var i = 0; i < 400; i++) s.Tick(0.02f, AccelSpring.OneG);
            Assert.AreEqual(0.5f, s.Pose.NormalizedTime, 1e-3f);
        }

        [Test] public void AccelSpring_Underdamped_Overshoots()
        {
            var s = Spring(k: 200f, c: 1f);
            var min = 1f;
            for (var i = 0; i < 200; i++) { s.Tick(0.01f, 0f); min = System.Math.Min(min, s.Pose.NormalizedTime); }
            Assert.Less(min, 0.1f);
        }

        [Test] public void AccelSpring_NoField_HoldsDeployedAnchor()
        {
            var s = Spring(deployed: 0.7f); s.Tick(0.02f, float.NaN);
            Assert.AreEqual(0.7f, s.Pose.NormalizedTime);
        }

        [Test] public void AccelSpring_StartsAtDeployedAnchor_AndIsNeverDirty()
        {
            var s = Spring(deployed: 0.5f);
            Assert.AreEqual(0.5f, s.Pose.NormalizedTime);
            s.Tick(0.02f, 0f);
            Assert.IsFalse(s.Changed);
        }

        [Test] public void AccelSpring_PoseStaysInsideClip()
        {
            var s = Spring(k: 5000f, c: 0f);
            for (var i = 0; i < 100; i++) { s.Tick(0.05f, 0f); Assert.That(s.Pose.NormalizedTime, Is.InRange(0f, 1f)); }
        }

        [TestCase(5f, 10f, 1f, 0.5f)] [TestCase(5f, 0f, 1f, 0f)] [TestCase(-1f, 10f, 1f, 0f)]
        [TestCase(5f, 10f, 3f, 1.5f)] [TestCase(5f, -2f, 1f, 0f)]
        public void Rcs_Scalar(float f, float max, float mult, float expected) =>
            Assert.AreEqual(expected, RcsScalar.Of(f, max, mult), 1e-5f);

        [TestCase(true, true, true)] [TestCase(true, false, false)] [TestCase(false, true, false)] [TestCase(false, false, false)]
        public void MeshHider_Rule(bool flight, bool retracted, bool hidden) =>
            Assert.AreEqual(hidden, MeshHiderRule.Hidden(flight, retracted));

        [Test] public void AxisBlend_UnconstrainedAxisKeepsOriginal()
        {
            float x = 10, y = 20, z = 30;
            AxisBlend.Blend(ref x, ref y, ref z, 100, 200, 300, new[] { true, false, false }, new[] { 1f, 1f, 1f });
            Assert.AreEqual((100f, 20f, 30f), (x, y, z));
        }

        [Test] public void AxisBlend_ScaleLerpsFromOriginal()
        {
            float x = 10, y = 20, z = 30;
            AxisBlend.Blend(ref x, ref y, ref z, 30, 60, 90, new[] { true, true, true }, new[] { 0.5f, 0f, 1f });
            Assert.AreEqual(20f, x, 1e-4f); Assert.AreEqual(20f, y, 1e-4f); Assert.AreEqual(90f, z, 1e-4f);
        }

        [Test] public void AxisBlend_TakesTheShortWayRound()
        {
            float x = 350, y = 0, z = 0;
            AxisBlend.Blend(ref x, ref y, ref z, 10, 0, 0, new[] { true, true, true }, new[] { 0.5f, 1f, 1f });
            Assert.AreEqual(360f, x, 1e-3f);
        }

        [Test] public void AxisBlend_NullArrays_KeepOriginal()
        {
            float x = 1, y = 2, z = 3;
            AxisBlend.Blend(ref x, ref y, ref z, 9, 9, 9, null, null);
            Assert.AreEqual((1f, 2f, 3f), (x, y, z));
        }

        [Test] public void AxisBlend_MissingScale_MeansOne()
        {
            float x = 1, y = 2, z = 3;
            AxisBlend.Blend(ref x, ref y, ref z, 9, 9, 9, new[] { true, true, true }, null);
            Assert.AreEqual((9f, 9f, 9f), (x, y, z));
        }
    }
}
