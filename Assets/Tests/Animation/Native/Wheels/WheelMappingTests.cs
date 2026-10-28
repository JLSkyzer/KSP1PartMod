using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Wheels
{
    /// <summary>The wheel mapping (plan (f)) on cfg text written here: invented values in KSP1's shape.</summary>
    public class WheelMappingTests
    {
        private static IKsp1Node M(string body) => CfgText.Module(body);

        private const string Leg =
            "name = ModuleWheelBase\nwheelColliderTransformName = wheelCollider\nwheelType = LEG\nradius = 0.12\ncenter = 0,0,0\n" +
            "mass = 0.040\nautoFrictionAvailable = False\nfrictionPeak = 4.0\nfrictionLimit = 4.0\nclipObject = piston_collider\n" +
            "TooltipTitle = Landing Leg\nTooltipPrimaryField =\nadherentStart = 0.01\nfrictionAdherent = 3.9\npeakStart = 3\nlimitStart = 7.0";

        private const string LegDeployment = "name = ModuleWheelDeployment\nanimationStateName = Deploy\nTsubSys = 0.7\nuseStandInCollider = True";

        private const string Rover =
            "name = ModuleWheelBase\nwheelColliderTransformName = WheelCollider\nwheelTransformName = WheelPivot\nwheelType = MOTORIZED\n" +
            "radius = 0.25\ncenter = 0,-0.1,0\nmass = 0.080\nfrictionPeak = 1.5\nfrictionLimit = 1.3\nFitWheelColliderToMesh = False\n" +
            "groundHeightOffset = 0.5\nuseNewFrictionModel = true";

        private static IsruResources Resources() => IsruFakes.Table();

        // ------------------------------------------------------------------ base

        [Test] public void Leg_KeepsKsp1Friction_ManualFriction_AndTheDeployTiming()
        {
            var m = WheelMapping.Base(M(Leg), M(LegDeployment), true, true);
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(WheelTypeName.LEG, v.WheelType);
            Assert.IsFalse(v.AutoFriction);
            Assert.AreEqual(4f, v.FrictionPeak);
            Assert.AreEqual(4f, v.FrictionLimit);
            Assert.AreEqual(0.7f, v.DeploymentSubsystemNormalized, 1e-6);
            Assert.AreEqual(0.3f, v.RetractionSubsystemNormalized, 1e-6);
            Assert.IsTrue(v.UseStandinCollider);
            Assert.AreEqual("wheelCollider", v.WheelColliderTransformName);
            Assert.AreEqual("piston_collider", v.ClipObject);
            Assert.AreEqual(0.04f, v.Mass, 1e-6);
            CollectionAssert.AreEquivalent(new[] { "TooltipTitle", "TooltipPrimaryField" }, m.NotReproduced);
            CollectionAssert.AreEquivalent(new[] { "adherentStart", "frictionAdherent", "peakStart", "limitStart" }, m.UnusedBySolver);
        }

        [Test] public void Leg_SwitchesOff_GiveKsp2Defaults()
        {
            var v = WheelMapping.Base(M(Leg), M(LegDeployment), false, false).Values;
            Assert.AreEqual(1.45f, v.FrictionPeak);
            Assert.AreEqual(1.1f, v.FrictionLimit);
            Assert.IsTrue(v.AutoFriction);
            Assert.AreEqual(0.98f, v.DeploymentSubsystemNormalized);
            Assert.AreEqual(0.02f, v.RetractionSubsystemNormalized);
            Assert.IsFalse(v.UseStandinCollider);
        }

        [Test] public void FixedWheel_WithoutDeployment_KeepsKsp2Timing()
        {
            var v = WheelMapping.Base(M(Rover), null, true, true).Values;
            Assert.AreEqual(0.98f, v.DeploymentSubsystemNormalized);
            Assert.AreEqual(0.02f, v.RetractionSubsystemNormalized);
        }

        [Test] public void Rover_CopiesRadiusAndCentre()
        {
            var m = WheelMapping.Base(M(Rover), null, true, true);
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(WheelTypeName.MOTORIZED, v.WheelType);
            Assert.AreEqual(0.25f, v.Radius, 1e-6);
            Assert.AreEqual(0f, v.Center.X);
            Assert.AreEqual(-0.1f, v.Center.Y, 1e-6);
            Assert.AreEqual(0f, v.Center.Z);
            Assert.AreEqual("WheelPivot", v.WheelTransformName);
            Assert.AreEqual(1.5f, v.FrictionPeak, 1e-6);
            Assert.AreEqual(1.3f, v.FrictionLimit, 1e-6);
            Assert.IsTrue(v.AutoFriction, "auto friction available by default");
            Assert.AreEqual(0.5f, v.GroundHeightOffset, 1e-6);
            CollectionAssert.IsEmpty(m.NotReproduced);
            CollectionAssert.IsEmpty(m.Notes);
        }

        [Test] public void UnknownWheelType_GivesFree_AndANote()
        {
            var m = WheelMapping.Base(M(Rover.Replace("MOTORIZED", "TRACKED")), null, true, true);
            Assert.IsTrue(m.Converted);
            Assert.AreEqual(WheelTypeName.FREE, m.Values.WheelType);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("wheelType 'TRACKED' unknown, FREE")), string.Join("; ", m.Notes));
        }

        [TestCase("1")]
        [TestCase("leg")]
        public void WheelType_AnyCase_NoNumbers(string text)
        {
            Assert.AreEqual(text == "leg", WheelMapping.TryWheelType(text, out var t));
            Assert.AreEqual(text == "leg" ? WheelTypeName.LEG : WheelTypeName.FREE, t);
        }

        [Test] public void RadiusZero_OrNoColliderHost_GivesNoWheel()
        {
            var zero = WheelMapping.Base(M(Rover.Replace("radius = 0.25", "radius = 0")), null, true, true);
            Assert.IsFalse(zero.Converted);
            Assert.IsTrue(zero.Notes.Any(n => n.Contains("is not above 0, no wheel")));
            var noHost = WheelMapping.Base(M(Rover.Replace("wheelColliderTransformName = WheelCollider\n", "")), null, true, true);
            Assert.IsFalse(noHost.Converted);
            Assert.IsTrue(noHost.Notes.Any(n => n.Contains("no wheelColliderTransformName, no wheel")));
            Assert.IsFalse(WheelMapping.Base(null, null, true, true).Converted);
        }

        [TestCase("1.2", 1f, 0f)]
        [TestCase("-1", 0f, 1f)]
        public void TsubSys_Clamped(string tsub, float deploy, float retract)
        {
            var m = WheelMapping.Base(M(Leg), M("name = ModuleWheelDeployment\nTsubSys = " + tsub), true, true);
            Assert.AreEqual(deploy, m.Values.DeploymentSubsystemNormalized);
            Assert.AreEqual(retract, m.Values.RetractionSubsystemNormalized);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("TsubSys") && n.Contains("clamped")), string.Join("; ", m.Notes));
        }

        [TestCase("TireForceSharpness")]
        [TestCase("tireForceSharpness")]
        public void TireForceSharpness_BothSpellingsMap(string key)
        {
            var m = WheelMapping.Base(M(Rover + "\n" + key + " = 3"), null, true, true);
            Assert.AreEqual(3f, m.Values.TireForceSharpness);
            CollectionAssert.Contains(m.UnusedBySolver, "TireForceSharpness");
            CollectionAssert.DoesNotContain(m.NotReproduced, key);
        }

        [Test] public void FrictionMultiplier_Clamped_AndUnparsableKeepsDefault()
        {
            Assert.AreEqual(6f, WheelMapping.Base(M(Rover + "\nfrictionMultiplier = 6"), null, true, true).Values.FrictionMultiplier);
            var high = WheelMapping.Base(M(Rover + "\nfrictionMultiplier = 40"), null, true, true);
            Assert.AreEqual(10f, high.Values.FrictionMultiplier);
            Assert.IsTrue(high.Notes.Any(n => n.Contains("frictionMultiplier 40 clamped to 10")));
            var bad = WheelMapping.Base(M(Rover.Replace("mass = 0.080", "mass = heavy")), null, true, true);
            Assert.AreEqual(0f, bad.Values.Mass);
            Assert.IsTrue(bad.Notes.Any(n => n.Contains("mass 'heavy' is not a number, KSP2's default kept")));
        }

        [Test] public void Base_NotedFields()
        {
            var m = WheelMapping.Base(M(Rover + "\nsuspensionEnabled = true\nTooltipTitle = Rover Wheel\nunknownKey = 3"), null, true, true);
            CollectionAssert.AreEquivalent(new[] { "suspensionEnabled", "TooltipTitle", "unknownKey" }, m.NotReproduced);
        }

        // ------------------------------------------------------------------ suspension

        [Test] public void Suspension_CopiesByName_UnusedListed()
        {
            var m = WheelMapping.Suspension(M(
                "name = ModuleWheelSuspension\nbaseModuleIndex = 0\nsuspensionTransformName = suspensionTgt\nsuspensionDistance = 0.2\n" +
                "suspensionOffset = -0.02\ntargetPosition = 0.5\nsuspensionColliderName = piston_collider\nmaximumLoad = 8.0\n" +
                "springRatio = 6\ndamperRatio = 1.0\nboostRatio = 0.6\nuseDistributedMass = true\nuseAutoBoost = False\n" +
                "adjustForHighGee = false\nextendDurationFactor = 1.0\nretractDuractionFactor = 1.0\nsuppressModuleInfo = True"));
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual("suspensionTgt", v.SuspensionTransformName);
            Assert.AreEqual(0.2f, v.SuspensionDistance, 1e-6);
            Assert.AreEqual(-0.02f, v.SuspensionOffset, 1e-6);
            Assert.AreEqual(0.5f, v.TargetPosition, 1e-6);
            Assert.AreEqual("piston_collider", v.SuspensionColliderName);
            Assert.AreEqual(8f, v.MaximumLoad);
            Assert.AreEqual(6f, v.SpringRatio);
            Assert.IsFalse(v.UseAutoBoost);
            Assert.IsFalse(v.AdjustForHighGee);
            CollectionAssert.AreEquivalent(WheelMapping.UnusedBySolverFields[WheelKind.Suspension], m.UnusedBySolver);
            CollectionAssert.AreEquivalent(new[] { "extendDurationFactor", "retractDuractionFactor", "suppressModuleInfo" }, m.NotReproduced);
        }

        [Test] public void BaseModuleIndex_QuietAtZero_NotedOtherwise()
        {
            CollectionAssert.IsEmpty(WheelMapping.Lock(M("name = ModuleWheelLock\nbaseModuleIndex = 0\nmaxTorque = 500")).NotReproduced);
            CollectionAssert.AreEqual(new[] { "baseModuleIndex" },
                WheelMapping.Lock(M("name = ModuleWheelLock\nbaseModuleIndex = 2\nmaxTorque = 500")).NotReproduced);
        }

        // ------------------------------------------------------------------ steering

        private const string SteeringBody =
            "name = ModuleWheelSteering\nbaseModuleIndex = 0\ncaliperTransformName = SteeringPivot\nsteeringResponse = 2\n" +
            "autoSteeringAdjust = true\nsteeringRange = 25\n" +
            "steeringCurve\n{\nkey = 0 20\nkey = 10 9\nkey = 30 2\n}\nsteeringMaxAngleCurve\n{\nkey = 0 1 0 0\nkey = 30 0.25 0 0\n}";

        [Test] public void Steering_CurvesAndRangeNoted()
        {
            var m = WheelMapping.Steering(M(SteeringBody));
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual("SteeringPivot", v.CaliperTransformName);
            Assert.AreEqual(2f, v.SteeringResponse);
            Assert.IsTrue(v.AutoSteeringAdjust);
            CollectionAssert.AreEqual(new[] { 0f, 20f, 0f, 0f, 10f, 9f, 0f, 0f, 30f, 2f, 0f, 0f }, v.SteeringCurve, "missing tangents 0");
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f, 0f, 30f, 0.25f, 0f, 0f }, v.SteeringMaxAngleCurve);
            CollectionAssert.AreEqual(new[] { "steeringRange" }, m.NotReproduced);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("KSP2 takes the curve maximum")));
        }

        [Test] public void Steering_WithoutSteeringCurve_GivesNoModule()
        {
            var m = WheelMapping.Steering(M("name = ModuleWheelSteering\ncaliperTransformName = SteeringPivot\nsteeringRange = 25"));
            Assert.IsFalse(m.Converted);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("no steeringCurve key, no steering")));
        }

        // ------------------------------------------------------------------ motor

        private const string MotorBody =
            "name = ModuleWheelMotor\nbaseModuleIndex = 0\nwheelSpeedMax = 42\ndriveResponse = 2\nidleDrain = 0.0\n" +
            "torqueCurve\n{\nkey = 0 1 0 0\nkey = 10 0.5 0 0\nkey = 42 0 0 0\n}\nRESOURCE\n{\nname = ElectricCharge\nrate = 2.5\n}";

        [Test] public void Motor_CopiesValues_AndTheEcRateAsIs()
        {
            var m = WheelMapping.Motor(M(MotorBody), Resources());
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual(42f, v.WheelSpeedMax);
            Assert.AreEqual(2f, v.DriveResponse);
            Assert.AreEqual(0.0, v.IdleDrain);
            Assert.IsTrue(v.UseResources);
            Assert.AreEqual("ElectricCharge", v.RequiredResourceName);
            Assert.AreEqual(2.5, v.RequiredResourceRate);
            Assert.AreEqual(12, v.TorqueCurve.Length);
            CollectionAssert.IsEmpty(m.NotReproduced);
        }

        [Test] public void Motor_TorqueCurveKeyWithTrailingComment_KeepsFourFloats()
        {
            var curve = new RawNode("torqueCurve", new[] { RawNode.KV("key", "0 50 0 0 // stall"), RawNode.KV("key", "42 0 0 0") });
            var node = new RawNode("MODULE", new[] { RawNode.KV("name", "ModuleWheelMotor") },
                new Dictionary<string, List<IKsp1Node>> { ["torqueCurve"] = new List<IKsp1Node> { curve } });
            var m = WheelMapping.Motor(node, Resources());
            Assert.IsTrue(m.Converted);
            CollectionAssert.AreEqual(new[] { 0f, 50f, 0f, 0f, 42f, 0f, 0f, 0f }, m.Values.TorqueCurve);
        }

        [Test] public void Motor_WithoutTorqueCurve_GivesNoModule()
        {
            var m = WheelMapping.Motor(M("name = ModuleWheelMotor\nwheelSpeedMax = 42\nRESOURCE\n{\nname = ElectricCharge\nrate = 1\n}"), Resources());
            Assert.IsFalse(m.Converted);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("no torqueCurve key, no motor")));
        }

        [Test] public void Motor_WithoutResource_DoesNotUseResources()
        {
            var m = WheelMapping.Motor(M("name = ModuleWheelMotor\ntorqueCurve\n{\nkey = 0 1\n}"), Resources());
            Assert.IsTrue(m.Converted);
            Assert.IsFalse(m.Values.UseResources);
            Assert.IsNull(m.Values.RequiredResourceName);
        }

        [Test] public void Motor_UnknownResource_DropsTheDrain_KeepsTheMotor()
        {
            var m = WheelMapping.Motor(M(MotorBody.Replace("ElectricCharge", "Kethane")), Resources());
            Assert.IsTrue(m.Converted);
            Assert.IsFalse(m.Values.UseResources);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("Kethane -> Kethane is not a game resource, no drain")), string.Join("; ", m.Notes));
        }

        [Test] public void Motor_ResourceNameMapped_SecondResourceNoted()
        {
            var m = WheelMapping.Motor(M(MotorBody.Replace("ElectricCharge", "LiquidFuel") + "\nRESOURCE\n{\nname = Oxidizer\nrate = 1\n}"), Resources());
            Assert.AreEqual("Methane", m.Values.RequiredResourceName, "through the resource map");
            Assert.AreEqual(2.5, m.Values.RequiredResourceRate, "the rate as is");
            CollectionAssert.AreEqual(new[] { "RESOURCE (second)" }, m.NotReproduced);
        }

        [Test] public void MotorSteering_MotorValuesPlusSteeringTorque()
        {
            var m = WheelMapping.MotorSteering(M(MotorBody.Replace("ModuleWheelMotor", "ModuleWheelMotorSteering") +
                                                 "\nsteeringTorque = 25\nmotorEnabled = True"), Resources());
            Assert.IsTrue(m.Converted);
            Assert.AreEqual(WheelKind.MotorSteering, m.Kind);
            Assert.AreEqual(25f, m.Values.SteeringTorque);
            Assert.IsTrue(m.Values.Motor.MotorEnabled);
            Assert.AreEqual(42f, m.Values.Motor.WheelSpeedMax);
            Assert.IsTrue(m.Values.Motor.UseResources);
            CollectionAssert.IsEmpty(m.NotReproduced);
        }

        // ------------------------------------------------------------------ brakes, damage, lock, bogey

        [Test] public void Brakes_KeepMaxBrakeTorque_StatusLightNoted()
        {
            var m = WheelMapping.Brakes(M("name = ModuleWheelBrakes\nbaseModuleIndex = 0\nmaxBrakeTorque = 0.34\nbrakeResponse = 1\nstatusLightModuleIndex = 6"));
            Assert.IsTrue(m.Converted);
            Assert.AreEqual(0.34f, m.Values.MaxBrakeTorque, 1e-6);
            Assert.AreEqual(1f, m.Values.BrakeResponse);
            CollectionAssert.AreEqual(new[] { "statusLightModuleIndex" }, m.NotReproduced);
        }

        [Test] public void Damage_CopiesTheTwoTransformNames_UnusedListed()
        {
            var m = WheelMapping.Damage(M("name = ModuleWheelDamage\nbaseModuleIndex = 0\ndamagedTransformName = bustedwheel\n" +
                                          "undamagedTransformName = wheel\nstressTolerance = 560\nimpactTolerance = 100\ndeflectionMagnitude = 1\n" +
                                          "deflectionSharpness = 2.0\nslipMagnitude = 15\nslipSharpness = 2.0\nexplodeMultiplier = 1.0\n" +
                                          "impactDamageColliderName = collisionEnhancer\nimpactDamageVelocity = 16\nisRepairable = false"));
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual("bustedwheel", v.DamagedTransformName);
            Assert.AreEqual("wheel", v.UndamagedTransformName);
            Assert.AreEqual(560f, v.StressTolerance);
            Assert.AreEqual(15f, v.SlipMagnitude);
            Assert.AreEqual(16f, v.ImpactDamageVelocity);
            Assert.IsFalse(v.IsRepairable);
            Assert.AreEqual(100f, v.ImpactTolerance);
            Assert.AreEqual("collisionEnhancer", v.ImpactDamageColliderName);
            CollectionAssert.AreEquivalent(WheelMapping.UnusedBySolverFields[WheelKind.Damage], m.UnusedBySolver);
            CollectionAssert.IsEmpty(m.NotReproduced);
        }

        [Test] public void Lock_MaxTorque()
        {
            var m = WheelMapping.Lock(M("name = ModuleWheelLock\nmaxTorque = 50000"));
            Assert.IsTrue(m.Converted);
            Assert.AreEqual(50000f, m.Values.MaxTorque);
        }

        [Test] public void Bogey_ParsesItsAxes_DeployIndexNoted()
        {
            var m = WheelMapping.Bogey(M("name = ModuleWheelBogey\nbaseModuleIndex = 0\nbogeyTransformName = Foot\ndeployModuleIndex = 2\n" +
                                         "maxPitch = 160\nminPitch = -160\nrestPitch = 0\npitchResponse = 100\nbogeyAxis = 1, 0, 0\n" +
                                         "bogeyUpAxis = 0.0, -1.0, 0.0"));
            Assert.IsTrue(m.Converted);
            var v = m.Values;
            Assert.AreEqual("Foot", v.BogeyTransformName);
            Assert.AreEqual(new WheelVector(1, 0, 0), v.BogeyAxis);
            Assert.AreEqual(new WheelVector(0, -1, 0), v.BogeyUpAxis);
            Assert.AreEqual(160f, v.MaxPitch);
            Assert.AreEqual(-160f, v.MinPitch);
            Assert.AreEqual(0f, v.RestPitch);
            Assert.AreEqual(100f, v.PitchResponse);
            CollectionAssert.AreEqual(new[] { "deployModuleIndex" }, m.NotReproduced);
        }

        [Test] public void Bogey_BadVector_KeepsTheDefault()
        {
            var m = WheelMapping.Bogey(M("name = ModuleWheelBogey\nbogeyAxis = 1, 0"));
            Assert.AreEqual(new WheelVector(1, 0, 0), m.Values.BogeyAxis);
            Assert.IsTrue(m.Notes.Any(n => n.Contains("bogeyAxis '1, 0' is not a vector")));
        }

        // ------------------------------------------------------------------ lists

        /// <summary>Every field of plan (b) that Redux's solver ignores lands in that list, never in "not reproduced".</summary>
        [Test] public void EveryUnusedField_LandsInUnusedBySolver()
        {
            var b = WheelMapping.Base(M(Rover + "\nadherentStart = 1\nfrictionAdherent = 1\npeakStart = 1\nlimitStart = 1\nfrictionSharpness = 1\n" +
                                        "TireForceSharpness = 1"), null, true, true);
            var s = WheelMapping.Suspension(M("name = ModuleWheelSuspension\nspringRatio = 1\ndamperRatio = 1\nboostRatio = 1\n" +
                                              "useDistributedMass = true\nuseAutoBoost = true\nadjustForHighGee = true"));
            var d = WheelMapping.Damage(M("name = ModuleWheelDamage\nimpactTolerance = 1\nimpactDamageColliderName = c"));
            foreach (var (kind, unused, notReproduced) in new[]
                     {
                         (WheelKind.Base, b.UnusedBySolver, b.NotReproduced),
                         (WheelKind.Suspension, s.UnusedBySolver, s.NotReproduced),
                         (WheelKind.Damage, d.UnusedBySolver, d.NotReproduced),
                     })
            {
                CollectionAssert.AreEquivalent(WheelMapping.UnusedBySolverFields[kind], unused, kind.ToString());
                CollectionAssert.IsEmpty(notReproduced.Intersect(WheelMapping.UnusedBySolverFields[kind]), kind.ToString());
            }
        }

        [Test] public void Kinds_NamesAndStateFields()
        {
            Assert.AreEqual("ModuleWheelMotorSteering", WheelMapping.Ksp1Module(WheelKind.MotorSteering));
            Assert.AreEqual("Module_WheelMotorSteering", WheelMapping.NativeModule(WheelKind.MotorSteering));
            Assert.AreEqual("Module_WheelBase", WheelMapping.NativeModule(WheelKind.Base));
            Assert.IsTrue(WheelMapping.TryKind("modulewheelbogey", out var k));
            Assert.AreEqual(WheelKind.Bogey, k);
            Assert.IsFalse(WheelMapping.TryKind(WheelMapping.DeploymentModule, out _));
            Assert.IsTrue(WheelMapping.IsStateField(WheelKind.Base, "frictionMultiplier"));
            Assert.IsFalse(WheelMapping.IsStateField(WheelKind.Brakes, "maxBrakeTorque"));
        }
    }
}
