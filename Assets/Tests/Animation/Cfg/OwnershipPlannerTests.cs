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

        // ---------------------------------------------------------------- phase 2b: plugin modules and B9 gates

        private static IKsp1Node DeployEngine(string clip, int layer = 1) =>
            CfgText.Module($"name = ModuleDeployableEngine\nEngineAnimationName = {clip}\nLayer = {layer}");
        private static IKsp1Node Science(string clip) =>
            CfgText.Module("name = DMModuleScienceAnimateGeneric\nanimationName = " + clip);
        private static IKsp1Node LookAt(string rotator, string target) =>
            CfgText.Module($"name = ModuleAdvancedLookAtConstraint\nCONSTRAINLOOKFX\n{{\nrotatorsName = {rotator}\ntargetName = {target}\n}}");
        private static IKsp1Node ReduxLookAt(string rotator, string target) =>
            CfgText.Module($"name = FXModuleLookAtConstraint\nCONSTRAINLOOKFX\n{{\nrotatorsName = {rotator}\ntargetName = {target}\n}}");

        /// <summary>A visual ModuleB9PartSwitch <c>sw</c>: each subtype switches off the modules named by its
        /// IDENTIFIER bodies (<c>name = …</c> plus any other values).</summary>
        private static IKsp1Node B9(params (string subtype, string[] offIdentifiers)[] subtypes)
        {
            var body = "name = ModuleB9PartSwitch\nmoduleID = sw\n";
            foreach (var (subtype, off) in subtypes)
            {
                body += $"SUBTYPE\n{{\nname = {subtype}\ntransform = {subtype}_t\n";
                foreach (var id in off) body += "MODULE\n{\nIDENTIFIER\n{\n" + id + "\n}\nmoduleActive = false\n}\n";
                body += "}\n";
            }
            return CfgText.Module(body);
        }

        private static PartAnimationPlan Plan2b(bool pluginModules, bool b9, params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, false, null, pluginModules, b9);

        [Test] public void Plan_PluginModulesOff_NoPluginEntries() =>
            CollectionAssert.IsEmpty(Plan2b(false, true, DeployEngine("a")).Entries);

        [Test] public void Plan_OldOverloads_NoPluginEntries() =>
            CollectionAssert.IsEmpty(Plan(DeployEngine("a"), Science("s")).Entries);

        [Test] public void Plan_PluginModulesOn_AfterPhase1Entries_OwnOrdinals()
        {
            var p = Plan2b(true, true, Science("s"), Generic("a"), Generic("b"), DeployEngine("n"), LookAt("r1", "t"), Throttle("t"),
                LookAt("r2", "t"));
            CollectionAssert.AreEqual(new[] { "toggle:b:0", "throttle:t:0", "sciencetoggle:s:0", "enginedeploy:n:0", "lookat::0", "lookat::1" },
                p.Entries.Select(e => e.Key));
            CollectionAssert.AreEqual(new[] { 2, 5, 0, 3, 4, 6 }, p.Entries.Select(e => e.ModuleIndex));
        }

        [Test] public void Plan_PluginModules_Phase1KeysUnchanged()
        {
            var modules = new[] { DeployEngine("n"), Generic("a"), Generic("b"), Throttle("t"), Science("b") };
            CollectionAssert.AreEqual(Plan(modules).Entries.Select(e => e.Key),
                Plan2b(true, true, modules).Entries.Where(e => e.Kind <= EntryKind.ChuteFollower).Select(e => e.Key));
        }

        [Test] public void Plan_B9GatesOff_NoGates() =>
            Assert.IsNull(Plan2b(true, false, DeployEngine("a"), B9(("x", new[] { "name = ModuleDeployableEngine" }), ("y", new string[0])))
                .Entries.Single().Gates);

        [Test] public void Plan_B9GatesOn_GatesThePluginEntry()
        {
            var gate = Plan2b(true, true, DeployEngine("a"), B9(("x", new[] { "name = ModuleDeployableEngine" }), ("y", new string[0])))
                .Entries.Single().Gates.Single();
            Assert.AreEqual("sw", gate.SetId);
            CollectionAssert.AreEqual(new[] { "x" }, gate.InactiveIn);
        }

        [Test] public void Plan_B9OnReduxSource_NotedNotGated()
        {
            var p = Plan2b(true, true, Generic("a"), Generic("b"), B9(("x", new[] { "name = ModuleAnimateGeneric\nanimationName = a" })));
            Assert.IsNull(p.Entries.Single().Gates);
            Assert.IsTrue(p.Notes.Any(n => n.Contains("left to Redux, not gated")), string.Join("\n", p.Notes));
        }

        [Test] public void Plan_PluginClipOfRedux_LeftToReduxWithNote()
        {
            var p = Plan2b(true, true, Generic("a"), DeployEngine("a"));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("MODULE[1] ModuleDeployableEngine: clip 'a' is played by Redux", p.Notes.Single());
        }

        [Test] public void Plan_PluginClipOfOurEntry_DroppedWithNote()
        {
            var p = Plan2b(true, true, Generic("a"), Generic("b"), DeployEngine("b"));
            Assert.AreEqual(EntryKind.Toggle, p.Entries.Single().Kind);
            StringAssert.Contains("MODULE[2] ModuleDeployableEngine: clip 'b' is already ours (MODULE[1] ModuleAnimateGeneric)", p.Notes.Single());
        }

        [Test] public void Plan_TwoPluginEntriesOnOneClip_GatedApart_BothKept()
        {
            var p = Plan2b(true, true, DeployEngine("n", 1), DeployEngine("n", 2),
                B9(("x", new[] { "name = ModuleDeployableEngine\nLayer = 1" }), ("y", new[] { "name = ModuleDeployableEngine\nLayer = 2" })));
            Assert.AreEqual(2, p.Entries.Count, string.Join("\n", p.Notes));
            Assert.IsTrue(B9GatePlanner.GatedApart(p.Entries));
        }

        [Test] public void Plan_TwoPluginEntriesOnOneClip_NotGatedApart_SecondDropped()
        {
            var p = Plan2b(true, true, DeployEngine("n", 1), DeployEngine("n", 2),
                B9(("x", new[] { "name = ModuleDeployableEngine\nLayer = 1" }), ("y", new string[0])));
            Assert.AreEqual(0, p.Entries.Single().ModuleIndex);
            Assert.IsTrue(p.Notes.Any(n => n.Contains("MODULE[1] ModuleDeployableEngine: clip 'n' is already ours (MODULE[0] ModuleDeployableEngine)")),
                string.Join("\n", p.Notes));
        }

        [Test] public void Plan_SkipModules_DropsPluginEntry()
        {
            var p = OwnershipPlanner.Plan(new[] { DeployEngine("n"), Science("s") }, false, new HashSet<string>(), false, false,
                new HashSet<string> { "ModuleDeployableEngine" }, true, true);
            Assert.AreEqual(EntryKind.ScienceToggle, p.Entries.Single().Kind);
            StringAssert.Contains("MODULE[0] ModuleDeployableEngine: skipped by skip_modules", p.Notes.Single());
        }

        [Test] public void Plan_LookAtOnReduxRotator_KeptWithNote()
        {
            var p = Plan2b(true, true, ReduxLookAt("piston", "anchor"), LookAt("Piston", "anchor2"), LookAt("gimbal", "t"));
            Assert.AreEqual(2, p.Entries.Count(e => e.Kind == EntryKind.LookAt));
            StringAssert.Contains("MODULE[1] ModuleAdvancedLookAtConstraint: rotator 'Piston' is also Redux's (FXModuleLookAtConstraint MODULE[0])",
                p.Notes.Single());
        }

        [Test] public void GatedApart_Rules()
        {
            B9Gate G(params string[] off) => new B9Gate { SetId = "sw", SubtypeIds = new[] { "x", "y", "z" }, InactiveIn = off, DefaultSubtype = "x" };
            AnimEntryDef E(params B9Gate[] g) => new AnimEntryDef { Gates = g.Length == 0 ? null : g };
            Assert.IsTrue(B9GatePlanner.GatedApart(new[] { E(G("y", "z")), E(G("x")) }));
            Assert.IsFalse(B9GatePlanner.GatedApart(new[] { E(G("y")), E(G("x")) }), "both active in z");
            Assert.IsFalse(B9GatePlanner.GatedApart(new[] { E(G("y", "z")), E() }), "ungated is active everywhere");
            Assert.IsTrue(B9GatePlanner.GatedApart(new[] { E(G("y", "z")), E(G("x", "z")), E(G("x", "y")) }));
            var other = new B9Gate { SetId = "other", SubtypeIds = new[] { "x", "y", "z" }, InactiveIn = new[] { "x" }, DefaultSubtype = "y" };
            Assert.IsFalse(B9GatePlanner.GatedApart(new[] { E(G("y", "z")), E(other) }), "different switches");
        }

        // ------------------------------------------------------------------ the ablator char (0.8.0)

        private const string StockShield = @"
