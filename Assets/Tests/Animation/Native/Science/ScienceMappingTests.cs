using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="ScienceMapping"/>: the experiment and container modules of a part into one <c>Module_ScienceExperiment</c>
    /// (science plan (h)). Cfg text in the shape of the stock and BDB files, names and titles invented.</summary>
    public class ScienceMappingTests
    {
        private const string Pod = @"
PART
{
    name = testPod
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
        resetActionName = Discard
        reviewActionName = Review
        useStaging = False
        useActionGroups = True
        hideUIwhenUnavailable = True
        rerunnable = True
        xmitDataScalar = 1.0
        usageReqMaskInternal = 5
        usageReqMaskExternal = -1
    }
    MODULE
    {
        name = ModuleScienceContainer
        reviewActionName = Review
        storeActionName = Store
        evaOnlyStorage = True
        storageRange = 1.3
    }
}";

        private const string Goo = @"
PART
{
    name = testGoo
    MODULE
    {
        name = ModuleAnimateGeneric
        animationName = Deploy
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = mysteryGoo
        experimentActionName = Observe
        rerunnable = False
        resettable = True
        resettableOnEVA = True
        xmitDataScalar = 0.3
        usageReqMaskInternal = 1
        FxModules = 0
    }
}";

        private const string Bdb = @"
