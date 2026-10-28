using KSP1PartMod.Native.Wheels;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Wheels
{
    /// <summary>The exact text of the wheel lines (plan, Tasks 3 and 4).</summary>
    public class WheelLinesTests
    {
        [Test] public void Reference()
        {
            Assert.AreEqual("[KSP1PartMod] native: wheel reference: 12 KSP2 wheel part(s) (LEG 3, FREE 5, MOTORIZED 4), median radius 0.3 m, " +
                            "brake 2000 kN·m, lock 1000 kN·m, motor peak 10 kN·m at 1.5 EC/s, rated load 8 t",
                WheelLines.Reference(12, 3, 5, 4, 0.3, 2000, 1000, 10, 1.5, 8));
            StringAssert.Contains("lock none kN·m", WheelLines.Reference(1, 0, 1, 0, 0.3, 2000, double.NaN, 10, 1.5, 8));
            Assert.AreEqual("[KSP1PartMod] native: wheel reference: none read (no game in EditMode)", WheelLines.ReferenceNone("no game in EditMode"));
        }

        [Test] public void Part()
        {
            var b = new WheelBaseValues
            {
                WheelType = WheelTypeName.LEG, Radius = 0.12f, WheelColliderTransformName = "wheelCollider", DeploymentSubsystemNormalized = 0.7f,
                RetractionSubsystemNormalized = 0.3f, FrictionPeak = 4, FrictionLimit = 4, AutoFriction = false,
            };
            Assert.AreEqual("[KSP1PartMod] native: landingLeg1: wheel LEG r 0.12 m on 'wheelCollider': base, suspension, lock " +
                            "(deploy on at 0.7, off at 0.3; friction 4/4, manual)",
                WheelLines.Part("landingLeg1", b, "base, suspension, lock"));
            b.AutoFriction = true;
            StringAssert.EndsWith(", auto)", WheelLines.Part("p", b, "base"));
        }

        [Test] public void Summary()
        {
            var t = new WheelTally();
            t.AddBase(WheelTypeName.LEG);
            t.AddBase(WheelTypeName.FREE);
            t.AddBase(WheelTypeName.MOTORIZED);
            t.AddConverted(WheelKind.Suspension);
            t.AddConverted(WheelKind.Suspension);
            t.AddConverted(WheelKind.Suspension);
            t.AddConverted(WheelKind.MotorSteering);
            t.AddRefused(WheelRefusal.SwitchOff);
            t.AddNotes(new[] { "TooltipTitle", "TooltipTitle", "steeringRange" }, new[] { "springRatio" });
            Assert.AreEqual("[KSP1PartMod] native: wheels: 3 part(s) got Module_WheelBase (1 leg(s), 1 free, 1 motorized); suspension 3, " +
                            "steering 0, motor 0, motor-steering 1, brakes 0, damage 0, lock 0, bogey 0; refused: switch off 1; " +
                            "not reproduced: TooltipTitle 2, steeringRange 1; unused by Redux's solver: springRatio 1",
                WheelLines.Summary(t));
            StringAssert.EndsWith("refused: none; not reproduced: none; unused by Redux's solver: none", WheelLines.Summary(new WheelTally()));
        }

        [Test] public void MemberNotFound_AndB9()
        {
            Assert.AreEqual("[KSP1PartMod] native: wheels: Data_WheelBogey.bogeyAxis not found, bogey off (Redux drops it, as in 0.13.0)",
                WheelLines.MemberNotFound("Data_WheelBogey.bogeyAxis", "bogey"));
            Assert.AreEqual("[KSP1PartMod] native: wheels: B9: 1 subtype edit(s) of wheel modules, 1 on state fields not carried " +
                            "(bluedog_LRV_Wheel: frictionMultiplier)",
                WheelLines.B9(1, new[] { ("bluedog_LRV_Wheel", "frictionMultiplier") }));
            StringAssert.EndsWith("0 on state fields not carried (none)", WheelLines.B9(0, null));
        }

        [Test] public void Runtime()
        {
            Assert.AreEqual("[KSP1PartMod] wheel: armed: base, steering", WheelLines.Armed(new[] { "base", "steering" }));
            Assert.AreEqual("[KSP1PartMod] wheel: armed: none", WheelLines.Armed(null));
            Assert.AreEqual("[KSP1PartMod] wheel: roverWheel1: MOTORIZED, collider host 'WheelCollider' found, wheel transform 'WheelPivot' found, " +
                            "deploy gate none (fixed wheel)",
                WheelLines.Setup("roverWheel1", WheelTypeName.MOTORIZED, "WheelCollider", true, "WheelPivot", true, WheelGate.FixedWheel));
            Assert.AreEqual("[KSP1PartMod] wheel: GearSmall: FREE, collider host 'wheelCollider' MISSING, wheel transform '' none, " +
                            "deploy gate native Module_Deployable",
                WheelLines.Setup("GearSmall", WheelTypeName.FREE, "wheelCollider", false, null, false, WheelGate.Native));
            StringAssert.Contains("wheel transform 'w' MISSING", WheelLines.Setup("p", WheelTypeName.FREE, "h", true, "w", false, WheelGate.Native));
            Assert.AreEqual("[KSP1PartMod] wheel: Module_WheelBase.OnInitialize not found, the wheel line is off (Redux's module left as is)",
                WheelLines.RuntimeMemberNotFound("Module_WheelBase.OnInitialize"));
            Assert.AreEqual("[KSP1PartMod] wheel: patch failed, stopped for this session: boom", WheelLines.PatchFailed("boom"));
            Assert.AreEqual("gear bridge: Module_WheelBase already on Gear", WheelLines.GearBridgeStandsDown);
        }

        [TestCase(true, false, true, false, WheelGate.Native, "native Module_Deployable")]
        [TestCase(false, false, false, false, WheelGate.FixedWheel, "none (fixed wheel)")]
        [TestCase(false, true, true, false, WheelGate.LegacyKept, "none: always active (Redux kept its legacy deployable)")]
        [TestCase(false, false, true, true, WheelGate.OwnFirst, "none: always active (own_first_deployable)")]
        public void Gate_FourForms(bool hasNative, bool hasLegacy, bool hadDeployment, bool ownFirst, WheelGate gate, string text)
        {
            Assert.AreEqual(gate, WheelLines.Gate(hasNative, hasLegacy, hadDeployment, ownFirst));
            Assert.AreEqual(text, WheelLines.GateText(gate));
        }
    }
}
