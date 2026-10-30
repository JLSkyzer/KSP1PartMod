using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c): the native import hook's targets resolve by name, and Redux's
    /// real <c>Ksp1PartModuleImporterRegistry</c>, built with our postfix armed, imports an in-memory
    /// <c>ModuleToggleCrossfeed</c> as <c>Module_ToggleCrossfeed</c>.</summary>
    public class NativeModulesTests
    {
        /// <summary>The science converters armed after the radiators (0.12.0).</summary>
        private const string ScienceArmed = ScienceImportTests.ArmedPairs;

        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            NativeModules.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(NativeModules.HarmonyId).UnpatchAll(NativeModules.HarmonyId);
            NativeModules.Reset();
            HeatshieldImporter.ReferenceSource = null;
        }

        private bool Arm(string json = "{}") =>
            NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), AnimationSettings.Parse(json, out _));

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        private static Ksp1ConfigNode Part(string modules, string partValues = "", string name = "testPart") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\n" + partValues + modules + "}\n").GetNodes("PART").First();

        private static string Toggle(string body) => "MODULE\n{\nname = ModuleToggleCrossfeed\n" + body + "\n}\n";

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part, bool fuelCrossFeed = true) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport())
            {
                PartNode = part,
                PartData = new PartData { fuelCrossFeed = fuelCrossFeed },
            };

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_FindsEveryHookTarget()
        {
            var t = NativeModules.Resolve();
            Assert.AreEqual("", string.Join(", ", t.Missing));
            Assert.AreEqual(typeof(Ksp1PartModuleImporterRegistry), t.Constructor.DeclaringType);
            Assert.AreEqual(0, t.Constructor.GetParameters().Length);
            Assert.IsTrue(t.Constructor.IsPublic);
            Assert.IsFalse(t.Importers.IsPublic, "private in 26w42c");
            Assert.AreEqual(typeof(Dictionary<string, IKsp1PartModuleImporter>), t.Importers.FieldType);
            Assert.IsTrue(t.CreateSerialized.IsStatic);
            Assert.IsFalse(t.CreateSerialized.IsPublic);
            CollectionAssert.AreEqual(new[] { typeof(Type), typeof(ModuleData), typeof(Ksp1PartImportContext) },
                t.CreateSerialized.GetParameters().Select(p => p.ParameterType));
            Assert.AreEqual(typeof(SerializedPartModule), t.CreateSerialized.ReturnType);
        }

        [Test] public void Resolve_ImportersAreCaseInsensitive_AsTheHookAssumes()
        {
            var importers = Importers(new Ksp1PartModuleImporterRegistry());
            Assert.IsTrue(importers.ContainsKey("moduledecouple"));
            Assert.IsFalse(importers.ContainsKey(CrossfeedMapping.Ksp1Module), "Redux 26w42c has no importer of its own: ours is added");
        }

        [Test] public void Resolve_CrossfeedTargets()
        {
            CollectionAssert.IsEmpty(CrossfeedImporter.MissingMembers());
            Assert.AreEqual(typeof(bool), AccessTools.Field(typeof(Data_ToggleCrossFeed), "ToggleInEditor").FieldType);
            Assert.AreEqual(typeof(bool), AccessTools.Field(typeof(Data_ToggleCrossFeed), "ToggleInFlight").FieldType);
            Assert.AreEqual(typeof(Module_ToggleCrossfeed), new Data_ToggleCrossFeed().ModuleType);
            Assert.AreEqual(typeof(bool), AccessTools.Field(typeof(PartData), "fuelCrossFeed").FieldType);
        }

        /// <summary>For Task 4: the <c>Data_Heatshield</c> definition fields of plan (f), by name.</summary>
        [Test] public void Resolve_HeatshieldFields()
        {
            var type = AccessTools.TypeByName("KSP.Modules.Data_Heatshield");
            Assert.IsNotNull(type);
            var expected = new Dictionary<string, Type>
            {
                ["requiredResources"] = typeof(List<PartModuleResourceSetting>),
                ["AblationTempThreshold"] = typeof(double),
                ["AblationTempHysteresis"] = typeof(double),
                ["AblationMaximumOverThreshold"] = typeof(double),
                ["PyrolysisLossFactor"] = typeof(double),
                ["AblationFluxReference"] = typeof(double),
                ["AblationFluxExponent"] = typeof(double),
                ["ShieldingScale"] = typeof(float),
                ["ShieldingDirection"] = typeof(UnityEngine.Vector3),
                ["DisabledWhenRetracted"] = typeof(bool),
                ["UseChar"] = typeof(bool),
                ["CharMaterialName"] = typeof(string),
                ["CharMin"] = typeof(float),
                ["CharMax"] = typeof(float),
            };
            foreach (var kv in expected)
            {
                var f = AccessTools.Field(type, kv.Key);
                Assert.IsNotNull(f, kv.Key);
                Assert.AreEqual(kv.Value, f.FieldType, kv.Key);
            }
            Assert.IsNotNull(AccessTools.TypeByName("KSP.Modules.Module_Heatshield"));
        }

        // ------------------------------------------------------------------ the hook

        [Test] public void Arm_LogsTheArmedLine_AndEveryNewRegistryGetsOurImporter()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, ModuleAblator -> Module_Heatshield, " +
                "ModuleResourceConverter -> Module_ResourceConverter, ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, ModuleResourceScanner -> Module_ResourceScanner, ModuleActiveRadiator -> Module_Cooler" + ScienceArmed + WheelImportTests.ArmedPairs);
            Assert.IsInstanceOf<CrossfeedImporter>(Importers(new Ksp1PartModuleImporterRegistry())[CrossfeedMapping.Ksp1Module]);
            Assert.IsInstanceOf<HeatshieldImporter>(Importers(new Ksp1PartModuleImporterRegistry())["moduleablator"]);
            Assert.IsTrue(NativeModules.IsConverterArmed(AblatorMapping.Ksp1Module));
            Assert.IsInstanceOf<CrossfeedImporter>(Importers(new Ksp1PartModuleImporterRegistry())["moduletogglecrossfeed"]);
        }

        [Test] public void SwitchOff_NothingAdded_ReduxAsIn070()
        {
            Assert.IsFalse(Arm("{\"ksp1_crossfeed_toggle\": false, \"ksp1_heatshields\": false, \"ksp1_converters\": false, " +
                               "\"ksp1_drills\": false, \"ksp1_survey_scanners\": false, \"ksp1_resource_scanners\": false, \"ksp1_radiators\": false, \"ksp1_science\": false, \"ksp1_wheels\": false, \"ksp1_procedural_fairings\": false, \"ksp1_saf_fairings\": false}"));
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: ModuleToggleCrossfeed -> Module_ToggleCrossfeed off (ksp1_crossfeed_toggle false)",
                "[KSP1PartMod] native: ModuleAblator -> Module_Heatshield off (ksp1_heatshields false)",
                "[KSP1PartMod] native: ModuleResourceConverter -> Module_ResourceConverter off (ksp1_converters false)",
                "[KSP1PartMod] native: ModuleResourceHarvester -> Module_Mine off (ksp1_drills false)",
                "[KSP1PartMod] native: ModuleOrbitalSurveyor -> Module_ResourceScanner off (ksp1_survey_scanners false)",
                "[KSP1PartMod] native: ModuleResourceScanner -> Module_ResourceScanner off (ksp1_resource_scanners false)",
                "[KSP1PartMod] native: ModuleActiveRadiator -> Module_Cooler off (ksp1_radiators false)",
                "[KSP1PartMod] native: ModuleScienceExperiment -> Module_ScienceExperiment off (ksp1_science false)",
                "[KSP1PartMod] native: DMModuleScienceAnimate -> Module_ScienceExperiment off (ksp1_science false)",
                "[KSP1PartMod] native: DMModuleScienceAnimateGeneric -> Module_ScienceExperiment off (ksp1_science false)",
                "[KSP1PartMod] native: ModuleScienceContainer -> report storage off (ksp1_science or science_report_storage false)",
                "[KSP1PartMod] native: ModuleWheelBase -> Module_WheelBase off (ksp1_wheels false)",
                "[KSP1PartMod] native: ModuleWheelSuspension -> Module_WheelSuspension off (ksp1_wheels false)",
                "[KSP1PartMod] native: ModuleWheelSteering -> Module_WheelSteering off (ksp1_wheels or wheel_steering false)",
                "[KSP1PartMod] native: ModuleWheelMotor -> Module_WheelMotor off (ksp1_wheels or wheel_motors false)",
                "[KSP1PartMod] native: ModuleWheelMotorSteering -> Module_WheelMotorSteering off (ksp1_wheels or wheel_motors false)",
                "[KSP1PartMod] native: ModuleWheelBrakes -> Module_WheelBrakes off (ksp1_wheels or wheel_brakes false)",
                "[KSP1PartMod] native: ModuleWheelDamage -> Module_WheelDamage off (ksp1_wheels or wheel_damage false)",
                "[KSP1PartMod] native: ModuleWheelLock -> Module_WheelLock off (ksp1_wheels or wheel_leg_locks false)",
                "[KSP1PartMod] native: ModuleWheelBogey -> Module_WheelBogey off (ksp1_wheels or wheel_bogeys false)",
                "[KSP1PartMod] native: ModuleProceduralFairing -> Module_Fairing off (ksp1_procedural_fairings false)",
                "[KSP1PartMod] native: ModuleSimpleAdjustableFairing -> Module_Fairing off (ksp1_saf_fairings false)",
                NativeLines.NoneArmed,
            }, _lines);
            Assert.IsFalse(NativeModules.IsConverterArmed(AblatorMapping.Ksp1Module));
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsFalse(Importers(registry).ContainsKey(CrossfeedMapping.Ksp1Module));
            var part = Part(Toggle("crossfeedStatus = false"));
            Assert.IsFalse(registry.TryImport(part.GetNodes("MODULE").First(), Context(part), out _), "unsupported, dropped by Redux");
        }

        [Test] public void ReduxImporterOfTheSameName_IsNeverReplaced()
        {
            Assert.IsTrue(Arm());
            var registry = new Ksp1PartModuleImporterRegistry();
            var importers = Importers(registry);
            var redux = importers["ModuleDecouple"];
            importers[CrossfeedMapping.Ksp1Module] = redux; // stands for a future Redux converter
            var postfix = AccessTools.Method(typeof(NativeModules), "AfterRegistryConstructor");
            postfix.Invoke(null, new object[] { registry });
            postfix.Invoke(null, new object[] { registry });
            Assert.AreSame(redux, importers[CrossfeedMapping.Ksp1Module]);
            Assert.AreEqual(1, _lines.Count(l => l == "[KSP1PartMod] native: ModuleToggleCrossfeed already has a Redux importer, ours not added"),
                "once per session");
        }

        // ------------------------------------------------------------------ Redux's real import

        [Test] public void TryImport_StockDecoupler_BuildsTheNativeModule_ThroughReduxCreateSerialized()
        {
            Assert.IsTrue(Arm());
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part("MODULE\n{\nname = ModuleDecouple\nejectionForce = 100\n}\n" +
                            Toggle("crossfeedStatus = false\ntoggleEditor = true\ntoggleFlight = false"), "fuelCrossFeed = True\n");
            var context = Context(part, true);
            var modules = part.GetNodes("MODULE").ToList();

            Assert.IsTrue(registry.TryImport(modules[0], context, out _), "Redux's decoupler");
            Assert.IsTrue(registry.TryImport(modules[1], context, out var module));

            Assert.AreEqual(typeof(Module_ToggleCrossfeed), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_ToggleCrossfeed", module.ComponentType.Name);
            Assert.AreEqual(1, module.ModuleData.Count);
            var data = context.ModuleData.OfType<Data_ToggleCrossFeed>().Single();
            Assert.IsTrue(data.ToggleInEditor);
            Assert.IsFalse(data.ToggleInFlight);
            Assert.IsFalse(context.PartData.fuelCrossFeed, "crossfeedStatus = false over the PART's True");
            CollectionAssert.AreEqual(new[] { CrossfeedMapping.NativeModule }, NativeModules.Record.ConvertedModules("testPart"));
            CollectionAssert.IsEmpty(_lines.Where(l => l.Contains("testPart")));

            NativeModules.LogImportSummaryOnce();
            NativeModules.LogImportSummaryOnce();
            Assert.AreEqual(1, _lines.Count(l => l.StartsWith("[KSP1PartMod] native: crossfeed:")));
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: crossfeed: 1 part(s) got Module_ToggleCrossfeed (0 start with crossfeed on), techRequired ignored on 0");
        }

        [Test] public void TryImport_SecondModule_ReturnsFalseWithANote_TechRequiredNoted()
        {
            Assert.IsTrue(Arm());
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(Toggle("crossfeedStatus = True\ntechRequired = fuelSystems") + Toggle("crossfeedStatus = false"));
            var context = Context(part, false);
            var modules = part.GetNodes("MODULE").ToList();

            Assert.IsTrue(registry.TryImport(modules[0], context, out _));
            Assert.IsTrue(context.PartData.fuelCrossFeed);
            Assert.IsFalse(registry.TryImport(modules[1], context, out _));
            Assert.IsTrue(context.PartData.fuelCrossFeed, "the second module changes nothing");
            Assert.AreEqual(1, context.ModuleData.OfType<Data_ToggleCrossFeed>().Count());

            // Rebuilding the same part logs and counts nothing twice.
            var again = Context(part, false);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(modules[0], again, out _));

            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: testPart: " + CrossfeedMapping.TechRequiredNote("fuelSystems"),
                "[KSP1PartMod] native: testPart: " + CrossfeedMapping.MissingToggleNote("toggleEditor"),
                "[KSP1PartMod] native: testPart: " + CrossfeedMapping.MissingToggleNote("toggleFlight"),
                "[KSP1PartMod] native: testPart: " + CrossfeedMapping.ExtraModuleNote,
            }, _lines.Where(l => l.Contains("testPart")));
            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: crossfeed: 1 part(s) got Module_ToggleCrossfeed (1 start with crossfeed on), techRequired ignored on 1");
        }

        [Test] public void TryImport_NoCrossfeedStatus_KeepsThePartValue()
        {
            Assert.IsTrue(Arm());
            var part = Part(Toggle("toggleEditor = false"));
            var context = Context(part, true);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), context, out _));
            Assert.IsTrue(context.PartData.fuelCrossFeed);
            Assert.IsFalse(context.ModuleData.OfType<Data_ToggleCrossFeed>().Single().ToggleInEditor);
        }

        [Test] public void TryImport_FailureAfterCreateSerialized_RollsBackTheDataAndTheCrossfeed()
        {
            Assert.IsTrue(Arm());
            NativeModules.Sink = l =>
            {
                if (l.Contains("techRequired 'boom'") && !l.Contains("not converted")) throw new InvalidOperationException("sink");
                _lines.Add(l);
            };
            var part = Part(Toggle("crossfeedStatus = True\ntechRequired = boom\ntoggleEditor = true\ntoggleFlight = true"));
            var context = Context(part, false);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), context, out _));
            CollectionAssert.IsEmpty(context.ModuleData, "no orphan Data_ToggleCrossFeed");
            Assert.IsFalse(context.PartData.fuelCrossFeed, "the PART value is back");
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart: ModuleToggleCrossfeed not converted")));
        }

        // ------------------------------------------------------------------ the heat shield

        private const string StockAblator = "MODULE\n{\nname = ModuleAblator\nablativeResource = Ablator\nlossExp = -7500\nlossConst = 0.1\n" +
                                            "pyrolysisLossFactor = 6000\nreentryConductivity = 0.01\nablationTempThresh = 500\nuseChar = True\n" +
                                            "charModuleName = shieldChar\n}\n";

        private static Ksp1PartImportContext ShieldContext(Ksp1ConfigNode part, double capacity)
        {
            var context = Context(part);
            if (capacity > 0)
                context.PartData.resourceContainers.Add(new ContainedResourceDefinition(capacity, "Ablator") { initialUnits = capacity });
            return context;
        }

        [Test] public void Resolve_HeatshieldTargets()
        {
            CollectionAssert.IsEmpty(HeatshieldImporter.MissingMembers());
            Assert.AreEqual(typeof(Module_Heatshield), new Data_Heatshield().ModuleType);
            Assert.AreEqual("Ablator", Ksp1ResourceMapper.MapResourceName("Ablator"));
        }

        [Test] public void TryImport_StockShield_BuildsModuleHeatshield_AgainstTheReference()
        {
            HeatshieldImporter.ReferenceSource = () => new[] { (8e-8, 0.8), (1e-7, 0.8), (1e-8, 0.8) };
            Assert.IsTrue(Arm("{\"ablator_rate_scale\": 2}"));
            var part = Part(StockAblator);
            var context = ShieldContext(part, 0.2);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), context, out var module));

            Assert.AreEqual(typeof(Module_Heatshield), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_Heatshield", module.ComponentType.Name);
            var data = context.ModuleData.OfType<Data_Heatshield>().Single();
            Assert.AreEqual("Ablator", data.requiredResources.Single().ResourceName);
            Assert.AreEqual((float)(8e-8 * 2), data.requiredResources[0].Rate, 1e-15f, "median Rate x scale, no capacity ratio");
            Assert.AreEqual(0.0, data.requiredResources[0].AcceptanceThreshold);
            Assert.AreEqual(500.0, data.AblationTempThreshold);
            Assert.AreEqual(1.0, data.PyrolysisLossFactor);
            Assert.AreEqual(1f, data.ShieldingScale);
            Assert.AreEqual(UnityEngine.Vector3.down, data.ShieldingDirection);
            Assert.IsFalse(data.DisabledWhenRetracted);
            Assert.IsFalse(data.UseChar);
            Assert.AreEqual(25.0, data.AblationTempHysteresis, "KSP2's default kept");
            CollectionAssert.AreEqual(new[] { AblatorMapping.NativeModule }, NativeModules.Record.ConvertedModules("testPart"));

            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: heat shield reference: 3 KSP2 shield(s), median Rate 8E-08, capacity 0.8",
                "[KSP1PartMod] native: heat shield reference: (Rate x PLF, capacity) of each: (1E-08, 0.8), (8E-08, 0.8), (1E-07, 0.8)",
                "[KSP1PartMod] native: testPart: ModuleAblator -> Module_Heatshield: Ablator capacity 0.2, Rate 1.6E-07 (reference 8E-08 for 0.8, " +
                "pyrolysisLossFactor 6000, ablator_rate_scale 2), ablation from 500 K, shielding along local -Y",
            }, _lines.Where(l => l.Contains("heat shield reference") || l.Contains("testPart")));

            // A second part reads the reference once; a rebuild logs nothing twice.
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), ShieldContext(part, 0.2), out _));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("median Rate")));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart")));

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: heat shields: 1 part(s) got Module_Heatshield (0 without the resource, skipped); " +
                                              "not reproduced: lossExp/lossConst shape, reentryConductivity");
        }

        [Test] public void TryImport_NoResource_NoModule_Noted_Counted_FallbackReference()
        {
            HeatshieldImporter.ReferenceSource = () => new (double, double)[0];
            Assert.IsTrue(Arm());
            var part = Part(StockAblator);
            var context = ShieldContext(part, 0);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPart: " + AblatorMapping.NoResourceNote("Ablator"));

            var other = Part(StockAblator.Replace("lossConst = 0.1", "lossConst = 0.1\noutputResource = Ore"), name: "other");
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(other.GetNodes("MODULE").First(), ShieldContext(other, 0.8), out _));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: heat shield reference: no KSP2 shield found, fallback Rate 8E-08");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: other: ModuleAblator: outputResource not reproduced");

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: heat shields: 1 part(s) got Module_Heatshield (1 without the resource, skipped); " +
                                              "not reproduced: lossExp/lossConst shape, reentryConductivity, outputResource on 1");
        }

        [Test] public void TryImport_SecondAblator_ReturnsFalseWithANote()
        {
            HeatshieldImporter.ReferenceSource = () => new[] { (8e-8, 0.8) };
            Assert.IsTrue(Arm());
            var part = Part(StockAblator + StockAblator);
            var context = ShieldContext(part, 0.8);
            var modules = part.GetNodes("MODULE").ToList();
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(modules[0], context, out _));
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(modules[1], context, out _));
            Assert.AreEqual(1, context.ModuleData.OfType<Data_Heatshield>().Count());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPart: " + AblatorMapping.ExtraModuleNote);
        }

        [Test] public void TryImport_HeatshieldFailureAfterCreateSerialized_RollsBack()
        {
            HeatshieldImporter.ReferenceSource = () => new[] { (8e-8, 0.8) };
            Assert.IsTrue(Arm());
            NativeModules.Sink = l =>
            {
                if (l.Contains("ModuleAblator -> Module_Heatshield:")) throw new InvalidOperationException("sink");
                _lines.Add(l);
            };
            var part = Part(StockAblator);
            var context = ShieldContext(part, 0.8);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(part.GetNodes("MODULE").First(), context, out _));
            CollectionAssert.IsEmpty(context.ModuleData, "no orphan Data_Heatshield");
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart: ModuleAblator not converted")));
        }

        [Test] public void HeatshieldSwitchOff_CrossfeedStillArmed()
        {
            Assert.IsTrue(Arm("{\"ksp1_heatshields\": false}"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleAblator -> Module_Heatshield off (ksp1_heatshields false)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, ModuleResourceConverter -> Module_ResourceConverter" +
                                              ", ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, ModuleResourceScanner -> Module_ResourceScanner, ModuleActiveRadiator -> Module_Cooler" + ScienceArmed + WheelImportTests.ArmedPairs);
            Assert.IsFalse(Importers(new Ksp1PartModuleImporterRegistry()).ContainsKey(AblatorMapping.Ksp1Module));
            Assert.IsFalse(NativeModules.IsConverterArmed(AblatorMapping.Ksp1Module));
        }
    }
}
