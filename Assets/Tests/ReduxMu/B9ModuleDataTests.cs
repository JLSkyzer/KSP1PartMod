using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.B9;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Tests.Animation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using SpaceWarp2.API.Parts;
using VSwift.Modules.Data;
using VSwift.Modules.InformationLoaders;
using VSwift.Modules.Transformers;
using VSwift.Modules.Variants;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>B9 module data and <c>moduleActive</c> (plan 2026-10-28, Task 3), against the referenced Ksp2Redux.dll
    /// (26w42c): the part's modules are imported by Redux's real registry as its translator does, then Redux's real
    /// <c>TryBuild</c> runs with our postfix armed; the edited KSP1 module is re-imported by Redux's importers and every
    /// native field that differs becomes a <c>ModuleDefinitionTransformer</c>.</summary>
    public class B9ModuleDataTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            B9Overrides.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            B9Overrides.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(B9Overrides.HarmonyId).UnpatchAll(B9Overrides.HarmonyId);
            B9Overrides.Reset();
        }

        private bool Arm(string json = "{}") =>
            B9Overrides.TryApply(new Harmony(B9Overrides.HarmonyId), AnimationSettings.Parse(json, out _));

        /// <summary>IOProvider's settings come from a RuntimeInitializeOnLoadMethod that EditMode does not run.</summary>
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

        private sealed class Imported
        {
            public bool Result;
            public Data_PartSwitch Data;
            public Ksp1PartImportContext Context;
            public string Name;
        }

        /// <summary>Redux's module loop of <c>TryTranslatePart</c>, as far as the variant builder: switched engines as one
        /// multi-mode module, only the first gimbal, every other module through the registry; then <c>TryBuild</c>.</summary>
        private static Imported Import(Ksp1ConfigFile file, Ksp1ConfigNode part)
        {
            var name = part.GetValue("name");
            var context = new Ksp1PartImportContext(name, new Ksp1ImportReport()) { PartNode = part };
            context.PartData = new PartData
            {
                partName = name,
                resourceContainers = Ksp1ResourceMapper.MapPartResources(part.GetNodes("RESOURCE"), null, context.Report, name, null),
            };
            var registry = new Ksp1PartModuleImporterRegistry();
            var orderedMethod = AccessTools.Method(AccessTools.TypeByName(B9ModuleDiff.TranslatorTypeName), B9ModuleDiff.OrderedEnginesName);
            var ordered = (List<Ksp1ConfigNode>)orderedMethod.Invoke(null, new object[] { part });
            var switched = false;
            if (ordered.Count > 0 && registry.TryImportEngineModes(ordered, context, out var modes))
            {
                switched = true;
                context.PartData.serializedPartModules.Add(modes);
            }
            var firstGimbal = part.GetNodes("MODULE").FirstOrDefault(m => Ksp1PartModuleImporterRegistry.IsGimbalModuleName(m.GetValue("name")));
            foreach (var m in part.GetNodes("MODULE"))
            {
                var n = m.GetValue("name");
                if (Ksp1PartModuleImporterRegistry.IsGimbalModuleName(n) && !ReferenceEquals(m, firstGimbal)) continue;
                if (switched && Ksp1PartModuleImporterRegistry.IsEngineModuleName(n)) continue;
                if (registry.TryImport(m, context, out var module)) context.PartData.serializedPartModules.Add(module);
            }
            var result = Ksp1B9PartSwitchVariantBuilder.TryBuild(file, part, 1f, context, new string[0], out var data, out _);
            return new Imported { Result = result, Data = data, Context = context, Name = name };
        }

        private static Imported Import(string partText)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(partText);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/test.cfg", "Test", node);
            return Import(file, node.GetNodes("PART").First());
        }

        private static List<ModuleDefinitionTransformer> Mdt(Variant v) => v.Transformers.OfType<ModuleDefinitionTransformer>().ToList();

        private static Variant VariantOf(Data_PartSwitch data, string id) =>
            data.VariantSets.SelectMany(s => s.Variants).Single(v => v.VariantId == id);

        private string Log => string.Join(Environment.NewLine, _lines);

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_ModuleDataMembers()
        {
            CollectionAssert.IsEmpty(B9ModuleDiff.Resolve().Missing);
        }

        /// <summary><c>ModuleDefinitionTransformer</c> finds its types by short name in <c>PartModuleTypes</c>.</summary>
        [Test] public void PartModuleTypes_EngineKeysResolve()
        {
            if (PartModuleTypes.ComponentModules.Count == 0)
                Assert.Ignore("PartModuleTypes is empty outside the game: the glue logs 'b9: module types: Module_Engine resolves …' in game instead");
            Assert.IsTrue(PartModuleTypes.ComponentModules.TryGetValue("Module_Engine", out var engine));
            Assert.AreEqual(typeof(Module_Engine), engine.behaviour);
            Assert.IsTrue(PartModuleTypes.DataModules.TryGetValue("Data_Engine", out var data));
            Assert.AreEqual(typeof(Data_Engine), data);
        }

        [Test] public void Arm_ModuleDataOnByDefault_OffBySetting()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, B9Lines.Armed(true, true, true, true, true, true));
            Disarm();
            _lines.Clear();
            Assert.IsTrue(Arm("{\"b9_module_data\": false}"));
            CollectionAssert.Contains(_lines, B9Lines.Armed(true, true, false, false, true, true), "module off needs module data");
        }

        // ------------------------------------------------------------------ engine round

        private const string EnginePart = @"
