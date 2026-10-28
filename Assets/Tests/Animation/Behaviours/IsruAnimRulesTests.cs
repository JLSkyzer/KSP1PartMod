using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary><see cref="IsruAnimRules"/>: Group entries and the native converter, drill and scanner (plan (k)).</summary>
    public class IsruAnimRulesTests
    {
        private static AnimEntryDef Group(string deploy = "Deploy", string active = "Drill", bool always = false) =>
            new AnimEntryDef { Kind = EntryKind.Group, Key = "group:" + deploy, Clip = deploy, SecondClip = active, AlwaysActive = always };

        [Test] public void WantsActiveInput_GroupWithActiveClip_NotAlwaysActive_SwitchOn()
        {
            Assert.IsTrue(IsruAnimRules.WantsActiveInput(Group(), true));
            Assert.IsFalse(IsruAnimRules.WantsActiveInput(Group(), false), "isru_active_animation off");
            Assert.IsFalse(IsruAnimRules.WantsActiveInput(Group(always: true), true), "AlwaysActive plays as in phase 1");
            Assert.IsFalse(IsruAnimRules.WantsActiveInput(Group(active: " "), true), "no active clip");
            Assert.IsTrue(IsruAnimRules.WantsActiveInput(Group(deploy: null), true), "an active clip without a deploy clip");
            Assert.IsFalse(IsruAnimRules.WantsActiveInput(new AnimEntryDef { Kind = EntryKind.Toggle, Clip = "a", SecondClip = "b" }, true));
            Assert.IsFalse(IsruAnimRules.WantsActiveInput(null, true));
        }

        [Test] public void CanDriveBridge_GroupWithADeployClip()
        {
            Assert.IsTrue(IsruAnimRules.CanDriveBridge(Group()));
            Assert.IsFalse(IsruAnimRules.CanDriveBridge(Group(deploy: " ")), "SurfaceScanner, MiniISRU: always extended");
            Assert.IsFalse(IsruAnimRules.CanDriveBridge(new AnimEntryDef { Kind = EntryKind.Toggle, Clip = "Lights" }));
            Assert.IsFalse(IsruAnimRules.CanDriveBridge(null));
        }

        [Test] public void StateOf_FollowsTheMachine_InactiveIsExtended()
        {
            Assert.AreEqual(BridgeDeploy.Retracted, IsruAnimRules.StateOf(false, false, false));
            Assert.AreEqual(BridgeDeploy.Extending, IsruAnimRules.StateOf(true, true, false));
            Assert.AreEqual(BridgeDeploy.Extended, IsruAnimRules.StateOf(true, false, false));
            Assert.AreEqual(BridgeDeploy.Retracting, IsruAnimRules.StateOf(false, true, false));
            foreach (var deployed in new[] { false, true })
            foreach (var moving in new[] { false, true })
                Assert.AreEqual(BridgeDeploy.Extended, IsruAnimRules.StateOf(deployed, moving, true), "gated by B9 or disabled");
        }

        [Test] public void StateOf_AGroupDeployAndRetract()
        {
            var g = new GroupMachine(Group(), 1f, 1f);
            BridgeDeploy S() => IsruAnimRules.StateOf(g.Deployed, g.IsMoving, false);
            Assert.AreEqual(BridgeDeploy.Retracted, S());
            g.Deploy(false);
            g.Tick(0.1f, false);
            Assert.AreEqual(BridgeDeploy.Extending, S());
            for (var i = 0; i < 20; i++) g.Tick(0.1f, false);
            Assert.AreEqual(BridgeDeploy.Extended, S());
            g.Retract(false);
            g.Tick(0.1f, false);
            Assert.AreEqual(BridgeDeploy.Retracting, S());
            for (var i = 0; i < 20; i++) g.Tick(0.1f, false);
            Assert.AreEqual(BridgeDeploy.Retracted, S());
        }
    }
}
