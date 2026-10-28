using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
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
        }

        private bool Arm(string json = "{}") =>
            NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), AnimationSettings.Parse(json, out _));

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        private static Ksp1ConfigNode Part(string modules, string partValues = "") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = testPart\n" + partValues + modules + "}\n").GetNodes("PART").First();

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
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed");
            Assert.IsInstanceOf<CrossfeedImporter>(Importers(new Ksp1PartModuleImporterRegistry())[CrossfeedMapping.Ksp1Module]);
            Assert.IsInstanceOf<CrossfeedImporter>(Importers(new Ksp1PartModuleImporterRegistry())["moduletogglecrossfeed"]);
        }

        [Test] public void SwitchOff_NothingAdded_ReduxAsIn070()
        {
            Assert.IsFalse(Arm("{\"ksp1_crossfeed_toggle\": false}"));
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: ModuleToggleCrossfeed -> Module_ToggleCrossfeed off (ksp1_crossfeed_toggle false)",
                NativeLines.NoneArmed,
            }, _lines);
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
    }
}
