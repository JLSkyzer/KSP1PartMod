using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.B9;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Fairings;
using Newtonsoft.Json;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Fairings plan, Tasks 2 and 3, against the referenced Ksp2Redux.dll (26w42c): the guard members resolve with their
    /// types, the guards arm on the real <c>Module_Fairing</c>, and Redux's real registry, built with our hook armed, imports
    /// in-memory KSP1-shaped fairing bases as KSP2's <c>Module_Fairing</c>; a failure leaves nothing of ours.</summary>
    public class FairingImportTests
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
            FairingFixes.Reset();
        }

        /// <summary>Arms the guards (unless <paramref name="guards"/> is false), then the hook.</summary>
        private bool Arm(string json = "{}", bool guards = true, bool prefabPatch = true)
        {
            if (guards) FairingFixes.TryApply(new Harmony(FairingFixes.HarmonyId), true);
            FairingImporter.PrefabPatchArmedSource = () => prefabPatch;
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), AnimationSettings.Parse(json, out _));
            return armed;
        }

        private const string StockModule = @"
MODULE
{
    name = ModuleProceduralFairing
    nSides = 30
    nArcs = 2
    BaseModelTransformName = Mesh
    pivot = 0,0.17,0
    axis = 0,1,0
    baseRadius = 0.625
    maxRadius = 1.5
    capRadius = 0.2
    UnitAreaMass = 0.01
}
";

        private const string SafModule = @"
