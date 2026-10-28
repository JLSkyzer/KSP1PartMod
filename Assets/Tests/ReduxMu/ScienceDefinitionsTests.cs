using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Game.Science;
using KSP.IO;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native.Science;
using KSP1PartMod.Tests.Animation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), science plan Task 3: the members the definitions glue calls resolve
    /// with their 26w42c signatures, and a generated definition goes through the game's own JSON path into an
    /// <c>ExperimentDefinition</c> that the real <c>ScienceExperimentsDataStore</c> accepts once, with a <c>Default</c> flavor text.
    /// Titles and flavor texts invented.</summary>
    public class ScienceDefinitionsTests
    {
        private const string Def = @"
EXPERIMENT_DEFINITION
{
    id = bd_inventedProbe
    title = Invented Probe
    baseValue = 10
    dataScale = 2
    situationMask = 49
    biomeMask = 17
    RESULTS
    {
        default = Invented default text.
        MunInSpaceLow = Invented Mun text.
    }
}";

        private static ScienceDefinitionModel Model(double x = 0.3) =>
            ScienceDefinitionBuilder.From(CfgText.Parse(Def).GetNodes("EXPERIMENT_DEFINITION").Single(), new DefinitionOverrides(x, null, null),
                AnimationSettings.Defaults, null);

        [SetUp] public void SetUp() => ScienceDefinitions.ResetForTests();
        [TearDown] public void TearDown() => ScienceDefinitions.ResetForTests();

        /// <summary>IOProvider's settings come from a RuntimeInitializeOnLoadMethod that EditMode does not run.</summary>
        private static void WithGameSettings(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            try
            {
                IOProvider.Init();
                test();
            }
            finally { JsonConvert.DefaultSettings = previous; }
        }

        // ---- resolution

        [Test] public void Resolve_PatchManagerScienceMembers()
        {
            var ext = AccessTools.TypeByName(ScienceDefinitions.PatchManagerExtensionsType);
            Assert.IsNotNull(ext, ScienceDefinitions.PatchManagerExtensionsType);
            var scope = AccessTools.TypeByName(ScienceDefinitions.PmScopeType);
            Assert.IsNotNull(scope, ScienceDefinitions.PmScopeType);
            var duplicate = AccessTools.Method(ext, "DuplicateExperiment", new[] { scope, typeof(string), typeof(string) });
            Assert.IsNotNull(duplicate, "DuplicateExperiment(PmScope, string, string)");
            Assert.IsTrue(duplicate.IsStatic);
            Assert.AreEqual("PatchBuilder`1", duplicate.ReturnType.Name);
            var userData = AccessTools.TypeByName(ScienceDefinitions.ExperimentUserDataType);
            Assert.IsNotNull(userData, ScienceDefinitions.ExperimentUserDataType);
            Assert.AreEqual(userData, duplicate.ReturnType.GetGenericArguments().Single());
            var full = AccessTools.Field(userData, "FullToken");
            Assert.IsNotNull(full, "ExperimentUserData.FullToken");
            Assert.AreEqual(typeof(JToken), full.FieldType);
            Assert.IsNotNull(AccessTools.Method(AccessTools.TypeByName("PatchManager.CSharpPatching.Patching"), "ForMod", new[] { typeof(string) }));
            Assert.IsTrue(ScienceDefinitions.PatchManagerPresent());
        }

        [Test] public void Resolve_ReduxLocalizer()
        {
            var t = AccessTools.TypeByName(ScienceDefinitions.LocalizerType);
            Assert.IsNotNull(t, ScienceDefinitions.LocalizerType);
            var create = AccessTools.Method(t, "CreateSourceFromCsv", new[] { typeof(string) });
            Assert.IsNotNull(create, "CreateSourceFromCsv(string)");
            Assert.IsTrue(create.IsStatic);
            Assert.AreEqual("LanguageSourceData", create.ReturnType.Name);
            var add = AccessTools.Method(t, "AddSource", new[] { create.ReturnType });
            Assert.IsNotNull(add, "AddSource(LanguageSourceData)");
            Assert.IsTrue(add.IsStatic);
            Assert.IsTrue(ScienceDefinitions.LocalizerPresent());
        }

        [Test] public void Resolve_ExperimentStore()
        {
            var store = typeof(ScienceExperimentsDataStore);
            var addDef = AccessTools.Method(store, nameof(ScienceExperimentsDataStore.AddExperimentDefinition), new[] { typeof(ExperimentDefinition) });
            Assert.IsNotNull(addDef);
            Assert.AreEqual(typeof(bool), addDef.ReturnType);
            Assert.IsFalse(addDef.IsStatic);
            Assert.IsNotNull(AccessTools.Method(store, nameof(ScienceExperimentsDataStore.Initialize), new[] { typeof(Action) }), "Initialize(Action), route B");
            Assert.IsNotNull(AccessTools.Method(store, "ResolveExperiments"), "the loader that calls the initialization callback");
            Assert.IsNotNull(AccessTools.Field(store, "_isInitialized"));
            Assert.AreEqual(0.1f, ExperimentCore.EXPERIMENT_SERIALIZATION_VERSION, "ScienceDefinitionJson.Version");
            Assert.AreEqual(0.1f, ScienceDefinitionJson.Version);
            // The JSON enum names.
            Assert.AreEqual("DataType", ScienceExperimentType.DataType.ToString());
            Assert.AreEqual("SampleType", ScienceExperimentType.SampleType.ToString());
            Assert.AreEqual("Both", ScienceExperimentType.Both.ToString());
            foreach (Ksp2Situation s in Enum.GetValues(typeof(Ksp2Situation)))
                Assert.AreEqual((byte)s, (byte)(ScienceSitutation)Enum.Parse(typeof(ScienceSitutation), s.ToString()), s.ToString());
        }

        // ---- integration

        [Test] public void Json_ThroughTheGamesPath_IntoTheRealStore()
        {
            var d = Model();
            Assert.IsTrue(d.IsBuilt);
            WithGameSettings(() =>
            {
                var core = IOProvider.FromJson<ExperimentCore>(ScienceDefinitionJson.Core(d));
                Assert.IsNotNull(core);
                Assert.AreEqual(0.1f, core.version, 1e-6);
                var def = core.data;
                Assert.AreEqual("KSP1_bd_inventedProbe", def.ExperimentID);
                Assert.AreEqual(d.NameTerm, def.DisplayName);
                Assert.AreEqual(d.RequirementsTerm, def.DisplayRequirements);
                Assert.AreEqual(ScienceExperimentType.Both, def.ExperimentType);
                Assert.AreEqual(3f, def.DataValue, 1e-5);
                Assert.AreEqual(7f, def.SampleValue, 1e-5);
                Assert.AreEqual(20f, def.TransmissionSize, 1e-5);
                Assert.IsFalse(def.RequiresEVA);
                Assert.AreEqual(d.DataReportTerm, def.DataReportDisplayName);
                Assert.AreEqual(d.SampleReportTerm, def.SampleReportDisplayName);
                Assert.AreEqual(3, def.ValidLocations.Count);
                var landed = def.ValidLocations.Single(l => l.ScienceSituation == ScienceSitutation.Landed);
                Assert.IsTrue(landed.RequiresRegion);
                Assert.IsTrue(string.IsNullOrEmpty(landed.BodyName));
                Assert.IsTrue(string.IsNullOrEmpty(landed.ScienceRegion));
                var low = def.ValidLocations.Single(l => l.ScienceSituation == ScienceSitutation.LowOrbit);
                Assert.IsTrue(low.RequiresRegion);
                Assert.IsFalse(def.ValidLocations.Single(l => l.ScienceSituation == ScienceSitutation.HighOrbit).RequiresRegion);
                Assert.IsTrue(def.IsLocationValid(new ResearchLocation(true, "Kerbin", ScienceSitutation.Landed, "Shores"), out var region));
                Assert.IsTrue(region);
                Assert.IsFalse(def.IsLocationValid(new ResearchLocation(false, "Kerbin", ScienceSitutation.Atmosphere, ""), out _));
                Assert.AreEqual(d.DataFlavor.Count, def.DataFlavorDescriptions.Count);
                Assert.AreEqual(d.SampleFlavor.Count, def.SampleFlavorDescriptions.Count);

                var store = new ScienceExperimentsDataStore();
                Assert.IsTrue(store.AddExperimentDefinition(def));
                var ignore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true; // the store logs its duplicate refusal as an error
                try { Assert.IsFalse(store.AddExperimentDefinition(ScienceDefinitions.ToDefinition(d)), "a second time: refused"); }
                finally { LogAssert.ignoreFailingMessages = ignore; }
                var defaultTerm = d.DataFlavor.Single(f => f.LocationId == "Default").Term;
                var munTerm = d.DataFlavor.Single(f => f.LocationId == "Mun_LowOrbit").Term;
                Assert.AreEqual(defaultTerm, store.GetFlavorText(def.ExperimentID, "Kerbin_Landed_Shores", ScienceReportType.DataType));
                Assert.AreEqual(defaultTerm, store.GetFlavorText(def.ExperimentID, "Kerbin_Landed_Shores", ScienceReportType.SampleType));
                Assert.AreEqual(munTerm, store.GetFlavorText(def.ExperimentID, "Mun_LowOrbit_Midlands", ScienceReportType.DataType));
                Assert.AreEqual(d.NameTerm, store.GetExperimentDisplayName(def.ExperimentID));
                Assert.AreEqual(d.SampleReportTerm, store.GetExperimentReportName(def.ExperimentID, ScienceReportType.SampleType));

                var live = ScienceDefinitions.Live(store, new[] { def.ExperimentID });
                var e = live.Find(def.ExperimentID);
                Assert.AreEqual(ScienceExperimentKind.Both, e.Type);
                Assert.AreEqual("Landed+R, LowOrbit+R, HighOrbit", Ksp1Situations.Labels(e.Locations.OrderByDescending(l => (byte)l.Situation)));
            });
        }

        [Test] public void Json_DataOnlyAndSampleOnly()
        {
            WithGameSettings(() =>
            {
                var data = ScienceDefinitions.ToDefinition(Model(1));
                Assert.AreEqual(ScienceExperimentType.DataType, data.ExperimentType);
                Assert.AreEqual(10f, data.DataValue, 1e-5);
                Assert.AreEqual(0f, data.SampleValue, 1e-5);
                var sample = ScienceDefinitions.ToDefinition(Model(0));
                Assert.AreEqual(ScienceExperimentType.SampleType, sample.ExperimentType);
                Assert.AreEqual(10f, sample.SampleValue, 1e-5);
            });
        }

        /// <summary>Route A's <c>.Do</c> on a duplicate of <c>EnvironmentSurvey</c>: the copy's <c>data</c> becomes the definition and its
        /// <c>version</c> stays; what Patch Manager writes back (<c>FullToken</c>) then reads through the game's path.</summary>
        [Test] public void PatchManagerDo_ReplacesData_KeepsVersion()
        {
            var d = Model();
            var copy = new JObject
            {
                ["version"] = 0.1,
                ["data"] = new JObject { ["ExperimentID"] = "EnvironmentSurvey", ["DisplayName"] = "Science/Experiments/EnvironmentSurvey/Name" },
            };
            var asset = new PatchManager.Science.UserData.ExperimentUserData(copy);
            ScienceDefinitions.ReplaceData(asset, ScienceDefinitionJson.Data(d).ToString(Formatting.None));
            Assert.AreEqual(0.1, (double)asset.FullToken["version"], 1e-6);
            Assert.AreEqual("KSP1_bd_inventedProbe", (string)asset.FullToken["data"]["ExperimentID"]);
            WithGameSettings(() =>
            {
                var core = IOProvider.FromJson<ExperimentCore>(asset.FullToken.ToString(Formatting.None));
                Assert.AreEqual("KSP1_bd_inventedProbe", core.data.ExperimentID);
                Assert.AreEqual(ScienceExperimentType.Both, core.data.ExperimentType);
                Assert.AreEqual(d.NameTerm, core.data.DisplayName);
            });
        }

        /// <summary>Route B: the wrapped callback adds the queued definitions once the store reports initialized, skips an id
        /// already there, then calls the game's own callback.</summary>
        [Test] public void StoreRoute_WrappedCallback_AddsThenCallsTheGame()
        {
            var a = Model();
            var b = ScienceDefinitionBuilder.From(CfgText.Parse(Def.Replace("bd_inventedProbe", "bd_inventedOther")).GetNodes("EXPERIMENT_DEFINITION").Single(),
                null, AnimationSettings.Defaults, null);
            ScienceDefinitions.QueueForTests(new[] { a, b });
            ScienceDefinitions.ViaStoreForTests(true);
            ScienceDefinitions.ModEnabledSource = () => true;
            WithGameSettings(() =>
            {
                var store = new ScienceExperimentsDataStore();
                Assert.IsTrue(store.AddExperimentDefinition(ScienceDefinitions.ToDefinition(a)), "already there (route A, or a second load)");
                var called = false;
                Action callback = () =>
                {
                    called = true;
                    Assert.IsNotNull(store.GetExperimentDefinition("KSP1_bd_inventedOther"), "added before the game hears of it");
                };
                ScienceDefinitions.BeforeStoreInitialize(store, ref callback);
                AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                callback();
                Assert.IsTrue(called);
                CollectionAssert.AreEquivalent(new[] { "KSP1_bd_inventedProbe", "KSP1_bd_inventedOther" }, store.GetAllExperimentIDs());
            });
        }

        /// <summary>Route B fails closed: no plugin instance (null), or KSP1PartMod off in the campaign, adds nothing and still
        /// calls the game.</summary>
        [Test] public void StoreRoute_NoPluginOrModOff_AddsNothing()
        {
            foreach (var enabled in new bool?[] { null, false })
            {
                ScienceDefinitions.QueueForTests(new[] { Model() });
                ScienceDefinitions.ViaStoreForTests(true);
                ScienceDefinitions.ModEnabledSource = () => enabled;
                WithGameSettings(() =>
                {
                    var store = new ScienceExperimentsDataStore();
                    var called = false;
                    Action callback = () => called = true;
                    ScienceDefinitions.BeforeStoreInitialize(store, ref callback);
                    AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                    callback();
                    Assert.IsTrue(called, "the game's callback still runs");
                    CollectionAssert.IsEmpty(store.GetAllExperimentIDs(), "enabled = " + (enabled?.ToString() ?? "null"));
                });
            }
        }

        [Test] public void StoreRoute_NotInitialized_AddsNothing()
        {
            ScienceDefinitions.QueueForTests(new[] { Model() });
            ScienceDefinitions.ViaStoreForTests(true);
            var store = new ScienceExperimentsDataStore();
            Action callback = () => { };
            ScienceDefinitions.BeforeStoreInitialize(store, ref callback);
            callback();
            CollectionAssert.IsEmpty(store.GetAllExperimentIDs());
        }

        /// <summary>The check runs from the store's callback in both routes, before the game's own callback, mod on or off (no
        /// campaign feature gates it). Route A adds nothing there; the mod-off store line says it is expected.</summary>
        [Test] public void StoreHook_ChecksInBothRoutes_ModOnOrOff_RouteAAddsNothing()
        {
            foreach (var viaStore in new[] { false, true })
            foreach (var enabled in new bool?[] { true, false })
            {
                ScienceDefinitions.ResetForTests();
                var lines = new List<string>();
                ScienceDefinitions.Sink = lines.Add;
                ScienceDefinitions.QueueForTests(new[] { Model() });
                ScienceDefinitions.ViaStoreForTests(viaStore);
                ScienceDefinitions.ModEnabledSource = () => enabled;
                var label = $"via store {viaStore}, enabled {enabled}";
                WithGameSettings(() =>
                {
                    var store = new ScienceExperimentsDataStore();
                    var storeLines = -1;
                    Action callback = () => storeLines = lines.Count(l => l.Contains("science: store: ") && l.Contains("KSP1 definition(s) present"));
                    ScienceDefinitions.BeforeStoreInitialize(store, ref callback);
                    AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                    callback();
                    Assert.AreEqual(1, storeLines, "one store line before the game's callback: " + label);
                    var added = viaStore && enabled == true;
                    Assert.AreEqual(added ? 1 : 0, store.GetAllExperimentIDs().Count(i => i.StartsWith("KSP1_")), label);
                    var line = lines.Single(l => l.Contains("KSP1 definition(s) present"));
                    StringAssert.StartsWith(ScienceLines.Prefix + $"store: {(added ? 1 : 0)} of 1 KSP1 definition(s) present", line, label);
                    if (enabled == false) StringAssert.Contains("(KSP1PartMod off in this campaign: expected)", line, label);
                    else StringAssert.DoesNotContain("expected", line, label);
                });
            }
        }

        /// <summary>A new store per campaign load: one check, and one store line, per store initialization.</summary>
        [Test] public void StoreHook_OneCheckPerStoreInitialization()
        {
            var lines = new List<string>();
            ScienceDefinitions.Sink = lines.Add;
            ScienceDefinitions.QueueForTests(new[] { Model() });
            ScienceDefinitions.ModEnabledSource = () => true;
            WithGameSettings(() =>
            {
                for (var load = 0; load < 2; load++)
                {
                    var store = new ScienceExperimentsDataStore();
                    Action callback = () => { };
                    ScienceDefinitions.BeforeStoreInitialize(store, ref callback);
                    AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                    callback();
                }
            });
            Assert.AreEqual(2, lines.Count(l => l.Contains("KSP1 definition(s) present")));
        }

        /// <summary>The import's natives are frozen at their first read: a later check does not change what a rebuilt part resolves
        /// against; the live list only feeds the difference line.</summary>
        [Test] public void Natives_FrozenAtTheFirstRead_LaterCheckOnlyLive()
        {
            Assert.IsTrue(ScienceDefinitions.Natives.Contains("EnvironmentSurvey"), "26w42c before any check");
            WithGameSettings(() =>
            {
                var store = new ScienceExperimentsDataStore();
                store.AddExperimentDefinition(new ExperimentDefinition
                {
                    ExperimentID = "CrewObservation", ExperimentType = ScienceExperimentType.DataType,
                    ValidLocations = new List<ResearchLocation> { new ResearchLocation(true, "", ScienceSitutation.Landed, "") },
                });
                AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                ScienceDefinitions.Check(store);
            });
            Assert.IsTrue(ScienceDefinitions.Natives.Contains("EnvironmentSurvey"), "still the first list");
            Assert.AreSame(NativeExperiments.Redux26w42c, ScienceDefinitions.Natives);
            Assert.IsFalse(ScienceDefinitions.LiveNatives.Contains("EnvironmentSurvey"), "the live list is the store's");
            Assert.IsTrue(ScienceDefinitions.LiveNatives.Contains("CrewObservation"));
        }

        [Test] public void Check_ReadsTheLiveNatives()
        {
            WithGameSettings(() =>
            {
                var store = new ScienceExperimentsDataStore();
                var d = Model();
                ScienceDefinitions.QueueForTests(new[] { d });
                store.AddExperimentDefinition(ScienceDefinitions.ToDefinition(d));
                store.AddExperimentDefinition(new ExperimentDefinition
                {
                    ExperimentID = "CrewObservation", ExperimentType = ScienceExperimentType.DataType,
                    ValidLocations = new List<ResearchLocation> { new ResearchLocation(true, "", ScienceSitutation.Landed, "") },
                });
                AccessTools.Field(typeof(ScienceExperimentsDataStore), "_isInitialized").SetValue(store, true);
                ScienceDefinitions.Check(store);
                Assert.IsTrue(ScienceDefinitions.Natives.Contains("CrewObservation"));
                Assert.IsFalse(ScienceDefinitions.Natives.Contains("KSP1_bd_inventedProbe"), "our own are not KSP2 experiments");
                Assert.IsFalse(ScienceDefinitions.Natives.Contains("EnvironmentSurvey"), "the live list replaces 26w42c's");
            });
        }
    }
}
