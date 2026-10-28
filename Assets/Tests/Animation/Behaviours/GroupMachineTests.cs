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

        // ---- 0.10.0: the optional active input (converters and drills plan, Task 4)

        [Test] public void ActiveInput_True_LoopsWhileDeployed()
        {
            var running = true;
            var g = Group(alwaysActive: false, deployLen: 2f, activeLen: 4f);
            g.ActiveInput = () => running;
            g.Deploy(false); Run(g, 1f); Assert.IsFalse(g.ActivePose.Enabled, "waits for the deploy clip");
            Run(g, 1.1f); Assert.IsTrue(g.ActivePose.Enabled);
            Run(g, 5f); Assert.IsTrue(g.ActivePose.Enabled, "loops");
            Assert.That(g.ActivePose.NormalizedTime, Is.InRange(0f, 1f));
        }

        [Test] public void ActiveInput_False_Stops_TrueAgain_Restarts()
        {
            var running = true;
            var g = Group(alwaysActive: false);
            g.ActiveInput = () => running;
            g.Deploy(false); Run(g, 3f); Assert.IsTrue(g.ActivePose.Enabled);
            running = false; Run(g, 0.02f); Assert.IsFalse(g.ActivePose.Enabled, "stops at once");
            Run(g, 2f); Assert.IsFalse(g.ActivePose.Enabled);
            running = true; Run(g, 0.04f); Assert.IsTrue(g.ActivePose.Enabled);
            Assert.That(g.ActivePose.NormalizedTime, Is.LessThan(0.02f), "from the start");
        }

        [Test] public void ActiveInput_NotDeployed_NoClip()
        {
            var g = Group(alwaysActive: false);
            g.ActiveInput = () => true;
            Run(g, 5f); Assert.IsFalse(g.ActivePose.Enabled);
            g.Deploy(false); Run(g, 3f); Assert.IsTrue(g.ActivePose.Enabled);
            g.Retract(false); Run(g, 0.02f); Assert.IsFalse(g.ActivePose.Enabled, "a retract stops it");
        }

        [Test] public void ActiveInput_AlwaysActive_Unchanged()
        {
            var g = Group(alwaysActive: true);
            g.ActiveInput = () => false;
            g.Deploy(false); Run(g, 3f); Assert.IsTrue(g.ActivePose.Enabled, "AlwaysActive wins over the input");
        }

        [Test] public void ActiveInput_NeverInEditor()
        {
            var g = Group(alwaysActive: false);
            g.ActiveInput = () => true;
            g.Deploy(true); Run(g, 10, editor: true); Assert.IsFalse(g.ActivePose.Enabled);
        }

        [Test] public void ActiveInput_DeployedKeyUnchanged()
        {
            var plain = Group(alwaysActive: false);
            var fed = Group(alwaysActive: false);
            fed.ActiveInput = () => true;
            foreach (var g in new[] { plain, fed }) { g.Deploy(false); Run(g, 3f); }
            var a = new Dictionary<string, float>();
            var b = new Dictionary<string, float>();
            plain.Save(a); fed.Save(b);
            CollectionAssert.AreEquivalent(new[] { Key + ".deployed" }, b.Keys);
            CollectionAssert.AreEquivalent(a, b);
            Assert.IsTrue(fed.HasDeploy);
            Assert.IsFalse(Group(deploy: null).HasDeploy);
        }
    }
}
