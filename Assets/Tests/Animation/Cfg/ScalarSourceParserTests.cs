using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary><c>FXModuleThrottleEffects</c> → our Color entries (spec 2a.7), and <c>skip_modules</c> in the planner.</summary>
    public class ScalarSourceParserTests
    {
        private static IKsp1Node Generic(string clip) => CfgText.Module("name = ModuleAnimateGeneric\nanimationName = " + clip);
        private static IKsp1Node Heat(string clip) => CfgText.Module("name = ModuleAnimateHeat\nThermalAnim = " + clip);

        private static IKsp1Node Color(string property = "_EmissiveColor", string moduleId = null, string id = null)
        {
            id = id ?? moduleId;
            return CfgText.Module($"name = ModuleColorChanger\nshaderProperty = {property}" + (id == null ? "" : "\nmoduleID = " + id));
        }

        private static IKsp1Node Fx(string names, float? response = null, bool? dependOnEngineState = null,
            bool? dependOnOutput = null, bool? dependOnThrottle = null, string engineName = null,
            string moduleName = "FXModuleThrottleEffects") =>
            CfgText.Module($"name = {moduleName}\nfxModuleNames = {names}"
                           + (response == null ? "" : "\nresponseSpeed = " + response.Value.ToString(CultureInfo.InvariantCulture))
                           + (dependOnEngineState == null ? "" : "\ndependOnEngineState = " + dependOnEngineState)
                           + (dependOnOutput == null ? "" : "\ndependOnOutput = " + dependOnOutput)
                           + (dependOnThrottle == null ? "" : "\ndependOnThrottle = " + dependOnThrottle)
                           + (engineName == null ? "" : "\nengineName = " + engineName));

        private static PartAnimationPlan Plan(params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false);

        private static PartAnimationPlan Plan(ISet<string> skip, params IKsp1Node[] modules) =>
            OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, false, skip);

        // --- skip_modules

        [Test] public void Plan_SkipModules_RemovesOnlyThatModuleAndKeepsOtherKeys()
        {
            var modules = new[] { Generic("a"), Generic("b"), Heat("h") };
            var keysWithoutHeat = Plan(modules).Entries.Where(e => e.Kind != EntryKind.Heat).Select(e => e.Key).ToList();
            var p = Plan(new HashSet<string> { "ModuleAnimateHeat" }, modules);
            CollectionAssert.AreEqual(keysWithoutHeat, p.Entries.Select(e => e.Key));
            StringAssert.Contains("skipped by skip_modules", p.Notes.Single());
        }

        [Test] public void Plan_SkipModules_NoteText() =>
            Assert.AreEqual("MODULE[1] ModuleAnimateHeat: skipped by skip_modules",
                Plan(new HashSet<string> { "ModuleAnimateHeat" }, Generic("a"), Heat("h")).Notes.Single());

        [Test] public void Plan_SkipModules_KeysAssignedBeforeTheFilter()
        {
            // An antenna and a radiator on the same clip are deploy:b:0 and deploy:b:1. Skipping the antenna must
            // not renumber the radiator: its saved values are under deploy:b:1.
            var modules = new[]
            {
                CfgText.Module("name = ModuleDeployableAntenna\nanimationName = b"),
                CfgText.Module("name = ModuleDeployableRadiator\nanimationName = b"),
            };
            CollectionAssert.AreEqual(new[] { "deploy:b:0", "deploy:b:1" }, Plan(modules).Entries.Select(e => e.Key));
            var p = Plan(new HashSet<string> { "ModuleDeployableAntenna" }, modules);
            Assert.AreEqual(("deploy:b:1", 1), (p.Entries.Single().Key, p.Entries.Single().ModuleIndex));
        }

        [Test] public void Plan_SkipModules_OwnFirstDeployableSkipped_StaysRedux()
        {
            var modules = new[] { Generic("a"), Generic("b"), CfgText.Module("name = ModuleDeployableAntenna\nanimationName = a") };
            var own = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), true, false, new HashSet<string>());
            Assert.IsTrue(own.RemoveReduxDeployable);
            var p = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), true, false, new HashSet<string> { "ModuleAnimateGeneric" });
            Assert.IsFalse(p.RemoveReduxDeployable);
            CollectionAssert.Contains(p.ReduxClips, "a");
            // The antenna on Redux's clip goes (no clip driven by both); nothing left changes key.
            CollectionAssert.IsEmpty(p.Entries);
            StringAssert.Contains("MODULE[2] ModuleDeployableAntenna: clip 'a' is played by Redux", string.Join("\n", p.Notes));
        }

        [Test] public void Plan_SkipModules_OwnFirstDeployableOtherSkipped_StillRemoves()
        {
            var p = OwnershipPlanner.Plan(new[] { Generic("a"), Heat("h") }, false, new HashSet<string>(), true, false,
                new HashSet<string> { "ModuleAnimateHeat" });
            Assert.IsTrue(p.RemoveReduxDeployable);
            Assert.AreEqual("toggle:a:0", p.Entries.Single().Key);
        }

        [Test] public void Plan_SkipModules_AeroGateOfSkippedController_Dropped()
        {
            var modules = new[]
            {
                CfgText.Module("name = ModuleDeployableAntenna\nanimationName = d"),
                CfgText.Module("name = ModuleDeployableAero\nDeployModuleIndex = 0\nDeployModulePos = 1"),
                Heat("h"),
            };
            Assert.AreEqual(3, Plan(modules).Entries.Count);
            var p = Plan(new HashSet<string> { "ModuleDeployableAntenna" }, modules);
            Assert.AreEqual(EntryKind.Heat, p.Entries.Single().Kind);
            StringAssert.Contains("MODULE[1] ModuleDeployableAero: controller MODULE[0] skipped by skip_modules", string.Join("\n", p.Notes));
        }

        [Test] public void Plan_SkipModules_OrdinalNames() =>
            Assert.AreEqual(1, Plan(new HashSet<string>(StringComparer.Ordinal) { "moduleanimateheat" }, Generic("a"), Heat("h")).Entries.Count);

        [Test] public void Plan_SkipModules_SkippedFxLinksNothing()
        {
            var p = Plan(new HashSet<string> { "FXModuleThrottleEffects" }, Color(id: "a"), Fx("a"));
            Assert.IsFalse(p.Entries.Single().ScalarLinked);
            StringAssert.Contains("skipped by skip_modules", p.Notes.Single());
        }

        [Test] public void Plan_OldOverloads_SkipNothing() =>
            Assert.AreEqual(2, OwnershipPlanner.Plan(new[] { Generic("a"), Heat("h"), Heat("i") }, false, new HashSet<string>(), false).Entries.Count);

        // --- FX setter

        [Test] public void Parse_FxSetter_LinksNamedColorEntry()
        {
            var e = Plan(Color("_EmissiveColor", moduleId: "heatColor"), Fx("heatColor", response: 1f, dependOnEngineState: true)).Entries.Single();
            Assert.AreEqual((true, 1f, true), (e.ScalarLinked, e.ScalarResponse, e.DependOnEngineState));
        }

        [Test] public void Parse_FxSetter_ReadsEveryField()
        {
            var e = Plan(Color(id: "c"), Fx("c", dependOnOutput: true, dependOnThrottle: true, engineName: "Main")).Entries.Single();
            Assert.AreEqual((false, true, true, "Main"), (e.DependOnEngineState, e.DependOnOutput, e.DependOnThrottle, e.EngineName));
        }

        [Test] public void Parse_FxSetter_CommaListLinksBoth() =>
            Assert.AreEqual(2, Plan(Color(id: "a"), Color(id: "b"), Fx("a, b")).Entries.Count(e => e.ScalarLinked));

        [Test] public void Parse_FxSetter_UnknownName_Noted() => StringAssert.Contains("names 'zzz'", Plan(Fx("zzz")).Notes.Single());

        [Test] public void Parse_FxSetter_UnknownName_NoteText() =>
            Assert.AreEqual("MODULE[0] FXModuleThrottleEffects names 'zzz', no colour changer of ours", Plan(Fx("zzz")).Notes.Single());

        [Test] public void Parse_FxSetter_KeepsModuleId() =>
            CollectionAssert.AreEquivalent(new[] { "a", "b" },
                Plan(Color(id: "a"), Color(id: "b"), Fx("a, b")).Entries.Select(e => e.ModuleId).ToArray());

        [Test] public void Parse_FxSetter_DefaultResponse_0_5() => Assert.AreEqual(0.5f, Plan(Color(id: "a"), Fx("a")).Entries.Single().ScalarResponse);

        [Test] public void Parse_FxSetter_OtherThrottleEffectsName_Links() =>
            Assert.IsTrue(Plan(Color(id: "a"), Fx("a", moduleName: "ModuleRealThrottleEffects")).Entries.Single().ScalarLinked);

        [Test] public void Parse_FxSetter_HeatEffects_NotAThrottleSource() =>
            Assert.IsFalse(Plan(Color(id: "a"), Fx("a", moduleName: "ModuleHeatEffects")).Entries.Single().ScalarLinked);

        [Test] public void Parse_FxSetter_NamesNonColorModule_Noted()
        {
            var p = Plan(Generic("x"), CfgText.Module("name = ModuleAnimateGeneric\nanimationName = z\nmoduleID = g"), Fx("g"));
            CollectionAssert.IsEmpty(p.Entries.Where(e => e.ScalarLinked));
            StringAssert.Contains("names 'g'", p.Notes.Single());
        }

        [Test] public void Parse_FxSetter_UnlinkedColorKeepsDefaults()
        {
            var e = Plan(Color(id: "a")).Entries.Single();
            Assert.AreEqual((false, 0.5f, false, false, false),
                (e.ScalarLinked, e.ScalarResponse, e.DependOnEngineState, e.DependOnOutput, e.DependOnThrottle));
        }

        [Test] public void Parse_FxSetter_KeysUnchanged() =>
            Assert.AreEqual(Plan(Color(id: "a")).Entries.Single().Key, Plan(Color(id: "a"), Fx("a")).Entries.Single().Key);

        [Test] public void Parse_FxSetter_TwoSettersOneId_NotedLaterWins()
        {
            var p = Plan(Color(id: "a"), Fx("a", response: 0.2f), Fx("a", response: 0.9f));
            Assert.AreEqual("MODULE[2] FXModuleThrottleEffects names 'a', already driven by MODULE[1]: the later setter wins",
                p.Notes.Single());
            Assert.AreEqual(0.9f, p.Entries.Single().ScalarResponse);
        }

        [Test] public void Parse_FxSetter_OneSetterNamingAnIdTwice_NoNote() =>
            CollectionAssert.IsEmpty(Plan(Color(id: "a"), Fx("a, a")).Notes);

        [Test] public void Parse_FxSetter_IsNotAnEntry() => CollectionAssert.IsEmpty(Plan(Fx("a")).Entries);

        // --- real BDB parts

        private static IEnumerable<IReadOnlyList<IKsp1Node>> BdbParts() =>
            InstalledKsp1.AllCfg().Where(p => InstalledKsp1.TopFolder(p).Equals("Bluedog_DB", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => CfgText.Parse(File.ReadAllText(p)).GetNodes("PART"))
                .Select(part => (IReadOnlyList<IKsp1Node>)part.GetNodes("MODULE").ToList());

        private sealed class FxName
        {
            public string Name;
            public bool ResolvesToColorEntry;
        }

        /// <summary>Every name of every <c>FXModuleThrottleEffects</c> of the part, and whether the plan linked a Color
        /// entry of that moduleID.</summary>
        private static IEnumerable<FxName> FxNames(IReadOnlyList<IKsp1Node> modules)
        {
            var plan = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), false, true);
            foreach (var fx in modules.Where(m => "FXModuleThrottleEffects".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase)))
            foreach (var name in (fx.GetValue("fxModuleNames") ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
                yield return new FxName
                {
                    Name = name,
                    ResolvesToColorEntry = plan.Entries.Any(e => e.Kind == EntryKind.Color && e.ScalarLinked
                                                                 && name == modules[e.ModuleIndex].GetValue("moduleID")?.Trim()),
                };
        }

        [Test] public void FxLinks_Bdb_253LinksAllResolve()
        {
            var l = BdbParts().SelectMany(FxNames).ToList();
            TestContext.WriteLine($"{l.Count} link(s): " + string.Join(", ", l.GroupBy(x => x.Name).Select(g => $"{g.Key} {g.Count()}")));
            Assert.AreEqual(253, l.Count);
            CollectionAssert.IsEmpty(l.Where(x => !x.ResolvesToColorEntry).Select(x => x.Name));
        }
    }
}
