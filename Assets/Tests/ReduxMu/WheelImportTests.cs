using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Tests.Animation;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), wheels plan Task 3: the wheel members resolve by name, and
    /// Redux's real <c>Ksp1PartModuleImporterRegistry</c> (and a real <c>Ksp1PartTranslator.TryTranslatePart</c>), built with
    /// our hook armed, imports in-memory KSP1 wheels as KSP2's wheel modules. Cfg text in KSP1's shape, values invented.
    /// <c>Data_WheelBrakes</c> and <c>Data_WheelMotorSteering</c> implement Unity.Entities' <c>IComponentData</c>, which this
    /// assembly does not reference: every wheel data object is read through <see cref="WheelAccess"/>.</summary>
    public class WheelImportTests
    {
        /// <summary>The wheel pairs of the armed line, after the science pairs.</summary>
        public const string ArmedPairs = ", ModuleWheelBase -> Module_WheelBase, ModuleWheelSuspension -> Module_WheelSuspension, " +
                                         "ModuleWheelSteering -> Module_WheelSteering, ModuleWheelMotor -> Module_WheelMotor, " +
                                         "ModuleWheelMotorSteering -> Module_WheelMotorSteering, ModuleWheelBrakes -> Module_WheelBrakes, " +
                                         "ModuleWheelDamage -> Module_WheelDamage, ModuleWheelLock -> Module_WheelLock, ModuleWheelBogey -> Module_WheelBogey";

        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
            Ksp1ImportedPartRegistry.Clear();
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            NativeModules.Sink = null;
            Ksp1ImportedPartRegistry.Clear();
        }

        private static void Disarm()
        {
            new Harmony(NativeModules.HarmonyId).UnpatchAll(NativeModules.HarmonyId);
            NativeModules.Reset();
        }

        private bool Arm(string json = "{}")
        {
            var armed = NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), AnimationSettings.Parse(json, out _));
            var db = Database();
            ConverterImporter.DatabaseSource = () => db;
            WheelImporter.ReferenceSource = () => new KeyValuePair<string, PartCore>[0];
            return armed;
        }

        private static ResourceDefinitionDatabase Database()
        {
            var db = new ResourceDefinitionDatabase();
            db.AddOrUpdateDefinition(Ksp1ResourceMapper.ToResourceDefinition(
                Ksp1ConfigNode.Parse("RESOURCE_DEFINITION\n{\nname = ElectricCharge\ndensity = 0\n}\n").GetNodes("RESOURCE_DEFINITION").First()));
            return db;
        }

        private string Log => string.Join("\n", _lines);

        // ------------------------------------------------------------------ cfg text (KSP1's shape, invented values)

        private static string M(string body) => "MODULE\n{\n" + body + "\n}\n";

        private const string RoverBase =
            "name = ModuleWheelBase\nwheelColliderTransformName = WheelCollider\nwheelTransformName = WheelPivot\nwheelType = MOTORIZED\n" +
            "radius = 0.3\ncenter = 0,-0.1,0\nmass = 0.04\nfrictionPeak = 1.5\nfrictionLimit = 1.3\nFitWheelColliderToMesh = False\nTooltipTitle = Rover Wheel";
        private const string Suspension =
            "name = ModuleWheelSuspension\nbaseModuleIndex = 0\nsuspensionTransformName = SuspensionPivot\nsuspensionDistance = 0.12\n" +
            "targetPosition = 0.5\nmaximumLoad = 2\nspringRatio = 20\ndamperRatio = 1";
        private const string Steering =
            "name = ModuleWheelSteering\nbaseModuleIndex = 0\ncaliperTransformName = SteeringPivot\nsteeringResponse = 2\n" +
            "steeringCurve\n{\nkey = 0 20\nkey = 10 9\nkey = 30 2\n}\nsteeringMaxAngleCurve\n{\nkey = 0 1 0 0\nkey = 30 0.25 0 0\n}";
        private const string MotorBody =
            "baseModuleIndex = 0\nwheelSpeedMax = 40\ndriveResponse = 2\nidleDrain = 0\ntractionControlScale = 3\n" +
            "torqueCurve\n{\nkey = 0 1.5 0 0\nkey = 10 0.75 0 0\nkey = 40 0 0 0\n}\nRESOURCE\n{\nname = ElectricCharge\nrate = 2.5\n}";
        private const string Motor = "name = ModuleWheelMotor\n" + MotorBody;
        private const string MotorSteering = "name = ModuleWheelMotorSteering\nsteeringTorque = 30\n" + MotorBody;
        private const string Brakes = "name = ModuleWheelBrakes\nbaseModuleIndex = 0\nmaxBrakeTorque = 3\nbrakeResponse = 4\nstatusLightModuleIndex = 1";
        private const string Damage =
            "name = ModuleWheelDamage\nbaseModuleIndex = 0\nstressTolerance = 120\nimpactTolerance = 50\ndamagedTransformName = bustedwheel\n" +
            "undamagedTransformName = wheel";

        private const string LegBase =
            "name = ModuleWheelBase\nwheelColliderTransformName = wheelCollider\nwheelType = LEG\nradius = 0.12\ncenter = 0,0,0\n" +
            "mass = 0.04\nautoFrictionAvailable = False\nfrictionPeak = 4.0\nfrictionLimit = 4.0";
        private const string LegDeployment =
            "name = ModuleWheelDeployment\nbaseModuleIndex = 0\nanimationStateName = Deploy\nanimationTrfName = model\nTsubSys = 0.7\nuseStandInCollider = True";
        private const string LegLock = "name = ModuleWheelLock\nmaxTorque = 500";
        private const string LegBogey =
            "name = ModuleWheelBogey\nbaseModuleIndex = 0\nbogeyTransformName = foot\nbogeyRefTransformName = leg\nbogeyAxis = 1, 0, 0\n" +
            "bogeyUpAxis = 0, 1, 0\nmaxPitch = 30\nminPitch = -30\nrestPitch = 0\ndeployModuleIndex = 1";

        private static string Rover => M(RoverBase) + M(Suspension) + M(Steering) + M(Motor) + M(Brakes) + M(Damage);
        private static string Leg => M(LegBase) + M(LegDeployment) + M(Suspension) + M(LegLock) + M(LegBogey) + M(Damage);

        private static Ksp1ConfigNode Part(string modules, string name = "testWheel") =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + "\nmodule = Part\nmass = 0.1\ncategory = Ground\n" + modules + "}\n").GetNodes("PART").First();

        private static Ksp1PartImportContext Context(Ksp1ConfigNode part) =>
            new Ksp1PartImportContext(part.GetValue("name"), new Ksp1ImportReport()) { PartNode = part, PartData = new PartData { partName = part.GetValue("name") } };

        /// <summary>Redux's module loop, in MODULE order: the modules each call returned (or default when it returned false).</summary>
        private static List<(string Ksp1, bool Ok, SerializedPartModule Module)> ImportAll(Ksp1ConfigNode part, Ksp1PartImportContext context)
        {
            var registry = new Ksp1PartModuleImporterRegistry();
            var result = new List<(string, bool, SerializedPartModule)>();
            foreach (var m in part.GetNodes("MODULE"))
            {
                var ok = registry.TryImport(m, context, out var module);
                if (ok) context.PartData.serializedPartModules.Add(module);
                result.Add((m.GetValue("name"), ok, module));
            }
            return result;
        }

        private static ModuleData DataOf(Ksp1PartImportContext c, WheelKind kind) =>
            c.ModuleData.Single(d => d.GetType() == WheelAccess.Get().DataOf(kind));

        private static object Read(Ksp1PartImportContext c, WheelKind kind, string field) => WheelAccess.Read(DataOf(c, kind), kind, field);

        private static object NewCurve(params (float T, float V)[] keys)
        {
            var t = WheelAccess.Get();
            var curve = Activator.CreateInstance(t.FloatCurve);
            foreach (var (time, value) in keys) t.CurveAdd2.Invoke(curve, new object[] { time, value });
            return curve;
        }

        private static (float, float, float, float)[] Keys(object curve) =>
            WheelAccess.KeysOf(curve).Select(k => (k.time, k.value, k.inTangent, k.outTangent)).ToArray();

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_EveryWheelMember()
        {
            var t = WheelAccess.Resolve();
            foreach (WheelKind kind in Enum.GetValues(typeof(WheelKind)))
            {
                CollectionAssert.IsEmpty(t.MissingFor(kind), kind.ToString());
                Assert.AreEqual(WheelMapping.NativeModule(kind), t.ModuleOf(kind).Name);
                Assert.AreEqual("KSP.Modules", t.ModuleOf(kind).Namespace);
                Assert.IsTrue(typeof(ModuleData).IsAssignableFrom(t.DataOf(kind)));
                Assert.AreEqual(t.ModuleOf(kind), ((ModuleData)Activator.CreateInstance(t.DataOf(kind))).ModuleType, "Data's ModuleType");
                CollectionAssert.AreEquivalent(WheelAccess.Fields[kind].Select(f => f.Name), t.Field[kind].Keys, kind.ToString());
            }
            Assert.IsTrue(t.ModuleOf(WheelKind.MotorSteering).IsSubclassOf(t.ModuleOf(WheelKind.Motor)), "Module_WheelMotorSteering : Module_WheelMotor");
            Assert.AreEqual("Data_WheelMotorSteering", t.DataOf(WheelKind.MotorSteering).Name);
            Assert.IsTrue(t.HasListForm);
            CollectionAssert.AreEqual(new[] { "FREE", "MOTORIZED", "LEG" }, Enum.GetNames(t.WheelType));
            Assert.AreEqual("FloatCurve", t.FloatCurve.FullName, "the global FloatCurve");
            Assert.AreEqual(typeof(Vector3), t.Field[WheelKind.Base]["Center"].FieldType);
            Assert.AreEqual(t.WheelType, t.Field[WheelKind.Base]["WheelType"].FieldType);
            Assert.AreEqual(typeof(ModuleProperty<bool>), t.Field[WheelKind.Base]["AutoFriction"].FieldType);
            Assert.AreEqual(typeof(ModuleProperty<float>), t.Field[WheelKind.Base]["FrictionMultiplier"].FieldType);
            Assert.AreEqual(t.FloatCurve, t.Field[WheelKind.Motor]["torqueCurve"].FieldType);
            Assert.AreEqual(typeof(PartModuleResourceSetting), t.Field[WheelKind.Motor]["requiredResource"].FieldType);
            Assert.AreEqual(typeof(ModuleProperty<float>), t.Field[WheelKind.Motor]["tractionControlScale"].FieldType);
            Assert.AreEqual(typeof(double), t.Field[WheelKind.Motor]["idleDrain"].FieldType);
        }

        [Test] public void HiddenListForm_OnlyMotorSteeringMissing()
        {
            RadiatorAccess.HideListFormForTests = true;
            var t = WheelAccess.Resolve();
            Assert.IsFalse(t.HasListForm);
            CollectionAssert.AreEqual(new[] { "Ksp1PartModuleImporterRegistry.CreateSerialized(Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext)" },
                t.MissingFor(WheelKind.MotorSteering));
            foreach (WheelKind kind in Enum.GetValues(typeof(WheelKind)))
                if (kind != WheelKind.MotorSteering) CollectionAssert.IsEmpty(t.MissingFor(kind), kind.ToString());
        }

        // ------------------------------------------------------------------ the hook

        [Test] public void Arm_AddsTheNineWheelImporters()
        {
            Assert.IsTrue(Arm());
            Assert.That(_lines, Has.Some.EndsWith(ScienceImportTests.ArmedPairs + ArmedPairs));
            var importers = (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(new Ksp1PartModuleImporterRegistry());
            foreach (WheelKind kind in Enum.GetValues(typeof(WheelKind)))
                Assert.IsInstanceOf<WheelImporter>(importers[WheelMapping.Ksp1Module(kind).ToLowerInvariant()], kind.ToString());
            Assert.IsFalse(importers[WheelMapping.DeploymentModule] is WheelImporter, "ModuleWheelDeployment stays Redux's");
        }

        [Test] public void Ksp1WheelsOff_NoWheelImporter_The0131Import()
        {
            Assert.IsTrue(Arm("{\"ksp1_wheels\": false}"));
            foreach (WheelKind kind in Enum.GetValues(typeof(WheelKind)))
                CollectionAssert.Contains(_lines, NativeLines.Off(NativeLines.Pair(WheelMapping.Ksp1Module(kind), WheelMapping.NativeModule(kind)), WheelImporter.SwitchKey(kind)));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: ModuleWheelBase -> Module_WheelBase off (ksp1_wheels false)");
            var part = Part(Rover);
            var context = Context(part);
            Assert.IsTrue(ImportAll(part, context).All(r => !r.Ok), "every wheel module dropped by Redux, as in 0.13.1");
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.That(_lines, Has.None.Contains("wheel reference"));
        }

        [TestCase("wheel_steering", WheelKind.Steering)]
        [TestCase("wheel_motors", WheelKind.Motor)]
        [TestCase("wheel_brakes", WheelKind.Brakes)]
        [TestCase("wheel_damage", WheelKind.Damage)]
        [TestCase("wheel_leg_locks", WheelKind.Lock)]
        [TestCase("wheel_bogeys", WheelKind.Bogey)]
        public void KindSwitchOff_ThatKindOnly(string key, WheelKind kind)
        {
            Assert.IsTrue(Arm("{\"" + key + "\": false}"));
            CollectionAssert.Contains(_lines, NativeLines.Off(NativeLines.Pair(WheelMapping.Ksp1Module(kind), WheelMapping.NativeModule(kind)),
                "ksp1_wheels or " + key));
            var part = Part(Rover + M(LegLock) + M(LegBogey));
            var context = Context(part);
            var results = ImportAll(part, context);
            foreach (var (ksp1, ok, _) in results)
            {
                Assert.IsTrue(WheelMapping.TryKind(ksp1, out var k), ksp1);
                var off = k == kind || (kind == WheelKind.Motor && k == WheelKind.MotorSteering);
                Assert.AreEqual(!off, ok, ksp1);
            }
            Assert.IsFalse(context.ModuleData.Any(d => d.GetType() == WheelAccess.Get().DataOf(kind)));
        }

        // ------------------------------------------------------------------ Redux's real registry

        [Test] public void RoverWheel_EveryModule_WithItsComponent()
        {
            Assert.IsTrue(Arm());
            var part = Part(Rover, "roverWheelT");
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.IsTrue(results.All(r => r.Ok), Log);
            CollectionAssert.AreEqual(new[] { "Module_WheelBase", "Module_WheelSuspension", "Module_WheelSteering", "Module_WheelMotor", "Module_WheelBrakes", "Module_WheelDamage" },
                results.Select(r => r.Module.BehaviourType.Name));
            CollectionAssert.AreEqual(new[] { "PartComponentModule_WheelBase", "PartComponentModule_WheelSuspension", "PartComponentModule_WheelSteering",
                    "PartComponentModule_WheelMotor", "PartComponentModule_WheelBrakes", "PartComponentModule_WheelDamage" },
                results.Select(r => r.Module.ComponentType?.Name));
            Assert.IsTrue(results.All(r => r.Module.ModuleData.Count == 1));
            Assert.AreEqual(6, context.ModuleData.Count);

            Assert.AreEqual("MOTORIZED", Read(context, WheelKind.Base, "WheelType").ToString());
            Assert.AreEqual(0.3f, (float)Read(context, WheelKind.Base, "Radius"));
            Assert.AreEqual(new Vector3(0, -0.1f, 0), (Vector3)Read(context, WheelKind.Base, "Center"));
            Assert.AreEqual("WheelCollider", Read(context, WheelKind.Base, "WheelColliderTransformName"));
            Assert.AreEqual("WheelPivot", Read(context, WheelKind.Base, "WheelTransformName"));
            Assert.AreEqual(1.5f, (float)Read(context, WheelKind.Base, "FrictionPeak"));
            Assert.AreEqual(1.3f, (float)Read(context, WheelKind.Base, "FrictionLimit"));
            Assert.AreEqual(true, Read(context, WheelKind.Base, "AutoFriction"));
            Assert.AreEqual(0.12f, (float)Read(context, WheelKind.Suspension, "suspensionDistance"));
            Assert.AreEqual(2f, (float)Read(context, WheelKind.Suspension, "maximumLoad"));
            Assert.AreEqual("SteeringPivot", Read(context, WheelKind.Steering, "CaliperTransformName"));

            // Review item: a 2-value key gives the curve FloatCurve.Add(t, v) gives (smooth tangents, as KSP1 loads it).
            var steering = Read(context, WheelKind.Steering, "SteeringCurve");
            CollectionAssert.AreEqual(Keys(NewCurve((0, 20), (10, 9), (30, 2))), Keys(steering));
            Assert.AreNotEqual(0f, WheelAccess.KeysOf(steering)[1].inTangent, "not flat: Unity's automatic tangent");
            CollectionAssert.AreEqual(new[] { (0f, 1f, 0f, 0f), (30f, 0.25f, 0f, 0f) }, Keys(Read(context, WheelKind.Steering, "SteeringMaxAngleCurve")),
                "4 values: the cfg's tangents");

            CollectionAssert.AreEqual(new[] { (0f, 1.5f, 0f, 0f), (10f, 0.75f, 0f, 0f), (40f, 0f, 0f, 0f) }, Keys(Read(context, WheelKind.Motor, "torqueCurve")));
            Assert.AreEqual(true, Read(context, WheelKind.Motor, "useResources"));
            var res = (PartModuleResourceSetting)Read(context, WheelKind.Motor, "requiredResource");
            Assert.AreEqual(("ElectricCharge", 2.5f), (res.ResourceName, res.Rate), "the KSP1 EC rate as is");
            Assert.AreEqual(40f, (float)Read(context, WheelKind.Motor, "wheelSpeedMax"));
            Assert.AreEqual(0.0, (double)Read(context, WheelKind.Motor, "idleDrain"));
            Assert.AreEqual(3f, (float)Read(context, WheelKind.Motor, "tractionControlScale"), "review item: a state initial value");
            Assert.AreEqual(true, Read(context, WheelKind.Motor, "autoTorque"));
            Assert.AreEqual(3f, (float)Read(context, WheelKind.Brakes, "MaxBrakeTorque"));
            Assert.AreEqual(4f, (float)Read(context, WheelKind.Brakes, "BrakeResponse"));
            Assert.AreEqual("bustedwheel", Read(context, WheelKind.Damage, "damagedTransformName"));
            Assert.AreEqual(120f, (float)Read(context, WheelKind.Damage, "stressTolerance"));

            CollectionAssert.AreEqual(new[] { "Module_WheelBase", "Module_WheelSuspension", "Module_WheelSteering", "Module_WheelMotor", "Module_WheelBrakes", "Module_WheelDamage" },
                NativeModules.Record.ConvertedModules("roverWheelT"));
            var record = WheelImporter.RecordOf("roverWheelT");
            Assert.AreEqual(WheelTypeName.MOTORIZED, record.Type);
            Assert.IsFalse(record.HasDeployment);
            Assert.AreEqual(6, record.Kinds.Count);
            CollectionAssert.Contains(_lines, WheelLines.ReferenceNone("no KSP2 part with Data_WheelBase"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: roverWheelT: ModuleWheelBrakes: statusLightModuleIndex not reproduced (Redux drops ModuleStatusLight)");

            NativeModules.LogImportSummaryOnce();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: roverWheelT: wheel MOTORIZED r 0.3 m on 'WheelCollider': base, suspension, steering, motor, brakes, damage " +
                                              "(deploy on at 0.98, off at 0.02; friction 1.5/1.3, auto)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: wheels: 1 part(s) got Module_WheelBase (0 leg(s), 0 free, 1 motorized); suspension 1, steering 1, " +
                                              "motor 1, motor-steering 0, brakes 1, damage 1, lock 0, bogey 0; refused: none; " +
                                              "not reproduced: TooltipTitle 1, statusLightModuleIndex 1; " +
                                              "unused by Redux's solver: damperRatio 1, impactTolerance 1, springRatio 1");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: wheels: B9: 0 subtype edit(s) of wheel modules, 0 on state fields not carried (none)");
        }

        [Test] public void Leg_BaseWithDeployTiming_LockBogey_ReduxDeploymentLeftAlone()
        {
            Assert.IsTrue(Arm());
            var part = Part(Leg, "legT");
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.AreEqual(new[] { true, false, true, true, true, true }, results.Select(r => r.Ok).ToArray(), "ModuleWheelDeployment: Redux's no-op");
            Assert.AreEqual("LEG", Read(context, WheelKind.Base, "WheelType").ToString());
            Assert.AreEqual(0.7f, (float)Read(context, WheelKind.Base, "DeploymentSubsystemNormalized"), 1e-6f);
            Assert.AreEqual(0.3f, (float)Read(context, WheelKind.Base, "RetractionSubsystemNormalized"), 1e-6f);
            Assert.AreEqual(true, Read(context, WheelKind.Base, "UseStandinCollider"));
            Assert.AreEqual(false, Read(context, WheelKind.Base, "AutoFriction"), "autoFrictionAvailable = False");
            Assert.AreEqual(4f, (float)Read(context, WheelKind.Base, "FrictionPeak"));
            Assert.AreEqual(500f, (float)Read(context, WheelKind.Lock, "MaxTorque"));
            Assert.AreEqual("foot", Read(context, WheelKind.Bogey, "bogeyTransformName"));
            Assert.AreEqual(Vector3.right, (Vector3)Read(context, WheelKind.Bogey, "bogeyAxis"));
            Assert.AreEqual(0f, (float)Read(context, WheelKind.Bogey, "restPitch"));
            Assert.IsTrue(WheelImporter.RecordOf("legT").HasDeployment);

            var off = Context(part);
            Disarm();
            Assert.IsTrue(Arm("{\"wheel_ksp1_deploy_timing\": false, \"wheel_ksp1_friction\": false}"));
            Assert.IsTrue(ImportAll(part, off)[0].Ok);
            Assert.AreEqual(0.98f, (float)Read(off, WheelKind.Base, "DeploymentSubsystemNormalized"), 1e-6f);
            Assert.AreEqual(0.02f, (float)Read(off, WheelKind.Base, "RetractionSubsystemNormalized"), 1e-6f);
            Assert.AreEqual(false, Read(off, WheelKind.Base, "UseStandinCollider"));
            Assert.AreEqual(true, Read(off, WheelKind.Base, "AutoFriction"));
            Assert.AreEqual(1.45f, (float)Read(off, WheelKind.Base, "FrictionPeak"));
        }

        [Test] public void MotorSteering_OneModule_BothData()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(RoverBase) + M(Suspension) + M(MotorSteering), "roverWheel3T");
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.IsTrue(results.All(r => r.Ok), Log);
            var module = results[2].Module;
            Assert.AreEqual("Module_WheelMotorSteering", module.BehaviourType.Name);
            Assert.AreEqual("PartComponentModule_WheelMotorSteering", module.ComponentType?.Name);
            CollectionAssert.AreEqual(new[] { "Data_WheelMotor", "Data_WheelMotorSteering" }, module.ModuleData.Select(d => d.DataObject.GetType().Name));
            Assert.AreSame(DataOf(context, WheelKind.Motor), module.ModuleData[0].DataObject);
            Assert.AreEqual(30f, (float)Read(context, WheelKind.MotorSteering, "SteeringTorque"));
            Assert.AreEqual(40f, (float)Read(context, WheelKind.Motor, "wheelSpeedMax"));
            Assert.AreEqual(4, context.ModuleData.Count);
        }

        [Test] public void MotorThenMotorSteering_OneMotorSlot()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(RoverBase) + M(Motor) + M(MotorSteering));
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.AreEqual(new[] { true, true, false }, results.Select(r => r.Ok).ToArray());
            Assert.AreEqual(1, context.ModuleData.Count(d => d.GetType() == WheelAccess.Get().DataOf(WheelKind.Motor)));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelMotorSteering not converted (second)");
        }

        [Test] public void SubBeforeBase_SecondBase_Refused()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(Suspension) + M(RoverBase) + M(Brakes) + M(RoverBase) + M(Brakes));
            var context = Context(part);
            var results = ImportAll(part, context);
            Assert.AreEqual(new[] { false, true, true, false, false }, results.Select(r => r.Ok).ToArray());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelSuspension not converted (no base)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelBase not converted (second)");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelBrakes not converted (second)");
            Assert.AreEqual(2, context.ModuleData.Count);
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("brakes 1, damage 0, lock 0, bogey 0; refused: no base 1, second 2;"));
        }

        [Test] public void NoBaseInThePart_SubModulesGetNothing()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(Suspension) + M(Brakes));
            var context = Context(part);
            Assert.IsTrue(ImportAll(part, context).All(r => !r.Ok));
            CollectionAssert.IsEmpty(context.ModuleData);
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("wheels: 0 part(s) got Module_WheelBase"));
            Assert.That(_lines, Has.Some.Contains("refused: no base 2;"));
        }

        [Test] public void BaseMappingRefused_NoRadius_NoSubModule()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(RoverBase.Replace("radius = 0.3", "radius = 0")) + M(Suspension));
            var context = Context(part);
            Assert.IsTrue(ImportAll(part, context).All(r => !r.Ok));
            CollectionAssert.IsEmpty(context.ModuleData);
            Assert.That(_lines, Has.Some.Contains("testWheel: ModuleWheelBase: radius 0 is not above 0, no wheel"));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelSuspension not converted (no base)");
        }

        [Test] public void BaseException_ModuleDataAsBefore_NoSubModule()
        {
            Assert.IsTrue(Arm());
            WheelImporter.FaultForTests = (p, k) => { if (k == WheelKind.Base) throw new InvalidOperationException("fault"); };
            var part = Part(Rover);
            var context = Context(part);
            var other = new KSP.Modules.Data_ToggleCrossFeed();
            context.ModuleData.Add(other);
            var results = ImportAll(part, context);
            Assert.IsTrue(results.All(r => !r.Ok), Log);
            Assert.IsNull(results[0].Module.ModuleData);
            Assert.AreSame(other, context.ModuleData.Single(), "no orphan wheel data");
            CollectionAssert.IsEmpty(NativeModules.Record.ConvertedModules("testWheel"));
            Assert.That(_lines, Has.Some.Contains("testWheel: ModuleWheelBase not converted"), Log);
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelSteering not converted (no base)");
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.Contains("refused: no base 5, failed 1;"), "review F2: an exception is not a mapping refusal");
        }

        [Test] public void SubException_OnlyThatModuleRolledBack()
        {
            Assert.IsTrue(Arm());
            WheelImporter.FaultForTests = (p, k) => { if (k == WheelKind.MotorSteering) throw new InvalidOperationException("fault"); };
            var part = Part(M(RoverBase) + M(MotorSteering) + M(Brakes));
            var context = Context(part);
            Assert.AreEqual(new[] { true, false, true }, ImportAll(part, context).Select(r => r.Ok).ToArray());
            Assert.AreEqual(2, context.ModuleData.Count, "both motor data objects of the failed call removed");
            Assert.IsFalse(context.ModuleData.Any(d => d.GetType() == WheelAccess.Get().DataOf(WheelKind.Motor)));
        }

        /// <summary>Review F1: KSP2's motor sets up its resource request in the VAB whatever <c>useResources</c> says, and a null
        /// name throws there on every fixed update. A motor without a usable resource names electric charge at rate 0.</summary>
        [Test] public void MotorWithoutResource_NamesElectricChargeAtZero()
        {
            Assert.IsTrue(Arm());
            var noResource = Motor.Substring(0, Motor.IndexOf("RESOURCE", StringComparison.Ordinal));
            foreach (var body in new[] { noResource, Motor.Replace("ElectricCharge", "Kethane"), Motor.Replace("rate = 2.5", "rate = -1") })
            {
                var part = Part(M(RoverBase) + M(body));
                var context = Context(part);
                Assert.IsTrue(ImportAll(part, context).All(r => r.Ok), Log);
                Assert.AreEqual(false, Read(context, WheelKind.Motor, "useResources"));
                var res = (PartModuleResourceSetting)Read(context, WheelKind.Motor, "requiredResource");
                Assert.AreEqual(("ElectricCharge", 0f), (res.ResourceName, res.Rate), body);
            }
            Assert.That(_lines, Has.Some.Contains("ModuleWheelMotor: no usable RESOURCE, requiredResource set to ElectricCharge at 0"));
        }

        /// <summary>Review F6 on a real installed model (KSP1 files optional): a collider name with a collider below it is kept,
        /// one whose object has none is cleared, an absent one is kept (KSP2 skips it), and without the cfg file it is kept and
        /// noted.</summary>
        [Test] public void SuspensionCollider_RealModel_FoundKept_NoColliderCleared_AbsentKept()
        {
            var cfg = InstalledKsp1.AllCfg().FirstOrDefault(p => File.ReadAllText(p).Contains("suspensionColliderName = DamperCollider"));
            if (cfg == null) Assert.Ignore("no installed cfg with suspensionColliderName = DamperCollider");
            var root = InstalledKsp1.Root;
            var text = File.ReadAllText(cfg);
            var original = Ksp1ConfigNode.Parse(text).GetNodes("PART").First();
            var snapshot = PartSnapshot.From(original.GetValue("name"), cfg, root, new Ksp1NodeAdapter(original), null);
            var resolver = new ModelPathResolver(root, () => DirectoryIndex.Build(root));
            var models = Enumerable.Range(0, snapshot.ModelRefs.Length)
                .Select(i => resolver.Resolve(cfg, snapshot.ModelRefs[i], snapshot.RefIsMesh[i], out _)).Where(x => x != null)
                .Select(x => MuReader.Read(x).Root).ToList();
            IEnumerable<MuNode> All(MuNode n) => new[] { n }.Concat(n.Children.SelectMany(All));
            var names = models.SelectMany(All).GroupBy(n => n.Name).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
            var bare = names.First(n => !SuspensionColliderRule.HasCollider(n)).Name;
            Assert.IsTrue(Arm());

            string Run(string colliderName, bool withFile)
            {
                var fileNode = Ksp1ConfigNode.Parse(text.Replace("suspensionColliderName = DamperCollider", "suspensionColliderName = " + colliderName));
                var part = fileNode.GetNodes("PART").First();
                if (withFile) WheelImporter.RememberFileForTests(part, new Ksp1ConfigFile(new Ksp1ModRoot(root, root), cfg, "Squad", fileNode));
                var context = Context(part);
                ImportAll(part, context);
                return (string)Read(context, WheelKind.Suspension, "suspensionColliderName");
            }

            Assert.AreEqual("DamperCollider", Run("DamperCollider", true), "a collider below it: kept");
            Assert.AreEqual("", Run(bare, true), "'" + bare + "' has no collider: cleared");
            Assert.That(_lines, Has.Some.Contains(SuspensionColliderRule.ClearedNote(bare)));
            Assert.AreEqual("notInTheModel", Run("notInTheModel", true), "absent: kept, KSP2 skips it");
            Assert.AreEqual("DamperCollider", Run("DamperCollider", false));
            Assert.That(_lines, Has.Some.Contains(WheelImporter.NotCheckedNote("DamperCollider")));
        }

        /// <summary>Carried from the Task 3 review: the collider state is cached by cfg file, part and name. A second part of
        /// the same cfg file whose model cannot be read is not handed the first part's answer.</summary>
        [Test] public void SuspensionCollider_TwoPartsInOneFile_EachChecksItsOwnModel()
        {
            var cfg = InstalledKsp1.AllCfg().FirstOrDefault(p => File.ReadAllText(p).Contains("suspensionColliderName = DamperCollider"));
            if (cfg == null) Assert.Ignore("no installed cfg with suspensionColliderName = DamperCollider");
            var root = InstalledKsp1.Root;
            var text = File.ReadAllText(cfg);
            var original = Ksp1ConfigNode.Parse(text).GetNodes("PART").First();
            var partName = original.GetValue("name");
            var model = original.GetNodes("MODEL").Select(m => m.GetValue("model")).FirstOrDefault(m => !string.IsNullOrEmpty(m));
            if (model == null) Assert.Ignore("the cfg names no MODEL");
            var snapshot = PartSnapshot.From(partName, cfg, root, new Ksp1NodeAdapter(original), null);
            var resolver = new ModelPathResolver(root, () => DirectoryIndex.Build(root));
            var models = Enumerable.Range(0, snapshot.ModelRefs.Length)
                .Select(i => resolver.Resolve(cfg, snapshot.ModelRefs[i], snapshot.RefIsMesh[i], out _)).Where(x => x != null)
                .Select(x => MuReader.Read(x).Root).ToList();
            IEnumerable<MuNode> All(MuNode n) => new[] { n }.Concat(n.Children.SelectMany(All));
            var bare = models.SelectMany(All).GroupBy(n => n.Name).Where(g => g.Count() == 1).Select(g => g.Single())
                .First(n => !SuspensionColliderRule.HasCollider(n)).Name;
            Assert.IsTrue(Arm());

            var partText = text.Replace("suspensionColliderName = DamperCollider", "suspensionColliderName = " + bare);
            var second = partText.Replace("name = " + partName, "name = " + partName + "_noModel").Replace("model = " + model, "model = " + model + "_absent");
            var fileNode = Ksp1ConfigNode.Parse(partText + Environment.NewLine + second);
            var parts = fileNode.GetNodes("PART").ToList();
            Assert.AreEqual(2, parts.Count);
            var file = new Ksp1ConfigFile(new Ksp1ModRoot(root, root), cfg, "Squad", fileNode);
            string Run(Ksp1ConfigNode part)
            {
                WheelImporter.RememberFileForTests(part, file);
                var context = Context(part);
                ImportAll(part, context);
                return (string)Read(context, WheelKind.Suspension, "suspensionColliderName");
            }

            Assert.AreEqual("", Run(parts[0]), "the first part's model: '" + bare + "' has no collider, cleared");
            Assert.AreEqual(bare, Run(parts[1]), "the second part's model cannot be read: kept, not the first part's answer");
            Assert.That(_lines, Has.Some.Contains(WheelImporter.NotCheckedNote(bare)));
        }

        /// <summary>Review M1 on the real installed GearLarge (KSP1 files optional): a bogey wheel base name found in the model is
        /// kept, one absent from it clears both wheel names with a note, and without the cfg file both are kept.</summary>
        [Test] public void BogeyWheels_RealModel_FoundKept_AbsentCleared_UncheckedKept()
        {
            var cfg = InstalledKsp1.AllCfg().FirstOrDefault(p => File.ReadAllText(p).Contains("wheelTransformBaseName = wheels2"));
            if (cfg == null) Assert.Ignore("no installed cfg with wheelTransformBaseName = wheels2");
            var root = InstalledKsp1.Root;
            var text = File.ReadAllText(cfg);
            Assert.IsTrue(Arm());

            (string Ref, string Base) Run(string baseName, bool withFile)
            {
                var fileNode = Ksp1ConfigNode.Parse(text.Replace("wheelTransformBaseName = wheels2", "wheelTransformBaseName = " + baseName));
                var part = fileNode.GetNodes("PART").First();
                if (withFile) WheelImporter.RememberFileForTests(part, new Ksp1ConfigFile(new Ksp1ModRoot(root, root), cfg, "Squad", fileNode));
                var context = Context(part);
                ImportAll(part, context);
                return ((string)Read(context, WheelKind.Bogey, "wheelTransformRefName"), (string)Read(context, WheelKind.Bogey, "wheelTransformBaseName"));
            }

            Assert.AreEqual(("wheels1", "wheels2"), Run("wheels2", true), "in the model: kept");
            Assert.AreEqual(("", ""), Run("absentWheels", true), "not in the model: both cleared");
            Assert.That(_lines, Has.Some.Contains(WheelMapping.BogeyWheelsClearedNote("wheels1", "absentWheels", "wheelTransformBaseName not in the model")));
            Assert.AreEqual(("wheels1", "absentWheels"), Run("absentWheels", false), "model not checked: kept");
        }

        [Test] public void Arm_HooksTheTranslatorForTheColliderCheck()
        {
            Assert.IsTrue(Arm());
            var method = AccessTools.Method(AccessTools.TypeByName(WheelImporter.TranslatorTypeName), "TryTranslatePart");
            Assert.IsTrue(Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.owner == NativeModules.HarmonyId) == true);
            Assert.That(_lines, Has.None.Contains("suspension collider check"));
        }

        [Test] public void ListFormHidden_MotorSteeringDropped_AndTheLine()
        {
            RadiatorAccess.HideListFormForTests = true;
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: wheels: Ksp1PartModuleImporterRegistry.CreateSerialized(Type, IReadOnlyList<ModuleData>, " +
                                              "Ksp1PartImportContext) not found, motor-steering off (Redux drops it, as in 0.13.1)");
            var part = Part(M(RoverBase) + M(MotorSteering));
            var context = Context(part);
            Assert.AreEqual(new[] { true, false }, ImportAll(part, context).Select(r => r.Ok).ToArray(), "no motor");
            Assert.AreEqual(1, context.ModuleData.Count);
        }

        [Test] public void ContextWithTheDataType_NothingAdded_Noted()
        {
            Assert.IsTrue(Arm());
            var part = Part(M(RoverBase) + M(Brakes));
            var context = Context(part);
            var redux = WheelAccess.NewBrakes(new WheelBrakesValues());
            context.ModuleData.Add(redux);
            Assert.AreEqual(new[] { true, false }, ImportAll(part, context).Select(r => r.Ok).ToArray());
            Assert.AreEqual(1, context.ModuleData.Count(d => d.GetType() == redux.GetType()));
            CollectionAssert.Contains(_lines, "[KSP1PartMod] native: testWheel: ModuleWheelBrakes not converted: the part already has Data_WheelBrakes");
        }

        [Test] public void Rebuild_CountsThePartOnce()
        {
            Assert.IsTrue(Arm());
            var part = Part(Rover, "roverWheelT");
            ImportAll(part, Context(part));
            ImportAll(part, Context(part));
            NativeModules.LogImportSummaryOnce();
            Assert.That(_lines, Has.Some.StartsWith("[KSP1PartMod] native: wheels: 1 part(s) got Module_WheelBase"));
            Assert.AreEqual(1, _lines.Count(l => l.Contains("wheel reference")), "read once");
        }

        // ------------------------------------------------------------------ the reference line

        [Test] public void Reference_FromPartCores_KSP2Only()
        {
            Assert.IsTrue(Arm());
            var rover = Part(Rover, "rv");
            var roverContext = Context(rover);
            ImportAll(rover, roverContext);
            var leg = Part(Leg, "lg");
            var legContext = Context(leg);
            ImportAll(leg, legContext);
            PartCore Core(string name, string tags, List<ModuleData> live) =>
                new PartCore { data = new PartData { partName = name, tags = tags, serializedPartModules = new List<SerializedPartModule>() }, modules = live };
            var parts = new Dictionary<string, PartCore>
            {
                ["wheel_rover"] = Core("wheel_rover", "wheel", roverContext.ModuleData.ToList()),
                ["leg_small"] = Core("leg_small", "leg", legContext.ModuleData.ToList()),
                ["ksp1Wheel"] = Core("ksp1Wheel", "wheel Squad KSP1", roverContext.ModuleData.ToList()),
                ["wheel_rover+big"] = Core("wheel_rover+big", "", roverContext.ModuleData.ToList()),
                ["tank"] = Core("tank", "", new List<ModuleData> { new KSP.Modules.Data_ToggleCrossFeed() }),
            };
            Assert.AreEqual("[KSP1PartMod] native: wheel reference: 2 KSP2 wheel part(s) (LEG 1, FREE 0, MOTORIZED 1), median radius 0.21 m, " +
                            "brake 3 kN·m, lock 500 kN·m, motor peak 1.5 kN·m at 2.5 EC/s, rated load 2 t",
                WheelImporter.ReferenceLine(() => parts));
            StringAssert.StartsWith("[KSP1PartMod] native: wheel reference: none read (", WheelImporter.ReferenceLine(() => throw new InvalidOperationException("x")));
        }

        [Test] public void Reference_InTheEditor_NoneRead()
        {
            StringAssert.StartsWith("[KSP1PartMod] native: wheel reference: none read (", WheelImporter.ReferenceLine(() =>
                (GameManagerParts() ?? throw new InvalidOperationException("the game's part data is not available"))));
        }

        private static IEnumerable<KeyValuePair<string, PartCore>> GameManagerParts()
        {
            try { return KSP.Game.GameManager.Instance?.Game?.Parts?.PartData?.ToList(); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ a real translation

        [Test] public void TryTranslatePart_Leg_WheelModulesBesideReduxDeployable()
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry, no clip) are not ours to judge here
            Assert.IsTrue(Arm());
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse("PART\n{\nname = wheelTranslateLeg\nmodule = Part\nmass = 0.1\ncategory = Ground\n" + Leg + "}\n");
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/leg.cfg", "Test", node);
            var part = node.GetNodes("PART").First();
            var previousJson = Newtonsoft.Json.JsonConvert.DefaultSettings;
            try
            {
                try { KSP.IO.IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                Ksp1ImportedPartRegistry.BeginImport("wheel-translate-test");
                var ok = new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out var core, out _);
                Assert.IsTrue(ok, Log);
                var names = core.data.serializedPartModules.Select(m => m.BehaviourType?.Name).ToList();
                TestContext.WriteLine(string.Join(", ", names));
                foreach (var expected in new[] { "Module_WheelBase", "Module_WheelSuspension", "Module_WheelLock", "Module_WheelBogey", "Module_WheelDamage" })
                    Assert.AreEqual(1, names.Count(n => n == expected), expected + " in " + string.Join(", ", names));
                Assert.IsTrue(names.Any(n => n == "Module_Deployable" || n == "Ksp1Module_Deployable"), "Redux's deployable: " + string.Join(", ", names));
                var deployable = core.data.serializedPartModules.First(m => m.BehaviourType.Name.EndsWith("Module_Deployable"));
                Assert.IsTrue(deployable.ModuleData.Any(d => d.DataObject?.GetType().Name == "Data_Deployable"), "untouched: Redux's Data_Deployable");
                var wheelBase = core.data.serializedPartModules.Single(m => m.BehaviourType.Name == "Module_WheelBase").ModuleData.Single().DataObject;
                Assert.AreEqual(0.7f, (float)WheelAccess.Read(wheelBase, WheelKind.Base, "DeploymentSubsystemNormalized"), 1e-6f);
                CollectionAssert.AreEqual(new[] { "Module_WheelBase", "Module_WheelSuspension", "Module_WheelLock", "Module_WheelBogey", "Module_WheelDamage" },
                    NativeModules.Record.ConvertedModules("wheelTranslateLeg"));
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
                Newtonsoft.Json.JsonConvert.DefaultSettings = previousJson;
            }
        }
    }
}
