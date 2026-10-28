using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="RadiatorAnimRules"/>: the deploy bridge of a deployable radiator (plan (g), Task 3).</summary>
    public class RadiatorAnimRulesTests
    {
        private static AnimEntryDef Deploy(int module) => new AnimEntryDef { Kind = EntryKind.Deploy, ModuleIndex = module, Key = "deploy:Deploy:0" };

        [Test] public void CanDriveBridge_DeployEntryOfThatModule()
        {
            Assert.IsTrue(RadiatorAnimRules.CanDriveBridge(Deploy(0), 0));
            Assert.IsTrue(RadiatorAnimRules.CanDriveBridge(Deploy(2), 2));
            Assert.IsFalse(RadiatorAnimRules.CanDriveBridge(Deploy(1), 0), "another module");
            Assert.IsFalse(RadiatorAnimRules.CanDriveBridge(Deploy(0), -1), "no ModuleDeployableRadiator");
            Assert.IsFalse(RadiatorAnimRules.CanDriveBridge(null, 0));
            Assert.IsFalse(RadiatorAnimRules.CanDriveBridge(new AnimEntryDef { Kind = EntryKind.Group, ModuleIndex = 0 }, 0), "only Deploy entries");
            Assert.IsFalse(RadiatorAnimRules.CanDriveBridge(new AnimEntryDef { Kind = EntryKind.Toggle, ModuleIndex = 0 }, 0));
        }

        [Test] public void StateOf_MapsTheDeployMachine()
        {
            Assert.AreEqual(BridgeDeploy.Retracted, RadiatorAnimRules.StateOf(DeployState.Retracted, false));
            Assert.AreEqual(BridgeDeploy.Extending, RadiatorAnimRules.StateOf(DeployState.Extending, false));
            Assert.AreEqual(BridgeDeploy.Extended, RadiatorAnimRules.StateOf(DeployState.Extended, false));
            Assert.AreEqual(BridgeDeploy.Retracting, RadiatorAnimRules.StateOf(DeployState.Retracting, false));
        }

        [Test] public void StateOf_InactiveEntry_AlwaysExtended()
        {
            foreach (DeployState s in System.Enum.GetValues(typeof(DeployState)))
                Assert.AreEqual(BridgeDeploy.Extended, RadiatorAnimRules.StateOf(s, true), s.ToString());
        }

        [Test] public void ShouldAutoEnable_OnlyOnATransitionToExtended()
        {
            Assert.IsTrue(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Extending, BridgeDeploy.Extended, true));
            Assert.IsTrue(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Retracted, BridgeDeploy.Extended, true));
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Extended, BridgeDeploy.Extended, true), "no transition");
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Extended, BridgeDeploy.Retracting, true));
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Retracting, BridgeDeploy.Retracted, true));
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(BridgeDeploy.Extending, BridgeDeploy.Extended, false), "radiator_auto_on off");
        }

        /// <summary>A B9-gated entry (inactive) shows Extended from its first step: the first step has no previous state, and
        /// is a transition to Extended, once.</summary>
        [Test] public void ShouldAutoEnable_GatedEntry_FirstStepOnly()
        {
            var first = RadiatorAnimRules.StateOf(DeployState.Retracted, true);
            Assert.AreEqual(BridgeDeploy.Extended, first);
            Assert.IsTrue(RadiatorAnimRules.ShouldAutoEnable(null, first, true));
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(first, RadiatorAnimRules.StateOf(DeployState.Retracted, true), true));
            Assert.IsFalse(RadiatorAnimRules.ShouldAutoEnable(null, BridgeDeploy.Retracted, true), "a folded first step");
        }
    }
}
