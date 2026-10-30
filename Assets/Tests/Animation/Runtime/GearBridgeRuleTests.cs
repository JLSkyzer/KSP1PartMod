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

        // ourBase, wheelBase, nativeDeployable, inFlight, transformsFound -> the wheel base owns the Gear toggle
        [TestCase(true, true, true, false, false, true, TestName = "VAB: stands down (S, no Gear toggle listed)")]
        [TestCase(true, true, true, true, true, true, TestName = "Flight, transforms found: the wheel base's toggle")]
        [TestCase(true, true, true, true, false, false, TestName = "Flight, a transform missing: early return, bridge kept")]
        [TestCase(true, true, false, true, true, false, TestName = "Legacy deployable only: bridge kept")]
        [TestCase(true, true, false, false, false, false, TestName = "Legacy deployable only, VAB: bridge kept")]
        [TestCase(false, true, true, true, true, false, TestName = "Base not ours (refused or no record): bridge kept")]
        [TestCase(true, false, true, true, true, false, TestName = "No wheel base: bridge kept")]
        public void NativeWheelGear_Cases(bool ourBase, bool wheelBase, bool nativeDeployable, bool inFlight, bool transformsFound,
            bool expected) =>
            Assert.AreEqual(expected, GearBridgeRule.NativeWheelGear(ourBase, wheelBase, nativeDeployable, inFlight, transformsFound));

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