PART
{
	name = testEngine
	mass = 1
	MODULE
	{
		name = ModuleEnginesFX
		maxThrust = 100
		minThrust = 0
		heatProduction = 100
		runningEffectName = running
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 0.9
		}
		PROPELLANT
		{
			name = Oxidizer
			ratio = 1.1
		}
		atmosphereCurve
		{
			key = 0 300
			key = 1 250
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = engineSwitch
		SUBTYPE
		{
			name = Base
			transform = baseMesh
		}
		SUBTYPE
		{
			name = Uprated
			transform = upMesh
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
				}
				DATA
				{
					maxThrust = 120
				}
			}
		}
	}
}
";

        [Test] public void TryBuild_EngineMaxThrustData_OneEngineModesTransformer_LoadedInFlight() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(EnginePart);
            TestContext.WriteLine(Log);
            Assert.IsTrue(imported.Result);
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "Base")), "the base subtype changes nothing");
            var mdts = Mdt(VariantOf(imported.Data, "Uprated"));
            Assert.AreEqual(1, mdts.Count, Log);
            var t = mdts[0];
            Assert.AreEqual("Module_Engine", t.BehaviourType);
            Assert.AreEqual("Data_Engine", t.DataType);
            Assert.AreEqual("engineModes", t.Key);
            var modes = (Data_Engine.EngineMode[])IOProvider.FromJson(t.Value.ToString(Formatting.None), typeof(Data_Engine.EngineMode[]));
            Assert.AreEqual(1, modes.Length);
            Assert.AreEqual(120f, modes[0].maxThrust, "the DATA value, through Redux's importer");
            var baseEngine = (Data_Engine)imported.Context.PartData.serializedPartModules
                .Single(m => m.BehaviourType == typeof(Module_Engine)).ModuleData.Single().DataObject;
            Assert.AreEqual(100f, baseEngine.engineModes[0].maxThrust, "the part definition stays Redux's");
            Assert.AreEqual(baseEngine.engineModes[0].propellant.mixtureName, modes[0].propellant.mixtureName, "propellants kept");
            Assert.AreEqual(baseEngine.engineModes[0].atmosphereCurve.Evaluate(0f), modes[0].atmosphereCurve.Evaluate(0f), "curve kept");

            Assert.DoesNotThrow(() => B9Overrides.Validate(t));
            CollectionAssert.Contains(_lines, B9Lines.ModuleEdit("testEngine", imported.Data.VariantSets[0].VariantSetId, "Uprated",
                "ModuleEnginesFX", "Module_Engine", new[] { "engineModes" }));
            Assert.IsTrue(_lines.Any(l => l.StartsWith(B9Lines.Prefix + "module types: Module_Engine resolves ")), Log);

            // Flight: V-SwiFT's own loader writes the saved value into the part definition's Data_Engine.
            var saved = t.SaveInformation();
            Assert.AreEqual(typeof(ModuleDefinitionLoader), saved.savedType);
            new ModuleDefinitionLoader().LoadInformationInto(imported.Context.PartData, saved.savedValue);
            var engine = (Data_Engine)imported.Context.PartData.serializedPartModules
                .Single(m => m.BehaviourType == typeof(Module_Engine)).ModuleData.Single().DataObject;
            Assert.AreEqual(120f, engine.engineModes[0].maxThrust);

            B9Overrides.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, B9Lines.ModuleDataSummary(1, 1, new[] { ("Module_Engine.engineModes", 1) }, 0, 0, null, 0, null));
        });

        // ------------------------------------------------------------------ off forms

        private const string GimbalPart = @"
