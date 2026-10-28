using System;
using System.Collections;
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
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), radiators plan Task 2: the radiator members resolve by name
    /// (the list form of <c>CreateSerialized</c>, <c>Data_Cooler</c>, <c>Data_ActiveRadiator</c>), and Redux's real
    /// <c>Ksp1PartModuleImporterRegistry</c>, built with our hook armed, imports in-memory stock-like radiators as KSP2's
    /// <c>Module_Cooler</c> or <c>Module_ActiveRadiator</c>. <c>Data_Cooler</c> implements Unity.Entities' <c>IComponentData</c>,
    /// which this assembly does not reference: it is read through <see cref="RadiatorAccess"/>.</summary>
    public class RadiatorImportTests
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
            RadiatorImporter.ResetSession();
            RadiatorAccess.Reset();
        }

        /// <summary>Arms the hook; the importer then reads <paramref name="db"/> and the fallback cooler reference (no KSP2
        /// part in the editor).</summary>
        private bool Arm(string json = "{}", ResourceDefinitionDatabase db = null)
        {
            var settings = AnimationSettings.Parse(json, out _);
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), settings);
            var database = db ?? Database();
            ConverterImporter.DatabaseSource = () => database;
            RadiatorImporter.ReferenceSource = () => new (string, double, double)[0];
            return armed;
        }

        private static ResourceDefinitionDatabase Database()
        {
            var db = new ResourceDefinitionDatabase();
            foreach (var (name, mass) in new[] { ("ElectricCharge", "0"), (Xenon, "0.0001") })
                db.AddOrUpdateDefinition(Ksp1ResourceMapper.ToResourceDefinition(
                    Ksp1ConfigNode.Parse("RESOURCE_DEFINITION\n{\nname = " + name + "\ndensity = " + mass + "\n}\n").GetNodes("RESOURCE_DEFINITION").First()));
            return db;
        }

        /// <summary>Redux's name for KSP1's XenonGas.</summary>
        private static string Xenon => Ksp1ResourceMapper.MapResourceName("XenonGas");

        private static string Radiator(string energy, string body = "", string resource = "ElectricCharge", string rate = "0.025") =>
            "MODULE\n{\nname = ModuleActiveRadiator\nmaxEnergyTransfer = " + energy + "\novercoolFactor = 0.25\nisCoreRadiator = true\n" + body +
            "RESOURCE\n{\nname = " + resource + "\nrate = " + rate + "\n}\n}\n";

        private static string Deployable() =>
            "MODULE\n{\nname = ModuleDeployableRadiator\nanimationName = Deploy\npivotName = Pivot\nwindResistance = 2.5\ntrackingSpeed = 0.1\n}\n";

        /// <summary>KSP1's stock large fixed panel, as its cfg (values written for the test).</summary>
        private static string RadPanelLg => Radiator("10000", "parentCoolingOnly = true\n");

        /// <summary>KSP1's stock large folding radiator.</summary>
        private static string FoldingRadLarge => Deployable() + Radiator("50000", rate: "0.5");

        private static Ksp1ConfigNode Part(string modules, string name = "testPart") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\n" + modules + "}\n").GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };

        private static List<Ksp1ConfigNode> Modules(Ksp1ConfigNode part) => part.GetNodes("MODULE").ToList();

        private static Ksp1ConfigNode RadiatorNode(Ksp1ConfigNode part, int i = 0) =>
            Modules(part).Where(m => m.GetValue("name") == RadiatorMapping.Ksp1Module).ElementAt(i);

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        private static ModuleData Cooler(Ksp1PartImportContext context) => context.ModuleData.Single(RadiatorAccess.IsCooler);

        private static (string Name, float Rate, double Threshold)[] Resources(ModuleData cooler) =>
            RadiatorAccess.ResourcesOf(cooler).Select(r => (r.ResourceName, r.Rate, r.AcceptanceThreshold)).ToArray();

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_EveryRadiatorMember()
        {
            var t = RadiatorAccess.Resolve();
            Assert.AreEqual("", string.Join(", ", t.MissingCooler.Concat(t.MissingActive)));
            Assert.IsTrue(t.HasListForm);
            Assert.IsTrue(t.CreateSerializedList.IsStatic);
            Assert.IsFalse(t.CreateSerializedList.IsPublic, "private in 26w42c");
            CollectionAssert.AreEqual(new[] { typeof(Type), typeof(IReadOnlyList<ModuleData>), typeof(Ksp1PartImportContext) },
                t.CreateSerializedList.GetParameters().Select(p => p.ParameterType));
            Assert.AreEqual(typeof(SerializedPartModule), t.CreateSerializedList.ReturnType);

            Assert.AreEqual(typeof(Module_Cooler), t.ModuleCooler);
            Assert.AreEqual(typeof(Module_ActiveRadiator), t.ModuleActiveRadiator);
            Assert.IsTrue(typeof(Module_Cooler).IsAssignableFrom(t.ModuleActiveRadiator), "Module_ActiveRadiator is a Module_Cooler");
            Assert.AreEqual("Data_Cooler", t.DataCooler.Name);
            Assert.AreEqual(typeof(double), t.FluxRemoved.FieldType);
            Assert.AreEqual(typeof(List<PartModuleResourceSetting>), t.RequiredResources.FieldType);
            Assert.AreEqual(typeof(ModuleProperty<bool>), t.CoolerEnabled.FieldType);
            Assert.AreEqual("CoolerStates", t.CurrentCoolerState.PropertyType.Name);
            Assert.IsTrue(t.CurrentCoolerState.GetSetMethod().IsPublic);
            Assert.AreEqual(typeof(Data_ActiveRadiator), t.DataActiveRadiator);
            Assert.AreEqual(typeof(Data_LiftingSurface.TransformDir), t.TransformDir);
            Assert.AreEqual(typeof(float), t.FluxPerArea.FieldType);
            CollectionAssert.AreEquivalent(new[] { "resourceDisplayNames", "emissiveMaterialNames", "InputResourcesIDs", "RequestConfigs" },
                t.EmptyCollections.Select(f => f.Name), "the collections the native part data holds empty");
            CollectionAssert.IsEmpty(RadiatorImporter.MissingMembers());

            // KSP2's own start: off, OFF.
            var native = (ModuleData)Activator.CreateInstance(t.DataCooler);
            Assert.IsFalse(RadiatorAccess.EnabledOf(native));
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(native));
            Assert.AreEqual(typeof(Module_Cooler), native.ModuleType);
        }

        [Test] public void HiddenListForm_OnlyTheActiveRadiatorMissing()
        {
            RadiatorAccess.HideListFormForTests = true;
            var t = RadiatorAccess.Resolve();
            CollectionAssert.IsEmpty(t.MissingCooler);
            Assert.IsFalse(t.HasListForm);
            Assert.IsFalse(t.HasActiveRadiator);
        }

        // ------------------------------------------------------------------ Redux's real import

        [Test] public void Arm_AddsTheRadiatorImporter_NotTheDeployable()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, " +
                                              "ModuleAblator -> Module_Heatshield, ModuleResourceConverter -> Module_ResourceConverter, " +
                                              "ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, " +
                                              "ModuleResourceScanner -> Module_ResourceScanner, ModuleActiveRadiator -> Module_Cooler");
            var importers = Importers(new Ksp1PartModuleImporterRegistry());
            Assert.IsInstanceOf<RadiatorImporter>(importers["moduleactiveradiator"]);
            Assert.IsFalse(importers.ContainsKey(RadiatorMapping.DeployableModule), "no importer of ours: Redux's unsupported line stays");
        }

        [Test] public void SwitchOff_NoRadiatorImporter_DroppedByRedux()
        {
            Assert.IsTrue(Arm("{\"ksp1_radiators\": false}"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleActiveRadiator -> Module_Cooler off (ksp1_radiators false)");
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsFalse(Importers(registry).ContainsKey(RadiatorMapping.Ksp1Module));
            var part = Part(RadPanelLg);
            Assert.IsFalse(registry.TryImport(RadiatorNode(part), Context(part), out _), "unsupported, dropped by Redux");
        }

        [Test] public void RadPanelLg_ModuleCooler_200kW_Ec2_OnAndIdle()
        {
            Assert.IsTrue(Arm());
            var part = Part(RadPanelLg, "radPanelLg");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out var module));

            Assert.AreEqual(typeof(Module_Cooler), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_Cooler", module.ComponentType.Name);
            Assert.AreEqual(1, module.ModuleData.Count);
            var cooler = Cooler(context);
            Assert.AreSame(cooler, context.ModuleData.Single());
            Assert.AreSame(cooler, module.ModuleData[0].DataObject);
            Assert.AreEqual(200, RadiatorAccess.FluxOf(cooler), 1e-9, "10000 / 50");
            CollectionAssert.AreEqual(new[] { ("ElectricCharge", 2f, 0.01) }, Resources(cooler), "200 kW at KSP2's 100 kW per EC/s");
            Assert.IsTrue(RadiatorAccess.EnabledOf(cooler));
            Assert.AreEqual("IDLE", RadiatorAccess.StateOf(cooler));
            foreach (var f in RadiatorAccess.Get().EmptyCollections)
            {
                var value = f.GetValue(cooler);
                Assert.IsNotNull(value, f.Name + ": empty, as the native part data, never null");
                Assert.AreEqual(0, ((ICollection)value).Count, f.Name);
            }

            CollectionAssert.AreEqual(new[] { RadiatorMapping.NativeCooler }, NativeModules.Record.ConvertedModules("radPanelLg"));
            CollectionAssert.Contains(_lines, RadiatorLines.CoolerReference(NativeCoolerRef.Fallback));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: cooler reference: no KSP2 cooler found, fallback 100 kW per EC/s");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: radPanelLg: radiator -> Module_Cooler: 200 kW (maxEnergyTransfer 10000 / 50 x 1), " +
                                              "ElectricCharge 2 (KSP1 ElectricCharge 0.025), on at start");
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: radPanelLg: ModuleActiveRadiator: parentCoolingOnly = true not reproduced"));

            // A rebuild of the same part logs and counts nothing twice.
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), Context(part), out _));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("radPanelLg: radiator -> ")));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("cooler reference:")), "read once");
        }

        [Test] public void FoldingRadLarge_ModuleActiveRadiator_BothData_1000kW()
        {
            Assert.IsTrue(Arm());
            var part = Part(FoldingRadLarge, "foldingRadLarge");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out var module));

            Assert.AreEqual(typeof(Module_ActiveRadiator), module.BehaviourType);
            Assert.AreEqual("PartComponentModule_ActiveRadiator", module.ComponentType.Name);
            Assert.AreEqual(2, module.ModuleData.Count, "Data_Cooler and Data_ActiveRadiator in one module");
            Assert.AreEqual(2, context.ModuleData.Count);
            var cooler = Cooler(context);
            Assert.AreSame(cooler, module.ModuleData[0].DataObject, "Data_Cooler first, as KSP2's own deployable radiator");
            var active = (Data_ActiveRadiator)module.ModuleData[1].DataObject;
            Assert.AreSame(active, context.ModuleData[1]);
            Assert.AreEqual(Data_LiftingSurface.TransformDir.X, active.RadiatorDirection);
            Assert.AreEqual(0f, active.ProceduralRadiatorFluxPerAreaUnit);
            Assert.AreEqual(1000, RadiatorAccess.FluxOf(cooler), 1e-9);
            CollectionAssert.AreEqual(new[] { ("ElectricCharge", 10f, 0.01) }, Resources(cooler));
            Assert.IsFalse(RadiatorAccess.EnabledOf(cooler), "a deployable radiator starts off: the bridge turns it on when extended");
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(cooler));

            CollectionAssert.AreEqual(new[] { RadiatorMapping.NativeActiveRadiator }, NativeModules.Record.ConvertedModules("foldingRadLarge"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: foldingRadLarge: radiator -> Module_ActiveRadiator: 1000 kW " +
                                              "(maxEnergyTransfer 50000 / 50 x 1), ElectricCharge 10 (KSP1 ElectricCharge 0.5), off at start");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: foldingRadLarge: " +
                                              RadiatorImporter.DeployableNote(new[] { "pivotName", "windResistance", "trackingSpeed" }));
        }

        [Test] public void SecondRadiatorNode_ReturnsFalse_Merged()
        {
            Assert.IsTrue(Arm());
            var part = Part(Radiator("10000") + Radiator("5000"));
            var context = Context(part);
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsTrue(registry.TryImport(RadiatorNode(part, 0), context, out _));
            Assert.IsFalse(registry.TryImport(RadiatorNode(part, 1), context, out var second));
            Assert.IsNull(second.ModuleData);
            Assert.AreEqual(1, context.ModuleData.Count, "one cooler per part");
            Assert.AreEqual(300, RadiatorAccess.FluxOf(Cooler(context)), 1e-9, "(10000 + 5000) / 50");
            CollectionAssert.AreEqual(new[] { ("ElectricCharge", 3f, 0.01) }, Resources(Cooler(context)));

            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: radiators: 1 part(s) got Module_Cooler, 0 Module_ActiveRadiator " +
                                                    "(300 kW in total); merged: 1;"));
        }

        [Test] public void ListFormHidden_FoldingRadLarge_ModuleCooler_AndTheLine()
        {
            RadiatorAccess.HideListFormForTests = true;
            Assert.IsTrue(Arm());
            var part = Part(FoldingRadLarge, "foldingRadLarge");
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out var module));
            Assert.AreEqual(typeof(Module_Cooler), module.BehaviourType);
            Assert.AreEqual(1, module.ModuleData.Count);
            Assert.AreEqual(1000, RadiatorAccess.FluxOf(Cooler(context)), 1e-9);
            Assert.IsTrue(RadiatorAccess.EnabledOf(Cooler(context)), "read as a fixed radiator: it cools folded, so it starts on");
            CollectionAssert.AreEqual(new[] { RadiatorMapping.NativeCooler }, NativeModules.Record.ConvertedModules("foldingRadLarge"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: radiators: the list form of CreateSerialized not found, " +
                                              "deployable radiators get Module_Cooler (they cool folded)");

            var other = Part(FoldingRadLarge, "foldingRadMed");
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(other), Context(other), out _));
            Assert.AreEqual(1, _lines.Count(l => l == RadiatorLines.ListFormMissing), "once");
        }

        [Test] public void ImporterException_LeavesTheModuleDataAsBefore()
        {
            Assert.IsTrue(Arm());
            RadiatorImporter.FaultForTests = p => throw new InvalidOperationException("fault");
            foreach (var cfg in new[] { RadPanelLg, FoldingRadLarge })
            {
                RadiatorImporter.ResetSession();
                RadiatorImporter.FaultForTests = p => throw new InvalidOperationException("fault");
                RadiatorImporter.ReferenceSource = () => new (string, double, double)[0];
                _lines.Clear();
                var part = Part(cfg);
                var context = Context(part);
                var other = new Data_ToggleCrossFeed();
                context.ModuleData.Add(other);
                Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out var module));
                Assert.IsNull(module.ModuleData);
                Assert.AreSame(other, context.ModuleData.Single(), "no orphan Data_Cooler or Data_ActiveRadiator");
                CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("testPart"), "not counted");
                Assert.That(_lines, Has.None.Contains("testPart: radiator -> "), "no line for a failed part");
                NativeModules.Record.Clear();
            }
            Assert.AreEqual(1, _lines.Count(l => l.Contains("testPart: ModuleActiveRadiator not converted")));
        }

        [Test] public void ContextWithACooler_NothingAdded_Noted()
        {
            Assert.IsTrue(Arm());
            var part = Part(RadPanelLg);
            var context = Context(part);
            var redux = RadiatorAccess.NewCooler(5, new[] { ("ElectricCharge", 1.0) }, false);
            context.ModuleData.Add(redux);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out _));
            Assert.AreSame(redux, context.ModuleData.Single());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testPart: " + RadiatorImporter.ExistingCoolerNote);
        }

        /// <summary>B9's module-data route (0.9.0): a scratch context called with an edited copy of the radiator builds the
        /// cooler from that copy.</summary>
        [Test] public void ScratchContext_EditedCopy_ReplacesTheRadiator()
        {
            Assert.IsTrue(Arm());
            var registry = new Ksp1PartModuleImporterRegistry();
            var part = Part(RadPanelLg);
            var edited = RadiatorNode(Part(Radiator("20000", "parentCoolingOnly = true\n")));
            var before = Context(part);
            Assert.IsTrue(registry.TryImport(RadiatorNode(part), before, out _));
            var scratch = Context(part);
            Assert.IsTrue(registry.TryImport(edited, scratch, out _));
            Assert.AreEqual(200, RadiatorAccess.FluxOf(Cooler(before)), 1e-9);
            Assert.AreEqual(400, RadiatorAccess.FluxOf(Cooler(scratch)), 1e-9, "the copy replaces the radiator, not added to it");
        }

        [Test] public void WithCalled_TheMostSimilarRadiatorIsReplaced()
        {
            var part = Part(Radiator("10000") + Radiator("5000", "parentCoolingOnly = true\n"));
            var raw = Modules(part);
            var edited = RadiatorNode(Part(Radiator("6000", "parentCoolingOnly = true\n")));
            var list = RadiatorImporter.WithCalled(raw, edited);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("10000", list[0].GetValue("maxEnergyTransfer"));
            Assert.AreEqual("6000", list[1].GetValue("maxEnergyTransfer"));
            var neither = RadiatorImporter.WithCalled(raw, RadiatorNode(Part(Radiator("1", "x = 1\n", "XenonGas", "9"))));
            Assert.AreEqual(2, neither.Count, "a node like neither still replaces one");
            Assert.AreEqual("1", neither[0].GetValue("maxEnergyTransfer"), "ties: the first");
            Assert.AreEqual(1, RadiatorImporter.WithCalled(new List<Ksp1ConfigNode>(), edited).Count, "no radiator in the part: appended");
        }

        [Test] public void UnknownResource_Dropped_CoolerStays_XenonKeepsItsRate()
        {
            Assert.IsTrue(Arm());
            var part = Part(Radiator("10000", "RESOURCE\n{\nname = Karbonite\nrate = 1\n}\nRESOURCE\n{\nname = XenonGas\nrate = 0.1\n}\n"));
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out _));
            var res = Resources(Cooler(context));
            CollectionAssert.AreEquivalent(new[] { Xenon, "ElectricCharge" }, res.Select(r => r.Name));
            Assert.AreEqual(IsruAccess.ConvertAmount("XenonGas", Xenon, 0.1, Database()), res.Single(r => r.Name == Xenon).Rate, 1e-6,
                "a non-EC resource keeps its cfg rate, through Redux's conversion");
            Assert.AreEqual(2f, res.Single(r => r.Name == "ElectricCharge").Rate, 1e-6f);
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("resources dropped: 1 (unknown resource)"));
        }

        [Test] public void Ksp1Rates_FluxScale_AutoOff()
        {
            Assert.IsTrue(Arm("{\"radiator_ksp1_rates\": true, \"radiator_flux_scale\": 0.5, \"radiator_auto_on\": false}"));
            var part = Part(RadPanelLg);
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out _));
            var cooler = Cooler(context);
            Assert.AreEqual(100, RadiatorAccess.FluxOf(cooler), 1e-9);
            Assert.AreEqual(0.025f, Resources(cooler).Single().Rate, 1e-7f);
            Assert.IsFalse(RadiatorAccess.EnabledOf(cooler));
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(cooler));
        }

        // ------------------------------------------------------------------ the native reference

        [Test] public void Reference_FromTheGamesCoolers_PacesTheEc()
        {
            Assert.IsTrue(Arm());
            RadiatorImporter.ReferenceSource = () => new[] { ("radiator_2v_radial_fixed", 200.0, 4.0), ("radiator_2v_radial_1x8", 500.0, 10.0) };
            var part = Part(RadPanelLg);
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out _));
            Assert.AreEqual(4f, Resources(Cooler(context)).Single().Rate, 1e-6f, "200 kW at 50 kW per EC/s");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: cooler reference: 2 KSP2 cooler(s), median 50 kW per EC/s " +
                                              "(radiator_2v_radial_1x8, radiator_2v_radial_fixed)");
        }

        [Test] public void Reference_ReadFails_Fallback()
        {
            Assert.IsTrue(Arm());
            RadiatorImporter.ReferenceSource = () => throw new InvalidOperationException("no parts");
            var part = Part(RadPanelLg);
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(part), context, out _));
            Assert.AreEqual(2f, Resources(Cooler(context)).Single().Rate, 1e-6f);
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: cooler reference not read, fallback used:"));
            CollectionAssert.Contains(_lines, RadiatorLines.CoolerReference(NativeCoolerRef.Fallback));
        }

        /// <summary><see cref="RadiatorImporter.CoolersOf"/> on real <c>PartCore</c>s: the live modules first, else the
        /// serialized ones; KSP1 imports, '+' variants and coolers without EC left out.</summary>
        [Test] public void CoolersOf_RealPartCores()
        {
            Assert.IsTrue(Arm());
            var fixedPart = Part(Radiator("10000", rate: "0.025"), "fixed");
            var fixedContext = Context(fixedPart);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(fixedPart), fixedContext, out var fixedModule));
            var liveCooler = RadiatorAccess.NewCooler(500, new[] { ("ElectricCharge", 5.0) }, false);
            var xenon = RadiatorAccess.NewCooler(100, new[] { ("XenonGas", 1.0) }, false);
            PartCore Core(string name, string tags, List<ModuleData> live, List<SerializedPartModule> serialized) => new PartCore
            {
                data = new PartData { partName = name, tags = tags, serializedPartModules = serialized ?? new List<SerializedPartModule>() },
                modules = live,
            };
            var parts = new Dictionary<string, PartCore>
            {
                ["radiator_2v_radial_fixed"] = Core("radiator_2v_radial_fixed", "radiators", null, new List<SerializedPartModule> { fixedModule }),
                ["radiator_2v_radial_1x8"] = Core("radiator_2v_radial_1x8", "", new List<ModuleData> { liveCooler, new Data_ActiveRadiator() }, null),
                ["xenonCooler"] = Core("xenonCooler", "", new List<ModuleData> { xenon }, null),
                ["radPanelLg"] = Core("radPanelLg", "radiator Squad Thermal KSP1", new List<ModuleData> { liveCooler }, null),
                ["radiator_2v_radial_1x8+big"] = Core("radiator_2v_radial_1x8+big", "", new List<ModuleData> { liveCooler }, null),
            };
            var coolers = RadiatorImporter.CoolersOf(parts).ToList();
            CollectionAssert.AreEqual(new[] { ("radiator_2v_radial_fixed", 200.0, 2.0), ("radiator_2v_radial_1x8", 500.0, 5.0) },
                coolers.Select(c => (c.Part, c.Kw, Math.Round(c.EcPerSecond, 6))));
            Assert.AreEqual(100, NativeCoolerRef.From(coolers).KwPerEc, 1e-9);
        }

        // ------------------------------------------------------------------ summary

        [Test] public void Summary_CountsBothModules_AndThePassiveCensus()
        {
            Assert.IsTrue(Arm());
            var lg = Part(RadPanelLg, "radPanelLg");
            var folding = Part(FoldingRadLarge, "foldingRadLarge");
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(lg), Context(lg), out _));
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(RadiatorNode(folding), Context(folding), out _));
            RadiatorImporter.PassiveSource = () => new[]
            {
                Part("emissiveConstant = 0.9\n", "passive"),
                Ksp1ConfigNode.Parse("PART\n{\nname = active\nemissiveConstant = 0.95\n" + RadPanelLg + "}\n").GetNodes("PART").First(),
                Part("emissiveConstant = 0.5\n", "dull"),
                Part("", "none"),
                Part("emissiveConstant = 0.8\n", "edge"),
            };
            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: radiators: 1 part(s) got Module_Cooler, 1 Module_ActiveRadiator (1200 kW in total); " +
                                              "merged: 0; resources dropped: 0 (unknown resource); not reproduced: isCoreRadiator 2, " +
                                              "load-proportional EC 2, overcoolFactor 2, parentCoolingOnly 1, pivotName 1, trackingSpeed 1, windResistance 1");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: radiators: passive: 2 KSP1 part(s) with emissiveConstant >= 0.8 and no radiator " +
                                              "module radiate through KSP2's own thermal model (Redux imports emissiveConstant)");
        }

        [Test] public void Summary_NoRadiator_ZeroCounts_RegistryRead()
        {
            Assert.IsTrue(Arm());
            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: radiators: 0 part(s) got Module_Cooler, 0 Module_ActiveRadiator (0 kW in total); " +
                                              "merged: 0; resources dropped: 0 (unknown resource); not reproduced: none");
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: radiators: passive: "), "Redux's registry read in the editor");
        }
    }
}
