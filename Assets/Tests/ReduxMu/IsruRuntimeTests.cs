using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using NUnit.Framework;
using Redux.Modules;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is =UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), converters and drills plan Task 4: the runtime glue of KSP1
    /// drills and scanners (ground probe, deploy bridge, drilling line), Redux's real <c>Module_Mine</c> and
    /// <c>Module_ResourceScanner</c> components on test GameObjects, our patch methods called as Harmony would.</summary>
    public class IsruRuntimeTests
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
            LogAssert.ignoreFailingMessages = true; // OnDestroy of the modules reaches for the game, absent in EditMode
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in _objects)
            {
                try { UnityEngine.Object.DestroyImmediate(go); }
                catch (Exception) { /* the modules' OnDestroy reaches for the game */ }
            }
            _objects.Clear();
            Disarm();
            NativeModules.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(IsruRuntime.HarmonyId).UnpatchAll(IsruRuntime.HarmonyId);
            IsruRuntime.Reset();
            NativeModules.Reset();
        }

        private bool Arm(string json = "{}")
        {
            var armed = IsruRuntime.TryApply(new Harmony(IsruRuntime.HarmonyId), AnimationSettings.Parse(json, out _));
            IsruRuntime.PartNameForTests = m => m.gameObject.name;
            return armed;
        }

        /// <summary>A part GameObject named <paramref name="part"/> with a model holding <c>ImpactTransform</c> (scaled 2,
        /// turned 90° about Y), and <typeparamref name="T"/> on the part.</summary>
        private T Part<T>(string part, out Transform impact, bool withImpact = true) where T : Component
        {
            var go = new GameObject(part);
            _objects.Add(go);
            var model = new GameObject("model").transform;
            model.SetParent(go.transform, false);
            model.localScale = new Vector3(2f, 2f, 2f);
            impact = null;
            if (withImpact)
            {
                impact = new GameObject("ImpactTransform").transform;
                impact.SetParent(model, false);
                impact.localPosition = new Vector3(0f, -1f, 0f);
                impact.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            T module;
            try { module = go.AddComponent<T>(); }
            catch (Exception e) { Assert.Ignore($"{typeof(T).Name} cannot be added in EditMode: " + e.Message); return null; }
            return module;
        }

        private static Data_Deployable Deployable(Module_Mine mine) =>
            (Data_Deployable)AccessTools.Field(typeof(Module_Mine), "_dataDeployable").GetValue(mine);

        private static Data_Deployable Deployable(Module_ResourceScanner scanner) =>
            (Data_Deployable)AccessTools.Field(typeof(Module_ResourceScanner), "_dataDeployable").GetValue(scanner);

        private static Transform Arm(Module_Mine mine, string field) =>
            (Transform)AccessTools.Field(typeof(Module_Mine), field).GetValue(mine);

        // ------------------------------------------------------------------ first step (plan (k))

        /// <summary>Task 4, first step: a <c>Data_Deployable</c> built outside any part, never bound to an entity, keeps a
        /// value set on <c>CurrentDeployState</c>, so the deploy bridge is a plain instance (no getter patch).</summary>
        [Test] public void UnboundDataDeployable_KeepsItsDeployState()
        {
            var d = new Data_Deployable();
            Assert.IsFalse(d.IsExtended, "starts retracted");
            d.CurrentDeployState.SetValue(Data_Deployable.DeployState.Extended);
            Assert.IsTrue(d.IsExtended);
            d.CurrentDeployState.SetValue(Data_Deployable.DeployState.Retracting);
            Assert.IsFalse(d.IsExtended);
            Assert.IsTrue(d.IsRetracting);
            d.CurrentDeployState.SetValue(Data_Deployable.DeployState.Extended);
            Assert.IsTrue(d.IsExtended);
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Arm_AllMembersFound_ArmedLine()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] isru: armed: impact transforms on, deploy bridge on, active clips on" }, _lines);
            Assert.IsTrue(IsruRuntime.ImpactTransformsOn);
            Assert.IsTrue(IsruRuntime.DeployBridgeOn);
            var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(IsruRuntime.HarmonyId))
                .Select(m => m.DeclaringType.Name + "." + m.Name).OrderBy(n => n).ToList();
            CollectionAssert.AreEqual(new[] { "Module_Mine.CheckCollisions", "Module_Mine.OnInitialize", "Module_ResourceScanner.OnInitialize" }, patched);
        }

        [Test] public void Arm_SwitchesOff_NothingPatched()
        {
            Assert.IsFalse(Arm("{\"ksp1_drills\": false, \"ksp1_survey_scanners\": false, \"ksp1_resource_scanners\": false, \"isru_active_animation\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] isru: armed: impact transforms off, deploy bridge off, active clips off" }, _lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(IsruRuntime.HarmonyId)));
        }

        [Test] public void Arm_ScannersOnly_NoImpactTransforms()
        {
            Assert.IsTrue(Arm("{\"ksp1_drills\": false, \"enabled\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] isru: armed: impact transforms off, deploy bridge on, active clips off" }, _lines);
        }

        // ------------------------------------------------------------------ ground probe and deploy bridge

        [Test] public void Drill_ProbeSet_FromTheImpactTransform_AlongZ_WorldRange_BridgeExtended()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.SetDrill("RadialDrill", new DrillImpact("ImpactTransform", 5.42, new[] { "CarbonRegolith" }));
            var mine = Part<Module_Mine>("RadialDrill", out var impact);
            _lines.Clear();

            IsruRuntime.BeforeMineInit(mine);
            Assert.AreSame(impact, Arm(mine, "vfxArmTopTransform"));
            var end = Arm(mine, "vfxArmBottomTransform");
            Assert.IsNotNull(end);
            Assert.AreEqual(IsruRuntime.ImpactEndName, end.name);
            Assert.AreSame(impact, end.parent);
            Assert.AreEqual(5.42f, Vector3.Distance(impact.position, end.position), 1e-4f, "metres in the world, the model scaled 2");
            Assert.AreEqual(0f, Vector3.Angle(impact.forward, end.position - impact.position), 1e-3f, "along the transform's +Z");
            Assert.IsTrue(IsruRuntime.TryGetDrillState(mine, out var armsSet) && armsSet);

            IsruRuntime.AfterMineInit(mine);
            var bridge = Deployable(mine);
            Assert.IsNotNull(bridge, "the bridge");
            Assert.IsTrue(bridge.IsExtended, "no Module_Ksp1Animations on the part: always extended");
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] isru: RadialDrill: drill probe 'ImpactTransform' found, 5.42 m along +Z",
                "[KSP1PartMod] isru: RadialDrill: Module_Mine always extended (no deploy clip)",
            }, _lines);

            // A second init: the same end, not a second one; the lines once per part.
            IsruRuntime.BeforeMineInit(mine);
            Assert.AreEqual(1, impact.Cast<Transform>().Count(t => t.name == IsruRuntime.ImpactEndName));
            Assert.AreEqual(2, _lines.Count);
        }

        [Test] public void Drill_ProbeNotFound_NoArms_NoBridge_NoGround()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.SetDrill("Odd", new DrillImpact("Missing", 2, new[] { "Water" }));
            var mine = Part<Module_Mine>("Odd", out _);
            _lines.Clear();
            IsruRuntime.BeforeMineInit(mine);
            IsruRuntime.AfterMineInit(mine);
            Assert.IsNull(Arm(mine, "vfxArmTopTransform"));
            Assert.IsNull(Arm(mine, "vfxArmBottomTransform"));
            Assert.IsNull(Deployable(mine), "ruling: no bridge without the arms, the drill stays NotDeployed");
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] isru: Odd: drill probe 'Missing' not found on the part, the drill stays not deployed (no deploy bridge)",
            }, _lines);
            var result = true;
            Assert.IsFalse(IsruRuntime.BeforeCheckCollisions(mine, ref result), "Redux's raycast skipped");
            Assert.IsFalse(result, "no ground");
        }

        [Test] public void NativeDrill_Untouched()
        {
            Assert.IsTrue(Arm());
            var mine = Part<Module_Mine>("drill_2v_radial", out _);
            IsruRuntime.BeforeMineInit(mine);
            IsruRuntime.AfterMineInit(mine);
            Assert.IsNull(Arm(mine, "vfxArmTopTransform"));
            Assert.IsNull(Deployable(mine));
            Assert.IsFalse(IsruRuntime.TryGetDrillState(mine, out _));
            var result = false;
            Assert.IsTrue(IsruRuntime.BeforeCheckCollisions(mine, ref result), "Redux's own code runs");
        }

        [Test] public void Drill_ReduxDeployableKept()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.SetDrill("RadialDrill", new DrillImpact("ImpactTransform", 5.42, new[] { "CarbonRegolith" }));
            var mine = Part<Module_Mine>("RadialDrill", out _);
            var redux = new Data_Deployable();
            AccessTools.Field(typeof(Module_Mine), "_dataDeployable").SetValue(mine, redux);
            IsruRuntime.BeforeMineInit(mine);
            IsruRuntime.AfterMineInit(mine);
            Assert.AreSame(redux, Deployable(mine));
        }

        [Test] public void Scanner_OursGetsABridge_OthersNot()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.AddScanner("SurveyScanner");
            var ours = Part<Module_ResourceScanner>("SurveyScanner", out _, withImpact: false);
            var native = Part<Module_ResourceScanner>("scanner_1v_orbital", out _, withImpact: false);
            _lines.Clear();
            IsruRuntime.AfterScannerInit(ours);
            IsruRuntime.AfterScannerInit(native);
            Assert.IsTrue(Deployable(ours).IsExtended);
            Assert.IsNull(Deployable(native));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] isru: SurveyScanner: Module_ResourceScanner always extended (no deploy clip)" }, _lines);
        }

        [Test] public void Scanner_AnimationsOff_Reason()
        {
            Assert.IsTrue(Arm("{\"enabled\": false}"));
            NativeModules.Isru.AddScanner("SurfaceScanner");
            var ours = Part<Module_ResourceScanner>("SurfaceScanner", out _, withImpact: false);
            _lines.Clear();
            IsruRuntime.AfterScannerInit(ours);
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] isru: SurfaceScanner: Module_ResourceScanner always extended (animations off)" }, _lines);
        }

        /// <summary>With our animation module on the part, the bridge waits for it: its init with no Group entry extends it.</summary>
        [Test] public void Bridge_HandedToTheAnimationModule()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.SetDrill("RadialDrill", new DrillImpact("ImpactTransform", 5.42, new[] { "CarbonRegolith" }));
            var mine = Part<Module_Mine>("RadialDrill", out _);
            Module_Ksp1Animations anim;
            try { anim = mine.gameObject.AddComponent<Module_Ksp1Animations>(); }
            catch (Exception e) { Assert.Ignore("the animation module cannot be added in EditMode: " + e.Message); return; }
            _lines.Clear();
            IsruRuntime.BeforeMineInit(mine);
            IsruRuntime.AfterMineInit(mine);
            var bridge = Deployable(mine);
            Assert.IsNotNull(bridge);
            Assert.IsFalse(bridge.IsExtended, "waits for the animation module");
            Assert.IsFalse(_lines.Any(l => l.Contains("always extended")));
            anim.FinishIsruInit();
            Assert.IsTrue(bridge.IsExtended);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] isru: RadialDrill: Module_Mine always extended (no deploy clip)");
        }

        // ------------------------------------------------------------------ drilling line

        [Test] public void Drilling_LoggedOnceAfter50OperationalChecks()
        {
            Assert.IsTrue(Arm());
            NativeModules.Isru.SetDrill("RadialDrill", new DrillImpact("ImpactTransform", 5.42, new[] { "CarbonRegolith" }));
            var mine = Part<Module_Mine>("RadialDrill", out _);
            IsruRuntime.BeforeMineInit(mine);
            var data = new Data_Mine
            {
                status = MineState.Operational,
                MiningFormulaDefinition = new ResourceConverterFormulaDefinition
                {
                    InputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "ElectricCharge", Rate = 15 } },
                    OutputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "CarbonRegolith", Rate = 4e-5f } },
                },
                RequestConfigs = new[]
                {
                    new ResourceFlowRequestCommandConfig { FlowUnits = 15 },
                    new ResourceFlowRequestCommandConfig { FlowUnits = 2e-5 },
                },
            };
            AccessTools.Field(typeof(Module_Mine), "_dataMine").SetValue(mine, data);
            _lines.Clear();
            var result = false;
            for (var i = 0; i < IsruRuntime.OperationalChecksBeforeLine - 1; i++) Assert.IsTrue(IsruRuntime.BeforeCheckCollisions(mine, ref result));
            CollectionAssert.IsEmpty(_lines);
            data.status = MineState.Inactive;
            IsruRuntime.BeforeCheckCollisions(mine, ref result);
            data.status = MineState.Operational;
            for (var i = 0; i < IsruRuntime.OperationalChecksBeforeLine - 1; i++) IsruRuntime.BeforeCheckCollisions(mine, ref result);
            CollectionAssert.IsEmpty(_lines, "the count restarts when the drill stops");
            for (var i = 0; i < 200; i++) IsruRuntime.BeforeCheckCollisions(mine, ref result);
            Assert.AreEqual(1, _lines.Count, "once per drill");
            StringAssert.StartsWith("[KSP1PartMod] isru: RadialDrill: drilling on ", _lines[0]);
            StringAssert.EndsWith(": CarbonRegolith 2E-05 units/s (density 0.5), ground at 0 m, EC 15/s", _lines[0]);
            Assert.IsFalse(IsruRuntime.Broken);
        }

        // ------------------------------------------------------------------ allocation

        /// <summary>The bridge write and the active input readers, 10 000 steps.</summary>
        [Test] public void BridgeWrite_AndActiveInputs_DoNotAllocate()
        {
            var bridge = new Data_Deployable();
            BridgeDeploy? last = null;
            var mine = new Data_Mine();
            var converter = new Data_ResourceConverter();
            var scanner = new Data_ResourceScanner();
            var readers = new[] { new NativeActivity(mine).Running, new NativeActivity(converter).Running, new NativeActivity(scanner).Running };
            var states = new[] { BridgeDeploy.Retracted, BridgeDeploy.Extending, BridgeDeploy.Extended, BridgeDeploy.Retracting };
            var i = 0;
            var any = 0;
            void Steps()
            {
                for (var k = 0; k < 10000; k++, i++)
                {
                    IsruRuntime.Write(bridge, ref last, states[(i / 3) & 3]);
                    mine.status = (i & 1) == 0 ? MineState.Operational : MineState.Inactive;
                    converter.conversionState = (i & 2) == 0 ? ResourceConversionState.OperationalLow : ResourceConversionState.NoPower;
                    scanner.status = (i & 4) == 0 ? ResourceScannerStatus.Scanning : ResourceScannerStatus.Idle;
                    for (var r = 0; r < readers.Length; r++)
                        if (readers[r]()) any++;
                }
            }

            Steps(); // warm up (JIT)
            Assert.Greater(any, 0);
            Assert.That(Steps, Is.Not.AllocatingGCMemory());
            IsruRuntime.Write(bridge, ref last, BridgeDeploy.Extended);
            Assert.IsTrue(bridge.IsExtended);
        }

        [Test] public void ActiveInputs_ReadTheNativeStates()
        {
            var mine = new Data_Mine();
            var m = new NativeActivity(mine);
            Assert.AreEqual("Module_Mine", m.NativeModule);
            Assert.IsFalse(m.Running(), "Inactive by default");
            mine.status = MineState.Operational;
            Assert.IsTrue(m.Running());
            mine.status = MineState.InsufficientResource;
            Assert.IsFalse(m.Running());

            var converter = new Data_ResourceConverter();
            var c = new NativeActivity(converter);
            Assert.IsFalse(c.Running());
            foreach (var on in new[] { ResourceConversionState.Operational, ResourceConversionState.OperationalLow })
            {
                converter.conversionState = on;
                Assert.IsTrue(c.Running(), on.ToString());
            }

            var scanner = new Data_ResourceScanner();
            var s = new NativeActivity(scanner);
            Assert.IsFalse(s.Running(), "NotDeployed by default");
            scanner.status = ResourceScannerStatus.Scanning;
            Assert.IsTrue(s.Running());
        }

        // ------------------------------------------------------------------ Task 3 review: the reference on real PartCore data

        /// <summary><c>IsruReference.FormulasOf</c> on real <c>PartCore</c>s: the live modules first, else the serialized ones
        /// (an authored instance, and a snapshot that only holds its JSON); KSP1 imports and '+' variants left out.</summary>
        [Test] public void GameFormulas_RealPartCores()
        {
            var mine = new Data_Mine
            {
                MiningFormulaDefinition = new ResourceConverterFormulaDefinition
                {
                    FormulaLocalizationKey = "Mine/Key",
                    InputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "ElectricCharge", Rate = 20 } },
                    OutputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "Water", Rate = 4.333e-5f } },
                    FluxGenerated = 350,
                },
            };
            var converter = new Data_ResourceConverter
            {
                FormulaDefinitions = new List<ResourceConverterFormulaDefinition>
                {
                    new ResourceConverterFormulaDefinition
                    {
                        InternalName = "HydrogenISRU",
                        InputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "ElectricCharge", Rate = 12 } },
                        OutputResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "Hydrogen", Rate = 1.944e-6f } },
                        FluxGenerated = 100,
                    },
                },
            };
            var scanner = new Data_ResourceScanner
            {
                RequiredResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "ElectricCharge", Rate = 14 } },
                ScannableResources = new List<PartModuleResourceSetting> { new PartModuleResourceSetting { ResourceName = "Water" } },
                TimeToComplete = 5, minAltitude = 0, maxAltitude = 5000000, minInclination = 80, maxInclination = 100,
            };
            PartCore Core(string name, string tags, List<ModuleData> live, List<SerializedPartModule> serialized) => new PartCore
            {
                data = new PartData { partName = name, tags = tags, serializedPartModules = serialized ?? new List<SerializedPartModule>() },
                modules = live,
            };
            SerializedPartModule Serialized(SerializedModuleData d) => new SerializedPartModule { ModuleData = new List<SerializedModuleData> { d } };
            var parts = new Dictionary<string, PartCore>
            {
                ["drill_2v_radial"] = Core("drill_2v_radial", "isru drill", new List<ModuleData> { mine }, null),
                ["converter_2v_inline"] = Core("converter_2v_inline", "", null, new List<SerializedPartModule> { Serialized(new SerializedModuleData(converter)) }),
                ["scanner_1v_orbital"] = Core("scanner_1v_orbital", "", null, new List<SerializedPartModule> { Serialized(SerializedModuleData.Snapshot(scanner)) }),
                ["RadialDrill"] = Core("RadialDrill", "KSP1 drill", new List<ModuleData> { mine }, null),
                ["drill_2v_radial+big"] = Core("drill_2v_radial+big", "", new List<ModuleData> { mine }, null),
            };
            var formulas = IsruReference.FormulasOf(parts).ToList();
            CollectionAssert.AreEqual(new[] { "drill_2v_radial", "converter_2v_inline", "scanner_1v_orbital" }, formulas.Select(f => f.Part));
            var r = NativeIsruRef.From(formulas);
            Assert.IsFalse(r.Drill.IsFallback);
            Assert.AreEqual(17.5, r.Drill.HeatPerEc, 1e-9);
            Assert.AreEqual(4.333e-5, r.Drill.RateOf("Water"), 1e-10);
            Assert.AreEqual(1, r.ConverterModes);
            Assert.AreEqual(100.0 / 12, r.ConverterHeatPerEc, 1e-9);
            Assert.IsFalse(r.Scanner.IsFallback, "the snapshot's JSON was read back");
            Assert.AreEqual(14, r.Scanner.Ec, 1e-6);
            CollectionAssert.AreEqual(new[] { "Water" }, r.Scanner.Resources);
            Assert.AreEqual(5000000, r.Scanner.MaxAltitude, 1e-3);
        }
    }
}