PART
{
	name = testGimbal
	mass = 1
	MODULE
	{
		name = ModuleGimbal
		gimbalTransformName = gimbal
		gimbalRange = 5
		gimbalRangeXP = 4
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = gimbalSwitch
		SUBTYPE
		{
			name = Free
		}
		SUBTYPE
		{
			name = Locked
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleGimbal
				}
				moduleActive = false
			}
		}
	}
}
";

        [Test] public void TryBuild_GimbalOff_RangeFieldsZero() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(GimbalPart);
            TestContext.WriteLine(Log);
            Assert.IsTrue(imported.Result, "a new set for the non-visual switch");
            var mdts = Mdt(VariantOf(imported.Data, "Locked"));
            var keys = mdts.Select(m => m.Key).ToList();
            CollectionAssert.Contains(keys, "gimbalRange");
            CollectionAssert.Contains(keys, "gimbalRangeXP", "4 in the cfg, 0 in the off form");
            CollectionAssert.IsSubsetOf(keys, new[] { "gimbalRange", "gimbalRangeXP", "gimbalRangeXN", "gimbalRangeYP", "gimbalRangeYN" });
            foreach (var m in mdts)
            {
                Assert.AreEqual("Module_Gimbal", m.BehaviourType);
                Assert.AreEqual("Data_Gimbal", m.DataType);
                Assert.AreEqual(0f, m.Value.Value<float>(), m.Key);
            }
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "Free")));
            CollectionAssert.Contains(_lines, B9Lines.ModuleOff("testGimbal", "ModuleGimbal", "Locked", "Module_Gimbal", keys));
        });

        private const string DecouplerPart = @"
PART
{
	name = testDecoupler
	mass = 0.1
	MODULE
	{
		name = ModuleDecouple
		ejectionForce = 100
		explosiveNodeID = top
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = decSwitch
		SUBTYPE
		{
			name = Live
		}
		SUBTYPE
		{
			name = Inert
			addedMass = 0.01
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleDecouple
				}
				moduleActive = false
			}
		}
	}
}
";

        [Test] public void TryBuild_DecouplerOff_NotStageable() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(DecouplerPart);
            TestContext.WriteLine(Log);
            var mdts = Mdt(VariantOf(imported.Data, "Inert"));
            Assert.AreEqual(1, mdts.Count, Log);
            Assert.AreEqual("Module_Decouple", mdts[0].BehaviourType);
            Assert.AreEqual("Data_Decouple", mdts[0].DataType);
            Assert.AreEqual("IsStageable", mdts[0].Key);
            Assert.AreEqual(false, mdts[0].Value.Value<bool>());
            Assert.AreEqual(1, VariantOf(imported.Data, "Inert").Transformers.OfType<MassModifier>().Count(), "mass with the off form");
            B9Overrides.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, B9Lines.ModuleDataSummary(1, 1, new[] { ("Module_Decouple.IsStageable", 1) }, 1, 0, null, 0, null));
        });

        // ------------------------------------------------------------------ notes only

        private const string NotesPart = @"
PART
{
	name = testNotes
	mass = 1
	MODULE
	{
		name = ModuleEnginesFX
		maxThrust = 100
		powerEffectName = power
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 1
		}
	}
	MODULE
	{
		name = ModuleSimpleAdjustableFairing
		segmentLength = 1
		CONE
		{
			shape = 1
		}
	}
	MODULE
	{
		name = ModuleDeployableEngine
		EngineAnimationName = deploy
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fxSwitch
		SUBTYPE
		{
			name = A
		}
		SUBTYPE
		{
			name = B
			addedMass = 0.1
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
				}
				DATA
				{
					powerEffectName = otherPower
				}
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleSimpleAdjustableFairing
				}
				DATA
				{
					segmentLength = 2
					CONE
					{
						shape = 2
					}
				}
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleDeployableEngine
				}
				moduleActive = false
			}
		}
	}
}
";

        [Test] public void TryBuild_FxNamesFairingAndNoOffForm_NotesOnly() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(NotesPart);
            TestContext.WriteLine(Log);
            Assert.IsTrue(imported.Data.VariantSets.SelectMany(s => s.Variants).All(v => Mdt(v).Count == 0), Log);
            CollectionAssert.Contains(_lines, B9Lines.NoNativeEffect("testNotes", "ModuleEnginesFX", new[] { "powerEffectName" }));
            CollectionAssert.Contains(_lines, B9Lines.NoNativeEffect("testNotes", "ModuleSimpleAdjustableFairing", new[] { "segmentLength", "CONE" }));
            CollectionAssert.Contains(_lines, B9Lines.ModuleOffNone("testNotes", "ModuleDeployableEngine", "B"));
            B9Overrides.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, B9Lines.ModuleDataSummary(0, 0, null, 0, 1, new[] { "ModuleDeployableEngine" },
                2, new[] { "ModuleEnginesFX", "ModuleSimpleAdjustableFairing" }));
        });

        // ------------------------------------------------------------------ one field, one writer

        private const string TwoEnginesPart = @"
