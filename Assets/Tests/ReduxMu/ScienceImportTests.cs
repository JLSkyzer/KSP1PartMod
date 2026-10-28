using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Science;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), science plan Task 4: the science members resolve by name, and Redux's
    /// real <c>Ksp1PartModuleImporterRegistry</c>, built with our hook armed, imports in-memory KSP1 science parts as KSP2's
    /// <c>Module_ScienceExperiment</c> with report storage. Cfg text in the shape of the stock and BDB files, titles invented.</summary>
    public class ScienceImportTests
    {
        /// <summary>The science pairs of the armed line, after the radiators.</summary>
        public const string ArmedPairs = ", ModuleScienceExperiment -> Module_ScienceExperiment, DMModuleScienceAnimate -> Module_ScienceExperiment, " +
                                         "DMModuleScienceAnimateGeneric -> Module_ScienceExperiment, ModuleScienceContainer -> report storage";

        private readonly List<string> _lines = new List<string>();
        private Dictionary<string, ScienceDefinitionModel> _defs = new Dictionary<string, ScienceDefinitionModel>(StringComparer.Ordinal);

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            _defs = new Dictionary<string, ScienceDefinitionModel>(StringComparer.Ordinal);
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

        private bool Arm(string json = "{}")
        {
            var settings = AnimationSettings.Parse(json, out _);
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), settings);
            ScienceImporter.DefinitionsSource = () => _defs;
            ScienceImporter.NativesSource = () => NativeExperiments.Redux26w42c;
            ScienceImporter.ImportedSource = () => Enumerable.Empty<Ksp1ConfigNode>();
            return armed;
        }

        private const string PodModules = @"
MODULE
{
    name = ModuleCommand
    minimumCrew = 1
}
MODULE
{
    name = ModuleScienceExperiment
    experimentID = crewReport
    experimentActionName = Crew Report
    useStaging = False
    rerunnable = True
    xmitDataScalar = 1.0
    usageReqMaskInternal = 5
    usageReqMaskExternal = -1
}
MODULE
{
    name = ModuleScienceContainer
    reviewActionName = Review
    evaOnlyStorage = True
    storageRange = 1.3
}
";

        private const string GooModules = @"
MODULE
{
    name = ModuleAnimateGeneric
    animationName = Deploy
}
MODULE
{
    name = ModuleScienceExperiment
    experimentID = mysteryGoo
    rerunnable = False
    xmitDataScalar = 0.3
    usageReqMaskInternal = 1
    FxModules = 0
}
";

        private const string BdbModules = @"
MODULE
{
    name = DMModuleScienceAnimateGeneric
    experimentID = bd_invented1
    animationName = Open
    experimentAnimation = true
    experimentWaitForAnimation = true
    waitForAnimationTime = 2.5
    xmitDataScalar = 1
    usageReqMaskInternal = 1
}
MODULE
{
    name = DMModuleScienceAnimateGeneric
    experimentID = bd_invented2
    animationName = Boom
    experimentAnimation = true
    experimentWaitForAnimation = false
    xmitDataScalar = 0.5
}
";

        private const string ContainerModules = @"
