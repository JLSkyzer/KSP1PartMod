using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using Redux.Managers;
using Redux.Modules;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), converters and drills plan Task 3: the drill and scanner
    /// members resolve by name, the native reference is read once (and sets the converters' heat), and Redux's real
    /// <c>Ksp1PartModuleImporterRegistry</c>, built with our hook armed, imports in-memory stock-like drills and scanners as
    /// Redux's <c>Module_Mine</c> and <c>Module_ResourceScanner</c>, every rate through Redux's own <c>ConvertAmount</c>.</summary>
    public class DrillScannerImportTests
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
            new Harmony(OreRemap.HarmonyId).UnpatchAll(OreRemap.HarmonyId);
            NativeModules.Reset();
            OreRemap.Reset();
            OreMigration.Reset();
        }

        private static readonly (string Body, string Resource)[] Maps =
        {
            ("Kerbin", "CarbonRegolith"), ("Duna", "CarbonRegolith"), ("Kerbin", "Water"), ("Duna", "Uranium"),
        };

        /// <summary>Arms the Ore remap and the hook as the plugin does; the importers then read <paramref name="db"/> and the
        /// test's resource maps (the native parts: none, so the 26w42c fallbacks).</summary>
        private bool Arm(ResourceDefinitionDatabase db, string json = "{}", bool maps = true)
        {
            var settings = AnimationSettings.Parse(json, out _);
            OreRemap.Arm(new Harmony(OreRemap.HarmonyId), settings.OreRemapTarget);
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), settings);
            ConverterImporter.DatabaseSource = () => db;
            IsruReference.FormulaSource = () => Array.Empty<NativeIsruFormula>();
            if (maps) IsruReference.MapSource = () => Maps;
            return armed;
        }

        private static ResourceDefinitionDatabase Database()
        {
            var db = new ResourceDefinitionDatabase();
            foreach (var (name, mass) in new[]
                     {
                         ("ElectricCharge", 0.0), ("CarbonRegolith", 1.0), ("TestRocks", 0.05), ("Water", 1.0), ("Oxidizer", 0.005),
                     })
                db.AddOrUpdateDefinition(Ksp1ResourceMapper.ToResourceDefinition(
                    Ksp1ConfigNode.Parse("RESOURCE_DEFINITION\n{\nname = " + name + "\ndensity = " + mass.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n}\n")
                        .GetNodes("RESOURCE_DEFINITION").First()));
            return db;
        }

        private const string RadialDrillModules =
            "MODULE\n{\nname = ModuleResourceHarvester\nHarvesterType = 0\nEfficiency = 1.5\nResourceName = Ore\nImpactTransform = ImpactTransform\n" +
            "ImpactRange = 5.42\nAutoShutdown = true\nGeneratesHeat = true\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 15\n}\n" +
            "SpecialistEfficiencyFactor = 0.2\nEfficiencyBonus = 1\nThermalEfficiency\n{\n}\n}\n" +
            "MODULE\n{\nname = ModuleAsteroidDrill\nEfficiency = 5\nImpactRange = 5\nImpactTransform = ImpactTransform\n}\n" +
            "MODULE\n{\nname = ModuleCometDrill\nEfficiency = 5\nImpactRange = 5\nImpactTransform = ImpactTransform\n}\n";

        private const string Surveyor = "MODULE\n{\nname = ModuleOrbitalSurveyor\nScanTime = 5\nSciBonus = 10\n}\nMODULE\n{\nname = ModuleOrbitalScanner\n}\n";

        private static string ResourceScanner(bool unlock, string altitude = "1000", string resource = "Ore") =>
            "MODULE\n{\nname = ModuleResourceScanner\nScannerType = 0\nResourceName = " + resource + "\nMaxAbundanceAltitude = " + altitude +
            "\nRequiresUnlock = " + (unlock ? "true" : "false") + "\n}\n";

        private static Ksp1ConfigNode Part(string modules, string name) =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\n" + modules + "}\n").GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };

        private static List<Ksp1ConfigNode> Modules(Ksp1ConfigNode part, string name) =>
            part.GetNodes("MODULE").Where(n => n.GetValue("name") == name).ToList();

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_DrillAndScannerMembers()
        {
            var t = IsruAccess.Resolve();
            Assert.AreEqual("", string.Join(", ", t.MissingDrill.Concat(t.MissingScanner)));
            CollectionAssert.IsEmpty(HarvesterImporter.MissingMembers());
            CollectionAssert.IsEmpty(ScannerImporters.MissingMembers());
            Assert.AreEqual(typeof(Module_Mine), new Data_Mine().ModuleType);
            Assert.AreEqual(typeof(Module_ResourceScanner), new Data_ResourceScanner().ModuleType);
            Assert.AreEqual(0.9, new Data_Mine().AutoShutdownTemperatureRatio);
            Assert.IsNull(new Data_Mine().MiningFormulaDefinition, "no default formula: ours is the only one");
            foreach (var name in new[] { "vfxArmTopTransform", "vfxArmBottomTransform", "_dataDeployable" })
            {
                var f = AccessTools.Field(typeof(Module_Mine), name);
                Assert.IsNotNull(f, name + ": Task 4 sets it");
                Assert.IsFalse(f.IsPublic, name);
            }
            Assert.IsNotNull(AccessTools.Field(typeof(Module_ResourceScanner), "_dataDeployable"));
        }

        [Test] public void Arm_AddsTheDrillAndScannerImporters()
        {
            Assert.IsTrue(Arm(Database()));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, " +
                                              "ModuleAblator -> Module_Heatshield, ModuleResourceConverter -> Module_ResourceConverter, " +
                                              "ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, " +
                                              "ModuleResourceScanner -> Module_ResourceScanner, ModuleActiveRadiator -> Module_Cooler" + ScienceArmed);
            var importers = Importers(new Ksp1PartModuleImporterRegistry());
            Assert.IsInstanceOf<HarvesterImporter>(importers["moduleresourceharvester"]);
            Assert.IsInstanceOf<SurveyorImporter>(importers["moduleorbitalsurveyor"]);
            Assert.IsInstanceOf<ResourceScannerImporter>(importers["moduleresourcescanner"]);
            Assert.IsFalse(importers.ContainsKey("moduleasteroiddrill"), "no importer of ours: Redux's unsupported line stays");
            Assert.IsFalse(importers.ContainsKey("moduleorbitalscanner"));
        }

        [Test] public void SwitchesOff_NoDrillOrScannerImporter()
        {
            Assert.IsTrue(Arm(Database(), "{\"ksp1_drills\": false, \"ksp1_survey_scanners\": false, \"ksp1_resource_scanners\": false}"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleResourceHarvester -> Module_Mine off (ksp1_drills false)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleOrbitalSurveyor -> Module_ResourceScanner off (ksp1_survey_scanners false)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleResourceScanner -> Module_ResourceScanner off (ksp1_resource_scanners false)");
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(RadialDrillModules, "RadialDrill");
            Assert.IsFalse(registry.TryImport(Modules(part, "ModuleResourceHarvester")[0], Context(part), out _), "unsupported, dropped by Redux");
            var scanner = Part(Surveyor, "SurveyScanner");
            Assert.IsFalse(registry.TryImport(Modules(scanner, "ModuleOrbitalSurveyor")[0], Context(scanner), out _));
        }

        // ------------------------------------------------------------------ the native reference

        [Test] public void Reference_FromTheGamesParts_SetsTheConverterHeat_LoggedOnce()
        {
            Assert.IsTrue(Arm(Database()));
            IsruReference.FormulaSource = () => new[]
            {
                NativeIsruFormula.Mine("drill_2v_radial", new[] { ("ElectricCharge", 20.0) },
                    new[] { ("Water", 4.333e-5), ("CarbonRegolith", 5.556e-5) }, 350),
                NativeIsruFormula.Converter("conv", "a", new[] { ("ElectricCharge", 10.0) }, new[] { ("Methane", 1.0) }, 100),
                NativeIsruFormula.Converter("conv", "b", new[] { ("ElectricCharge", 10.0) }, new[] { ("Methane", 1.0) }, 200),
                NativeIsruFormula.Converter("conv", "c", new[] { ("ElectricCharge", 10.0) }, new[] { ("Methane", 1.0) }, 300),
                NativeIsruFormula.Scanner("scanner_1v_orbital", new[] { ("ElectricCharge", 14.0) }, new[] { "Water", "Ammonia" }, 5, 0, 5000000, 80, 100),
            };
            Assert.AreEqual(20, ConverterImporter.HeatPerEc(), 1e-9, "the median of 10, 20 and 30 kW per EC/s");
            Assert.AreEqual(20, ConverterImporter.HeatPerEc(), 1e-9);
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: isru reference: drill drill_2v_radial 20 EC/s, 17.5 kW per EC/s, Water 0.155988 t/h, CarbonRegolith 0.200016 t/h; " +
                "converters 3 mode(s), median 20 kW per EC/s; scanner scanner_1v_orbital 14 EC/s, Water, Ammonia, altitude 0 m to 5000 km, inclination 80° to 100°",
                "[KSP1PartMod] native: minable resources: CarbonRegolith, Uranium, Water (from resource maps); CarbonRegolith map on 2 body/bodies: Duna, Kerbin",
                "[KSP1PartMod] native: minable resources: CarbonRegolith, Uranium, Water (from resource maps); Uranium map on 1 body/bodies: Duna",
                "[KSP1PartMod] native: minable resources: CarbonRegolith, Uranium, Water (from resource maps); Water map on 1 body/bodies: Kerbin",
            }, _lines.Where(l => l.Contains("isru reference") || l.Contains("minable resources")).ToList(), "once per session");
            Assert.AreEqual("drill_2v_radial", IsruReference.Get().Drill.Part);
        }

        [Test] public void Reference_NoGameParts_NoMaps_Fallbacks()
        {
            Assert.IsTrue(Arm(Database(), maps: false));
            IsruReference.MapSource = Array.Empty<(string, string)>;
            Assert.AreEqual(NativeIsruRef.FallbackConverterHeatPerEc, ConverterImporter.HeatPerEc());
            Assert.AreEqual(MinableSource.Fallback, IsruReference.MinableFrom);
            CollectionAssert.AreEqual(NativeIsruRef.FallbackMinable, IsruReference.Minable());
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: isru reference: no KSP2 drill found, fallback drill_2v_radial 20 EC/s"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: isru reference: no KSP2 converter found, fallback median 10.5 kW per EC/s");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: minable resources: Water, Ammonia, CarbonRegolith, Uranium (from fallback)");
        }

        [Test] public void Reference_NativeDrillOutputs_WhenNoMap()
        {
            Assert.IsTrue(Arm(Database(), maps: false));
            IsruReference.MapSource = Array.Empty<(string, string)>;
            IsruReference.FormulaSource = () => new[]
            {
                NativeIsruFormula.Mine("drill", new[] { ("ElectricCharge", 10.0) }, new[] { ("Water", 1e-5) }, 100),
            };
            CollectionAssert.AreEqual(new[] { "Water" }, IsruReference.Minable());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: minable resources: Water (from the KSP2 drill)");
        }

        /// <summary>The real source: Redux's own <c>ISRUResourceManager.CbResources</c> (body → resource → chart).</summary>
        [Test] public void Reference_ReadsReduxsLoadedCharts()
        {
            Assert.IsTrue(Arm(Database(), maps: false));
            var added = new List<string>();
            try
            {
                foreach (var body in new[] { "TestBodyA", "TestBodyB" })
                {
                    Assume.That(ISRUResourceManager.CbResources.ContainsKey(body), Is.False);
                    ISRUResourceManager.CbResources[body] = new Dictionary<string, CBResourceChart> { ["Ammonia"] = new CBResourceChart(), ["Nothing"] = null };
                    added.Add(body);
                }
                CollectionAssert.Contains(IsruReference.Minable(), "Ammonia");
                CollectionAssert.DoesNotContain(IsruReference.Minable(), "Nothing", "a null chart is no map");
                Assert.AreEqual(MinableSource.ResourceMaps, IsruReference.MinableFrom);
                Assert.That(_lines, Has.Some.EndsWith("Ammonia map on 2 body/bodies: TestBodyA, TestBodyB"));
            }
            finally
            {
                foreach (var body in added) ISRUResourceManager.CbResources.Remove(body);
            }
        }

        // ------------------------------------------------------------------ drills

        [Test] public void RadialDrill_OneMine_CarbonRegolithRate_Ec15_ImpactRecorded()
        {
            Assert.IsTrue(Arm(Database()));
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(RadialDrillModules, "RadialDrill");
            var context = Context(part);
            var harvester = Modules(part, "ModuleResourceHarvester")[0];

            Assert.IsTrue(registry.TryImport(harvester, context, out var module));
            Assert.AreEqual(typeof(Module_Mine), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_Mine", module.ComponentType.Name);
            Assert.IsFalse(registry.TryImport(harvester, context, out _), "a later harvester call in the same context");
            Assert.IsFalse(registry.TryImport(Modules(part, "ModuleAsteroidDrill")[0], context, out _), "Redux: unsupported");

            var data = context.ModuleData.OfType<Data_Mine>().Single();
            var f = data.MiningFormulaDefinition;
            Assert.AreEqual(HarvesterMapping.MineId, f.InternalName);
            Assert.AreEqual(0.9, data.AutoShutdownTemperatureRatio);
            Assert.AreEqual("ElectricCharge", f.InputResources.Single().ResourceName);
            Assert.AreEqual(15f, f.InputResources.Single().Rate, "EC never converted");
            Assert.AreEqual("CarbonRegolith", f.OutputResources.Single().ResourceName, "Ore through the remap");
            Assert.AreEqual(5.556e-5f / 20 * 15, f.OutputResources.Single().Rate, 1e-11f, "the native drill's CarbonRegolith per EC/s x 15 EC/s (a stock drill)");
            Assert.AreEqual(262.5f, f.FluxGenerated, 1e-4f, "17.5 kW per EC/s x 15");

            Assert.IsTrue(NativeModules.Isru.TryGetDrill("RadialDrill", out var impact));
            Assert.AreEqual("ImpactTransform", impact.Transform);
            Assert.AreEqual(5.42, impact.Range, 1e-12);
            CollectionAssert.AreEqual(new[] { "CarbonRegolith" }, impact.Resources);
            CollectionAssert.AreEqual(new[] { HarvesterMapping.NativeModule }, NativeModules.Record.ConvertedModules("RadialDrill"));

            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: RadialDrill: drill: Ore -> CarbonRegolith at 4.167E-05 units/s at density 1 (KSP2 yield 2.778E-06 per EC/s from drill_2v_radial " +
                "x 15 EC/s x Efficiency factor 1 (1.5 / (0.1 x 15)) x harvest_multiplier 1); per EC/s: KSP1 drill 2.778E-06, KSP2 drill 2.778E-06; " +
                "EC 15/s, heat 262.5 kW, impact 'ImpactTransform' 5.42 m");
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: drills: a KSP1 drill yields what a KSP2 drill mines per EC/s x its EC/s x Efficiency / (0.1 x EC/s) x 1 (harvest_multiplier, default)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: RadialDrill: " + IsruLines.AsteroidDrillIgnored(2));

            // A rebuild logs and counts nothing twice.
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(harvester, Context(part), out _));
            Assert.AreEqual(1, _lines.Count(l => l.StartsWith("[KSP1PartMod] native: RadialDrill: drill: ")));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("(harvest_multiplier")));

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: drills: 1 part(s) got Module_Mine (CarbonRegolith); skipped: 0 ocean, 0 atmospheric, 0 exospheric, " +
                "0 resource without a map; ModuleAsteroidDrill/ModuleCometDrill ignored on 1 part(s) (no asteroids or comets in KSP2); " +
                "not reproduced: ThermalEfficiency 1");
        }

        [Test] public void Drill_MultiplierSetting_Used_AndLogged()
        {
            Assert.IsTrue(Arm(Database(), "{\"harvest_multiplier\": 4}"));
            var part = Part(RadialDrillModules, "RadialDrill");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceHarvester")[0], context, out _));
            Assert.AreEqual(5.556e-5f / 20 * 15 * 4, context.ModuleData.OfType<Data_Mine>().Single().MiningFormulaDefinition.OutputResources[0].Rate, 1e-10f);
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: drills: a KSP1 drill yields what a KSP2 drill mines per EC/s x its EC/s x Efficiency / (0.1 x EC/s) x 4 (harvest_multiplier)");
        }

        [Test] public void AtmosphericHarvester_False_Noted_Counted()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part("MODULE\n{\nname = ModuleResourceHarvester\nHarvesterType = 2\nResourceName = Oxidizer\nEfficiency = 1\n" +
                            "INPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}\n}\n", "Intake");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceHarvester")[0], context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: Intake: ModuleResourceHarvester: HarvesterType 2 (atmospheric) skipped, Module_Mine only mines the ground");
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: drills: 0 part(s) got Module_Mine (); skipped: 0 ocean, 1 atmospheric"));
        }

        [Test] public void OreRemapOff_TestRocksHasNoMap_NoMine_Noted()
        {
            Assert.IsTrue(Arm(Database(), "{\"ksp1_ore_resource\": \"TestRocks\"}"));
            var part = Part(RadialDrillModules, "RadialDrill");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceHarvester")[0], context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: RadialDrill: ModuleResourceHarvester: Ore -> TestRocks has no resource map in KSP2, harvester skipped");
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("drills: 0 part(s) got Module_Mine (); skipped: 0 ocean, 0 atmospheric, 0 exospheric, 1 resource without a map"));
        }

        [Test] public void ContextWithAMine_NothingAdded_Noted()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(RadialDrillModules, "RadialDrill");
            var context = Context(part);
            var redux = new Data_Mine();
            context.ModuleData.Add(redux);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceHarvester")[0], context, out _));
            Assert.AreSame(redux, context.ModuleData.Single());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: RadialDrill: " + IsruLines.ExistingMineNote);
        }

        [Test] public void DrillException_LeavesTheModuleDataAsBefore()
        {
            Assert.IsTrue(Arm(Database()));
            HarvesterImporter.FaultForTests = p => throw new InvalidOperationException("fault");
            var part = Part(RadialDrillModules, "RadialDrill");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceHarvester")[0], context, out var module));
            Assert.IsNull(module.ModuleData);
            CollectionAssert.IsEmpty(context.ModuleData, "no orphan Data_Mine");
            Assert.AreEqual(1, _lines.Count(l => l.Contains("RadialDrill: ModuleResourceHarvester not converted")));
            CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("RadialDrill"));
            Assert.IsFalse(NativeModules.Isru.TryGetDrill("RadialDrill", out _), "no impact data for a failed part");
            Assert.That(_lines, Has.None.Contains("RadialDrill: drill: "));
        }

        /// <summary>B9's scratch re-import: an edited copy of the harvester replaces its entry.</summary>
        [Test] public void ScratchContext_EditedHarvester_Used()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(RadialDrillModules, "RadialDrill");
            var edited = Modules(Part(RadialDrillModules.Replace("Efficiency = 1.5", "Efficiency = 3"), "RadialDrill"), "ModuleResourceHarvester")[0];
            var scratch = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(edited, scratch, out _));
            var outputs = scratch.ModuleData.OfType<Data_Mine>().Single().MiningFormulaDefinition.OutputResources;
            Assert.AreEqual(1, outputs.Count, "replaced, not appended");
            Assert.AreEqual(5.556e-5f / 20 * 15 * 2, outputs[0].Rate, 1e-10f, "Efficiency 3 for 15 EC/s: twice a stock drill per EC/s");
        }

        // ------------------------------------------------------------------ scanners

        [Test] public void SurveyScanner_OneScanner_NativeFields_OrbitalScannerNoted()
        {
            Assert.IsTrue(Arm(Database()));
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(Surveyor, "SurveyScanner");
            var context = Context(part);
            Assert.IsTrue(registry.TryImport(Modules(part, "ModuleOrbitalSurveyor")[0], context, out var module));
            Assert.AreEqual(typeof(Module_ResourceScanner), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_ResourceScanner", module.ComponentType.Name);
            Assert.IsFalse(registry.TryImport(Modules(part, "ModuleOrbitalScanner")[0], context, out _), "Redux: unsupported");

            var data = context.ModuleData.OfType<Data_ResourceScanner>().Single();
            CollectionAssert.AreEqual(new[] { "Water", "CarbonRegolith", "Ammonia", "Uranium" }, data.ScannableResources.Select(s => s.ResourceName));
            Assert.AreEqual("ElectricCharge", data.RequiredResources.Single().ResourceName);
            Assert.AreEqual(14f, data.RequiredResources.Single().Rate);
            Assert.AreEqual(5f, data.TimeToComplete);
            Assert.AreEqual(0, data.minAltitude);
            Assert.AreEqual(5000000, data.maxAltitude);
            Assert.AreEqual(80, data.minInclination);
            Assert.AreEqual(100, data.maxInclination);
            Assert.IsTrue(NativeModules.Isru.IsScanner("SurveyScanner"));

            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: SurveyScanner: scanner: surveyor -> Module_ResourceScanner: Water, CarbonRegolith, Ammonia, Uranium, 14 EC/s, 5 s, " +
                "altitude 0 m to 5000 km, inclination 80° to 100°");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: SurveyScanner: " + IsruLines.OrbitalScannerFolded(1));
        }

        [Test] public void SurfaceScanner_ItsResource_ToOneKm()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(ResourceScanner(false), "SurfaceScanner");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceScanner")[0], context, out _));
            var data = context.ModuleData.OfType<Data_ResourceScanner>().Single();
            CollectionAssert.AreEqual(new[] { "CarbonRegolith" }, data.ScannableResources.Select(s => s.ResourceName));
            CollectionAssert.IsEmpty(data.RequiredResources);
            Assert.AreEqual(1000, data.maxAltitude);
            Assert.AreEqual(-180, data.minInclination);
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: SurfaceScanner: scanner: resource scanner -> Module_ResourceScanner: CarbonRegolith, 0 EC/s, 5 s, " +
                "altitude 0 m to 1 km, inclination -180° to 180°");
        }

        [Test] public void OrbitalScanner_RequiresUnlock_False_Noted_Counted()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(ResourceScanner(true, "500000"), "OrbitalScanner");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleResourceScanner")[0], context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: OrbitalScanner: " + ScannerMapping.RequiresUnlockNote);

            var surface = Part(ResourceScanner(false), "SurfaceScanner");
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(surface, "ModuleResourceScanner")[0], Context(surface), out _));
            var survey = Part(Surveyor, "SurveyScanner");
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(survey, "ModuleOrbitalSurveyor")[0], Context(survey), out _));
            NativeModules.LogImportSummaryOnce();
            Assert.AreEqual(1, _lines.Count(l => l ==
                "[KSP1PartMod] native: scanners: 1 survey scanner(s) and 1 resource scanner(s) got Module_ResourceScanner; " +
                "1 ModuleResourceScanner left out (RequiresUnlock: KSP1 reads local abundance after a survey, KSP2 has no part readout), " +
                "0 with no scannable resource"), "one scanners line for both converters");
        }

        [Test] public void BothScanners_SurveyorWins_WhateverTheOrder()
        {
            Assert.IsTrue(Arm(Database()));
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(ResourceScanner(false) + Surveyor, "Both");
            var context = Context(part);
            Assert.IsFalse(registry.TryImport(Modules(part, "ModuleResourceScanner")[0], context, out _), "the resource scanner first");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: Both: " + IsruLines.SurveyorWinsNote);
            Assert.IsTrue(registry.TryImport(Modules(part, "ModuleOrbitalSurveyor")[0], context, out _));
            var data = context.ModuleData.OfType<Data_ResourceScanner>().Single();
            Assert.AreEqual(4, data.ScannableResources.Count, "the surveyor's");
        }

        [Test] public void TwoKeptResourceScanners_OneModule_Merged()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(ResourceScanner(false, "1000") + ResourceScanner(false, "3000", "Water"), "Two");
            var context = Context(part);
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsTrue(registry.TryImport(Modules(part, "ModuleResourceScanner")[0], context, out _));
            Assert.IsFalse(registry.TryImport(Modules(part, "ModuleResourceScanner")[1], context, out _));
            var data = context.ModuleData.OfType<Data_ResourceScanner>().Single();
            CollectionAssert.AreEqual(new[] { "CarbonRegolith", "Water" }, data.ScannableResources.Select(s => s.ResourceName));
            Assert.AreEqual(3000, data.maxAltitude);
        }

        [Test] public void ScannerException_LeavesTheModuleDataAsBefore()
        {
            Assert.IsTrue(Arm(Database()));
            ScannerImporters.FaultForTests = p => throw new InvalidOperationException("fault");
            var part = Part(Surveyor, "SurveyScanner");
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part, "ModuleOrbitalSurveyor")[0], context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("SurveyScanner"));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("SurveyScanner: ModuleOrbitalSurveyor not converted")));
        }

        // ------------------------------------------------------------------ carry from the Task 2 review

        [Test] public void OreMigration_NoDatabase_LoggedOnce()
        {
            OreMigration.NoDatabase("p", 0);
            CollectionAssert.IsEmpty(_lines, "nothing saved, nothing lost");
            OreMigration.NoDatabase("p", 1500);
            OreMigration.NoDatabase("q", 10);
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: ore: old saves: no resource database while p loads, its saved TestRocks 1500 not migrated " +
                "(logged once; later parts without a database are not migrated either)",
            }, _lines);
        }
    }
}