PART
{
	name = testTwoEngines
	mass = 1
	MODULE
	{
		name = ModuleEngines
		engineID = A
		maxThrust = 100
	}
	MODULE
	{
		name = ModuleEngines
		engineID = B
		maxThrust = 50
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = sw
		SUBTYPE
		{
			name = One
		}
		SUBTYPE
		{
			name = Two
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEngines
					engineID = A
				}
				DATA
				{
					maxThrust = 200
				}
			}
		}
	}
}
";

        [Test] public void TryBuild_BehaviourTwiceOnThePart_EditsSkipped() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(TwoEnginesPart);
            TestContext.WriteLine(Log);
            Assert.AreEqual(2, imported.Context.PartData.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_Engine)),
                "no engine switch module: two Module_Engine");
            CollectionAssert.Contains(_lines, B9Lines.BehaviourRepeated("testTwoEngines", "Module_Engine", 2));
            Assert.IsTrue(imported.Data == null || imported.Data.VariantSets.SelectMany(s => s.Variants).All(v => Mdt(v).Count == 0));
            Assert.IsFalse(_lines.Any(l => l.Contains("no native effect")), "skipped, not 'no native effect'");
        });

        private const string ConflictPart = @"
PART
{
	name = testConflict
	mass = 1
	MODULE
	{
		name = ModuleEngines
		maxThrust = 100
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = first
		SUBTYPE
		{
			name = F1
		}
		SUBTYPE
		{
			name = F2
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEngines
				}
				DATA
				{
					maxThrust = 110
				}
			}
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = second
		SUBTYPE
		{
			name = S1
		}
		SUBTYPE
		{
			name = S2
			addedMass = 0.5
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEngines
				}
				DATA
				{
					maxThrust = 130
				}
			}
		}
	}
}
";

        [Test] public void TryBuild_TwoSetsEditTheSameField_FirstKept() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(ConflictPart);
            TestContext.WriteLine(Log);
            Assert.AreEqual(1, Mdt(VariantOf(imported.Data, "F2")).Count);
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "S2")));
            Assert.AreEqual(1, VariantOf(imported.Data, "S2").Transformers.OfType<MassModifier>().Count(), "the second set keeps its mass");
            CollectionAssert.Contains(_lines, B9Lines.FieldConflict("testConflict", "Module_Engine", "engineModes", "first"));
        });

        // ------------------------------------------------------------------ several modes, off-form rules

        private const string TwoModePart = @"