MODULE
{
    name = ModuleAblator
    ablativeResource = Ablator
    pyrolysisLossFactor = 6000
    ablationTempThresh = 500
    useChar = True
    charModuleName = shieldChar
}
MODULE
{
    name = ModuleColorChanger
    moduleID = shieldChar
    shaderProperty = _BurnColor
    useRate = False
    toggleInEditor = False
    toggleInFlight = False
    redCurve
    {
        key = 0 0
        key = 1 1
    }
    alphaCurve
    {
        key = 0 0.8
    }
}";

        private static IKsp1Node[] Modules(string text) => CfgText.Parse(text).GetNodes("MODULE").ToArray();

        private static PartAnimationPlan PlanChar(bool ablatorChar, params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, true, null, true, true, false, ablatorChar);

        [Test] public void AblatorChar_StockShield_OneColorEntryFollowingTheAblator()
        {
            var p = PlanChar(true, Modules(StockShield));
            var e = p.Entries.Single();
            Assert.AreEqual(EntryKind.Color, e.Kind);
            Assert.AreEqual(ScalarSource.Ablator, e.ScalarSource);
            Assert.IsTrue(e.ScalarLinked, "no ramp, nothing saved");
            Assert.AreEqual("Ablator", e.ScalarResource);
            Assert.AreEqual("shieldChar", e.ModuleId);
            Assert.AreEqual("color:_BurnColor:0", e.Key);
            Assert.AreEqual(1, e.ModuleIndex);
            Assert.IsFalse(e.ToggleInEditor || e.ToggleInFlight, "no button");
            Assert.IsNull(e.ActionGroup);
            CollectionAssert.AreEqual(new[] { "MODULE[1] ModuleColorChanger: moduleID 'shieldChar' follows the ablator (Module_Heatshield)" },
                p.Notes);
        }

        [Test] public void AblatorChar_Off_The070Note_Unchanged()
        {
            var p = PlanChar(false, Modules(StockShield));
            CollectionAssert.IsEmpty(p.Entries);
            CollectionAssert.AreEqual(new[] { "MODULE[1] ModuleColorChanger: moduleID 'shieldChar' is driven by ModuleAblator, stays at 0, not ours" },
                p.Notes);
            var older = OwnershipPlanner.Plan(Modules(StockShield), false, new HashSet<string>(), false);
            CollectionAssert.AreEqual(p.Notes, older.Notes, "the default of every older overload");
        }

        [Test] public void AblatorChar_NoAblatorOrNoUseChar_The070Note()
        {
            var p = PlanChar(true, Color("_BurnColor", moduleId: "shieldChar"));
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("stays at 0, not ours", p.Notes.Single());

            var noUseChar = PlanChar(true, CfgText.Module("name = ModuleAblator"), Color("_BurnColor", moduleId: "shieldChar"));
            CollectionAssert.IsEmpty(noUseChar.Entries);
            StringAssert.Contains("stays at 0, not ours", noUseChar.Notes.Single());

            var other = PlanChar(true, CfgText.Module("name = ModuleAblator\nuseChar = true\ncharModuleName = myChar\nablativeResource = Ab2"),
                Color("_BurnColor", moduleId: "myChar"));
            Assert.AreEqual("Ab2", other.Entries.Single().ScalarResource);
            Assert.AreEqual("myChar", other.Entries.Single().ModuleId);
        }

        /// <summary>A KV pod: its shieldChar and a light changer drive the same shader property. The char is not planned (it
        /// would outrank the light every frame); the light keeps the key it had in 0.7.0, and the note says why.</summary>
        [Test] public void AblatorChar_KvPodLike_NotPlanned_TheLightKeepsItsKeyAndSlot()
        {
            var modules = Modules(StockShield.Replace("_BurnColor", "_EmissiveColor")).Concat(new[]
            {
                Color("_EmissiveColor"),
            }).ToArray();
            var before = PlanChar(false, modules);
            var after = PlanChar(true, modules);
            Assert.AreEqual("color:_EmissiveColor:0", before.Entries.Single().Key);
            var light = after.Entries.Single();
            Assert.AreEqual("color:_EmissiveColor:0", light.Key);
            Assert.AreEqual(2, light.ModuleIndex);
            Assert.AreEqual(ScalarSource.None, light.ScalarSource);
            CollectionAssert.Contains(after.Notes, "MODULE[1] ModuleColorChanger: moduleID 'shieldChar' not planned, MODULE[2] " +
                                                   "ModuleColorChanger writes the same shader property (_EmissiveColor): it keeps it");
            Assert.IsFalse(after.Notes.Any(n => n.Contains("follows the ablator")));
        }

        /// <summary>The comparison goes through <c>shader_property_map</c>: <c>_EmissiveColor</c> and <c>_EmissionColor</c> share a
        /// KSP2 slot under the built-in map, and a custom map that keeps them apart lets the char through.</summary>
        [Test] public void AblatorChar_SlotComparedThroughTheShaderPropertyMap()
        {
            var modules = Modules(StockShield.Replace("_BurnColor", "_EmissiveColor")).Concat(new[]
            {
                Color("_EmissionColor"),
            }).ToArray();
            Assert.AreEqual(1, PlanChar(true, modules).Entries.Count, "built-in map: same slot");
            var apart = new Dictionary<string, string[]> { ["_EmissiveColor"] = new[] { "_EmissiveColor" } };
            var p = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, true, null, true, true, false, true, apart);
            Assert.AreEqual(2, p.Entries.Count);
            Assert.AreEqual(ScalarSource.Ablator, p.Entries[1].ScalarSource);
        }

        /// <summary>A light on another property does not stop the char; one on the same property does, whatever its kind (a
        /// BDB RCS glow writes <c>_EmissiveColor</c> too), unless <c>skip_modules</c> drops that rival.</summary>
        [Test] public void AblatorChar_OtherPropertyOrSkippedRival_StillPlanned()
        {
            var other = PlanChar(true, Modules(StockShield).Concat(new[] { Color("_EmissiveColor") }).ToArray());
            Assert.AreEqual(1, other.Entries.Count(e => e.ScalarSource == ScalarSource.Ablator), "another property");

            var kv = Modules(StockShield.Replace("_BurnColor", "_EmissiveColor"));
            var glow = CfgText.Module("name = ModuleBdbRcsEmissive\nalphaCurve\n{\nkey = 0 0\nkey = 1 1\n}");
            var modules = kv.Concat(new[] { glow }).ToArray();
            var p = PlanChar(true, modules);
            Assert.AreEqual(EntryKind.RcsEmissive, p.Entries.Single().Kind, "the glow keeps the slot");

            var skipped = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, true,
                new HashSet<string> { "ModuleBdbRcsEmissive" }, true, true, false, true);
            Assert.AreEqual(ScalarSource.Ablator, skipped.Entries.Single().ScalarSource, "the rival is skipped: the char is planned");
        }

        [Test] public void AblatorChar_AThrottleSetterNamingIt_DoesNotLinkIt()
        {
            var setter = CfgText.Module("name = FXModuleThrottleEffects\nfxModuleNames = shieldChar");
            var p = PlanChar(true, Modules(StockShield).Concat(new[] { setter }).ToArray());
            var e = p.Entries.Single();
            Assert.AreEqual(ScalarSource.Ablator, e.ScalarSource);
            Assert.IsNull(e.EngineName);
            Assert.AreEqual(0.5f, e.ScalarResponse);
            CollectionAssert.Contains(p.Notes, "MODULE[2] FXModuleThrottleEffects names 'shieldChar', no colour changer of ours");
        }

        [Test] public void AblatorChar_SkipModules_DropsIt()
        {
            var p = OwnershipPlanner.Plan(Modules(StockShield), false, new HashSet<string>(), false, true,
                new HashSet<string> { "ModuleColorChanger" }, true, true, false, true);
            CollectionAssert.IsEmpty(p.Entries);
        }
    }
}
