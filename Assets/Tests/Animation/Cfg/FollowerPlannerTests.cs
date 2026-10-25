using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>The "Redux follower" entries: Redux 26w42c binds a part's single deployable source, and every
    /// parachute, to a Mecanim copy of the legacy clip (<c>Ksp1GenericDeployableAnimatorBuilder.TryConfigure</c>,
    /// <c>Ksp1ParachuteAnimatorBuilder.TryConfigure</c>); we draw that clip from Redux's state.</summary>
    public class FollowerPlannerTests
    {
        private static IKsp1Node Generic(string clip) => CfgText.Module("name = ModuleAnimateGeneric\nanimationName = " + clip);
        private static IKsp1Node Solar(string clip) => CfgText.Module("name = ModuleDeployableSolarPanel\nanimationName = " + clip);
        private static IKsp1Node Leg(string clip) => CfgText.Module("name = ModuleLandingLeg\nanimationName = " + clip);
        private static IKsp1Node Wheel(string transform, string clip, int deployed) =>
            CfgText.Module($"name = ModuleWheelDeployment\nanimationTrfName = {transform}\nanimationStateName = {clip}\ndeployedPosition = {deployed}\nextendDurationFactor = 2");
        private static IKsp1Node Chute(string extra = "") => CfgText.Module("name = ModuleParachute\n" + extra);

        private static PartAnimationPlan Plan(bool follow, bool own, bool nativeSolar, params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, nativeSolar, new HashSet<string>(), own, follow);

        private static AnimEntryDef Follower(PartAnimationPlan p) => p.Entries.Single(e => e.Kind == EntryKind.ReduxFollower);

        [Test] public void Follow_SingleAnimateGeneric_FollowerOnReduxClip()
        {
            var p = Plan(true, false, false, Generic("door"));
            var f = Follower(p);
            Assert.AreEqual(("door", 0, "reduxfollower:door:0"), (f.Clip, f.ModuleIndex, f.Key));
            CollectionAssert.AreEqual(new[] { "door" }, p.ReduxClips);
            Assert.AreEqual(("Module_Deployable", false), (p.ReduxDeployableModule, p.RemoveReduxDeployable));
        }

        [Test] public void Follow_Disabled_NoFollower() =>
            CollectionAssert.IsEmpty(Plan(false, false, false, Generic("door")).Entries);

        [Test] public void Follow_OldOverload_NoFollower() =>
            CollectionAssert.IsEmpty(OwnershipPlanner.Plan(new[] { Generic("door") }, false, new HashSet<string>(), false).Entries);

        [Test] public void Follow_OwnFirstDeployable_OwnEntryInstead()
        {
            var p = Plan(true, true, false, Generic("door"));
            Assert.AreEqual(EntryKind.Toggle, p.Entries.Single().Kind);
            Assert.IsTrue(p.RemoveReduxDeployable);
        }

        [Test] public void Follow_TwoSources_FallbackRuntime_NoFollower()
        {
            // Two sources: Redux's legacy runtime (Ksp1DeployableAnimationRuntime) plays the first clip; it works.
            var p = Plan(true, false, false, Generic("a"), Generic("b"));
            Assert.IsFalse(p.Entries.Any(e => e.Kind == EntryKind.ReduxFollower));
            Assert.AreEqual("Ksp1Module_Deployable", p.ReduxDeployableModule);
        }

        [Test] public void Follow_LandingLeg_BridgeAndFollower()
        {
            var p = Plan(true, false, false, Leg("XDeploy"));
            CollectionAssert.AreEquivalent(new[] { EntryKind.GearBridge, EntryKind.ReduxFollower }, p.Entries.Select(e => e.Kind));
            var f = Follower(p);
            Assert.AreEqual(("XDeploy", 1000f, true), (f.Clip, f.EditorSpeedMult, f.DeployForward));
        }

        [Test] public void Follow_Wheel_KeepsKsp1Direction()
        {
            var f = Follower(Plan(true, false, false, Wheel("leg", "Deploy", 0)));
            Assert.AreEqual(("Deploy", "leg", false, 2f), (f.Clip, f.AnimatorName, f.DeployForward, f.SpeedFactorIn));
        }

        [Test] public void Follow_SolarAloneWithNativePanel_Followed()
        {
            // One source: Redux binds the solar clip to its Animator for Module_SolarPanel (HasNativeSolarDeployableModule).
            var p = Plan(true, false, true, Solar("panel"));
            Assert.AreEqual("panel", Follower(p).Clip);
            Assert.IsNull(p.ReduxDeployableModule);
        }

        [Test] public void Follow_SolarPlusGeneric_TwoSources_NoFollower() =>
            Assert.IsFalse(Plan(true, false, true, Solar("s"), Generic("g")).Entries.Any(e => e.Kind == EntryKind.ReduxFollower));

        [Test] public void Follow_SourceWithoutClip_NotCounted()
        {
            var p = Plan(true, false, false, CfgText.Module("name = ModuleAnimateGeneric"), Generic("door"));
            Assert.AreEqual("door", Follower(p).Clip);
        }

        [Test] public void Chute_Defaults()
        {
            var c = Plan(true, false, false, Chute()).Entries.Single();
            Assert.AreEqual((EntryKind.ChuteFollower, "semiDeploy", "fullyDeploy"), (c.Kind, c.Clip, c.SecondClip));
            Assert.AreEqual((0.5f, 0.16667f), (c.SpeedFactorIn, c.SpeedFactorOut));
            Assert.AreEqual("chutefollower:semiDeploy:0", c.Key);
        }

        [Test] public void Chute_CfgValues()
        {
            var c = Plan(true, false, false,
                Chute("semiDeployedAnimation = s\nfullyDeployedAnimation = f\nsemiDeploymentSpeed = 0.3\ndeploymentSpeed = 0.12")).Entries.Single();
            Assert.AreEqual(("s", "f", 0.3f, 0.12f), (c.Clip, c.SecondClip, c.SpeedFactorIn, c.SpeedFactorOut));
        }

        [Test] public void Chute_OnlyFirstModule()
        {
            var p = Plan(true, false, false, Chute(), Chute("semiDeployedAnimation = x"));
            Assert.AreEqual(0, p.Entries.Single().ModuleIndex);
        }

        [Test] public void Chute_FollowDisabled_None() => CollectionAssert.IsEmpty(Plan(false, false, false, Chute()).Entries);

        [Test] public void Chute_PlusDeployable_Both()
        {
            var p = Plan(true, false, false, Chute(), Generic("cover"));
            CollectionAssert.AreEquivalent(new[] { EntryKind.ChuteFollower, EntryKind.ReduxFollower }, p.Entries.Select(e => e.Kind));
        }
    }
}