PART
{
	name = testTwoModes
	mass = 1
	MODULE
	{
		name = ModuleEnginesFX
		engineID = SL
		maxThrust = 100
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 1
		}
	}
	MODULE
	{
		name = ModuleEnginesFX
		engineID = Vac
		maxThrust = 110
		PROPELLANT
		{
			name = LiquidFuel
			ratio = 1
		}
	}
	MODULE
	{
		name = MultiModeEngine
		primaryEngineID = SL
		secondaryEngineID = Vac
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = thrust
		SUBTYPE
		{
			name = Low
		}
		SUBTYPE
		{
			name = High
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
					engineID = SL
				}
				DATA
				{
					maxThrust = 200
				}
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
					engineID = Vac
				}
				DATA
				{
					maxThrust = 220
				}
			}
		}
	}
}
";

        private static Data_Engine.EngineMode[] Modes(ModuleDefinitionTransformer t) =>
            (Data_Engine.EngineMode[])IOProvider.FromJson(t.Value.ToString(Formatting.None), typeof(Data_Engine.EngineMode[]));

        /// <summary>One subtype edits both modes of a multi-mode engine: one <c>engineModes</c> holds both edits.</summary>
        [Test] public void TryBuild_TwoModesEditedByOneSubtype_OneTransformerWithBoth() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(TwoModePart);
            TestContext.WriteLine(Log);
            Assert.AreEqual(1, imported.Context.PartData.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_Engine)));
            var mdts = Mdt(VariantOf(imported.Data, "High"));
            Assert.AreEqual(1, mdts.Count, Log);
            var modes = Modes(mdts[0]);
            Assert.AreEqual(200f, modes.Single(m => m.engineID == "SL").maxThrust);
            Assert.AreEqual(220f, modes.Single(m => m.engineID == "Vac").maxThrust);
            var tally = B9Overrides.ModuleTallyOf("testTwoModes");
            Assert.AreEqual(1, tally.Transformers);
            Assert.AreEqual(1, tally.ByField["Module_Engine.engineModes"]);
            Assert.IsTrue(tally.Modules.All(o => o.Fields.SequenceEqual(new[] { "engineModes" })), "both modes are edited");
        });

        /// <summary>Every DATA <c>maxThrust</c> of a real multi-mode part reaches its mode: the mode is found by the
        /// IDENTIFIER's values (engineID, thrustVectorTransformName).</summary>
        private void AssertEveryModeEdit(string relativePath, string partName)
        {
            var file = RealFile(relativePath);
            Assert.IsTrue(Arm());
            var part = file.Node.GetNodes("PART").Single(p => p.GetValue("name") == partName);
            var imported = Import(file, part);
            TestContext.WriteLine(Log);
            var checkedEdits = 0;
            foreach (var st in part.GetNodes("MODULE").Where(m => m.GetValue("name") == "ModuleB9PartSwitch").SelectMany(m => m.GetNodes("SUBTYPE")))
            {
                var edits = st.GetNodes("MODULE").Where(b => b.GetNode("IDENTIFIER")?.GetValue("name") == "ModuleEnginesFX" &&
                                                             b.GetNode("DATA")?.GetValue("maxThrust") != null).ToList();
                if (edits.Count == 0) continue;
                var mdts = Mdt(VariantOf(imported.Data, st.GetValue("name")));
                Assert.AreEqual(1, mdts.Count(t => t.Key == "engineModes"), st.GetValue("name") + ": one engineModes per variant");
                var modes = Modes(mdts.Single(t => t.Key == "engineModes"));
                foreach (var b in edits)
                {
                    var id = b.GetNode("IDENTIFIER");
                    var mode = modes.Single(m =>
                        (id.GetValue("engineID") == null || m.engineID == id.GetValue("engineID")) &&
                        (id.GetValue("thrustVectorTransformName") == null || m.thrustVectorTransformName == id.GetValue("thrustVectorTransformName")));
                    Assert.AreEqual(float.Parse(b.GetNode("DATA").GetValue("maxThrust"), System.Globalization.CultureInfo.InvariantCulture),
                        mode.maxThrust, 1e-3f, $"{st.GetValue("name")}: mode {mode.engineID}");
                    checkedEdits++;
                }
            }
            TestContext.WriteLine($"{checkedEdits} mode edit(s) checked");
            Assert.Greater(checkedEdits, 1);
            Assert.IsFalse(_lines.Any(l => l.Contains("failed") || l.Contains("dropped")), Log);
        }

        /// <summary>A real cfg, with its <c>MODULE:NEEDS[!DefaultActionGroups]</c> nodes named <c>MODULE</c>: in game
        /// ModuleManager resolves them (that mod is not installed) before Redux reads the part, and the UA120's
        /// <c>MultiModeEngine</c> is such a node.</summary>
        private static Ksp1ConfigFile RealFile(string relativePath)
        {
            var path = Path.Combine(InstalledKsp1.Root, relativePath);
            if (!File.Exists(path)) Assert.Ignore("not installed: " + path);
            var text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"MODULE:NEEDS\[!DefaultActionGroups\]", "MODULE");
            return new Ksp1ConfigFile(new Ksp1ModRoot(InstalledKsp1.Root, InstalledKsp1.Root), path, "Bluedog_DB", Ksp1ConfigNode.Parse(text));
        }

        [Test] public void TryBuild_RealBdbXlr129_BothModesEdited() => WithGameSettings(() =>
            AssertEveryModeEdit("Bluedog_DB/Parts/Engines/bluedog_XLR129.cfg", "bluedog_XLR129"));

        [Test] public void TryBuild_RealBdbUa120_SrbMainModeChanges() => WithGameSettings(() =>
        {
            AssertEveryModeEdit("Bluedog_DB/Parts/Titan/bluedog_UA120.cfg", "bluedog_UA120");
            Disarm();
            _lines.Clear();
            B9Overrides.Sink = _lines.Add;
            Assert.IsTrue(Arm());
            var file = RealFile("Bluedog_DB/Parts/Titan/bluedog_UA120.cfg");
            var imported = Import(file, file.Node.GetNodes("PART").Single());
            Assert.AreEqual(1, imported.Context.PartData.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_Engine)), "one multi-mode engine");
            var main = Modes(Mdt(VariantOf(imported.Data, "UA1202")).Single(t => t.Key == "engineModes")).Single(m => m.engineID == "S1SRB");
            Assert.AreEqual(486.3f, main.maxThrust, 1e-3f, "the Titan SRB main mode, 1311.4 kN in the cfg");
        });

        private const string DecouplerEnginePart = @"