MODULE
{
    name = ModuleSimpleAdjustableFairing
    numSlices = 2
    shieldingBaseRadius = 1.27
    ejectionForce = 75
    WALL_BASE
    {
        transformName = Wallbase
        rootOffset = 0, 0.06, 0
    }
    WALL
    {
        transformName = Wall
    }
    CONE
    {
        transformName = Cone
    }
}
";

        private static Ksp1ConfigNode Part(string modules, string name = "testFairingPart") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\nnode_stack_top = 0.0, 0.03, 0.0, 0.0, 1.0, 0.0, 0\n" + modules + "}\n")
                .GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };

        private static Ksp1ConfigNode ModuleNode(Ksp1ConfigNode part, string name, int i = 0) =>
            part.GetNodes("MODULE").Where(m => m.GetValue("name") == name).ElementAt(i);

        // ------------------------------------------------------------------ resolution

        [Test] public void GuardMembers_ResolveWithTheirTypes()
        {
            Assert.AreEqual("", string.Join(", ", FairingFixes.MissingGuardMembers()));
        }

        [Test] public void Guards_ArmOnTheRealModuleFairing()
        {
            Assert.IsFalse(FairingFixes.GuardsArmed);
            FairingFixes.TryApply(new Harmony(FairingFixes.HarmonyId), true);
            Assert.IsTrue(FairingFixes.GuardsArmed);
            FairingFixes.Reset();
            FairingFixes.TryApply(new Harmony(FairingFixes.HarmonyId), false);
            Assert.IsFalse(FairingFixes.GuardsArmed, "fix_fairing_init false: no guards");
        }

        [Test] public void DeployNames_MatchKsp2Enum()
        {
            CollectionAssert.AreEqual(Enum.GetNames(typeof(FairingDeployType)), Enum.GetNames(typeof(FairingDeploy)));
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Arm_WithGuards_BothConvertersArmed()
        {
            Assert.IsTrue(Arm());
            Assert.IsTrue(NativeModules.IsConverterArmed(FairingMapping.ProceduralModule));
            Assert.IsTrue(NativeModules.IsConverterArmed(FairingMapping.SafModule));
            StringAssert.Contains("ModuleProceduralFairing -> Module_Fairing, ModuleSimpleAdjustableFairing -> Module_Fairing",
                _lines.Single(l => l.StartsWith(NativeLines.Prefix + "armed:")));
        }

        [Test] public void Arm_WithoutGuards_NoFairingConverter_OneLineEach()
        {
            Arm(guards: false);
            Assert.IsFalse(NativeModules.IsConverterArmed(FairingMapping.ProceduralModule));
            Assert.IsFalse(NativeModules.IsConverterArmed(FairingMapping.SafModule));
            CollectionAssert.Contains(_lines, FairingLines.ConverterOff(FairingKind.Procedural, FairingLines.GuardsNotArmed));
            CollectionAssert.Contains(_lines, FairingLines.ConverterOff(FairingKind.Saf, FairingLines.GuardsNotArmed));
        }

        [Test] public void Arm_SwitchesOff_As0140()
        {
            Arm("{\"ksp1_procedural_fairings\": false, \"ksp1_saf_fairings\": false}");
            Assert.IsFalse(NativeModules.IsConverterArmed(FairingMapping.ProceduralModule));
            Assert.IsFalse(NativeModules.IsConverterArmed(FairingMapping.SafModule));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleProceduralFairing -> Module_Fairing off (ksp1_procedural_fairings false)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleSimpleAdjustableFairing -> Module_Fairing off (ksp1_saf_fairings false)");
        }

        // ------------------------------------------------------------------ import

        [Test] public void Procedural_AddsOneModuleFairing_WithTheCfgValues()
        {
            Assert.IsTrue(Arm());
            var part = Part(StockModule);
            var context = Context(part);
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsTrue(registry.TryImport(ModuleNode(part, FairingMapping.ProceduralModule), context, out var module));
            Assert.AreEqual(typeof(Module_Fairing), module.BehaviourType);
            var data = context.ModuleData.OfType<Data_Fairing>().Single();
            Assert.AreEqual(0.625f, data.BaseRadius);
            Assert.AreEqual(1.5f, data.MaxRadius);
            Assert.AreEqual(0.2f, data.CapRadius);
            Assert.AreEqual("Mesh", data.BaseModelTransformName);
            Assert.AreEqual(30, data.FairingSideCount);
            Assert.AreEqual(new Vector3(0, 0.17f, 0), data.Pivot);
            Assert.AreEqual(FairingDeployType.Clamshellx4, data.DefaultDeployType);
            Assert.AreEqual(FairingDeployType.Clamshellx4, data.DeployType.GetValue());
            Assert.AreEqual(100f, data.EjectionForce.GetValue());
            Assert.IsFalse(data.AllowInterstageNode);
            Assert.AreEqual(FairingKind.Procedural, FairingImporter.Record.KindOf("testFairingPart"));
            Assert.That(_lines.Any(l => l.StartsWith(NativeLines.Prefix + "testFairingPart: ModuleProceduralFairing -> Module_Fairing: base r 0.625")));
        }

        [Test] public void Saf_TwoClamshells_FromTheTopNode()
        {
            Assert.IsTrue(Arm());
            var part = Part(SafModule);
            var context = Context(part);
            Assert.IsTrue(new Ksp1PartModuleImporterRegistry().TryImport(ModuleNode(part, FairingMapping.SafModule), context, out _));
            var data = context.ModuleData.OfType<Data_Fairing>().Single();
            Assert.AreEqual(0.3125f, data.BaseRadius);
            Assert.AreEqual(1.27f, data.MaxRadius);
            Assert.AreEqual(FairingDeployType.Clamshellx2, data.DefaultDeployType);
            Assert.AreEqual(75f, data.EjectionForce.GetValue());
            Assert.AreEqual(FairingKind.Saf, FairingImporter.Record.KindOf("testFairingPart"));
        }

        [Test] public void ThrownAfterCreate_RemovesWhatWasAdded()
        {
            Assert.IsTrue(Arm());
            FairingImporter.FaultForTests = _ => throw new InvalidOperationException("test");
            var part = Part(StockModule);
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(ModuleNode(part, FairingMapping.ProceduralModule), context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.IsFalse(FairingImporter.Record.Contains("testFairingPart"));
            CollectionAssert.AreEqual(new[] { ("failed", 1) }, FairingImporter.Record.Refusals(FairingKind.Procedural));
        }

        [Test] public void SecondFairingModule_Refused()
        {
            Assert.IsTrue(Arm());
            var part = Part(StockModule + SafModule);
            var context = Context(part);
            var registry = new Ksp1PartModuleImporterRegistry();
            Assert.IsTrue(registry.TryImport(ModuleNode(part, FairingMapping.ProceduralModule), context, out _));
            Assert.IsFalse(registry.TryImport(ModuleNode(part, FairingMapping.SafModule), context, out _));
            Assert.AreEqual(1, context.ModuleData.OfType<Data_Fairing>().Count());
            CollectionAssert.AreEqual(new[] { ("second", 1) }, FairingImporter.Record.Refusals(FairingKind.Saf));
        }

        [Test] public void NoBaseRadius_Refused_NothingAdded()
        {
            Assert.IsTrue(Arm());
            var part = Part(StockModule.Replace("baseRadius = 0.625", "baseRadius = 0"));
            var context = Context(part);
            Assert.IsFalse(new Ksp1PartModuleImporterRegistry().TryImport(ModuleNode(part, FairingMapping.ProceduralModule), context, out _));
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.AreEqual("[KSP1PartMod] native: ModuleProceduralFairing -> Module_Fairing: 0 part(s); refused mapping 1",
                FairingImporter.Summary(FairingKind.Procedural));
        }

        // ------------------------------------------------------------------ B9

        /// <summary>A SAF <c>DATA</c> edit of the 0p625 shape, re-imported in a scratch context: only <c>Data_Fairing</c> definition
        /// fields differ (here <c>MaxRadius</c>, from <c>shieldingBaseRadius</c>), and the scratch import records nothing.</summary>
        [Test] public void B9_SafDataEdit_DiffsOnlyDefinitionFields()
        {
            Assert.IsTrue(Arm());
            Assert.IsTrue(B9ModuleDiff.Bind(B9ModuleDiff.Resolve()));
            var previous = JsonConvert.DefaultSettings;
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                var part = Part(SafModule, "testFairingBase_0p625m_SAF");
                var context = Context(part);
                var registry = new Ksp1PartModuleImporterRegistry();
                Assert.IsTrue(registry.TryImport(ModuleNode(part, FairingMapping.SafModule), context, out var module));
                context.PartData.serializedPartModules.Add(module);
                var data = Ksp1ConfigNode.Parse("DATA\n{\nshieldingBaseRadius = 1.05\nWALL_BASE\n{\nrootOffset = 0,0.06,0\n}\n}\n").GetNodes("DATA").First();
                var adapter = new Ksp1NodeAdapter(ModuleNode(part, FairingMapping.SafModule));
                var index = part.GetNodes("MODULE").ToList().FindIndex(m => m.GetValue("name") == FairingMapping.SafModule);
                var r = B9ModuleDiff.Diff(registry, part, context, index, B9ModuleEdit.EditedCopy(adapter, new Ksp1NodeAdapter(data)));
                Assert.AreEqual(B9ModuleDiff.Outcome.Compared, r.Outcome, r.Reason);
                CollectionAssert.AreEqual(new[] { "Data_Fairing.MaxRadius" }, r.Transformers.Select(t => t.DataType + "." + t.Key));
                Assert.AreEqual(1, FairingImporter.Record.Count(FairingKind.Saf), "the scratch imports record nothing more");
                CollectionAssert.IsEmpty(FairingImporter.Record.Refusals(FairingKind.Saf));
            }
            finally
            {
                JsonConvert.DefaultSettings = previous;
                B9ModuleDiff.Reset();
            }
        }

        // ------------------------------------------------------------------ SAF meshes

        [Test] public void HideSafTransforms_DisablesRenderersAndColliders_ListsMissing()
        {
            var root = new GameObject("prefab");
            try
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(root.transform);
                var mesh = new GameObject("mesh");
                mesh.transform.SetParent(wall.transform);
                var renderer = mesh.AddComponent<MeshRenderer>();
                var collider = mesh.AddComponent<BoxCollider>();
                var other = new GameObject("BaseRing");
                other.transform.SetParent(root.transform);
                var otherRenderer = other.AddComponent<MeshRenderer>();

                var hidden = ShroudImport.HideSafTransforms(root, new[] { "wall", "Cone" }, out var missing);
                Assert.AreEqual(1, hidden);
                CollectionAssert.AreEqual(new[] { "Cone" }, missing);
                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
                Assert.IsTrue(otherRenderer.enabled);
                Assert.IsTrue(wall.activeSelf, "never deactivated or destroyed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