PART
{
    name = testBdbProbe
    MODULE
    {
        name = DMModuleScienceAnimateGeneric
        experimentID = bd_invented1
        animationName = Open
        experimentAnimation = true
        experimentWaitForAnimation = true
        waitForAnimationTime = 2.5
        keepDeployedMode = 2
        xmitDataScalar = 1
        usageReqMaskInternal = 1
        resourceExpCost = 0.5
    }
    MODULE
    {
        name = DMModuleScienceAnimateGeneric
        experimentID = bd_invented2
        animationName = Boom
        experimentAnimation = true
        experimentWaitForAnimation = false
        xmitDataScalar = 0.5
        sitMask = 48
        bioMask = 16
    }
}";

        private static IReadOnlyList<IKsp1Node> Modules(string part) => CfgText.Parse(part).GetNodes("PART").Single().GetNodes("MODULE").ToList();

        private static ScienceDefinitionModel Definition(string id, int sit = 63, int bio = 3, double x = 1) =>
            ScienceDefinitionBuilder.From(CfgText.Parse($@"
EXPERIMENT_DEFINITION
{{
    id = {id}
    title = Invented {id}
    baseValue = 10
    situationMask = {sit}
    biomeMask = {bio}
}}").GetNodes("EXPERIMENT_DEFINITION").Single(), new DefinitionOverrides(x, null, null), AnimationSettings.Defaults, null);

        private static Dictionary<string, ScienceDefinitionModel> Defs(params ScienceDefinitionModel[] d) => d.ToDictionary(x => x.Key);

        private static IKsp1Node DefNode(string id, int sit = 63, int bio = 3, string baseValue = "10") =>
            CfgText.Parse($"EXPERIMENT_DEFINITION\n{{\nid = {id}\ntitle = Invented {id}\nbaseValue = {baseValue}\nsituationMask = {sit}\nbiomeMask = {bio}\n}}")
                .GetNodes("EXPERIMENT_DEFINITION").Single();

        /// <summary>The definitions as the game glue builds them: the base ones and one per DMagic mask pair of the part.</summary>
        private static Dictionary<string, ScienceDefinitionModel> AllDefs(string part, params string[] ids) =>
            ScienceDefinitionBuilder.BuildAll(ids.Select(i => DefNode(i)), new[] { Modules(part) }, AnimationSettings.Defaults, null);

        private static SciencePart Resolve(string part, Dictionary<string, ScienceDefinitionModel> defs = null, string json = "{}") =>
            ScienceMapping.Resolve(Modules(part), defs ?? new Dictionary<string, ScienceDefinitionModel>(), NativeExperiments.Redux26w42c,
                AnimationSettings.Parse(json, out _));

        [Test] public void StockPod_CrewReport_CrewObservation_Crew1_Storage()
        {
            var p = Resolve(Pod);
            Assert.IsTrue(p.HasModule);
            var c = p.Configurations.Single();
            Assert.AreEqual("CrewObservation", c.ExperimentDefinitionId);
            Assert.AreEqual(ScienceResolutionKind.Identical, c.Kind);
            Assert.AreEqual(1, c.CrewRequired, "usageReqMaskInternal 5: crew in the part");
            Assert.AreEqual(0, c.TimeToComplete, 1e-12);
            Assert.IsFalse(c.DenyInterruption);
            Assert.IsFalse(c.ContextualExperiment);
            Assert.AreEqual("", c.DeployedBoolAnimatorParamName);
            Assert.AreEqual("", c.CFXName);
            Assert.AreEqual(1, c.ModuleIndex);
            Assert.IsFalse(c.Clip.HasSource);
            Assert.IsTrue(p.NotifyOnCompletion);
            Assert.IsTrue(p.HasContainer);
            Assert.IsTrue(p.ReportStorage);
            Assert.AreEqual(1, p.CrewRequired);
            Assert.AreEqual("crewReport", p.Resolutions.Single().Ksp1Id);
            Assert.IsTrue(p.NotReproduced.Contains("evaOnlyStorage"), string.Join(", ", p.NotReproduced));
        }

        [Test] public void Goo_NoDefinition_ClosestEnvironmentSurvey_FxModules()
        {
            var p = Resolve(Goo);
            var c = p.Configurations.Single();
            Assert.AreEqual("EnvironmentSurvey", c.ExperimentDefinitionId);
            Assert.AreEqual(ScienceResolutionKind.Closest, c.Kind);
            Assert.AreEqual(0, c.CrewRequired);
            CollectionAssert.AreEqual(new[] { 0 }, c.Clip.FxModules.ToArray());
            Assert.AreEqual(-1, c.Clip.ToggleModuleIndex);
            Assert.IsFalse(c.Waits);
            Assert.IsTrue(p.ReportStorage, "an instrument stores, as in KSP1");
            Assert.IsTrue(p.Notes.Any(n => n.Contains("science_fallback_ids")), string.Join("\n", p.Notes));
            CollectionAssert.Contains(p.NotReproduced, "rerunnable");
            CollectionAssert.Contains(p.NotReproduced, "resettable");
        }

        [Test] public void Goo_WithADefinition_Generated()
        {
            var p = Resolve(Goo, Defs(Definition("mysteryGoo", x: 0.3)));
            var c = p.Configurations.Single();
            Assert.AreEqual("KSP1_mysteryGoo", c.ExperimentDefinitionId);
            Assert.AreEqual(ScienceResolutionKind.Generated, c.Kind);

            var off = Resolve(Goo, Defs(Definition("mysteryGoo")), "{\"science_generate_definitions\": false}");
            Assert.AreEqual("EnvironmentSurvey", off.Configurations.Single().ExperimentDefinitionId, "closest from the definition's mask (63, Both)");
            Assert.AreEqual(ScienceResolutionKind.Closest, off.Configurations.Single().Kind);
        }

        [Test] public void Bdb_TwoDMagicGenericModules_TwoConfigurations_Waits()
        {
            var p = Resolve(Bdb, AllDefs(Bdb, "bd_invented1", "bd_invented2"));
            Assert.AreEqual(2, p.Configurations.Count);
            var a = p.Configurations[0];
            var b = p.Configurations[1];
            Assert.AreEqual("KSP1_bd_invented1", a.ExperimentDefinitionId);
            Assert.AreEqual("KSP1_bd_invented2_s48_b16", b.ExperimentDefinitionId, "its own masks: its own definition");
            Assert.AreEqual(ScienceResolutionKind.Generated, b.Kind);
            Assert.AreEqual(0, a.Clip.ToggleModuleIndex);
            Assert.AreEqual(1, b.Clip.ToggleModuleIndex);
            Assert.IsTrue(a.Clip.ExperimentAnimation);
            Assert.IsTrue(a.Clip.WaitForAnimation);
            Assert.AreEqual(2.5, a.Clip.WaitForAnimationTime, 1e-12);
            Assert.IsTrue(a.Waits);
            Assert.AreEqual(2.5, a.TimeToComplete, 1e-12);
            Assert.IsFalse(b.Waits);
            Assert.AreEqual(0, b.TimeToComplete, 1e-12);
            CollectionAssert.Contains(p.NotReproduced, "keepDeployedMode");
            CollectionAssert.Contains(p.NotReproduced, "resourceExpCost");
            CollectionAssert.IsEmpty(a.Clip.FxModules);

            var noWait = Resolve(Bdb, Defs(Definition("bd_invented1"), Definition("bd_invented2")), "{\"science_wait_for_animation\": false}");
            Assert.IsFalse(noWait.Configurations[0].Waits);
            Assert.AreEqual(0, noWait.Configurations[0].TimeToComplete, 1e-12);
            Assert.IsFalse(Resolve(Bdb, Defs(Definition("bd_invented1")), "{\"enabled\": false}").Configurations[0].Waits, "animations off");
            Assert.IsFalse(Resolve(Bdb, Defs(Definition("bd_invented1")), "{\"science_deploy_animations\": false}").Configurations[0].Waits);
        }

        [Test] public void DMagicMaskOverride_NeverTheBaseDefinition()
        {
            var defs = AllDefs(Bdb, "bd_invented2");
            Assert.IsTrue(defs.ContainsKey("bd_invented2_s48_b16"));
            Assert.AreEqual("LowOrbit+R, HighOrbit", Ksp1Situations.Labels(defs["bd_invented2_s48_b16"].Locations));
            Assert.AreEqual("Landed+R, Splashed+R, Atmosphere, LowOrbit, HighOrbit", Ksp1Situations.Labels(defs["bd_invented2"].Locations), "the base keeps its own");

            // Without its own definition (the glue did not build it), the module takes the closest match by its own mask.
            var p = Resolve(Bdb, Defs(Definition("bd_invented1"), Definition("bd_invented2")));
            var b = p.Configurations[1];
            Assert.AreEqual(ScienceResolutionKind.Closest, b.Kind);
            Assert.AreEqual("OrbitalSurvey", b.ExperimentDefinitionId, "sitMask 48, x 0.5 (Both)");
            Assert.IsTrue(p.Notes.Any(n => n.Contains("bd_invented2_s48_b16")), string.Join("\n", p.Notes));
        }

        [Test] public void DefinitionNotBuilt_ClosestFromItsMask_ThenTheTable()
        {
            var noValue = Defs(ScienceDefinitionBuilder.From(DefNode("mysteryGoo", 32, 0, "0"), null, AnimationSettings.Defaults, null));
            Assert.IsFalse(noValue["mysteryGoo"].IsBuilt);
            var p = Resolve(Goo, noValue);
            Assert.AreEqual(ScienceResolutionKind.Closest, p.Configurations.Single().Kind);
            Assert.AreEqual("OrbitalSurvey", p.Configurations.Single().ExperimentDefinitionId, "situationMask 32 of the definition, not the table");

            var noSituation = Defs(ScienceDefinitionBuilder.From(DefNode("mysteryGoo", 0, 0, "0"), null, AnimationSettings.Defaults, null));
            var t = Resolve(Goo, noSituation);
            Assert.AreEqual("EnvironmentSurvey", t.Configurations.Single().ExperimentDefinitionId, "nothing matches its mask: the table");
            Assert.IsTrue(t.Notes.Any(n => n.Contains("science_fallback_ids")), string.Join("\n", t.Notes));

            var neither = Resolve(Goo.Replace("mysteryGoo", "bd_inventedX"),
                Defs(ScienceDefinitionBuilder.From(DefNode("bd_inventedX", 0, 0, "0"), null, AnimationSettings.Defaults, null)));
            Assert.AreEqual(ScienceSkipReason.NoCounterpart, neither.Resolutions.Single().SkipReason);
        }

        [Test] public void QuietUiKeys_NotedBehaviourKeys()
        {
            var p = Resolve(@"
PART
{
    MODULE
    {
        name = DMModuleScienceAnimateGeneric
        experimentID = mysteryGoo
        xmitDataScalar = 0.3
        planetFailMessage = x
        hideFxModuleUI = true
        extraResultString = x
        storeFailMessage = x
        startEventGUIName = x
        someActionName = x
        externalDeploy = true
        labDataBoost = 0.5
        resetLevel = 1
        keepDeployedMode = 1
        planetaryMask = 1
        excludeAtmosphere = true
        asteroidReports = true
        requiredParts = x
        experimentsLimit = 1
        scienceValueRatio = 1
        totalScienceLevel = 1
        resourceExpCost = 0
    }
}");
            CollectionAssert.AreEquivalent(new[] { "resettable", "keepDeployedMode", "planetaryMask", "excludeAtmosphere", "asteroidReports",
                "requiredParts", "experimentsLimit", "scienceValueRatio", "totalScienceLevel" }, p.NotReproduced);
        }

        [Test] public void ResetLevel_NotNoted_WhenTheModuleIsNotResettable()
        {
            var p = Resolve(@"
PART
{
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = mysteryGoo
        resettable = False
        resetLevel = 1
    }
}");
            CollectionAssert.DoesNotContain(p.NotReproduced, "resettable", string.Join("\n", p.Notes));
            Assert.IsFalse(p.Notes.Any(n => n.Contains("resettable = True")), string.Join("\n", p.Notes));
        }

        [Test] public void VariantNotBuilt_BaseBuilt_ClosestNoteNamesTheVariant()
        {
            var baseDef = ScienceDefinitionBuilder.From(DefNode("bd_invented2"), null, AnimationSettings.Defaults, null);
            var variant = ScienceDefinitionBuilder.From(DefNode("bd_invented2", baseValue: "0"), new DefinitionOverrides(0.5, 48, 16),
                AnimationSettings.Defaults, null);
            Assert.IsTrue(baseDef.IsBuilt);
            Assert.IsFalse(variant.IsBuilt);
            Assert.AreEqual("bd_invented2_s48_b16", variant.Key);
            var p = Resolve(Bdb, Defs(Definition("bd_invented1"), baseDef, variant));
            var b = p.Configurations[1];
            Assert.AreEqual(ScienceResolutionKind.Closest, b.Kind);
            Assert.AreEqual("OrbitalSurvey", b.ExperimentDefinitionId);
            var note = p.Notes.Single(n => n.StartsWith("bd_invented2: the closest", System.StringComparison.Ordinal));
            StringAssert.Contains("definition bd_invented2_s48_b16 not generated: " + ScienceDefinitionBuilder.SkipNoBaseValue, note);
            StringAssert.DoesNotContain("science_generate_definitions off", note);

            var off = Resolve(Goo, Defs(Definition("mysteryGoo")), "{\"science_generate_definitions\": false}");
            Assert.IsTrue(off.Notes.Any(n => n.Contains("(science_generate_definitions off)")), string.Join("\n", off.Notes));
        }

        [Test] public void WaitTime_AtLeastATenth()
        {
            var p = Resolve(Bdb.Replace("waitForAnimationTime = 2.5", "waitForAnimationTime = 0"), Defs(Definition("bd_invented1")));
            Assert.AreEqual(0.1, p.Configurations[0].TimeToComplete, 1e-12);
        }

        [Test] public void Bdb_NoDefinitionForTheSecond_UsesItsSitMaskForNothing_FallsToTheTable()
        {
            var p = Resolve(Bdb, Defs(Definition("bd_invented1")));
            Assert.AreEqual(1, p.Configurations.Count, "bd_invented2: no definition, no table entry");
            var skipped = p.Resolutions.Single(r => r.Ksp1Id == "bd_invented2");
            Assert.AreEqual(ScienceResolutionKind.Skipped, skipped.Kind);
            Assert.AreEqual(ScienceSkipReason.NoDefinitionNoCounterpart, skipped.SkipReason);
        }

        [Test] public void RepeatedKsp2Id_KeptOnce()
        {
            const string twoInstruments = @"
PART
{
    name = testTwo
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = mysteryGoo
        xmitDataScalar = 0.3
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = temperatureScan
        xmitDataScalar = 0.5
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = temperatureScan
        xmitDataScalar = 0.5
    }
}";
            var p = Resolve(twoInstruments);
            Assert.AreEqual(1, p.Configurations.Count, "both go to EnvironmentSurvey");
            Assert.AreEqual(2, p.Resolutions.Count, "unique per KSP1 id");
            Assert.IsTrue(p.Resolutions[1].DuplicateKsp2);
            Assert.AreEqual(1, p.DuplicateKsp2);
            Assert.AreEqual(1, p.MergedModules, "the second temperatureScan module");
            Assert.IsTrue(p.Notes.Any(n => n.Contains("score once per location together")), string.Join("\n", p.Notes));
        }

        [Test] public void SkipIds_DefaultAndCustom()
        {
            const string eva = @"
PART
{
    name = testKerbal
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = evaReport
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = ROCScience
    }
}";
            var p = Resolve(eva);
            Assert.IsFalse(p.HasModule);
            Assert.AreEqual(ScienceSkipReason.EvaKerbal, p.Resolutions[0].SkipReason);
            Assert.AreEqual(ScienceSkipReason.NoCounterpart, p.Resolutions[1].SkipReason);
            Assert.IsFalse(p.ReportStorage, "nothing resolved, no container");

            var custom = Resolve(Pod, null, "{\"science_skip_ids\": [\"crewReport\"]}");
            Assert.IsFalse(custom.HasModule);
            Assert.AreEqual(ScienceSkipReason.NoCounterpart, custom.Resolutions.Single().SkipReason);
            Assert.IsTrue(custom.ReportStorage, "the container still stores");
        }

        [Test] public void GooLabLike_NoDefinitionNoTable_Skipped()
        {
            var p = Resolve(Goo.Replace("experimentID = mysteryGoo", "experimentID = bd_InventedLab"));
            Assert.IsFalse(p.HasModule);
            Assert.AreEqual(ScienceSkipReason.NoDefinitionNoCounterpart, p.Resolutions.Single().SkipReason);
            Assert.IsFalse(p.ReportStorage);
        }

        [Test] public void ContainerOnly_Storage_NoModule()
        {
            var p = Resolve(@"
PART
{
    name = testBox
    MODULE
    {
        name = ModuleScienceContainer
        storageRange = 2
        allowRepeatedSubjects = True
    }
}");
            Assert.IsFalse(p.HasModule);
            Assert.IsTrue(p.HasContainer);
            Assert.IsTrue(p.ReportStorage);
            CollectionAssert.IsEmpty(p.Resolutions);
            Assert.IsFalse(Resolve(@"
PART
{
    name = testBox
    MODULE
    {
        name = ModuleScienceContainer
    }
}", null, "{\"science_report_storage\": false}").ReportStorage);
        }

        [Test] public void Ksp1ScienceOff_Nothing()
        {
            var p = Resolve(Pod, null, "{\"ksp1_science\": false}");
            Assert.IsFalse(p.HasModule);
            Assert.IsFalse(p.ReportStorage);
            CollectionAssert.IsEmpty(p.Resolutions);
            Assert.IsTrue(p.Notes.Any(n => n.Contains("ksp1_science")));
        }

        [Test] public void NativeIdsReplaced_And_NativeMissing()
        {
            var custom = Resolve(Goo, null, "{\"science_native_ids\": {\"mysteryGoo\": \"MarineSurvey\"}}");
            Assert.AreEqual("MarineSurvey", custom.Configurations.Single().ExperimentDefinitionId);
            Assert.AreEqual(ScienceResolutionKind.Identical, custom.Configurations.Single().Kind);

            var live = new NativeExperiments(NativeExperiments.Redux26w42c.All.Where(e => e.Id != "CrewObservation"));
            var p = ScienceMapping.Resolve(Modules(Pod), new Dictionary<string, ScienceDefinitionModel>(), live, AnimationSettings.Defaults);
            Assert.IsFalse(p.HasModule, "CrewObservation absent and crewReport has no definition or table entry");
        }

        [Test] public void ScientistBit_Noted_CrewStillOne()
        {
            var p = Resolve(Goo.Replace("usageReqMaskInternal = 1", "usageReqMaskInternal = 9"));
            Assert.AreEqual(1, p.Configurations.Single().CrewRequired);
            Assert.IsTrue(p.Notes.Any(n => n.Contains("scientist")), string.Join("\n", p.Notes));
        }

        [Test] public void DMagicAnimate_NoClipEntry_Noted()
        {
            var p = Resolve(Bdb.Replace("DMModuleScienceAnimateGeneric", "DMModuleScienceAnimate"), Defs(Definition("bd_invented1"), Definition("bd_invented2")));
            Assert.AreEqual(2, p.Configurations.Count);
            Assert.IsFalse(p.Configurations[0].Clip.HasSource);
            Assert.IsFalse(p.Configurations[0].Waits, "no clip to wait for");
            Assert.IsTrue(p.Notes.Any(n => n.Contains("DMModuleScienceAnimate")), string.Join("\n", p.Notes));
        }

        [Test] public void OutOfScope_Categories()
        {
            Assert.AreEqual("ModuleScienceLab", ScienceMapping.OutOfScope("ModuleScienceLab"));
            Assert.AreEqual("ModuleScienceConverter", ScienceMapping.OutOfScope("ModuleScienceConverter"));
            Assert.AreEqual("ModuleTestSubject", ScienceMapping.OutOfScope("ModuleTestSubject"));
            Assert.AreEqual("ground science", ScienceMapping.OutOfScope("ModuleGroundExperiment"));
            Assert.AreEqual("ground science", ScienceMapping.OutOfScope("ModuleGroundSciencePart"));
            Assert.AreEqual("specialised DMagic", ScienceMapping.OutOfScope("DMSeismicSensor"));
            Assert.AreEqual("specialised DMagic", ScienceMapping.OutOfScope("DMUniversalStorageScience"));
            Assert.IsNull(ScienceMapping.OutOfScope("DMModuleScienceAnimateGeneric"));
            Assert.IsNull(ScienceMapping.OutOfScope("ModuleScienceExperiment"));
        }
    }
}
