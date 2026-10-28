using System.Linq;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="ScienceLines"/>: every exact line of the science plan, Tasks 3 to 5.</summary>
    public class ScienceLinesTests
    {
        // ---- Task 3

        [Test] public void DefinitionsRead()
        {
            Assert.AreEqual("[KSP1PartMod] science: definitions: read 55 EXPERIMENT_DEFINITION(s) from 55 of 1234 cfg file(s) under mods/KSP1 in 42 ms; 0 MM edit(s) of EXPERIMENT_DEFINITION not applied",
                ScienceLines.DefinitionsRead(55, 55, 1234, 42, 0));
        }

        [Test] public void Definition()
        {
            var d = ScienceDefinitionBuilder.From(CfgText.Parse(@"
EXPERIMENT_DEFINITION
{
    id = testProbe
    title = Invented Probe
    baseValue = 10
    dataScale = 2
    situationMask = 48
    biomeMask = 16
    RESULTS
    {
        default = Invented.
        KerbinSrfLandedShores = Invented shore.
    }
}").GetNodes("EXPERIMENT_DEFINITION").Single(), new DefinitionOverrides(0.3, null, null), AnimationSettings.Defaults, null);
            Assert.AreEqual("[KSP1PartMod] science: definition KSP1_testProbe (Invented Probe): Both data 3 sample 7 (baseValue 10 x 1, xmitDataScalar 0.3), 20 kbit, LowOrbit+R, HighOrbit, flavor 1/2",
                ScienceLines.Definition(d));
        }

        [Test] public void Queued_PatchManagerMissing()
        {
            Assert.AreEqual("[KSP1PartMod] science: definitions: 54 queued through Patch Manager; skipped: no baseValue 1, no mapped situation 2",
                ScienceLines.DefinitionsQueued(54, true, new[] { ("no baseValue", 1), ("no mapped situation", 2) }));
            Assert.AreEqual("[KSP1PartMod] science: definitions: 3 queued through the experiment store; skipped: none",
                ScienceLines.DefinitionsQueued(3, false, null));
            Assert.AreEqual("[KSP1PartMod] science: definitions: Patch Manager's C# science patching not found, definitions go to the experiment store",
                ScienceLines.PatchManagerMissing);
        }

        [Test] public void Localization_Store()
        {
            Assert.AreEqual("[KSP1PartMod] science: localization: 400 term(s) added (55 name(s), 180 flavor text(s))", ScienceLines.Localization(400, 55, 180));
            Assert.AreEqual("[KSP1PartMod] science: localization: ReduxLocalizer not found, generated experiments show no name", ScienceLines.LocalizationMissing);
            Assert.AreEqual("[KSP1PartMod] science: store: 55 of 55 KSP1 definition(s) present; KSP2 experiments: 2 (CrewObservation, ResourceScan)",
                ScienceLines.Store(55, 55, new[] { "CrewObservation", "ResourceScan" }));
            Assert.AreEqual("[KSP1PartMod] science: store: KSP1PartMod is off in this campaign, no KSP1 definition added", ScienceLines.StoreOff);
        }

        [Test] public void PatchManagerPartial_StoreWithTheModOff()
        {
            Assert.AreEqual("[KSP1PartMod] science: definitions: Patch Manager queued 12 of 55, the rest go to the experiment store",
                ScienceLines.PatchManagerPartial(12, 55));
            Assert.AreEqual("[KSP1PartMod] science: store: 0 of 55 KSP1 definition(s) present (KSP1PartMod off in this campaign: expected); " +
                            "KSP2 experiments: 1 (CrewObservation)",
                ScienceLines.Store(0, 55, new[] { "CrewObservation" }, modOff: true));
            Assert.AreEqual("[KSP1PartMod] science: store: 55 of 55 KSP1 definition(s) present; KSP2 experiments: 0 ()",
                ScienceLines.Store(55, 55, null, modOff: false));
        }

        // ---- Task 4

        [Test] public void ImporterPart_Skipped()
        {
            Assert.AreEqual("[KSP1PartMod] native: testPod: science -> Module_ScienceExperiment: crewReport -> CrewObservation (identical), mysteryGoo -> KSP1_mysteryGoo (generated), temperatureScan -> EnvironmentSurvey (closest); crew 1; report storage on",
                ScienceLines.Part("testPod", new[]
                {
                    ("crewReport", "CrewObservation", ScienceResolutionKind.Identical),
                    ("mysteryGoo", "KSP1_mysteryGoo", ScienceResolutionKind.Generated),
                    ("temperatureScan", "EnvironmentSurvey", ScienceResolutionKind.Closest),
                }, 1, true));
            Assert.AreEqual("[KSP1PartMod] native: kerbalEVA: science: evaReport skipped (EVA kerbal experiment)",
                ScienceLines.Skipped("kerbalEVA", "evaReport", ScienceSkipReason.EvaKerbal));
            Assert.AreEqual("[KSP1PartMod] native: arm: science: ROCScience skipped (no KSP2 counterpart)",
                ScienceLines.Skipped("arm", "ROCScience", ScienceSkipReason.NoCounterpart));
            Assert.AreEqual("[KSP1PartMod] native: lab: science: bd_GooLab skipped (no definition and no counterpart)",
                ScienceLines.Skipped("lab", "bd_GooLab", ScienceSkipReason.NoDefinitionNoCounterpart));
        }

        [Test] public void PartFromTheMapping()
        {
            var p = ScienceMapping.Resolve(CfgText.Parse(@"
PART
{
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = crewReport
        usageReqMaskInternal = 5
    }
}").GetNodes("PART").Single().GetNodes("MODULE").ToList(), new System.Collections.Generic.Dictionary<string, ScienceDefinitionModel>(),
                NativeExperiments.Redux26w42c, AnimationSettings.Defaults);
            Assert.AreEqual("[KSP1PartMod] native: p: science -> Module_ScienceExperiment: crewReport -> CrewObservation (identical); crew 1; report storage on",
                ScienceLines.Part("p", p));
        }

        [Test] public void ImporterSummary()
        {
            Assert.AreEqual("[KSP1PartMod] native: science: 286 part(s) got Module_ScienceExperiment (269 generated, 48 identical, 41 closest); report storage on 300 part(s) (74 container(s)); merged: 5; skipped: evaReport 2, ROCScience 11; not reproduced: rerunnable 20, keepDeployedMode 100; not imported: ModuleScienceLab 12, ModuleScienceConverter 12, ModuleTestSubject 247, ground science 3, specialised DMagic 0",
                ScienceLines.Summary(286, 269, 48, 41, 300, 74, 5, new[] { ("evaReport", 2), ("ROCScience", 11) },
                    new[] { ("rerunnable", 20), ("keepDeployedMode", 100) }, 12, 12, 247, 3, 0));
            StringAssert.Contains("skipped: none; not reproduced: none;", ScienceLines.Summary(0, 0, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0, 0));
        }

        // ---- Task 5

        [Test] public void Runtime()
        {
            Assert.AreEqual("[KSP1PartMod] science: armed: deploy animations on, wait for animation off", ScienceLines.Armed(true, false));
            Assert.AreEqual("[KSP1PartMod] science: Data_ScienceExperiment.PartIsDeployed not found, wait for animation off (Redux's module left as is)",
                ScienceLines.MemberNotFound("Data_ScienceExperiment.PartIsDeployed", "wait for animation"));
            Assert.AreEqual("[KSP1PartMod] science: GooExperiment: EnvironmentSurvey opens deploy:Deploy:0", ScienceLines.Opens("GooExperiment", "EnvironmentSurvey", "deploy:Deploy:0"));
            Assert.AreEqual("[KSP1PartMod] science: p: X has no clip (no FxModules)", ScienceLines.NoClip("p", "X", ScienceNoClip.NoFxModules));
            Assert.AreEqual("[KSP1PartMod] science: p: X has no clip (no entry)", ScienceLines.NoClip("p", "X", ScienceNoClip.NoEntry));
            Assert.AreEqual("[KSP1PartMod] science: p: X has no clip (entry gated by B9)", ScienceLines.NoClip("p", "X", ScienceNoClip.GatedByB9));
            Assert.AreEqual("[KSP1PartMod] science: p: X has no clip (animations off)", ScienceLines.NoClip("p", "X", ScienceNoClip.AnimationsOff));
            Assert.AreEqual("[KSP1PartMod] science: p: X has no clip (experimentAnimation false)", ScienceLines.NoClip("p", "X", ScienceNoClip.NoExperimentAnimation));
            Assert.AreEqual("[KSP1PartMod] science: p: KSP1_bd_x waits for science:Boom:0 (KSP2 deploy gate, 2.5 s)", ScienceLines.Waits("p", "KSP1_bd_x", "science:Boom:0", 2.5));
            Assert.AreEqual("[KSP1PartMod] science: p: report KSP1_bd_x stored at Kerbin_LowOrbit_Shores, value 12.5",
                ScienceLines.ReportStored("p", "KSP1_bd_x", "Kerbin_LowOrbit_Shores", 12.5));
            Assert.AreEqual("[KSP1PartMod] science: p: loaded with KSP1_bd_x running, the native trigger already fired", ScienceLines.LoadedRunning("p", "KSP1_bd_x"));
            Assert.AreEqual("[KSP1PartMod] science: patch failed, stopped for this session: boom", ScienceLines.PatchFailed("boom"));
        }
    }
}
