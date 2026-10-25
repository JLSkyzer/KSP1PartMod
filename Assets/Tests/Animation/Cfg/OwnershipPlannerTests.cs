using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    public class OwnershipPlannerTests
    {
        private static IKsp1Node Generic(string clip) => CfgText.Module("name = ModuleAnimateGeneric\nanimationName = " + clip);
        private static IKsp1Node Solar(string clip) => CfgText.Module("name = ModuleDeployableSolarPanel\nanimationName = " + clip);
        private static IKsp1Node Antenna(string clip) => CfgText.Module("name = ModuleDeployableAntenna\nanimationName = " + clip);
        private static IKsp1Node Wheel(string transform, string clip) =>
            CfgText.Module($"name = ModuleWheelDeployment\nanimationTrfName = {transform}\nanimationStateName = {clip}\ndeployedPosition = 1");
        private static IKsp1Node Throttle(string clip) => CfgText.Module("name = FXModuleAnimateThrottle\nanimationName = " + clip);
        private static IKsp1Node Group(string deploy, string active) =>
            CfgText.Module($"name = ModuleAnimationGroup\ndeployAnimationName = {deploy}\nactiveAnimationName = {active}");
        private static IKsp1Node Aero(int index, int pos) =>
            CfgText.Module($"name = ModuleDeployableAero\nDeployModuleIndex = {index}\nDeployModulePos = {pos}");
        private static IKsp1Node Other(string name) => CfgText.Module("name = " + name);
        private static IKsp1Node Color(string property, string moduleId = null) =>
            CfgText.Module($"name = ModuleColorChanger\nshaderProperty = {property}" + (moduleId == null ? "" : "\nmoduleID = " + moduleId));

        private static PartAnimationPlan Plan(params IKsp1Node[] modules) => PlanWith(false, false, null, modules);

        private static PartAnimationPlan PlanWith(bool nativeSolar, bool own, string[] native, params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, nativeSolar, new HashSet<string>(native ?? new string[0]), own);

        [Test] public void Plan_SingleAnimateGeneric_LeftToRedux()
        {
            var p = Plan(Generic("a"));
            CollectionAssert.IsEmpty(p.Entries);
            CollectionAssert.AreEqual(new[] { "a" }, p.ReduxClips);
            Assert.AreEqual(("Module_Deployable", false), (p.ReduxDeployableModule, p.RemoveReduxDeployable));
        }

        [Test] public void Plan_TwoAnimateGeneric_SecondIsOurs()
        {
            var p = Plan(Generic("a"), Generic("b"));
            Assert.AreEqual("b", p.Entries.Single().Clip);
            Assert.AreEqual(1, p.Entries.Single().ModuleIndex);
            CollectionAssert.AreEqual(new[] { "a" }, p.ReduxClips);
            Assert.AreEqual("Ksp1Module_Deployable", p.ReduxDeployableModule);
        }

        [Test] public void Plan_TwoAnimateGenericSameClip_SecondLeftToReduxWithNote()
        {
            var p = Plan(Generic("a"), Generic("a"));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("MODULE[1]", p.Notes.Single());
        }

        [Test] public void Plan_SolarPlusGeneric_ReduxKeepsGeneric()
        {
            var p = PlanWith(true, false, null, Solar("s"), Generic("g"));
            CollectionAssert.AreEqual(new[] { "g" }, p.ReduxClips);
            Assert.AreEqual(EntryKind.Deploy, p.Entries.Single(e => e.Clip == "s").Kind);
        }

        [Test] public void Plan_NativeSolarAlone_LeftToRedux()
        {
            var p = PlanWith(true, false, null, Solar("s"));
            CollectionAssert.IsEmpty(p.Entries);
            CollectionAssert.AreEqual(new[] { "s" }, p.ReduxClips);
            Assert.IsNull(p.ReduxDeployableModule);
        }

        [Test] public void Plan_WheelDeploymentFirst_ReduxPlusGearBridge()
        {
            var p = Plan(Wheel("leg", "Deploy"));
            Assert.AreEqual(EntryKind.GearBridge, p.Entries.Single().Kind);
            Assert.AreEqual(("Deploy", "leg", "Gear"), (p.Entries[0].Clip, p.Entries[0].AnimatorName, p.Entries[0].ActionGroup));
            CollectionAssert.AreEqual(new[] { "Deploy" }, p.ReduxClips);
        }

        [Test] public void Plan_WheelDeploymentMerged_IsGearEntry()
        {
            var p = Plan(Generic("a"), Wheel("leg", "Deploy"));
            Assert.AreEqual(EntryKind.Gear, p.Entries.Single().Kind);
        }

        [Test] public void Plan_OwnFirstDeployable_TakesItAndRemovesRedux()
        {
            var p = PlanWith(false, true, null, Generic("a"));
            Assert.IsTrue(p.RemoveReduxDeployable);
            Assert.AreEqual("a", p.Entries.Single().Clip);
            CollectionAssert.IsEmpty(p.ReduxClips);
        }

        [Test] public void Plan_OwnFirstDeployable_WheelIsGearNotBridge() =>
            Assert.AreEqual(EntryKind.Gear, PlanWith(false, true, null, Wheel("leg", "Deploy")).Entries.Single().Kind);

        [Test] public void Plan_OwnFirstDeployable_SolarStaysRedux()
        {
            var p = PlanWith(false, true, null, Solar("s"));
            Assert.IsFalse(p.RemoveReduxDeployable);
            CollectionAssert.IsEmpty(p.Entries);
            CollectionAssert.AreEqual(new[] { "s" }, p.ReduxClips);
        }

        [Test] public void Plan_ReduxNativeCounterpartPresent_Skips()
        {
            // Redux 26w42c imports none of our modules natively, so the shipped table is empty
            // (OwnershipPlanner.NativeCounterparts). The mechanism is tested with a table given here.
            var table = new Dictionary<string, string[]> { ["ModuleDeployableAntenna"] = new[] { "Module_Antenna" } };
            var p = OwnershipPlanner.Plan(new[] { Antenna("dish") }, false, new HashSet<string> { "Module_Antenna" }, false, table);
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("Module_Antenna", p.Notes.Single());
        }

        [Test] public void Plan_NativeCounterpartAbsent_Owns()
        {
            var table = new Dictionary<string, string[]> { ["ModuleDeployableAntenna"] = new[] { "Module_Antenna" } };
            var p = OwnershipPlanner.Plan(new[] { Antenna("dish") }, false, new HashSet<string> { "Module_Engine" }, false, table);
            Assert.AreEqual(EntryKind.Deploy, p.Entries.Single().Kind);
        }

        [Test] public void Plan_ShippedNativeTable_HasNoEntryForNow() => CollectionAssert.IsEmpty(OwnershipPlanner.NativeCounterparts);

        [Test] public void Plan_AblatorColorChanger_NotedNotOwned()
        {
            var p = Plan(Color("_BurnColor", moduleId: "shieldChar"));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("shieldChar", p.Notes.Single());
        }

        [Test] public void Plan_ColorChangerNamedByAblator_NotedNotOwned()
        {
            var ablator = CfgText.Module("name = ModuleAblator\ncharModuleName = myChar");
            var p = Plan(ablator, Color("_BurnColor", moduleId: "myChar"));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("myChar", p.Notes.Single());
        }

        [Test] public void Plan_EmptyAnimationName_IsNotAReduxSource()
        {
            var p = Plan(Generic(""));
            CollectionAssert.IsEmpty(p.ReduxClips);
            Assert.IsNull(p.ReduxDeployableModule);
        }

        [Test] public void Plan_UnsupportedModules_BecomeEntries()
        {
            var p = Plan(Throttle("t"), Color("_EmissiveColor"), Group("d", "act"), Antenna("dish"),
                Other("ModuleParachute"), Other("FXModuleAnimateRCS"));
            CollectionAssert.AreEqual(new[] { EntryKind.Throttle, EntryKind.Color, EntryKind.Group, EntryKind.Deploy },
                p.Entries.Select(e => e.Kind));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, p.Entries.Select(e => e.ModuleIndex));
        }

        [Test] public void Plan_EntryOnReduxClip_LeftToRedux()
        {
            var p = Plan(Generic("a"), Throttle("a"), Group("x", "a"));
            CollectionAssert.IsEmpty(p.Entries);
            Assert.AreEqual(2, p.Notes.Count);
        }

        [Test] public void Plan_DeployableAero_AeroGateOnController()
        {
            var p = Plan(Generic("fins"), Other("ModuleControlSurface"), Aero(0, 1));
            var gate = p.Entries.Single();
            Assert.AreEqual((EntryKind.AeroGate, 0, 1f, 2), (gate.Kind, gate.ControllerModuleIndex, gate.DeployedPosition, gate.ModuleIndex));
        }

        [Test] public void Plan_DeployableAero_ControllerOutOfRange_Noted()
        {
            var p = Plan(Aero(5, 1));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("DeployModuleIndex", p.Notes.Single());
        }

        [Test] public void Plan_Ordinal_CountsSameKindAndClip()
        {
            var p = Plan(Generic("a"), Throttle("t"), Throttle("t"), Throttle("u"));
            CollectionAssert.AreEqual(new[] { "throttle:t:0", "throttle:t:1", "throttle:u:0" }, p.Entries.Select(e => e.Key));
        }
    }
}
