using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using Newtonsoft.Json;
using NUnit.Framework;
using Redux.Ecs.CompilerServices;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.13.1, against the referenced Ksp2Redux.dll (26w42c): Redux's KSP1 engine data has no emissive curve, it comes
    /// back null from the part JSON and Redux's blob builder throws on it; our fix fills it before the JSON is written.</summary>
    public class EngineCurveFixTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            EngineCurveFix.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            EngineCurveFix.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(EngineCurveFix.HarmonyId).UnpatchAll(EngineCurveFix.HarmonyId);
            EngineCurveFix.Reset();
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

        /// <summary>As <c>Ksp1PartModuleImporterRegistry.EngineImporter.TryImport</c> builds it: one mode, no emissive curve.</summary>
        private static Data_Engine ReduxEngine()
        {
            var mode = new Data_Engine.EngineMode { engineID = "Engine", maxThrust = 20f, thrustVectorTransformName = "thrustTransform" };
            mode.HeatAtmosphereCurve = new FloatCurve();
            mode.HeatAtmosphereCurve.Add(0f, 0f, 0f, 0f);
            mode.HeatAtmosphereCurve.Add(1f, 0f, 0f, 0f);
            var data = new Data_Engine { engineModes = new[] { mode } };
            data.activeEngineMode.SetValue(mode.engineID);
            return data;
        }

        private static PartCore Core(ModuleData data) => new PartCore
        {
            version = 0.3, useExternal = false, data = new PartData { partName = "testEngine" }, modules = new List<ModuleData> { data },
        };

        private static Data_Engine RoundTrip(PartCore core) =>
            IOProvider.FromJson<PartCore>(IOProvider.ToJson(core)).modules.OfType<Data_Engine>().Single();

        [Test] public void WithoutTheFix_TheCurveComesBackNull_AndReduxThrows() => WithGameSettings(() =>
        {
            var back = RoundTrip(Core(ReduxEngine()));
            Assert.IsNull(back.EmissiveTemperatureCurve, "the bug: Redux's importer never sets it");
            Assert.Throws<NullReferenceException>(() => BlobFloatCurveBuilder.ResolveMaxTime(back.EmissiveTemperatureCurve));
        });

        [Test] public void TheFix_SurvivesTheJsonRoundTrip_AndReduxReadsIt() => WithGameSettings(() =>
        {
            var core = Core(ReduxEngine());
            var tally = new EngineCurveLines.Tally();
            Assert.AreEqual(1, EngineCurveFix.Fix(core, tally));
            var back = RoundTrip(core);
            Assert.IsNotNull(back.EmissiveTemperatureCurve);
            Assert.AreEqual(1f, BlobFloatCurveBuilder.ResolveMaxTime(back.EmissiveTemperatureCurve), 1e-6f);
            Assert.AreEqual(0f, back.EmissiveTemperatureCurve.Evaluate(0.5f), 1e-6f);
            Assert.AreEqual("[KSP1PartMod] engines: emissive curve filled on 1 engine module(s) of 1 part(s); other null curves: none; intake mach curve filled on 0 intake(s) of 0 part(s) (0 from the cfg, 0 default)",
                EngineCurveLines.Summary(tally));
        });

        [Test] public void AnExistingCurveIsKept_AndOtherNullCurvesAreOnlyCounted()
        {
            var engine = ReduxEngine();
            var own = new FloatCurve();
            own.Add(0f, 2f);
            engine.EmissiveTemperatureCurve = own;
            engine.engineModes[0].thrustCurve = null;
            var lit = new Data_LitPart();
            var core = Core(engine);
            core.modules.Add(lit);
            var tally = new EngineCurveLines.Tally();
            Assert.AreEqual(0, EngineCurveFix.Fix(core, tally));
            Assert.AreSame(own, engine.EmissiveTemperatureCurve);
            Assert.IsNull(engine.engineModes[0].thrustCurve, "diagnostic only");
            StringAssert.Contains("Data_Engine.EngineMode.thrustCurve x1", EngineCurveLines.Summary(tally));
            StringAssert.StartsWith("[KSP1PartMod] engines: emissive curve filled on 0 engine module(s) of 0 part(s)", EngineCurveLines.Summary(tally));
        }

        [Test] public void Members_Resolve() => Assert.IsNotNull(EngineCurveFix.FindBuildConfiguredPrefab());

        [Test] public void Switch_Off_ArmsNothing()
        {
            Assert.IsFalse(EngineCurveFix.TryApply(new Harmony(EngineCurveFix.HarmonyId),
                AnimationSettings.Parse("{\"fix_engine_emissive_curve\": false, \"fix_intake_mach_curve\": false}", out _)));
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(EngineCurveFix.HarmonyId) == true));
        }

        [Test] public void EngineSwitchOff_IntakeSwitchOn_StillArms()
        {
            Assert.IsTrue(EngineCurveFix.TryApply(new Harmony(EngineCurveFix.HarmonyId),
                AnimationSettings.Parse("{\"fix_engine_emissive_curve\": false}", out _)));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: emissive curve fix off, intake mach curve fix armed (fix_intake_mach_curve)");
        }

        [Test] public void RealTranslation_PutsTheCurveInTheRawJson() => WithGameSettings(() =>
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Assert.IsTrue(EngineCurveFix.TryApply(new Harmony(EngineCurveFix.HarmonyId), AnimationSettings.Defaults));
            Ksp1ImportedPartRegistry.Clear();
            try
            {
                var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
                var node = Ksp1ConfigNode.Parse("PART\n{\nname = curveTestEngine\nmodule = Part\nmass = 0.5\ncategory = Engine\n" +
                                                "MODULE\n{\nname = ModuleEngines\nthrustVectorTransformName = thrustTransform\nmaxThrust = 20\n" +
                                                "PROPELLANT\n{\nname = LiquidFuel\nratio = 0.9\n}\nPROPELLANT\n{\nname = Oxidizer\nratio = 1.1\n}\n" +
                                                "atmosphereCurve\n{\nkey = 0 320\nkey = 1 250\n}\n}\n}\n");
                var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/engine.cfg", "Test", node);
                var part = node.GetNodes("PART").First();
                Ksp1ImportedPartRegistry.BeginImport("engine-curve-test");
                Assert.IsTrue(new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out _, out var rawJson));
                var engine = IOProvider.FromJson<PartCore>(rawJson).modules.OfType<Data_Engine>().SingleOrDefault();
                Assert.IsNotNull(engine, "Redux imported the KSP1 engine");
                Assert.IsNotNull(engine.EmissiveTemperatureCurve);
                Assert.DoesNotThrow(() => BlobFloatCurveBuilder.ResolveMaxTime(engine.EmissiveTemperatureCurve));
                EngineCurveFix.AfterImportComplete(); // the postfix on MarkImportComplete (not run: it renders icons)
                CollectionAssert.Contains(_lines, "[KSP1PartMod] engines: emissive curve filled on 1 engine module(s) of 1 part(s); other null curves: none; intake mach curve filled on 0 intake(s) of 0 part(s) (0 from the cfg, 0 default)");
            }
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
        });
        // ---------------------------------------------------------------- 0.15.1: intake mach curve

        /// <summary>As <c>ResourceIntakeImporter.TryImport</c> builds it: resource, transform and area, no mach curve.</summary>
        private static Data_ResourceIntake ReduxIntake() =>
            new Data_ResourceIntake { resourceName = "IntakeAir", intakeTransformName = "Intake", area = 0.006f };

        /// <summary><c>GetMachEntry</c> itself cannot run in EditMode: its localized format string is null there, and
        /// <c>string.Format</c> throws before the curve is read. The tooltip's own call, <c>machCurve.Evaluate(mach)</c> for
        /// Mach 0, 1 and 5, is checked instead.</summary>
        [Test] public void Intake_TheFix_LetsTheTooltipEvaluateMach0_1_5()
        {
            Assert.IsNull(ReduxIntake().machCurve, "the bug: Redux's importer never sets it");
            var data = ReduxIntake();
            var core = Core(data);
            var tally = new EngineCurveLines.Tally();
            EngineCurveFix.Fix(core, tally);
            Assert.IsNotNull(data.machCurve);
            foreach (var mach in new[] { 0, 1, 5 })
            {
                Assert.DoesNotThrow(() => data.machCurve.Evaluate(mach), "Mach " + mach);
                Assert.AreEqual(1f, data.machCurve.Evaluate(mach), 1e-6f, "KSP2's default: full intake at every speed");
            }
            StringAssert.EndsWith("intake mach curve filled on 1 intake(s) of 1 part(s) (0 from the cfg, 1 default)", EngineCurveLines.Summary(tally));
        }

        [Test] public void Intake_ExistingCurveKept_SwitchOffLeavesItNull()
        {
            var own = ReduxIntake();
            own.machCurve = new FloatCurve();
            own.machCurve.Add(0f, 0.5f);
            var keep = own.machCurve;
            EngineCurveFix.Fix(Core(own), null);
            Assert.AreSame(keep, own.machCurve);

            var off = ReduxIntake();
            EngineCurveFix.Fix(Core(off), null, null, true, false);
            Assert.IsNull(off.machCurve);
        }

        [Test] public void Intake_RealTranslation_PutsTheCfgCurveInTheRawJson() => WithGameSettings(() =>
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Assert.IsTrue(EngineCurveFix.TryApply(new Harmony(EngineCurveFix.HarmonyId), AnimationSettings.Defaults));
            Ksp1ImportedPartRegistry.Clear();
            try
            {
                var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
                var node = Ksp1ConfigNode.Parse("PART\n{\nname = curveTestIntake\nmodule = Part\nmass = 0.01\ncategory = Aero\n" +
                                                "MODULE\n{\nname = ModuleResourceIntake\nresourceName = IntakeAir\nintakeTransformName = Intake\narea = 0.006\n" +
                                                "machCurve\n{\nkey = 1 1 0 0\nkey = 1.5 0.9 -0.4 -0.4\nkey = 3 0.5\n}\n}\n}\n");
                var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/intake.cfg", "Test", node);
                var part = node.GetNodes("PART").First();
                Ksp1ImportedPartRegistry.BeginImport("intake-curve-test");
                Assert.IsTrue(new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out _, out var rawJson));
                var intake = IOProvider.FromJson<PartCore>(rawJson).modules.OfType<Data_ResourceIntake>().SingleOrDefault();
                Assert.IsNotNull(intake, "Redux imported the KSP1 intake");
                Assert.IsNotNull(intake.machCurve, "the fix reached the raw JSON");
                Assert.AreEqual(1f, intake.machCurve.Evaluate(1f), 1e-4f);
                Assert.AreEqual(0.5f, intake.machCurve.Evaluate(3f), 1e-4f);
                Assert.AreEqual(0.5f, intake.machCurve.Evaluate(5f), 1e-4f, "clamped past the last key");
                EngineCurveFix.AfterImportComplete();
                Assert.IsTrue(_lines.Any(l => l.EndsWith("intake mach curve filled on 1 intake(s) of 1 part(s) (1 from the cfg, 0 default)")),
                    string.Join("\n", _lines));
            }
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
        });
    }
}
