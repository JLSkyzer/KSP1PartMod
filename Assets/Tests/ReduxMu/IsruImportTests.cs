using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP.Sim.State;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), converters and drills plan Task 2: the ISRU members resolve by
    /// name, the Ore remap edits Redux's own table (and restores it), and Redux's real <c>Ksp1PartModuleImporterRegistry</c>,
    /// built with our hook armed, imports an in-memory stock-like ISRU as one <c>Module_ResourceConverter</c> with four
    /// modes, every rate through Redux's own <c>ConvertAmount</c> over a resource database built in the test.</summary>
    public class IsruImportTests
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
            new Harmony(OreRemap.HarmonyId).UnpatchAll(OreRemap.HarmonyId);
            new Harmony(OreMigration.HarmonyId).UnpatchAll(OreMigration.HarmonyId);
            NativeModules.Reset();
            OreRemap.Reset();
            OreMigration.Reset();
        }

        /// <summary>Arms the hook (and the Ore remap, as the plugin does, first); the importer then reads <paramref name="db"/>.</summary>
        private bool Arm(ResourceDefinitionDatabase db, string json = "{}")
        {
            var settings = AnimationSettings.Parse(json, out _);
            OreRemap.Arm(new Harmony(OreRemap.HarmonyId), settings.OreRemapTarget);
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), settings);
            ConverterImporter.DatabaseSource = () => db;
            return armed;
        }

        /// <summary>A resource database with KSP2-like unit masses (t per unit), written for the test.</summary>
        private static ResourceDefinitionDatabase Database()
        {
            var db = new ResourceDefinitionDatabase();
            foreach (var (name, mass) in new[]
                     {
                         ("ElectricCharge", 0.0), ("CarbonRegolith", 1.0), ("TestRocks", 0.05), ("Methane", 0.0025), ("Oxidizer", 0.005),
                         ("MonoPropellant", 0.004),
                     })
                db.AddOrUpdateDefinition(Ksp1ResourceMapper.ToResourceDefinition(
                    Ksp1ConfigNode.Parse("RESOURCE_DEFINITION\n{\nname = " + name + "\ndensity = " + mass.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n}\n")
                        .GetNodes("RESOURCE_DEFINITION").First()));
            return db;
        }

        private static string Converter(string name, string body) =>
            "MODULE\n{\nname = ModuleResourceConverter\nConverterName = " + name + "\nAutoShutdown = true\nGeneratesHeat = true\n" + body + "}\n";

        private static string Flow(string kind, string resource, string ratio) =>
            kind + "\n{\nResourceName = " + resource + "\nRatio = " + ratio + "\n}\n";

        private static string Isru(string lfOxOre = "0.5") =>
            Converter("Lf+Ox", Flow("INPUT_RESOURCE", "Ore", lfOxOre) + Flow("INPUT_RESOURCE", "ElectricCharge", "30") +
                               Flow("OUTPUT_RESOURCE", "LiquidFuel", "0.45") + Flow("OUTPUT_RESOURCE", "Oxidizer", "0.55")) +
            Converter("Monoprop", Flow("INPUT_RESOURCE", "Ore", "0.5") + Flow("INPUT_RESOURCE", "ElectricCharge", "30") +
                                  Flow("OUTPUT_RESOURCE", "MonoPropellant", "1")) +
            Converter("LiquidFuel", Flow("INPUT_RESOURCE", "Ore", "0.45") + Flow("INPUT_RESOURCE", "ElectricCharge", "30") +
                                    Flow("OUTPUT_RESOURCE", "LiquidFuel", "0.9")) +
            Converter("Oxidizer", Flow("INPUT_RESOURCE", "Ore", "0.55") + Flow("INPUT_RESOURCE", "ElectricCharge", "30") +
                                  Flow("OUTPUT_RESOURCE", "Oxidizer", "1.1"));

        private static Ksp1ConfigNode Part(string modules, string name = "testPart") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\n" + modules + "}\n").GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };

        private static List<Ksp1ConfigNode> Modules(Ksp1ConfigNode part) => part.GetNodes("MODULE").ToList();

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        private static (string Name, float Rate)[] Rates(List<PartModuleResourceSetting> settings) =>
            settings.Select(s => (s.ResourceName, s.Rate)).ToArray();

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_EveryIsruMember()
        {
            var t = IsruAccess.Resolve();
            Assert.AreEqual("", string.Join(", ", t.MissingOre.Concat(t.MissingConverter).Concat(t.MissingNames)));
            Assert.IsTrue(t.ResourceMap.IsStatic);
            Assert.IsFalse(t.ResourceMap.IsPublic, "private in 26w42c");
            Assert.AreEqual(typeof(Dictionary<string, string>), t.ResourceMap.FieldType);
            Assert.IsTrue(t.ConvertAmount.IsStatic);
            Assert.IsFalse(t.ConvertAmount.IsPublic);
            CollectionAssert.AreEqual(new[]
            {
                typeof(string), typeof(string), typeof(double), typeof(ResourceDefinitionDatabase), typeof(Ksp1ResourceMapper.Ksp2ResourceMassLookup),
            }, t.ConvertAmount.GetParameters().Select(p => p.ParameterType));
            Assert.AreEqual(typeof(double), t.ConvertAmount.ReturnType);
            Assert.AreEqual("Ksp1PartLocalizationBuilder", t.AddPart.DeclaringType.Name);
            Assert.IsTrue(t.AddPart.IsPublic);
            Assert.IsFalse(t.Add.IsPublic);
            Assert.IsFalse(t.Catalog.IsPublic);
            Assert.AreEqual(typeof(Ksp1LocalizationCatalog), t.Catalog.FieldType);
            CollectionAssert.IsEmpty(ConverterImporter.MissingMembers());
            Assert.AreEqual(typeof(Module_ResourceConverter), new Data_ResourceConverter().ModuleType);
            Assert.AreEqual(0.5f, new Data_ResourceConverter().conversionRate.GetValue(), "KSP2's own initial rate; ours is 1");
            Assert.AreEqual(0.9, new Data_ResourceConverter().AutoShutdownTemperatureRatio);
        }

        [Test] public void ConvertAmount_IsReduxs_ByMass_EcUnchanged()
        {
            var db = Database();
            Assert.AreEqual(0.025, IsruAccess.ConvertAmount("Ore", "CarbonRegolith", 0.5, db), 1e-12, "Redux's Ore: 0.05 t per unit");
            Assert.AreEqual(0.9, IsruAccess.ConvertAmount("LiquidFuel", "Methane", 0.45, db), 1e-12);
            Assert.AreEqual(30, IsruAccess.ConvertAmount("ElectricCharge", "ElectricCharge", 30, db));
            Assert.AreEqual(0.5, IsruAccess.ConvertAmount("Ore", "CarbonRegolith", 0.5, null), "no database: unchanged, as Redux");
        }

        // ------------------------------------------------------------------ the Ore remap

        [Test] public void OreRemap_SetsReduxsTable_ThenRestores()
        {
            Assert.AreEqual("TestRocks", Ksp1ResourceMapper.MapResourceName("Ore"), "Redux 26w42c");
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "CarbonRegolith"));
            Assert.AreEqual("CarbonRegolith", Ksp1ResourceMapper.MapResourceName("Ore"));
            Assert.IsTrue(Ksp1ResourceMapper.IsKnownMappedResource("Ore"), "the key stays: B9 tank types still see Ore");
            CollectionAssert.AreEqual(new[] { IsruLines.OreImported("CarbonRegolith") }, _lines);
            OreRemap.Reset();
            Assert.AreEqual("TestRocks", Ksp1ResourceMapper.MapResourceName("Ore"));
        }

        [Test] public void OreRemap_Off_ReduxsMappingKept_NoLine()
        {
            Assert.IsNull(AnimationSettings.Parse("{\"ksp1_ore_resource\": \"TestRocks\"}", out _).OreRemapTarget);
            Assert.IsFalse(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), null));
            Assert.AreEqual("TestRocks", Ksp1ResourceMapper.MapResourceName("Ore"));
            CollectionAssert.IsEmpty(_lines);
        }

        [Test] public void OreRemap_UnknownTarget_RestoredAtTheFirstRegistry()
        {
            OreRemap.ExistsOverride = name => name != "Regolith";
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "Regolith"));
            Assert.AreEqual("Regolith", Ksp1ResourceMapper.MapResourceName("Ore"));
            new Ksp1PartModuleImporterRegistry();
            Assert.AreEqual("TestRocks", Ksp1ResourceMapper.MapResourceName("Ore"), "restored before any part");
            new Ksp1PartModuleImporterRegistry();
            CollectionAssert.AreEqual(new[] { IsruLines.OreImported("Regolith"), IsruLines.OreNotAResource("Regolith") }, _lines, "once");
        }

        [Test] public void OreRemap_KnownTarget_Kept_NoDatabase_NotCheckedYet()
        {
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "CarbonRegolith"));
            OreRemap.EnsureVerified();
            Assert.AreEqual("CarbonRegolith", Ksp1ResourceMapper.MapResourceName("Ore"), "no game database: not checked, kept");
            OreRemap.ExistsOverride = name => name == "CarbonRegolith";
            OreRemap.EnsureVerified();
            Assert.AreEqual("CarbonRegolith", Ksp1ResourceMapper.MapResourceName("Ore"));
            CollectionAssert.AreEqual(new[] { IsruLines.OreImported("CarbonRegolith"), IsruLines.OreCheckDeferred("CarbonRegolith") }, _lines, "no restore line; the deferral once");
        }

        // ------------------------------------------------------------------ Redux's real import

        [Test] public void Arm_AddsTheConverterImporter()
        {
            Assert.IsTrue(Arm(Database()));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, " +
                                              "ModuleAblator -> Module_Heatshield, ModuleResourceConverter -> Module_ResourceConverter, ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, ModuleResourceScanner -> Module_ResourceScanner");
            Assert.IsInstanceOf<ConverterImporter>(Importers(new Ksp1PartModuleImporterRegistry())["moduleresourceconverter"]);
        }

        [Test] public void SwitchOff_NoConverterImporter()
        {
            Assert.IsTrue(Arm(Database(), "{\"ksp1_converters\": false}"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleResourceConverter -> Module_ResourceConverter off (ksp1_converters false)");
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsFalse(Importers(registry).ContainsKey(ConverterMapping.Ksp1Module));
            var part = Part(Isru());
            Assert.IsFalse(registry.TryImport(Modules(part)[0], Context(part), out _), "unsupported, dropped by Redux");
        }

        /// <summary>With <c>isru_ksp1_rates</c>: the KSP1 rates, through Redux's <c>ConvertAmount</c> only.</summary>
        [Test] public void StockIsru_OneModule_FourModes_MappedNames_ConvertedRates()
        {
            Assert.IsTrue(Arm(Database(), KeepKsp1Rates));
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(Isru());
            var context = Context(part);
            var modules = Modules(part);

            Assert.IsTrue(registry.TryImport(modules[0], context, out var module));
            Assert.AreEqual(typeof(Module_ResourceConverter), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_ResourceConverter", module.ComponentType.Name);
            Assert.AreEqual(1, module.ModuleData.Count);
            for (var i = 1; i < modules.Count; i++)
                Assert.IsFalse(registry.TryImport(modules[i], context, out _), "converter " + i + ": already a mode");
            var data = context.ModuleData.OfType<Data_ResourceConverter>().Single();

            Assert.IsFalse(data.ConvertByMass);
            Assert.IsTrue(data.ResourceAutoShutdown);
            Assert.AreEqual(0.9, data.AutoShutdownTemperatureRatio);
            Assert.AreEqual(1f, data.conversionRate.GetValue(), "KSP1 runs at full rate");
            Assert.AreEqual(4, data.FormulaDefinitions.Count);
            CollectionAssert.AreEqual(new[] { "ksp1_0", "ksp1_1", "ksp1_2", "ksp1_3" }, data.FormulaDefinitions.Select(f => f.InternalName));
            CollectionAssert.AreEqual(Enumerable.Range(0, 4).Select(i => "Parts/Ksp1Converter/testPart/" + i),
                data.FormulaDefinitions.Select(f => f.FormulaLocalizationKey));

            var lfox = data.FormulaDefinitions[0];
            var inputs = Rates(lfox.InputResources);
            CollectionAssert.AreEqual(new[] { "CarbonRegolith", "ElectricCharge" }, inputs.Select(x => x.Name));
            Assert.AreEqual(0.025f, inputs[0].Rate, 1e-9f, "Ore 0.5 x 0.05 t / 1 t");
            Assert.AreEqual(30f, inputs[1].Rate, "EC never converted");
            var outputs = Rates(lfox.OutputResources);
            CollectionAssert.AreEqual(new[] { "Methane", "Oxidizer" }, outputs.Select(x => x.Name));
            Assert.AreEqual(0.9f, outputs[0].Rate, 1e-7f, "LiquidFuel 0.45 x 0.005 t / 0.0025 t");
            Assert.AreEqual(0.55f, outputs[1].Rate, 1e-7f);
            Assert.AreEqual(315f, lfox.FluxGenerated, 1e-4f, "10.5 kW per EC/s x 30");
            Assert.AreEqual("MonoPropellant", data.FormulaDefinitions[1].OutputResources.Single().ResourceName);
            Assert.AreEqual(1f, data.FormulaDefinitions[1].OutputResources.Single().Rate, 1e-7f, "1 x 0.004 / 0.004");

            CollectionAssert.AreEqual(new[] { ConverterMapping.NativeModule }, NativeModules.Record.ConvertedModules("testPart"));
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: testPart: converter 'Lf+Ox' -> mode 0: in CarbonRegolith 0.025, ElectricCharge 30 out Methane 0.9, Oxidizer 0.55 " +
                "(mass in 0.025 t/s, out 0.005 t/s), heat 315 kW; Ore weighs 0.05 t per KSP1 unit in Redux, 0.01 t in KSP1: Ore mass x5; " +
                "pace: KSP1 rates (isru_ksp1_rates)");
            Assert.AreEqual(4, _lines.Count(l => l.StartsWith("[KSP1PartMod] native: testPart: converter '")));

            // A rebuild of the same part logs and counts nothing twice.
            var again = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(modules[0], again, out _));
            Assert.AreEqual(4, _lines.Count(l => l.StartsWith("[KSP1PartMod] native: testPart: converter '")));

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines,
                "[KSP1PartMod] native: converters: 1 part(s) got Module_ResourceConverter with 4 mode(s) " +
                "(1 part(s) with several KSP1 converters, one runs at a time); modes dropped: 0 (unknown resource); " +
                "not reproduced: several modes at once 1");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: converter names: 0 term(s) added to Redux's part localization");
        }

        private const string KeepKsp1Rates = "{\"isru_ksp1_rates\": true}";

        /// <summary>The default (the final review's I1 ruling): each mode consumes per EC/s what the KSP2 converter consumes
        /// (here the 26w42c fallback, MethaloxISRU: CarbonRegolith 3.333e-5 per 20 EC/s); EC, heat and the KSP1 ratios kept.</summary>
        [Test] public void StockIsru_Default_PacedToTheKsp2ConverterPerEc()
        {
            Assert.IsTrue(Arm(Database()));
            ConverterImporter.NativeConverters = () => NativeIsruRef.FallbackConverters;
            var part = Part(Isru());
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out _));
            var data = context.ModuleData.OfType<Data_ResourceConverter>().Single();
            const double nativePerEc = 3.333e-5 / 20;
            foreach (var f in data.FormulaDefinitions)
            {
                var inputs = Rates(f.InputResources);
                Assert.AreEqual("CarbonRegolith", inputs[0].Name);
                Assert.AreEqual(30 * nativePerEc, inputs[0].Rate, 1e-9, f.InternalName + ": CarbonRegolith per EC/s = KSP2's");
                Assert.AreEqual(30f, inputs[1].Rate, "EC unchanged");
            }
            var lfox = data.FormulaDefinitions[0];
            var factor = 30 * nativePerEc / 0.025;
            Assert.AreEqual(0.9 * factor, lfox.OutputResources[0].Rate, 1e-9, "Methane: KSP1's ratio to the input kept");
            Assert.AreEqual(0.55 * factor, lfox.OutputResources[1].Rate, 1e-9);
            Assert.AreEqual(315f, lfox.FluxGenerated, 1e-4f, "heat follows the EC: unchanged");
            var line = _lines.Single(l => l.StartsWith("[KSP1PartMod] native: testPart: converter 'Lf+Ox' -> mode 0:"));
            StringAssert.Contains($"; pace x{IsruCfg.Inv(factor)} (KSP2's rate per EC): consumes {IsruCfg.Inv(0.025 / 30)} t/s per EC/s in KSP1, " +
                                  $"{IsruCfg.Inv(nativePerEc)} in KSP2 (converter_2v_inline 'MethaloxISRU')", line);
        }

        [Test] public void NoDatabase_NotPaced()
        {
            Assert.IsTrue(Arm(null));
            ConverterImporter.NativeConverters = () => NativeIsruRef.FallbackConverters;
            var part = Part(Isru());
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], Context(part), out _));
            Assert.That(_lines.Where(l => l.Contains("converter 'Lf+Ox'")), Has.All.EndsWith("; pace: KSP1 rates (no resource database)"));
        }

        [Test] public void UnknownResource_ModeDropped_Counted()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(Isru() + Converter("Karbonite", Flow("INPUT_RESOURCE", "Karbonite", "1") + Flow("OUTPUT_RESOURCE", "LiquidFuel", "1")));
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out _));
            Assert.AreEqual(4, context.ModuleData.OfType<Data_ResourceConverter>().Single().FormulaDefinitions.Count);
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("with 4 mode(s)").And.Contains("modes dropped: 1 (unknown resource)"));
        }

        [Test] public void NoDatabase_EveryResourceKnown_RatesUnchanged_LoggedOnce()
        {
            Assert.IsTrue(Arm(null));
            var part = Part(Isru());
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out _));
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], Context(part), out _));
            var lfox = context.ModuleData.OfType<Data_ResourceConverter>().Single().FormulaDefinitions[0];
            Assert.AreEqual(0.5f, lfox.InputResources[0].Rate, "Redux's ConvertAmount without a database: unchanged");
            Assert.AreEqual(1, _lines.Count(l => l == IsruLines.NoResourceDatabase));
            Assert.That(_lines.Where(l => l.Contains("converter 'Lf+Ox'")), Has.All.Not.Contains("Ore weighs"), "Ore mass unknown");
        }

        /// <summary>B9's module-data route (plan (l)): a scratch context called with an edited copy of one converter builds the
        /// whole list with only that mode edited.</summary>
        [Test] public void ScratchContext_EditedLfOx_FourModes_OnlyThatOneEdited()
        {
            // KSP1 rates: under the pace, an edited input ratio is brought back to KSP2's consumption per EC/s.
            Assert.IsTrue(Arm(Database(), KeepKsp1Rates));
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(Isru());
            var edited = Modules(Part(Isru("1.0")))[0];

            var before = Context(part);
            Assert.IsTrue(registry.TryImport(Modules(part)[0], before, out _));
            var scratch = Context(part);
            Assert.IsTrue(registry.TryImport(edited, scratch, out _));

            var a = before.ModuleData.OfType<Data_ResourceConverter>().Single().FormulaDefinitions;
            var b = scratch.ModuleData.OfType<Data_ResourceConverter>().Single().FormulaDefinitions;
            Assert.AreEqual(4, b.Count);
            Assert.AreEqual(0.05f, b[0].InputResources[0].Rate, 1e-9f, "Ore 1.0 edited in");
            Assert.AreEqual(0.025f, a[0].InputResources[0].Rate, 1e-9f);
            for (var i = 1; i < 4; i++)
                CollectionAssert.AreEqual(Rates(a[i].InputResources).Concat(Rates(a[i].OutputResources)),
                    Rates(b[i].InputResources).Concat(Rates(b[i].OutputResources)), "mode " + i + " unchanged");
        }

        [Test] public void ContextWithAReduxConverter_NothingAdded_Noted()
        {
            Assert.IsTrue(Arm(Database()));
            var part = Part(Isru());
            var context = Context(part);
            var redux = new Data_ResourceConverter();
            context.ModuleData.Add(redux);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out _));
            Assert.AreSame(redux, context.ModuleData.Single());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPart: " + ConverterMapping.ExistingModuleNote);
        }

        [Test] public void ImporterException_LeavesTheModuleDataAsBefore()
        {
            Assert.IsTrue(Arm(Database()));
            ConverterImporter.FaultForTests = p => throw new InvalidOperationException("fault");
            var part = Part(Isru());
            var context = Context(part);
            var other = new Data_ToggleCrossFeed();
            context.ModuleData.Add(other);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out var module));
            Assert.IsNull(module.ModuleData);
            Assert.AreSame(other, context.ModuleData.Single(), "no orphan Data_ResourceConverter");
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart: ModuleResourceConverter not converted") &&
                                                 l.Contains("no Module_ResourceConverter on this part")));
            CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("testPart"), "not counted");
            Assert.That(_lines, Has.None.Contains("testPart: converter '"), "no mode line for a failed part");
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[1], context, out _), "the part has no converter at all");
        }

        [Test] public void LogFailure_AfterTheConversion_KeepsTheModule()
        {
            Assert.IsTrue(Arm(Database()));
            NativeModules.Sink = l =>
            {
                if (l.Contains("converter 'Monoprop'")) throw new InvalidOperationException("sink");
                _lines.Add(l);
            };
            var part = Part(Isru());
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(Modules(part)[0], context, out _));
            Assert.AreEqual(4, context.ModuleData.OfType<Data_ResourceConverter>().Single().FormulaDefinitions.Count);
            CollectionAssert.AreEqual(new[] { ConverterMapping.NativeModule }, NativeModules.Record.ConvertedModules("testPart"));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart: converter lines not logged")));
        }

        // ------------------------------------------------------------------ old saves

        [Test] public void OreMigration_TargetsResolve()
        {
            CollectionAssert.IsEmpty(OreMigration.MissingMembers());
            Assert.IsFalse(OreMigration.Arm(new Harmony(OreMigration.HarmonyId)), "the remap is not active: nothing armed");
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "CarbonRegolith"));
            Assert.IsTrue(OreMigration.Arm(new Harmony(OreMigration.HarmonyId)));
        }

        private static ResourceContainer Container(ResourceDefinitionDatabase db, params (string Name, double Capacity)[] resources) =>
            new ResourceContainer(db, resources.Select(r => new ContainedResourceDefinition(r.Capacity, r.Name) { initialUnits = r.Capacity }).ToArray());

        private static Dictionary<string, ContainedResourceState> Saved(params (string Name, double Units)[] resources) =>
            resources.ToDictionary(r => r.Name, r => new ContainedResourceState { name = r.Name, storedUnits = r.Units, capacityUnits = r.Units });

        /// <summary>Redux's <c>ResourceContainer</c> keeps its slots on an ECS entity of the default world: a world of the
        /// test's own, set as the default for <paramref name="body"/> (by reflection, as <c>EcsTypeRegistrationTests</c>).</summary>
        private static void WithWorld(Action body)
        {
            var entities = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Unity.Entities");
            entities.GetType("Unity.Entities.TypeManager").GetMethod("Initialize", Type.EmptyTypes).Invoke(null, null);
            var worldType = entities.GetType("Unity.Entities.World");
            var ctor = worldType.GetConstructors().First(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType == typeof(string)
                                                              && c.GetParameters().Skip(1).All(p => p.IsOptional));
            var args = new object[ctor.GetParameters().Length];
            args[0] = "KSP1PartMod.Tests.OreMigration";
            for (var i = 1; i < args.Length; i++) args[i] = ctor.GetParameters()[i].DefaultValue;
            var world = (IDisposable)ctor.Invoke(args);
            var property = worldType.GetProperty("DefaultGameObjectInjectionWorld");
            var previous = property.GetValue(null);
            property.SetValue(null, world);
            try
            {
                body();
            }
            finally
            {
                property.SetValue(null, previous);
                world.Dispose();
            }
        }

        [Test] public void OreMigration_MovesTheSavedTestRocks_ByMass_OnReduxsContainer() => WithWorld(() =>
        {
            var db = Database();
            var id = db.GetResourceIDFromName("CarbonRegolith");
            var container = Container(db, ("CarbonRegolith", 100));
            Assert.AreEqual(100, container.GetResourceStoredUnits(id), "the definition's initial units, as after UpdateSavedVesselPartDefinitions");
            var r = OreMigration.Migrate(Saved(("TestRocks", 1500)), container, db, "CarbonRegolith");
            Assert.IsTrue(r.Migrated);
            Assert.AreEqual(75, container.GetResourceStoredUnits(id), 1e-9, "1500 x 0.05 t / 1 t: the stored mass kept");
            Assert.IsFalse(r.Capped);

            var small = Container(db, ("CarbonRegolith", 50));
            r = OreMigration.Migrate(Saved(("TestRocks", 1500)), small, db, "CarbonRegolith");
            Assert.IsTrue(r.Capped);
            Assert.AreEqual(50, small.GetResourceStoredUnits(id), "capped at the capacity");
        });

        [Test] public void OreMigration_LeavesEverythingElse() => WithWorld(() =>
        {
            var db = Database();
            var id = db.GetResourceIDFromName("CarbonRegolith");
            void Untouched(Dictionary<string, ContainedResourceState> saved, ResourceContainer c, string target, string why)
            {
                var before = c.IsResourceContained(id) ? c.GetResourceStoredUnits(id) : -1;
                Assert.IsFalse(OreMigration.Migrate(saved, c, db, target).Migrated, why);
                Assert.AreEqual(before, c.IsResourceContained(id) ? c.GetResourceStoredUnits(id) : -1, why);
            }
            Untouched(Saved(("TestRocks", 1500)), Container(db, ("CarbonRegolith", 100), ("TestRocks", 100)), "CarbonRegolith", "the container still holds TestRocks");
            Untouched(Saved(("TestRocks", 1500)), Container(db, ("Methane", 100)), "CarbonRegolith", "no target in the container");
            Untouched(Saved(("TestRocks", 0)), Container(db, ("CarbonRegolith", 100)), "CarbonRegolith", "nothing stored");
            Untouched(Saved(("Methane", 5)), Container(db, ("CarbonRegolith", 100)), "CarbonRegolith", "no TestRocks saved");
            Untouched(Saved(("TestRocks", 1500)), Container(db, ("CarbonRegolith", 100)), "Unobtainium", "unknown target");
            Assert.IsFalse(OreMigration.Migrate(Saved(("TestRocks", 1500)), Container(db, ("CarbonRegolith", 100)), null, "CarbonRegolith").Migrated, "no database");
        });

        /// <summary>The final review's C1: Patch Manager's <c>UpdateSavedVesselPartDefinitions</c> adds the definition's
        /// containers the save lacks, at their initial units, before <c>SetState</c>. A 0.9.0 save then holds both TestRocks
        /// and the target: it is still migrated, and the injected amount is replaced, not added to.</summary>
        [Test] public void OreMigration_PatchManagerAddedTheTargetFirst_StillMigrated_Replaced() => WithWorld(() =>
        {
            var db = Database();
            var id = db.GetResourceIDFromName("CarbonRegolith");
            foreach (var injected in new[] { 0.0, 10.0, 100.0 })
            {
                var container = Container(db, ("CarbonRegolith", 100));
                var saved = Saved(("TestRocks", 1500), ("CarbonRegolith", injected));
                var r = OreMigration.Migrate(saved, container, db, "CarbonRegolith");
                Assert.IsTrue(r.Migrated, "injected " + injected);
                Assert.AreEqual(75, container.GetResourceStoredUnits(id), 1e-9, "replaced, not added: injected " + injected);
                Assert.AreEqual(injected, r.ReplacedUnits, 1e-12);
                Assert.IsFalse(r.Capped);
            }
            var small = Container(db, ("CarbonRegolith", 50));
            var capped = OreMigration.Migrate(Saved(("TestRocks", 1500), ("CarbonRegolith", 50)), small, db, "CarbonRegolith");
            Assert.IsTrue(capped.Capped);
            Assert.AreEqual(50, small.GetResourceStoredUnits(id), "min(capacity, converted)");
        });

        /// <summary>No double migration: what the game saves after a migration (<c>GetState</c> writes only the contained
        /// resources: the target, no TestRocks), plus Patch Manager's pass at the next load (nothing to add), is left alone.</summary>
        [Test] public void OreMigration_TheNextSave_NotMigratedAgain() => WithWorld(() =>
        {
            var db = Database();
            var id = db.GetResourceIDFromName("CarbonRegolith");
            var container = Container(db, ("CarbonRegolith", 100));
            Assert.IsTrue(OreMigration.Migrate(Saved(("TestRocks", 1500), ("CarbonRegolith", 0)), container, db, "CarbonRegolith").Migrated);
            var next = Saved(("CarbonRegolith", container.GetResourceStoredUnits(id)));
            var reloaded = Container(db, ("CarbonRegolith", 100));
            reloaded.SetResourceStoredUnits(id, 75);
            Assert.IsFalse(OreMigration.Migrate(next, reloaded, db, "CarbonRegolith").Migrated);
            Assert.AreEqual(75, reloaded.GetResourceStoredUnits(id), 1e-9);
        });

        [Test] public void OreMigration_PartLine_NamesTheReplacedAmount() =>
            Assert.AreEqual("[KSP1PartMod] native: ore: old save: T: saved TestRocks 1500 moved to CarbonRegolith 75 (mass kept, " +
                            "replaces the CarbonRegolith 10 the load gave the part)",
                OreMigration.Lines.Part("T", 1500, "CarbonRegolith", 75, null, 10));

        [Test] public void OreMigration_OneLinePerPartType_AndASessionTotal()
        {
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "CarbonRegolith"));
            var scheduled = 0;
            OreMigration.ScheduleTotal = () => scheduled++;
            var r = new OreMigration.Result { Migrated = true, SourceUnits = 1500, TargetUnits = 75, Capacity = 100 };
            OreMigration.Count("RadialOreTank", "CarbonRegolith", r);
            OreMigration.Count("RadialOreTank", "CarbonRegolith", r);
            OreMigration.Count("SmallTank", "CarbonRegolith", new OreMigration.Result { Migrated = true, SourceUnits = 300, TargetUnits = 10, Capacity = 10, Capped = true });
            Assert.AreEqual(1, scheduled, "one total per batch");
            OreMigration.LogTotal();
            CollectionAssert.AreEqual(new[]
            {
                IsruLines.OreImported("CarbonRegolith"),
                "[KSP1PartMod] native: ore: old save: RadialOreTank: saved TestRocks 1500 moved to CarbonRegolith 75 (mass kept)",
                "[KSP1PartMod] native: ore: old save: SmallTank: saved TestRocks 300 moved to CarbonRegolith 10 (mass kept, capped at the capacity 10)",
                "[KSP1PartMod] native: ore: old saves: 3 part(s) migrated this session, TestRocks 3300 -> CarbonRegolith 160",
            }, _lines);
            OreMigration.Count("SmallTank", "CarbonRegolith", r);
            Assert.AreEqual(2, scheduled, "a later batch schedules a new total");
        }

        [Test] public void OreRemap_DeferredCheck_Logged()
        {
            Assert.IsTrue(OreRemap.Arm(new Harmony(OreRemap.HarmonyId), "CarbonRegolith"));
            OreRemap.EnsureVerified();
            OreRemap.EnsureVerified();
            Assert.AreEqual(1, _lines.Count(l => l == IsruLines.OreCheckDeferred("CarbonRegolith")));
        }


        // ------------------------------------------------------------------ mode names

        [Test] public void AddPart_RegistersOneTermPerConverter_ResolvedByRedux()
        {
            Assert.IsTrue(Arm(Database()));
            var builder = new Ksp1PartLocalizationBuilder(Ksp1LocalizationCatalog.FromFiles(new Ksp1ConfigFile[0]));
            var part = Part(Isru() + "MODULE\n{\nname = ModuleResourceConverter\nOUTPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 1\n}\n}\n");
            builder.AddPart(part, "testPart");
            var csv = builder.BuildCsv();
            StringAssert.Contains("\"Parts/Ksp1Converter/testPart/0\",Text,,\"Lf+Ox\",", csv);
            StringAssert.Contains("\"Parts/Ksp1Converter/testPart/3\",Text,,\"Oxidizer\",", csv);
            StringAssert.Contains("\"Parts/Ksp1Converter/testPart/4\",Text,,\"Converter 5\",", csv, "no ConverterName");
            StringAssert.Contains("Parts/Title/testPart", csv, "Redux's own rows kept");

            var plain = new Ksp1PartLocalizationBuilder(Ksp1LocalizationCatalog.FromFiles(new Ksp1ConfigFile[0]));
            plain.AddPart(Part("MODULE\n{\nname = ModuleDecouple\n}\n", "other"), "other");
            StringAssert.DoesNotContain("Ksp1Converter", plain.BuildCsv(), "a part without converters gets no row");

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: converter names: 5 term(s) added to Redux's part localization");
        }

        [Test] public void AddPart_AutoLocName_ResolvedByReduxsCatalog()
        {
            Assert.IsTrue(Arm(Database()));
            var dictionary = Ksp1ConfigNode.Parse("Localization\n{\nen-us\n{\n#autoLOC_6002401 = Lf+Ox Converter\n}\n}\n");
            var catalog = Ksp1LocalizationCatalog.FromFiles(new[] { new Ksp1ConfigFile(null, "Localization/en-us.cfg", "Squad", dictionary) });
            var builder = new Ksp1PartLocalizationBuilder(catalog);
            builder.AddPart(Part(Converter("#autoLOC_6002401", Flow("OUTPUT_RESOURCE", "ElectricCharge", "1"))), "testPart");
            StringAssert.Contains("\"Parts/Ksp1Converter/testPart/0\",Text,,\"Lf+Ox Converter\",", builder.BuildCsv());
        }

        [Test] public void ConvertersOff_NoNameRows()
        {
            Assert.IsTrue(Arm(Database(), "{\"ksp1_converters\": false}"));
            var builder = new Ksp1PartLocalizationBuilder(Ksp1LocalizationCatalog.FromFiles(new Ksp1ConfigFile[0]));
            builder.AddPart(Part(Isru()), "testPart");
            StringAssert.DoesNotContain("Ksp1Converter", builder.BuildCsv());
        }
    }
}
