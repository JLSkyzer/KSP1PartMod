using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class PluginMachinesTests
    {
        private const string SKey = "accelspring:whip:0";
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

        [Test] public void Decoupler_SavedT_StillAttached_ShowsPoseZero()
        {
            var m = Dec(); m.Load(SavedValues.Of(DKey, ("t", 0.4f))); m.Tick(0.01f, false, false, 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime, "KSP1 resets the position at flight start");
        }

        [Test] public void Decoupler_EditorPreviewLeftOut_LaunchesAttached()
        {
            var m = Dec(); m.TogglePreview(); m.Tick(10f, true, false, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            var d = new Dictionary<string, float>(); m.Save(d);
            Assert.AreEqual(0f, d[DKey + ".t"]);
            m.Tick(0.01f, false, false, 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime);
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

        [Test] public void AccelSpring_SettlesAtAnchorPlusAccelOverK()
        {
            var s = Spring(k: 200f, c: 20f);
            for (var i = 0; i < 400; i++) s.Tick(0.02f, AccelSpring.OneG);
            Assert.AreEqual(0.5f + AccelSpring.OneG / 200f, s.Pose.NormalizedTime, 1e-3f);
            s.Toggle();
            for (var i = 0; i < 400; i++) s.Tick(0.02f, AccelSpring.OneG);
            Assert.AreEqual(0.1f + AccelSpring.OneG / 200f, s.Pose.NormalizedTime, 1e-3f);
        }

        [Test] public void AccelSpring_Toggle_FlipsDeployed_AndSaves()
        {
            var s = Spring(); Assert.IsTrue(s.Deployed); Assert.IsFalse(s.Changed);
            s.Toggle();
            Assert.IsFalse(s.Deployed); Assert.IsTrue(s.Changed);
            var d = new Dictionary<string, float>(); s.Save(d);
            Assert.AreEqual((0f, 1f), (d[SKey + ".deployed"], d[SKey + ".bounce"]));
            Assert.IsFalse(s.Changed);
            var r = Spring(); r.Load(d);
            Assert.IsFalse(r.Deployed);
        }

        [Test] public void AccelSpring_WallBounce_ReversesVelocity_ElseStops()
        {
            var bounce = Spring(k: 0f, c: 0f); var stick = Spring(k: 0f, c: 0f);
            stick.Load(SavedValues.Of(SKey, ("bounce", 0f)));
            var lowest = 1f; var stuck = 1f;
            for (var i = 0; i < 100; i++)
            {
                bounce.Tick(0.01f, AccelSpring.OneG); stick.Tick(0.01f, AccelSpring.OneG);
                if (i > 40) { lowest = System.Math.Min(lowest, bounce.Pose.NormalizedTime); stuck = System.Math.Min(stuck, stick.Pose.NormalizedTime); }
            }
            Assert.Less(lowest, 0.95f, "bounces back off the wall");
            Assert.GreaterOrEqual(stuck, 0.999f, "stays on the wall");
        }

        [Test] public void AccelSpring_NoField_HoldsCurrentAnchor()
        {
            var s = Spring(deployed: 0.7f, stowed: 0.2f); s.Tick(0.02f, float.NaN);
            Assert.AreEqual(0.7f, s.Pose.NormalizedTime);
            s.Toggle(); s.Tick(0.02f, float.NaN);
            Assert.AreEqual(0.2f, s.Pose.NormalizedTime);
        }

        [Test] public void AccelSpring_HugeDt_IsClampedAndStable()
        {
            var s = Spring(k: 200f, c: 5f); s.Tick(1000f, AccelSpring.OneG);
            Assert.That(s.Pose.NormalizedTime, Is.InRange(0f, 1f));
            Assert.IsFalse(float.IsNaN(s.Pose.NormalizedTime));
        }

        [Test] public void AccelSpring_PoseStaysInsideClip()
        {
            var s = Spring(k: 5000f, c: 0f);
            for (var i = 0; i < 100; i++) { s.Tick(0.05f, 100f); Assert.That(s.Pose.NormalizedTime, Is.InRange(0f, 1f)); }
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
            AxisBlend.Blend(ref x, ref y, ref z, 100, 200, 300, new[] { true, false, false }, new[] { 0f, 0f, 0f });
            Assert.AreEqual((100f, 20f, 30f), (x, y, z));
        }

        [Test] public void AxisBlend_ScaleWeightsTheOriginal_AsKsp1()
        {
            float x = 0, y = 0, z = 0;
            AxisBlend.Blend(ref x, ref y, ref z, 100, 0, 0, new[] { true, true, true }, new[] { 0.75f, 0f, 0f });
            Assert.AreEqual(25f, x, 1e-4f);
        }

        [Test] public void AxisBlend_ScaleLerpsFromOriginal()
        {
            float x = 10, y = 20, z = 30;
            AxisBlend.Blend(ref x, ref y, ref z, 30, 60, 90, new[] { true, true, true }, new[] { 0.5f, 0f, 1f });
            Assert.AreEqual(20f, x, 1e-4f); Assert.AreEqual(60f, y, 1e-4f); Assert.AreEqual(30f, z, 1e-4f);
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
    
        [Test] public void Decoupler_PreviewOn_FollowsTheToggle()
        {
            var m = Dec(); Assert.IsFalse(m.PreviewOn);
            m.TogglePreview(); Assert.IsTrue(m.PreviewOn);
            m.TogglePreview(); Assert.IsFalse(m.PreviewOn);
        }

        [Test] public void EngineDeploy_PreviewOn_EditorToggleOnly()
        {
            var m = new EngineDeployMachine(new AnimEntryDef { Kind = EntryKind.EngineDeploy, Key = "enginedeploy:n:0", Clip = "n", Retractable = true }, 1f);
            Assert.IsFalse(m.PreviewOn);
            m.Toggle(true); Assert.IsTrue(m.PreviewOn);
            m.Toggle(false); Assert.IsTrue(m.PreviewOn, "a flight toggle is not the preview");
        }
    }
}