PART
{
	name = testSepMotor
	mass = 0.1
	MODULE
	{
		name = ModuleDecouple
		ejectionForce = 100
	}
	MODULE
	{
		name = ModuleEngines
		maxThrust = 20
		PROPELLANT
		{
			name = SolidFuel
			ratio = 1
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = sw
		SUBTYPE
		{
			name = Live
		}
		SUBTYPE
		{
			name = Inert
			addedMass = 0.01
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleDecouple
				}
				moduleActive = false
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEngines
				}
				moduleActive = false
			}
		}
	}
}
";

        /// <summary>A decoupler off on a part that also stages through an engine: the decoupler off form is skipped
        /// (staging is per part), and the engine off form is dropped (maxThrust 0 gives NaN Δv).</summary>
        [Test] public void TryBuild_DecouplerOffWithAnEngine_Skipped_EngineOffDropped() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(DecouplerEnginePart);
            TestContext.WriteLine(Log);
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "Inert")), Log);
            CollectionAssert.Contains(_lines, B9Lines.ModuleOffStaging("testSepMotor", "ModuleDecouple", "Inert", "ModuleEngines"));
            CollectionAssert.Contains(_lines, B9Lines.ModuleOffEngine("testSepMotor", "ModuleEngines", "Inert"));
            Assert.IsFalse(_lines.Any(l => l.Contains("no native effect")), "a skipped off form is not 'no native effect'");
            B9Overrides.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, B9Lines.ModuleDataSummary(0, 0, null, 0, 2, new[] { "ModuleDecouple", "ModuleEngines" }, 0, null));
        });

        private const string RcsZeroPart = @"
