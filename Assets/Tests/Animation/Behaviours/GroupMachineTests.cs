using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class GroupMachineTests
    {
        private const string Key = "group:Deploy:0";

        private static GroupMachine Group(bool alwaysActive = true, string deploy = "Deploy", string active = "Drill",
            float deployLen = 2f, float activeLen = 4f) =>
            new GroupMachine(new AnimEntryDef
            {
                Kind = EntryKind.Group, Key = Key, Clip = deploy, SecondClip = active, Layer = 3,
                EditorSpeedMult = 1000f, AlwaysActive = alwaysActive,
            }, deployLen, activeLen);

        private static Dictionary<string, float> Saved(bool deployed) => SavedValues.Of(Key, ("deployed", SavedValues.B(deployed)));

        private static void Run(GroupMachine g, float seconds, bool editor = false)
        {
            for (; seconds > 1e-6f; seconds -= 0.02f) g.Tick(seconds < 0.02f ? seconds : 0.02f, editor);
        }

        [Test] public void Group_Load_SnapsInstantly()
        {
            var g = Group(); g.Load(Saved(deployed: true)); g.Tick(0.001f, false); Assert.AreEqual(1f, g.DeployPose.NormalizedTime);
        }

        [Test] public void Group_ActiveClip_OnlyWithAlwaysActive()
        {
            var g = Group(alwaysActive: false); g.Deploy(false); Run(g, 10); Assert.IsFalse(g.ActivePose.Enabled);
        }

        [Test] public void Group_Default_RetractedAtZero()
        {
            var g = Group(); Assert.AreEqual((false, 0f, false), (g.Deployed, g.DeployPose.NormalizedTime, g.ActivePose.Enabled));
        }

        [Test] public void Group_Deploy_PlaysAtFlightSpeed()
        {
            var g = Group(deployLen: 2f); g.Deploy(false); Assert.IsTrue(g.Deployed); Run(g, 1f);
            Assert.AreEqual(0.5f, g.DeployPose.NormalizedTime, 1e-4f); Assert.AreEqual(1f, g.DeployPose.Speed);
        }

        [Test] public void Group_ActiveWaitsForDeployEnd_ThenLoops()
        {
            var g = Group(deployLen: 2f, activeLen: 4f); g.Deploy(false); Run(g, 1f); Assert.IsFalse(g.ActivePose.Enabled);
            Run(g, 1.1f); Assert.IsTrue(g.ActivePose.Enabled);
            Run(g, 5f); Assert.That(g.ActivePose.NormalizedTime, Is.InRange(0f, 1f));
            Assert.AreEqual(1f, g.ActivePose.Speed);
        }

        [Test] public void Group_ActiveNeverPlaysInEditor()
        {
            var g = Group(); g.Deploy(true); Run(g, 10, editor: true); Assert.IsFalse(g.ActivePose.Enabled);
        }

        [Test] public void Group_Editor_DeploySnaps()
        {
            var g = Group(deployLen: 5f); g.Deploy(true); g.Tick(0.001f, true); Assert.AreEqual(1f, g.DeployPose.NormalizedTime);
        }

        [Test] public void Group_Retract_StopsActiveAndRunsBack()
        {
            var g = Group(deployLen: 1f); g.Load(Saved(true)); Run(g, 0.1f); Assert.IsTrue(g.ActivePose.Enabled);
            g.Retract(false); Assert.AreEqual((false, false, 1f, -1f),
                (g.Deployed, g.ActivePose.Enabled, g.DeployPose.NormalizedTime, g.DeployPose.Speed));
            Run(g, 1.1f); Assert.AreEqual(0f, g.DeployPose.NormalizedTime);
        }

        [Test] public void Group_DeployTwice_Ignored()
        {
            var g = Group(deployLen: 2f); g.Deploy(false); Run(g, 1f); g.Deploy(false);
            Assert.AreEqual(0.5f, g.DeployPose.NormalizedTime, 1e-4f);
        }

        [Test] public void Group_NoDeployClip_DeploysAtOnce()
        {
            var g = Group(deploy: null); Assert.IsFalse(g.DeployPose.Enabled); g.Deploy(false); g.Tick(0.02f, false);
            Assert.IsTrue(g.ActivePose.Enabled);
        }

        [Test] public void Group_NoActiveClip_ActiveDisabled()
        {
            var g = Group(active: null); g.Load(Saved(true)); Run(g, 1f); Assert.IsFalse(g.ActivePose.Enabled);
        }

        [Test] public void Group_SaveLoad_RoundTrips()
        {
            var g = Group(); g.Deploy(false); var d = new Dictionary<string, float>(); g.Save(d);
            CollectionAssert.AreEquivalent(new[] { Key + ".deployed" }, d.Keys);
            var n = Group(); n.Load(d); Assert.AreEqual((true, 1f), (n.Deployed, n.DeployPose.NormalizedTime));
        }

        [Test] public void Group_Changed_OnlyOnDeployOrRetract()
        {
            var g = Group(); g.Tick(0.02f, false); Assert.IsFalse(g.Changed);
            g.Deploy(false); Assert.IsTrue(g.Changed);
            g.Save(new Dictionary<string, float>()); Assert.IsFalse(g.Changed);
            Run(g, 1f); Assert.IsFalse(g.Changed, "only Deployed is saved");
            g.Retract(false); Assert.IsTrue(g.Changed);
        }
    }
}
