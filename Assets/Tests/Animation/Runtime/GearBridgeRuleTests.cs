using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class GearBridgeRuleTests
    {
        [TestCase("None", true)]
        [TestCase("Lights", true)]
        [TestCase(null, true)]
        [TestCase("Gear", false)]
        [TestCase(" gear ", false)]
        public void NeedsOwnAction_UnlessReduxIsOnGear(string group, bool expected) =>
            Assert.AreEqual(expected, GearBridgeRule.NeedsOwnAction(group));

        [TestCase("None", false, true)]
        [TestCase("None", true, false)]
        [TestCase("Gear", false, false)]
        [TestCase("Gear", true, false)]
        public void NeedsOwnAction_UnlessReduxOrANativeWheelBaseIsOnGear(string group, bool nativeWheelGear, bool expected) =>
            Assert.AreEqual(expected, GearBridgeRule.NeedsOwnAction(group, nativeWheelGear));

        [TestCase("ModuleWheelDeployment", "animationStateName")]
        [TestCase("ModuleLandingLeg", "animationName")]
        public void Bridge_CarriesTheGearGroup(string module, string clipKey)
        {
            var node = CfgText.Module($"name = {module}\n{clipKey} = Deploy\nanimationTrfName = leg");
            var plan = OwnershipPlanner.Plan(new[] { node }, false, null, false);
            var bridge = plan.Entries.Single();
            Assert.AreEqual(EntryKind.GearBridge, bridge.Kind);
            Assert.AreEqual("Gear", bridge.ActionGroup);
        }
    }
}