MODULE
{
    name = ModuleScienceContainer
    storageRange = 2
}
";

        private static Ksp1ConfigNode Part(string modules, string name = "testPart") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\n" + modules + "}\n").GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };

        private static List<Ksp1ConfigNode> Named(Ksp1ConfigNode part, string module) =>
            part.GetNodes("MODULE").Where(m => string.Equals(m.GetValue("name"), module, StringComparison.OrdinalIgnoreCase)).ToList();

        private static ScienceDefinitionModel Definition(string id, int sit = 63, int bio = 3, double x = 1) =>
            ScienceDefinitionBuilder.From(CfgText.Parse(
                    $"EXPERIMENT_DEFINITION\n{{\nid = {id}\ntitle = Invented {id}\nbaseValue = 10\nsituationMask = {sit}\nbiomeMask = {bio}\n}}")
                .GetNodes("EXPERIMENT_DEFINITION").Single(), new DefinitionOverrides(x, null, null), AnimationSettings.Defaults, null);

        private void Define(params ScienceDefinitionModel[] defs)
        {
            foreach (var d in defs) _defs[d.Key] = d;
        }

        /// <summary>Imports the science modules of <paramref name="part"/> through one registry, in cfg order, as Redux's translator does
        /// (the other modules' importers are not under test).</summary>
        private static List<(string Module, bool Imported, SerializedPartModule Result)> ImportAll(Ksp1ConfigNode part, Ksp1PartImportContext context)
        {
            var registry = new Ksp1PartModuleImporterRegistry();
            var results = new List<(string, bool, SerializedPartModule)>();
            foreach (var m in part.GetNodes("MODULE").Where(x => IsScience(x.GetValue("name"))))
            {
                var ok = registry.TryImport(m, context, out var module);
                results.Add((m.GetValue("name"), ok, module));
            }
            return results;
        }

        private static bool IsScience(string module) =>
            ScienceMapping.ExperimentModuleNames.Concat(new[] { ScienceMapping.ContainerModule }).Any(n => string.Equals(n, module?.Trim(), StringComparison.OrdinalIgnoreCase));

        private static Data_ScienceExperiment Science(Ksp1PartImportContext context) => context.ModuleData.OfType<Data_ScienceExperiment>().Single();

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_EveryScienceMember()
        {
            var t = ScienceAccess.Resolve();
            Assert.AreEqual("", string.Join(", ", t.MissingExperiment.Concat(t.MissingStorage).Concat(t.MissingRuntime)));
            Assert.AreEqual(typeof(Data_ScienceExperiment), t.Data);
            Assert.AreEqual(typeof(Module_ScienceExperiment), t.Module);
            Assert.AreEqual(typeof(ExperimentConfiguration), t.Configuration);
            Assert.AreEqual(typeof(List<ExperimentConfiguration>), t.Experiments.FieldType);
            Assert.AreEqual(typeof(bool), t.NotifyOnCompletion.FieldType);
            Assert.AreEqual(typeof(bool), t.IsPartAnimated.FieldType);
            Assert.AreEqual(typeof(bool), t.PartIsDeployed.FieldType);
            Assert.AreEqual(typeof(float), t.TimeToComplete.FieldType);
            Assert.AreEqual(typeof(List<PartModuleResourceSetting>), t.ResourcesCost.FieldType);
            Assert.IsNotNull(t.UsesResources, "ExperimentUsesResources (optional)");
            Assert.AreEqual(typeof(bool), t.HasReportStorage.FieldType);
            Assert.AreEqual(typeof(Module_ScienceExperiment), new Data_ScienceExperiment().ModuleType);
            Assert.IsNull(new Data_ScienceExperiment().Experiments, "null by default: the importer always sets a list");
            CollectionAssert.IsEmpty(ScienceImporter.MissingMembers());
            CollectionAssert.IsEmpty(ScienceImporter.MissingStorageMembers());
        }

        // ------------------------------------------------------------------ Redux's real import

        [Test] public void Arm_AddsTheFourScienceImporters()
        {
            Assert.IsTrue(Arm());
            Assert.That(_lines, Has.Some.EndsWith("ModuleActiveRadiator -> Module_Cooler" + ArmedPairs));
            var importers = (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(new Ksp1PartModuleImporterRegistry());
            foreach (var name in new[] { "modulescienceexperiment", "dmmodulescienceanimate", "dmmodulescienceanimategeneric", "modulesciencecontainer" })
                Assert.IsInstanceOf<ScienceImporter>(importers[name], name);
            Assert.IsFalse(importers.ContainsKey("ModuleScienceLab"), "out of scope: Redux's unsupported line stays");
        }

        [Test] public void StockPod_CrewObservation_Crew1_Time0_EmptyCost_StorageOn()
        {
            Assert.IsTrue(Arm());
            var part = Part(PodModules, "testPod");
            var context = Context(part);
            var results = ImportAll(part, context);

            var science = results.Single(r => r.Module == "ModuleScienceExperiment");
            Assert.IsTrue(science.Imported);
            Assert.AreEqual(typeof(Module_ScienceExperiment), science.Result.BehaviourType);
            Assert.AreEqual("PartComponentModule_ScienceExperiment", science.Result.ComponentType.Name);
            Assert.IsFalse(results.Single(r => r.Module == "ModuleScienceContainer").Imported, "storage only");
            var data = Science(context);
            Assert.AreSame(data, science.Result.ModuleData.Single().DataObject);
            var c = data.Experiments.Single();
            Assert.AreEqual("CrewObservation", c.ExperimentDefinitionID);
            Assert.AreEqual(1, c.CrewRequired);
            Assert.AreEqual(0f, c.TimeToComplete);
            Assert.IsNotNull(c.ResourcesCost, "the part info reads its count");
            Assert.AreEqual(0, c.ResourcesCost.Count);
            Assert.IsFalse(c.ContextualExperiment);
            Assert.IsFalse(c.DenyInterruption);
            Assert.IsFalse(c.ExperimentUsesResources);
            Assert.AreEqual("", c.DeployedBoolAnimatorParamName);
            Assert.AreEqual("", c.CFXName);
            Assert.IsTrue(data.NotifyOnCompletion);
            Assert.IsFalse(data.IsPartAnimated);
            Assert.IsFalse(data.PartIsDeployed);
            Assert.IsTrue(context.PartData.HasReportStorage);

            CollectionAssert.AreEqual(new[] { ScienceMapping.NativeModule }, NativeModules.Record.ConvertedModules("testPod"));
            var record = ScienceImporter.PartOf("testPod");
            Assert.IsNotNull(record);
            Assert.AreEqual("CrewObservation", record.Configurations.Single().ExperimentDefinitionId);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPod: science -> Module_ScienceExperiment: crewReport -> CrewObservation (identical); " +
                                              "crew 1; report storage on");
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: testPod: ModuleScienceContainer: evaOnlyStorage not reproduced"));

            // A rebuild of the same part logs and counts nothing twice.
            ImportAll(part, Context(part));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPod: science -> ")));
        }

        [Test] public void Goo_GeneratedWithADefinition_ClosestWithout()
        {
            Assert.IsTrue(Arm());
            var part = Part(GooModules, "testGoo");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceExperiment")[0], context, out _));
            var c = Science(context).Experiments.Single();
            Assert.AreEqual("EnvironmentSurvey", c.ExperimentDefinitionID, "no definition on disk: science_fallback_ids");
            Assert.AreEqual(0, c.CrewRequired, "usageReqMaskInternal 1: vessel control, native");
            CollectionAssert.AreEqual(new[] { 0 }, ScienceImporter.PartOf("testGoo").Configurations.Single().Clip.FxModules, "the clip target, for the runtime");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testGoo: science -> Module_ScienceExperiment: mysteryGoo -> EnvironmentSurvey (closest); " +
                                              "crew 0; report storage on");

            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
            Assert.IsTrue(Arm());
            Define(Definition("mysteryGoo", x: 0.3));
            var withDef = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceExperiment")[0], withDef, out _));
            Assert.AreEqual("KSP1_mysteryGoo", Science(withDef).Experiments.Single().ExperimentDefinitionID);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testGoo: science -> Module_ScienceExperiment: mysteryGoo -> KSP1_mysteryGoo (generated); " +
                                              "crew 0; report storage on");
        }

        [Test] public void BdbTwoDMagicGeneric_OneModuleTwoConfigurations_SecondCallFalse()
        {
            Assert.IsTrue(Arm());
            Define(Definition("bd_invented1", 48, 16), Definition("bd_invented2", 63, 3, 0.5));
            var part = Part(BdbModules, "testBdb");
            var context = Context(part);
            var registry = new Ksp1PartModuleImporterRegistry();
            var nodes = Named(part, "DMModuleScienceAnimateGeneric");
            Assert.IsTrue(registry.TryImport(nodes[0], context, out var first));
            Assert.IsFalse(registry.TryImport(nodes[1], context, out var second), "merged into the first call's module");
            Assert.IsNull(second.ModuleData);
            Assert.AreEqual(1, context.ModuleData.Count);
            var data = Science(context);
            Assert.AreSame(data, first.ModuleData.Single().DataObject);
            CollectionAssert.AreEqual(new[] { "KSP1_bd_invented1", "KSP1_bd_invented2" }, data.Experiments.Select(e => e.ExperimentDefinitionID));
            Assert.AreEqual(2.5f, data.Experiments[0].TimeToComplete, 1e-6f, "waits for its clip: waitForAnimationTime");
            Assert.AreEqual(0f, data.Experiments[1].TimeToComplete, "no wait: completes at once");
            Assert.IsTrue(context.PartData.HasReportStorage);
            var record = ScienceImporter.PartOf("testBdb");
            Assert.AreEqual(0, record.Configurations[0].Clip.ToggleModuleIndex);
            Assert.AreEqual(1, record.Configurations[1].Clip.ToggleModuleIndex);

            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: science: 1 part(s) got Module_ScienceExperiment (2 generated, 0 identical, 0 closest); " +
                                                    "report storage on 1 part(s) (0 container(s)); merged: 1;"));
            Assert.AreEqual(1, _lines.Count(l => l.StartsWith("[KSP1PartMod] native: science: ")), "one summary for the four converters");
        }

        [Test] public void ContainerOnly_Storage_NoModule()
        {
            Assert.IsTrue(Arm());
            var part = Part(ContainerModules, "testBox");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceContainer")[0], context, out var module));
            Assert.IsNull(module.ModuleData);
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.IsTrue(context.PartData.HasReportStorage);
            CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("testBox"));
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: science: 0 part(s) got Module_ScienceExperiment (0 generated, 0 identical, 0 closest); " +
                                                    "report storage on 1 part(s) (1 container(s)); merged: 0; skipped: none; not reproduced: storageRange 1;"));
        }

        [Test] public void ContainerBeforeTheExperiment_BothDone()
        {
            Assert.IsTrue(Arm());
            var part = Part(ContainerModules + GooModules, "testBoth");
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.IsFalse(results[0].Imported);
            Assert.IsTrue(results.Single(r => r.Module == "ModuleScienceExperiment").Imported, "the first experiment call builds the module");
            Assert.AreEqual(1, context.ModuleData.OfType<Data_ScienceExperiment>().Count());
            Assert.IsTrue(context.PartData.HasReportStorage);
        }

        [Test] public void NoResolvedExperiment_NoModule_StorageFollowsTheContainer()
        {
            Assert.IsTrue(Arm());
            var eva = "MODULE\n{\nname = ModuleScienceExperiment\nexperimentID = evaReport\n}\n";
            var bare = Part(eva, "testEva");
            var context = Context(bare);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(bare, "ModuleScienceExperiment")[0], context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.IsFalse(context.PartData.HasReportStorage, "no experiment and no container");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testEva: science: evaReport skipped (EVA kerbal experiment)");

            var boxed = Part(eva + ContainerModules, "testEvaBox");
            var withBox = Context(boxed);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(boxed, "ModuleScienceExperiment")[0], withBox, out _));
            Assert.IsTrue(withBox.PartData.HasReportStorage, "the container's storage, from the experiment call too");
        }

        [Test] public void Ksp1ScienceOff_NoModule_NoStorage()
        {
            Assert.IsTrue(Arm("{\"ksp1_science\": false}"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleScienceExperiment -> Module_ScienceExperiment off (ksp1_science false)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleScienceContainer -> report storage off (ksp1_science or science_report_storage false)");
            var part = Part(PodModules, "testPod");
            var context = Context(part);
            Assert.IsTrue(ImportAll(part, context).All(r => !r.Imported), "dropped by Redux");
            CollectionAssert.IsEmpty(context.ModuleData.OfType<Data_ScienceExperiment>());
            Assert.IsFalse(context.PartData.HasReportStorage);
        }

        [Test] public void ReportStorageOff_ModuleWithoutStorage()
        {
            Assert.IsTrue(Arm("{\"science_report_storage\": false}"));
            var part = Part(PodModules, "testPod");
            var context = Context(part);
            ImportAll(part, context);
            Assert.AreEqual("CrewObservation", Science(context).Experiments.Single().ExperimentDefinitionID);
            Assert.IsFalse(context.PartData.HasReportStorage);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPod: science -> Module_ScienceExperiment: crewReport -> CrewObservation (identical); " +
                                              "crew 1; report storage off");
        }

        [Test] public void ImporterException_LeavesModuleDataAndStorageAsBefore()
        {
            foreach (var before in new[] { false, true })
            {
                Disarm();
                _lines.Clear();
                NativeModules.Sink = _lines.Add;
                Assert.IsTrue(Arm());
                ScienceImporter.FaultForTests = p => throw new InvalidOperationException("fault");
                var part = Part(PodModules, "testPod");
                var context = Context(part);
                context.PartData.HasReportStorage = before;
                var other = new Data_ToggleCrossFeed();
                context.ModuleData.Add(other);
                Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceExperiment")[0], context, out var module));
                Assert.IsNull(module.ModuleData);
                Assert.AreSame(other, context.ModuleData.Single(), "no orphan Data_ScienceExperiment");
                Assert.AreEqual(before, context.PartData.HasReportStorage, "restored");
                CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("testPod"), "not counted");
                Assert.IsNull(ScienceImporter.PartOf("testPod"), "not recorded");
                Assert.That(_lines, Has.None.Contains("testPod: science -> "));
                Assert.AreEqual(1, _lines.Count(l => l.Contains("testPod: ModuleScienceExperiment not converted")));

                var container = Context(part);
                container.PartData.HasReportStorage = before;
                Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceContainer")[0], container, out _));
                Assert.AreEqual(before, container.PartData.HasReportStorage, "a failed container call restores it too");
            }
        }

        [Test] public void ContextWithAnExperiment_NothingAdded_Noted()
        {
            Assert.IsTrue(Arm());
            var part = Part(GooModules);
            var context = Context(part);
            var redux = new Data_ScienceExperiment { Experiments = new List<ExperimentConfiguration>() };
            context.ModuleData.Add(redux);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "ModuleScienceExperiment")[0], context, out _));
            Assert.AreSame(redux, context.ModuleData.Single());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPart: " + ScienceImporter.ExistingModuleNote);
        }

        /// <summary>B9's module-data route (0.9.0): a scratch context called with an edited copy builds the module from that copy,
        /// in place of the module it matches; the copy is neither recorded nor logged.</summary>
        [Test] public void ScratchContext_EditedCopy_ReplacesTheModule_NotRecorded()
        {
            Assert.IsTrue(Arm());
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(PodModules, "testPod");
            Assert.IsTrue(registry.TryImport(Named(part, "ModuleScienceExperiment")[0], Context(part), out _));
            var edited = Named(Part(PodModules.Replace("usageReqMaskInternal = 5", "usageReqMaskInternal = 1")), "ModuleScienceExperiment")[0];
            var scratch = Context(part);
            Assert.IsTrue(registry.TryImport(edited, scratch, out _));
            var c = Science(scratch).Experiments.Single();
            Assert.AreEqual("CrewObservation", c.ExperimentDefinitionID);
            Assert.AreEqual(0, c.CrewRequired, "the copy replaces the module, not added to it");
            Assert.AreEqual(1, ScienceImporter.PartOf("testPod").Configurations.Single().CrewRequired, "the record keeps the part's own module");
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPod: science -> ")), "the copy logs nothing");
        }

        [Test] public void WithCalled_TheMostSimilarModuleOfTheSameNameIsReplaced()
        {
            var part = Part(GooModules + BdbModules);
            var raw = part.GetNodes("MODULE").ToList();
            var edited = Named(Part(BdbModules.Replace("waitForAnimationTime = 2.5", "waitForAnimationTime = 4")), "DMModuleScienceAnimateGeneric")[0];
            var list = ScienceImporter.WithCalled(raw, edited, out var copy);
            Assert.IsTrue(copy);
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual("4", list[2].GetValue("waitForAnimationTime"), "same index as the module it edits");
            Assert.AreEqual("bd_invented2", list[3].GetValue("experimentID"));
            ScienceImporter.WithCalled(raw, raw[1], out var own);
            Assert.IsFalse(own, "the part's own node");
            var other = Named(Part(ContainerModules), "ModuleScienceContainer")[0];
            Assert.AreEqual(5, ScienceImporter.WithCalled(raw, other, out _).Count, "no module of that name: appended");
        }

        /// <summary>The built <c>Data_ScienceExperiment</c> through the game's own JSON (IOProvider), as Redux writes the part definition.</summary>
        [Test] public void BuiltModule_RoundTripsThroughIOProvider()
        {
            Assert.IsTrue(Arm());
            Define(Definition("bd_invented1", 48, 16), Definition("bd_invented2", 63, 3, 0.5));
            var part = Part(BdbModules, "testBdb");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Named(part, "DMModuleScienceAnimateGeneric")[0], context, out _));
            var previous = Newtonsoft.Json.JsonConvert.DefaultSettings;
            try
            {
                try { KSP.IO.IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                var json = KSP.IO.IOProvider.ToJson(Science(context));
                var back = (Data_ScienceExperiment)KSP.IO.IOProvider.FromJson(json, typeof(Data_ScienceExperiment));
                CollectionAssert.AreEqual(new[] { "KSP1_bd_invented1", "KSP1_bd_invented2" }, back.Experiments.Select(e => e.ExperimentDefinitionID), json);
                Assert.AreEqual(2.5f, back.Experiments[0].TimeToComplete, 1e-6f, json);
                Assert.IsNotNull(back.Experiments[0].ResourcesCost, json);
                Assert.IsTrue(back.NotifyOnCompletion, json);
            }
            finally { Newtonsoft.Json.JsonConvert.DefaultSettings = previous; }
        }

        [Test] public void Summary_CountsKinds_Storage_Skipped_NotImported()
        {
            Assert.IsTrue(Arm());
            Define(Definition("mysteryGoo", x: 0.3));
            foreach (var (modules, name) in new[] { (PodModules, "pod"), (GooModules, "goo"), (ContainerModules, "box"),
                         ("MODULE\n{\nname = ModuleScienceExperiment\nexperimentID = evaReport\n}\n", "eva") })
            {
                var part = Part(modules, name);
                ImportAll(part, Context(part));
            }
            ScienceImporter.ImportedSource = () => new[]
            {
                Part("MODULE\n{\nname = ModuleScienceLab\n}\nMODULE\n{\nname = ModuleScienceConverter\n}\n", "lab"),
                Part("MODULE\n{\nname = ModuleTestSubject\n}\nMODULE\n{\nname = ModuleTestSubject\n}\n", "test"),
                Part("MODULE\n{\nname = ModuleGroundExperiment\n}\n", "ground"),
                Part("MODULE\n{\nname = DMSIGINT\n}\n", "sigint"),
                Part(PodModules, "pod"),
            };
            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: science: 2 part(s) got Module_ScienceExperiment (1 generated, 1 identical, 0 closest); " +
                                              "report storage on 3 part(s) (2 container(s)); merged: 0; skipped: evaReport 1; " +
                                              "not reproduced: evaOnlyStorage 1, rerunnable 1, storageRange 2; " +
                                              "not imported: ModuleScienceLab 1, ModuleScienceConverter 1, ModuleTestSubject 1, ground science 1, specialised DMagic 1");
        }

        // ------------------------------------------------------------------ census

        /// <summary>Every installed PART (raw cfg, ModuleManager patches not applied) through Redux's real registry with our science
        /// importers, the definitions built from the installed <c>EXPERIMENT_DEFINITION</c>s as the game glue builds them. The counts
        /// match the pure census (<c>ScienceCensusTests</c>); the summary line is printed. Also counted: the B9 subtypes that edit a
        /// science module (their DATA reaches the importer as an edited copy).</summary>
        [Test] public void Census_InstalledParts_ThroughTheRegistry()
        {
            var cfgs = InstalledKsp1.AllCfg();
            Assume.That(cfgs.Length > 0, "no installed KSP1 cfg");
            Assert.IsTrue(Arm());
            var parsed = new List<Ksp1ConfigNode>();
            foreach (var path in cfgs)
            {
                try { parsed.Add(Ksp1ConfigNode.Parse(File.ReadAllText(path))); }
                catch (Exception) { /* as the pure census: a file that does not parse is left out */ }
            }
            var partNodes = parsed.SelectMany(n => n.GetNodes("PART")).ToList();
            _defs = ScienceDefinitionBuilder.BuildAll(
                parsed.SelectMany(n => n.GetNodes("EXPERIMENT_DEFINITION")).Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)),
                partNodes.Select(Ksp1NodeAdapter.Modules), AnimationSettings.Defaults, null);
            ScienceImporter.ImportedSource = () => partNodes;

            var names = new HashSet<string>(ScienceMapping.ExperimentModuleNames.Concat(new[] { ScienceMapping.ContainerModule }), StringComparer.OrdinalIgnoreCase);
            var registry = new Ksp1PartModuleImporterRegistry();
            int withModule = 0, storage = 0, failed = 0, b9Subtypes = 0, b9Blocks = 0;
            foreach (var part in partNodes)
            {
                var context = Context(part);
                foreach (var m in part.GetNodes("MODULE").Where(x => names.Contains(x.GetValue("name")?.Trim() ?? "")))
                    registry.TryImport(m, context, out _);
                if (context.ModuleData.OfType<Data_ScienceExperiment>().Any()) withModule++;
                if (context.PartData.HasReportStorage) storage++;
                var subtypes = part.GetNodes("MODULE").Where(m => string.Equals(m.GetValue("name"), "ModuleB9PartSwitch", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(m => m.GetNodes("SUBTYPE")).ToList();
                foreach (var st in subtypes)
                {
                    var blocks = st.GetNodes("MODULE").Count(b => names.Contains(b.GetNode("IDENTIFIER")?.GetValue("name")?.Trim() ?? ""));
                    if (blocks == 0) continue;
                    b9Subtypes++;
                    b9Blocks += blocks;
                }
            }
            failed = _lines.Count(l => l.Contains(" not converted (Redux drops it"));
            NativeModules.LogImportSummaryOnce();
            var summary = _lines.Single(l => l.StartsWith("[KSP1PartMod] native: science: "));
            TestContext.WriteLine(summary);
            TestContext.WriteLine($"{withModule} part(s) with Module_ScienceExperiment, {storage} with report storage, {failed} failure(s); " +
                                  $"B9 subtypes editing a science module: {b9Subtypes} ({b9Blocks} MODULE block(s))");
            Assert.AreEqual(0, failed, string.Join("\n", _lines.Where(l => l.Contains("not converted"))));
            Assert.AreEqual(286, withModule, "the pure census: 286 parts get Module_ScienceExperiment");
            StringAssert.StartsWith("[KSP1PartMod] native: science: 286 part(s) got Module_ScienceExperiment", summary);
            // Counted on 2026-10-28 (stock ScienceDefs.cfg installed): 286 with the module + 26 container-only parts.
            Assert.AreEqual(312, storage);
            StringAssert.Contains("report storage on 312 part(s) (74 container(s)); merged: 72;", summary);
            // The installed IDENTIFIER blocks that name a science module belong to ModuleB9AssignUiGroups (UI groups), not to subtypes.
            Assert.AreEqual(0, b9Subtypes, "no installed B9 subtype edits a science module");
        }
    }
}
