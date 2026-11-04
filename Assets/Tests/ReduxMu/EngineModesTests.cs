using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Engines;
using KSP1PartMod.Tests.Animation;
using KSP1PartMod.Tests.Animation.Native.Engines;
using Newtonsoft.Json;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using UnityEngine.TestTools;
using VSwift.Modules.Transformers;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.0, Task 2, against the referenced Ksp2Redux.dll (26w42c): the engine order postfix on Redux's
    /// <c>GetOrderedSwitchedEngineModules</c> makes Redux's own <c>TryImportEngineModes</c> build one multi-mode
    /// <c>Module_Engine</c>; the <c>BuildConfiguredPrefab</c> postfix writes the SEP thrust and mass and unique ids.</summary>
    public class EngineModesTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            EngineModes.Sink = _lines.Add;
            B9Overrides.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            EngineModes.Sink = null;
            B9Overrides.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(EngineModes.HarmonyId).UnpatchAll(EngineModes.HarmonyId);
            new Harmony(SepEngines.HarmonyId).UnpatchAll(SepEngines.HarmonyId);
            new Harmony(B9Overrides.HarmonyId).UnpatchAll(B9Overrides.HarmonyId);
            new Harmony(MmPatches.HarmonyId).UnpatchAll(MmPatches.HarmonyId);
            EngineModes.Reset();
            SepEngines.Reset();
            B9Overrides.Reset();
            MmPatches.Reset();
            MmPatches.Sink = null;
        }

        private static bool Arm(string json = "{}")
        {
            var on = EngineModes.TryApply(new Harmony(EngineModes.HarmonyId), AnimationSettings.Parse(json, out _));
            if (on) Assert.IsTrue(SepEngines.TryApply(new Harmony(SepEngines.HarmonyId)));
            return on;
        }

        private static void WithGameSettings(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                test();
            }
            finally { JsonConvert.DefaultSettings = previous; }
        }

        private string Log => string.Join(Environment.NewLine, _lines);

        // ------------------------------------------------------------------ fixtures

        private static string Lr91 => "PART\n{\nname = bluedog_LR91_3\nmass = 0.5\n" + EngineModeOrderTests.Engine("basicEngine", "thrustTransform", 88.7) +
                                      "MODULE\n{\nname = ModuleGimbal\ngimbalTransformName = thrustTransform\ngimbalRange = 3\n}\n" +
                                      EngineModeOrderTests.Engine("vernier", "vernierTransform", 10) + "}\n";

        private static string Aj260 => "PART\n{\nname = bluedog_Saturn_AJ260_Inline\nmass = 4\n" + EngineModeOrderTests.Engine(null, "thrustTransform", 3870) +
                                       EngineModeOrderTests.Engine(null, "vernierTransform", 20) + "}\n";

        private static string Ua120 => "PART\n{\nname = bluedog_UA120\nmass = 4\n" + EngineModeOrderTests.Engine("S1SRB_Abort", "abortTransform", 1311.4) +
                                       EngineModeOrderTests.Engine("S1SRB", "thrustTransform", 1311.4) +
                                       "MODULE\n{\nname = MultiModeEngine\nprimaryEngineID = S1SRB\nsecondaryEngineID = S1SRB_Abort\n}\n}\n";

        private static Ksp1ConfigNode PartNode(string text) => Ksp1ConfigNode.Parse(text).GetNodes("PART").First();

        private static List<Ksp1ConfigNode> Ordered(Ksp1ConfigNode part) =>
            (List<Ksp1ConfigNode>)EngineModes.FindOrdered().Invoke(null, new object[] { part });

        private static string[] Ids(List<Ksp1ConfigNode> nodes) => nodes.Select(n => n.GetValue("engineID")).ToArray();

        private sealed class Translated
        {
            public PartCore Core;
            public Ksp1ConfigNode Part;
            public Data_Engine[] Engines;
            public int ModuleEngines;
        }

        /// <summary>Redux's whole translation of one part (the model is missing: Redux's fallback geometry).</summary>
        private static Translated Translate(string text)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(text);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/engine.cfg", "Test", node);
            var part = node.GetNodes("PART").First();
            Ksp1ImportedPartRegistry.BeginImport("engine-modes-test");
            Assert.IsTrue(new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out var core, out var rawJson));
            var back = IOProvider.FromJson<PartCore>(rawJson);
            return new Translated
            {
                Core = core,
                Part = part,
                Engines = back.modules.OfType<Data_Engine>().ToArray(),
                ModuleEngines = back.data.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_Engine)),
            };
        }

        private static void WithRegistry(Action test)
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Ksp1ImportedPartRegistry.Clear();
            try { test(); }
            finally
            {
                foreach (var e in Ksp1ImportedPartRegistry.ImportedParts.ToList())
                {
                    if (e.Prefab != null) UnityEngine.Object.DestroyImmediate(e.Prefab);
                    e.Prefab = null;
                }
                Ksp1ImportedPartRegistry.Clear();
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Members_Resolve()
        {
            Assert.IsNotNull(EngineModes.FindOrdered());
            Assert.IsNotNull(EngineCurveFix.FindBuildConfiguredPrefab());
        }

        [Test] public void BothKeysOff_ArmNothing()
        {
            Assert.IsFalse(Arm("{\"sep_procedural_engines\": false, \"ksp1_engine_modes_fallback\": false}"));
            Assert.IsFalse(SepEngines.TryApply(new Harmony(SepEngines.HarmonyId)));
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(EngineModes.HarmonyId) == true ||
                                                                   Harmony.GetPatchInfo(m)?.Owners.Contains(SepEngines.HarmonyId) == true));
            CollectionAssert.AreEqual(new[] { EngineSizeLines.EnginesOff }, _lines);
            CollectionAssert.IsEmpty(Ordered(PartNode(Lr91)), "0.15.1: no switch, no list");
        }

        [Test] public void Armed_Line()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: modes armed (sep_procedural_engines on, ksp1_engine_modes_fallback on)");
        }

        // ------------------------------------------------------------------ order postfix

        [Test] public void Order_ReturnsReduxsOwnNodes()
        {
            Assert.IsTrue(Arm());
            var part = PartNode(SepEngineSetsTests.Sep25());
            var list = Ordered(part);
            CollectionAssert.AreEqual(new[] { "AllEngines", "MiddleInner", "CenterThree" }, Ids(list));
            var modules = part.GetNodes("MODULE").ToList();
            Assert.IsTrue(list.All(n => modules.Any(m => ReferenceEquals(m, n))), "the node objects of the part");
        }

        [Test] public void Order_MultiModeEnginePart_Untouched()
        {
            var part = PartNode(Ua120);
            var before = Ids(Ordered(part));
            CollectionAssert.AreEqual(new[] { "S1SRB", "S1SRB_Abort" }, before, "Redux's own switch order");
            Assert.IsTrue(Arm());
            CollectionAssert.AreEqual(before, Ids(Ordered(part)));
            Assert.IsFalse(_lines.Any(l => l.Contains("bluedog_UA120")), Log);
        }

        [Test] public void Order_SepOff_SepPartKeptAs0151_FallbackStillOn()
        {
            Assert.IsTrue(Arm("{\"sep_procedural_engines\": false}"));
            CollectionAssert.IsEmpty(Ordered(PartNode(SepEngineSetsTests.Sep25())));
            CollectionAssert.AreEqual(new[] { "basicEngine", "vernier" }, Ids(Ordered(PartNode(Lr91))));
        }

        [Test] public void Order_AThrowLeavesReduxsList_AndStopsThePatch()
        {
            Assert.IsTrue(Arm());
            EngineModes.BeforeMapForTests = _ => throw new InvalidOperationException("test");
            CollectionAssert.IsEmpty(Ordered(PartNode(Lr91)));
            Assert.IsTrue(_lines.Any(l => l.StartsWith("[KSP1PartMod] engines: mode order stopped for this session: ")), Log);
            EngineModes.BeforeMapForTests = null;
            CollectionAssert.IsEmpty(Ordered(PartNode(Lr91)), "stopped for the session");
            Assert.IsFalse(EngineModes.IsArmed);
        }

        // ------------------------------------------------------------------ through Redux's translator

        [Test] public void Sep25_OneEngine_ThreeModes_RefusedWithoutTheModel() => WithGameSettings(() => WithRegistry(() =>
        {
            Assert.IsTrue(Arm());
            var t = Translate(SepEngineSetsTests.Sep25());
            Assert.AreEqual(1, t.ModuleEngines, Log);
            var e = t.Engines.Single();
            CollectionAssert.AreEqual(new[] { "AllEngines", "MiddleInner", "CenterThree" }, e.engineModes.Select(m => m.engineID).ToArray());
            Assert.AreEqual("AllEngines", e.activeEngineMode.GetValue());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: SEP_25_BOOSTER_CLUSTER: ModuleSEPEngineSwitch order AllEngines, MiddleInner, CenterThree");
            CollectionAssert.Contains(_lines, EngineSizeLines.SepRefused("SEP_25_BOOSTER_CLUSTER", "ENGINESET 'RBoosts': no transform RB_<n> in the model"));
            Assert.AreEqual(1000f, e.engineModes[0].maxThrust, "Redux's values kept");
            EngineModes.AfterImportComplete();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: summary: 1 SEP part(s), 0 fallback part(s), 1 refused (SEP_25_BOOSTER_CLUSTER)");
        }));

        [Test] public void Sep25_WithTheEnginesInTheModel_ThrustAndMass_TwiceTheSame() => WithGameSettings(() => WithRegistry(() =>
        {
            Assert.IsTrue(Arm());
            var t = Translate(SepEngineSetsTests.Sep25());
            var model = new GameObject("model");
            try
            {
                foreach (var n in SepEngineSetsTests.Names()) new GameObject(n).transform.SetParent(model.transform);
                for (var run = 0; run < 2; run++) // the prefab rebuild runs the postfix again
                {
                    SepEngines.Apply(t.Part, t.Core, model);
                    var e = SepEngines.Engines(t.Core, 3).Single();
                    CollectionAssert.AreEqual(new[] { 19140f, 7540f, 1740f }, e.engineModes.Select(m => m.maxThrust).ToArray());
                    Assert.AreEqual(44.5, t.Core.data.mass, 1e-9, "cfg mass 0 + engines, run " + run);
                }
                Assert.AreEqual(1, _lines.Count(l => l.StartsWith("[KSP1PartMod] engines: SEP_25_BOOSTER_CLUSTER: SEP type ")), Log);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }));

        [Test] public void Lr91_OneEngine_MainThenVernier() => WithGameSettings(() => WithRegistry(() =>
        {
            Assert.IsTrue(Arm());
            var t = Translate(Lr91);
            Assert.AreEqual(1, t.ModuleEngines, Log);
            var e = t.Engines.Single();
            CollectionAssert.AreEqual(new[] { "basicEngine", "vernier" }, e.engineModes.Select(m => m.engineID).ToArray());
            Assert.AreEqual("basicEngine", e.activeEngineMode.GetValue(), "mode 0 is the main engine");
            Assert.AreEqual(88.7f, e.engineModes[0].maxThrust, 1e-4f);
            Assert.AreEqual(0.5, t.Core.data.mass, 1e-9, "a fallback part keeps its mass");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: bluedog_LR91_3: 2 engine modules, no switch: modes basicEngine, vernier (mode 0 88.7 kN, KSP1 sum 98.7 kN)");
        }));

        [Test] public void Lr91_FallbackOff_TwoEngines_As0151() => WithGameSettings(() => WithRegistry(() =>
        {
            Assert.IsTrue(Arm("{\"ksp1_engine_modes_fallback\": false}"));
            Assert.AreEqual(2, Translate(Lr91).ModuleEngines);
        }));

        [Test] public void Aj260_NoIds_EngineAndEngine2() => WithGameSettings(() => WithRegistry(() =>
        {
            Assert.IsTrue(Arm());
            var t = Translate(Aj260);
            var e = t.Engines.Single();
            CollectionAssert.AreEqual(new[] { "Engine", "Engine_2" }, e.engineModes.Select(m => m.engineID).ToArray());
            Assert.AreEqual("Engine", e.activeEngineMode.GetValue());
            Assert.AreEqual(e.engineModes[0].EngineDisplayName, e.engineModes[1].EngineDisplayName, "the display name is kept");
            CollectionAssert.Contains(_lines, EngineSizeLines.DuplicateId("bluedog_Saturn_AJ260_Inline", "Engine", "Engine_2"));
        }));

        // ------------------------------------------------------------------ B9

        private const string Aj260B9 = @"
PART
{
	name = testAj260B9
	mass = 4
	MODULE
	{
		name = ModuleEnginesFX
		thrustVectorTransformName = thrustTransform
		maxThrust = 3870
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 0.9
		}
		atmosphereCurve
		{
			key = 0 300
		}
	}
	MODULE
	{
		name = ModuleEnginesFX
		thrustVectorTransformName = vernierTransform
		maxThrust = 20
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 0.9
		}
		atmosphereCurve
		{
			key = 0 290
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = sw
		SUBTYPE
		{
			name = Base
		}
		SUBTYPE
		{
			name = Long
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
					thrustVectorTransformName = thrustTransform
				}
				DATA
				{
					maxThrust = 7740
				}
			}
		}
	}
}
";

        /// <summary>Redux's module loop of <c>TryTranslatePart</c> as far as the variant builder (as <c>B9ModuleDataTests</c>).</summary>
        private static (bool result, VSwift.Modules.Data.Data_PartSwitch data, Ksp1PartImportContext context) ImportB9(string text)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(text);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/test.cfg", "Test", node);
            var part = node.GetNodes("PART").First();
            var name = part.GetValue("name");
            var context = new Ksp1PartImportContext(name, new Ksp1ImportReport()) { PartNode = part };
            context.PartData = new PartData { partName = name, resourceContainers = new List<KSP.Sim.ResourceSystem.ContainedResourceDefinition>() };
            var registry = new Ksp1PartModuleImporterRegistry();
            var ordered = Ordered(part);
            var switched = false;
            if (ordered.Count > 0 && registry.TryImportEngineModes(ordered, context, out var modes))
            {
                switched = true;
                context.PartData.serializedPartModules.Add(modes);
            }
            foreach (var m in part.GetNodes("MODULE"))
            {
                if (switched && Ksp1PartModuleImporterRegistry.IsEngineModuleName(m.GetValue("name"))) continue;
                if (registry.TryImport(m, context, out var module)) context.PartData.serializedPartModules.Add(module);
            }
            var result = Ksp1B9PartSwitchVariantBuilder.TryBuild(file, part, 1f, context, new string[0], out var data, out _);
            return (result, data, context);
        }

        [Test] public void B9_EngineEdit_NowCompared_WithUniqueIds() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            Assert.IsTrue(B9Overrides.TryApply(new Harmony(B9Overrides.HarmonyId), AnimationSettings.Parse("{}", out _)));
            var (_, data, context) = ImportB9(Aj260B9);
            Assert.AreEqual(1, context.PartData.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_Engine)), Log);
            var mdt = data.VariantSets.SelectMany(s => s.Variants).Single(v => v.VariantId == "Long")
                .Transformers.OfType<ModuleDefinitionTransformer>().Single(t => t.Key == "engineModes");
            var modes = (Data_Engine.EngineMode[])IOProvider.FromJson(mdt.Value.ToString(Formatting.None), typeof(Data_Engine.EngineMode[]));
            CollectionAssert.AreEqual(new[] { "Engine", "Engine_2" }, modes.Select(m => m.engineID).ToArray(), "ids as on the part");
            CollectionAssert.AreEqual(new[] { 7740f, 20f }, modes.Select(m => m.maxThrust).ToArray());
            StringAssert.EndsWith("; 1 engine edit(s) now on a multi-mode engine, 0 dropped on SEP clusters", B9Overrides.ModuleDataSummary());
        });

        [Test] public void B9_EngineEdit_OnAnSepCluster_Dropped() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            Assert.IsTrue(B9Overrides.TryApply(new Harmony(B9Overrides.HarmonyId), AnimationSettings.Parse("{}", out _)));
            var b9 = "MODULE\n{\nname = ModuleB9PartSwitch\nmoduleID = sw\nSUBTYPE\n{\nname = Base\n}\nSUBTYPE\n{\nname = Hot\nMODULE\n{\n" +
                     "IDENTIFIER\n{\nname = ModuleEnginesFX\nengineID = AllEngines\n}\nDATA\n{\nmaxThrust = 5\n}\n}\n}\n}\n";
            var text = SepEngineSetsTests.Sep25();
            text = text.Substring(0, text.LastIndexOf('}')) + b9 + "}\n";
            var (_, data, _) = ImportB9(text);
            Assert.IsTrue(data == null || data.VariantSets.SelectMany(s => s.Variants)
                .All(v => !v.Transformers.OfType<ModuleDefinitionTransformer>().Any(t => t.DataType == "Data_Engine")), Log);
            CollectionAssert.Contains(_lines, EngineSizeLines.B9EngineEditDropped("SEP_25_BOOSTER_CLUSTER", "Hot"));
            StringAssert.EndsWith("; 1 engine edit(s) now on a multi-mode engine, 1 dropped on SEP clusters", B9Overrides.ModuleDataSummary());
        });

        // ------------------------------------------------------------------ installed cfgs

        /// <summary>The installed <c>mods/KSP1</c> set through Redux's finalizer with our ModuleManager patches at their
        /// defaults (as in game): the parts each rule takes (census of plan (d)).</summary>
        [Test] public void Census_Installed_TwoSep_TwelveFallback()
        {
            var root = InstalledKsp1.Root;
            InstalledKsp1.AllCfg(); // ignores the test when the install is absent
            MmPatches.Sink = _ => { };
            MmPatches.ImportRootsOverride = () => new[] { root };
            MmPatches.TryApply(new Harmony(MmPatches.HarmonyId), new MmPatches.Switches());
            var report = new Ksp1ImportReport();
            var files = Ksp1PartModDiscovery.LoadConfigFiles(report, true, new[] { new Ksp1ModRoot(root, root) });
            var db = Ksp1ConfigFinalizer.Finalize(files, report);
            var sep = new SortedSet<string>(StringComparer.Ordinal);
            var fallback = new SortedSet<string>(StringComparer.Ordinal);
            var redux = new SortedSet<string>(StringComparer.Ordinal);
            var orderedMethod = EngineModes.FindOrdered();
            foreach (var part in db.Files.SelectMany(f => f.Node.Nodes).Where(n => n.Name == "PART"))
            {
                var name = part.GetValue("name") ?? "";
                var adapter = new Ksp1NodeAdapter(part);
                if (EngineModeOrder.Sep(adapter).Length > 0) sep.Add(name);
                if (EngineModeOrder.Fallback(adapter).Length > 0) fallback.Add(name);
                if (((List<Ksp1ConfigNode>)orderedMethod.Invoke(null, new object[] { part })).Count > 0) redux.Add(name);
            }
            TestContext.WriteLine($"engine census (installed): SEP {sep.Count}: {string.Join(", ", sep)}");
            TestContext.WriteLine($"fallback {fallback.Count}: {string.Join(", ", fallback)}");
            TestContext.WriteLine($"Redux's own switch {redux.Count}: {string.Join(", ", redux)}");
            CollectionAssert.IsEmpty(sep.Intersect(redux).Concat(fallback.Intersect(redux)).ToList(), "a part Redux switches is never ours");
            Assert.AreEqual(2, sep.Count, "SEP clusters");
            Assert.AreEqual(12, fallback.Count, "parts with several engines and no switch");
        }
    }
}