PART
{
	name = testRcsZero
	mass = 0.1
	MODULE
	{
		name = ModuleRCSFX
		thrusterPower = 0
		resourceName = MonoPropellant
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = sw
		SUBTYPE
		{
			name = On
		}
		SUBTYPE
		{
			name = Off
			addedMass = 0.01
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleRCSFX
				}
				moduleActive = false
			}
		}
	}
}
";

        [Test] public void TryBuild_OffFormChangingNothing_LoggedAndCounted() => WithGameSettings(() =>
        {
            Assert.IsTrue(Arm());
            var imported = Import(RcsZeroPart);
            TestContext.WriteLine(Log);
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "Off")));
            CollectionAssert.Contains(_lines, B9Lines.ModuleOffNoChange("testRcsZero", "ModuleRCSFX", "Off"));
            B9Overrides.LogImportSummaryOnce();
            Assert.IsTrue(_lines.Any(l => l.Contains("off forms 0 (1 changing nothing)")), Log);
        });

        // ------------------------------------------------------------------ real files

        /// <summary>The installed BDB Titan Transtage: its 34D subtype raises <c>maxThrust</c> to 36.5 and changes the
        /// <c>atmosphereCurve</c> of its single <c>ModuleEnginesFX</c>.</summary>
        [Test] public void TryBuild_RealBdbTranstage_34dEngine() => WithGameSettings(() =>
        {
            var partPath = Path.Combine(InstalledKsp1.Root, "Bluedog_DB/Parts/Titan/bluedog_Titan_Transtage.cfg");
            if (!File.Exists(partPath)) Assert.Ignore("BDB not installed at " + InstalledKsp1.Root);
            Assert.IsTrue(Arm());
            var file = new Ksp1ConfigFile(new Ksp1ModRoot(InstalledKsp1.Root, InstalledKsp1.Root), partPath, "Bluedog_DB", Ksp1ConfigNode.ParseFile(partPath));
            var imported = Import(file, file.Node.GetNodes("PART").First());
            TestContext.WriteLine(Log);
            CollectionAssert.IsEmpty(Mdt(VariantOf(imported.Data, "Transtage")));
            var mdt = Mdt(VariantOf(imported.Data, "Transtage-34D")).Single();
            Assert.AreEqual("engineModes", mdt.Key);
            var mode = ((Data_Engine.EngineMode[])IOProvider.FromJson(mdt.Value.ToString(Formatting.None), typeof(Data_Engine.EngineMode[])))[0];
            Assert.AreEqual(36.5f, mode.maxThrust, 1e-4f);
            Assert.AreEqual(309.1f, mode.atmosphereCurve.Evaluate(0f), 1e-3f, "vacuum Isp of the 34D");
            Assert.IsFalse(_lines.Any(l => l.Contains("failed") || l.Contains("dropped")), Log);
        });

        private static readonly HashSet<string> NativeTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModuleEngines", "ModuleEnginesFX", "ModuleGimbal", "ModuleRCS", "ModuleRCSFX", "ModuleDeployableSolarPanel", "ModuleCommand",
        };

        private static readonly HashSet<string> NotesOnlyTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ModuleSimpleAdjustableFairing", "ModuleConformalDecal",
        };

        /// <summary>Every installed PART (raw cfg) whose B9 subtypes carry a module DATA block: imported as Redux does,
        /// then built with the postfix. An engine, gimbal, RCS, solar or command target gives transformers or a
        /// "no native effect" note. A repeated behaviour is not an accepted outcome: it is listed apart, and each one is
        /// checked against the real import (the part holds that behaviour twice or more) and its line. Fairings and decals
        /// give notes only; every DATA block's FX names, re-imported alone, change no native field; nothing fails.</summary>
        [Test] public void Census_InstalledDataBlocks_TransformerOrNote() => WithGameSettings(() =>
        {
            var cfgs = InstalledKsp1.AllCfg();
            Assert.IsTrue(Arm());
            var root = InstalledKsp1.Root;
            var modRoot = new Ksp1ModRoot(root, root);
            int parts = 0, reduxThrows = 0, effect = 0, noEffect = 0, unmatched = 0, notesOnlyChecked = 0, fxOnlyChecked = 0;
            var repeated = new List<string>();
            var registry = new Ksp1PartModuleImporterRegistry();
            var byField = new Dictionary<string, int>();
            var fxKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "powerEffectName", "runningEffectName", "spoolEffectName", "engageEffectName", "disengageEffectName", "flameoutEffectName" };
            foreach (var path in cfgs)
            {
                Ksp1ConfigNode node;
                // MODULE:NEEDS[!DefaultActionGroups] resolved as ModuleManager does in game (BDB's MultiModeEngine nodes).
                try { node = Ksp1ConfigNode.Parse(System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"MODULE:NEEDS\[!DefaultActionGroups\]", "MODULE")); }
                catch { continue; }
                var mod = path.Substring(root.Length).TrimStart('/', '\\').Split('/', '\\')[0];
                foreach (var part in node.GetNodes("PART"))
                {
                    var blocks = part.GetNodes("MODULE")
                        .Where(m => string.Equals(m.GetValue("name"), "ModuleB9PartSwitch", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(m => m.GetNodes("SUBTYPE")).SelectMany(st => st.GetNodes("MODULE"))
                        .Where(b => b.GetNodes("DATA").Any() && b.GetNode("IDENTIFIER") != null).ToList();
                    if (blocks.Count == 0) continue;
                    parts++;
                    var name = part.GetValue("name");
                    Imported imported;
                    try { imported = Import(new Ksp1ConfigFile(modRoot, path, mod, node), part); }
                    catch (Exception e)
                    {
                        reduxThrows++;
                        TestContext.WriteLine($"Redux threw on {name}: {e.GetType().Name}: {e.Message}");
                        continue;
                    }
                    var tally = B9Overrides.ModuleTallyOf(name);
                    foreach (var block in blocks)
                    {
                        var target = block.GetNode("IDENTIFIER").GetValue("name")?.Trim() ?? "?";
                        var outcomes = tally?.Modules.Where(o => string.Equals(o.Ksp1Module, target, StringComparison.OrdinalIgnoreCase)).ToList()
                                       ?? new List<B9Overrides.ModuleOutcome>();
                        if (outcomes.Count == 0)
                        {
                            unmatched++;
                            Assert.IsTrue(_lines.Any(l => l.StartsWith(B9Lines.Prefix + name + ": ") && l.Contains("IDENTIFIER " + target)),
                                $"{name}: {target} has no outcome and no IDENTIFIER note");
                            continue;
                        }
                        Assert.IsFalse(outcomes.Any(o => o.Failed), $"{name}: {target} re-import failed");
                        var keys = block.GetNodes("DATA").SelectMany(d => d.Values.Select(v => v.Name).Concat(d.Nodes.Select(n => n.Name))).ToList();
                        if (NotesOnlyTargets.Contains(target))
                        {
                            notesOnlyChecked++;
                            Assert.IsTrue(outcomes.All(o => o.Fields.Count == 0), $"{name}: {target} gave native fields");
                        }
                        var fx = block.GetNodes("DATA").SelectMany(d => d.Values).Where(v => fxKeys.Contains(v.Name)).ToList();
                        if (fx.Count > 0)
                        {
                            // The block's FX names alone, written over its target and re-imported: no native field.
                            var adapters = Ksp1NodeAdapter.Modules(part);
                            var match = B9GatePlanner.Match(adapters, new Ksp1NodeAdapter(block.GetNode("IDENTIFIER")));
                            if (match.Count == 1)
                            {
                                var fxData = new B9Node("DATA");
                                fxData.Values.AddRange(fx.Select(v => new KeyValuePair<string, string>(v.Name, v.RawValue)));
                                var r = B9ModuleDiff.Diff(registry, part, imported.Context, match[0], B9ModuleEdit.EditedCopy(adapters[match[0]], fxData));
                                CollectionAssert.IsEmpty(r.Transformers.Select(t => t.Key), $"{name}: FX names of {target} gave native fields");
                                fxOnlyChecked++;
                            }
                        }
                        if (!NativeTargets.Contains(target)) continue;
                        foreach (var o in outcomes)
                        {
                            if (o.Fields.Count > 0) effect++;
                            else if (o.NoEffect) noEffect++;
                            else if (o.Repeated)
                            {
                                // Not an accepted outcome: a verified skip, listed apart.
                                var count = imported.Context.PartData.serializedPartModules.Count(m => m.BehaviourType?.Name == o.Behaviour);
                                Assert.GreaterOrEqual(count, 2, $"{name}: {target} reported repeated, but {o.Behaviour} appears {count} time(s)");
                                Assert.IsTrue(_lines.Contains(B9Lines.BehaviourRepeated(name, o.Behaviour, count)), $"{name}: no repeated line");
                                repeated.Add($"{name} ({target} -> {o.Behaviour} x{count})");
                            }
                            else Assert.Fail($"{name}: {target} gave neither a transformer nor a note");
                        }
                    }
                    if (tally != null)
                        foreach (var kv in tally.ByField) byField[kv.Key] = byField.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
                }
            }
            B9Overrides.LogImportSummaryOnce();
            TestContext.WriteLine($"parts with module DATA {parts}, Redux threw {reduxThrows}; engine/gimbal/RCS/solar/command targets: " +
                                  $"native fields {effect}, no native effect {noEffect}; skipped, behaviour repeated {repeated.Count}; no IDENTIFIER match {unmatched}; " +
                                  $"fairing/decal checked {notesOnlyChecked}, FX-only checked {fxOnlyChecked}");
            TestContext.WriteLine("repeated (skipped, not applied): " + string.Join("; ", repeated.Distinct()));
            TestContext.WriteLine("by field: " + string.Join(", ", byField.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
            TestContext.WriteLine(_lines.LastOrDefault(l => l.Contains("module data:")));
            TestContext.WriteLine(string.Join(Environment.NewLine, _lines.Where(l => l.Contains("dropped") || l.Contains("failed") ||
                                                                                     l.Contains("appears") || l.Contains("edited by two"))));
            if (parts == 0) Assert.Ignore("no installed part with B9 module DATA");
            Assert.AreEqual(0, reduxThrows, "Redux's own import threw");
            Assert.IsFalse(_lines.Any(l => l.Contains(": failed, Redux's variants kept")), Log);
            Assert.Greater(effect, 0);
            Assert.Greater(fxOnlyChecked, 0, "at least one DATA block with FX names");
            Assert.Greater(byField.TryGetValue("Module_Engine.engineModes", out var em) ? em : 0, 0);
        });
    }
}
