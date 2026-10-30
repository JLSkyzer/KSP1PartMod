using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Wheels
{
    /// <summary>The B9 census and the reference statistics of the wheel importers (wheels plan, Task 3).</summary>
    public class WheelSessionTests
    {
        [Test] public void B9Census_CountsWheelEditsOnce_AndTheStateFields()
        {
            var c = new WheelB9Census();
            c.Add("bluedog_LRV_Wheel", "Tire", "ModuleWheelBase", new[] { "frictionMultiplier" });
            c.Add("bluedog_LRV_Wheel", "Tire", "ModuleWheelBase", new[] { "frictionMultiplier" }); // a rebuild
            c.Add("rover", "Hard", "ModuleWheelBrakes", new[] { "maxBrakeTorque" });
            c.Add("rover", "Hard", "ModuleEnginesFX", new[] { "maxThrust" });
            c.Add("rover", "Fast", "ModuleWheelMotorSteering", new[] { "autoTorque", "wheelSpeedMax" });
            Assert.AreEqual(3, c.Edits);
            CollectionAssert.AreEqual(new[] { ("bluedog_LRV_Wheel", "frictionMultiplier"), ("rover", "autoTorque") }, c.StateEdits);
            Assert.AreEqual("[KSP1PartMod] native: wheels: B9: 3 subtype edit(s) of wheel modules, 2 on state fields not carried " +
                            "(bluedog_LRV_Wheel: frictionMultiplier, rover: autoTorque)", c.Line());
            c.Clear();
            Assert.AreEqual("[KSP1PartMod] native: wheels: B9: 0 subtype edit(s) of wheel modules, 0 on state fields not carried (none)", c.Line());
        }

        /// <summary>Review F6: KSP2's suspension needs a collider on or below the named object; an absent name is skipped.</summary>
        [Test] public void SuspensionCollider_AbsentFoundNoCollider()
        {
            var root = new MuNode("model", null);
            var piston = new MuNode("piston_collider", root);
            var mesh = new MuNode("piston_mesh", piston);
            mesh.AddEntryType(MuEntryType.MeshCollider2);
            var bare = new MuNode("damper", root);
            new MuNode("damper_mesh", bare).AddEntryType(MuEntryType.MeshFilter);
            var wheel = new MuNode("wheelCollider", root);
            wheel.AddEntryType(MuEntryType.WheelCollider);
            Assert.AreEqual(SuspensionColliderState.Found, SuspensionColliderRule.Check(new[] { root }, "piston_collider"), "a collider below counts");
            Assert.AreEqual(SuspensionColliderState.NoCollider, SuspensionColliderRule.Check(new[] { root }, "damper"));
            Assert.AreEqual(SuspensionColliderState.NoCollider, SuspensionColliderRule.Check(new[] { root }, "wheelCollider"),
                "Redux skips the .mu WheelCollider entry");
            Assert.AreEqual(SuspensionColliderState.Absent, SuspensionColliderRule.Check(new[] { root }, "Piston_Collider"), "exact name, as KSP2 searches");
            Assert.AreEqual(SuspensionColliderState.Absent, SuspensionColliderRule.Check(new MuNode[0], "damper"));
            var other = new MuNode("model2", null);
            new MuNode("piston_collider", other);
            Assert.AreEqual(SuspensionColliderState.NoCollider, SuspensionColliderRule.Check(new[] { root, other }, "piston_collider"),
                "two objects of that name: every one must have a collider");
        }

        [Test] public void Median_SkipsNaN()
        {
            Assert.AreEqual(2, WheelReferenceStats.Median(new[] { 3, double.NaN, 1, 2 }));
            Assert.AreEqual(1.5, WheelReferenceStats.Median(new[] { 1.0, 2 }));
            Assert.IsNaN(WheelReferenceStats.Median(new[] { double.NaN }));
        }

        [Test] public void ReferenceLine_CountsTypes_MediansOfWhatExists()
        {
            var samples = new[]
            {
                new WheelReferenceSample { Part = "a", Type = WheelTypeName.LEG, Radius = 0.2, Lock = 1000, RatedLoad = 5 },
                new WheelReferenceSample { Part = "b", Type = WheelTypeName.MOTORIZED, Radius = 0.4, Brake = 2000, MotorPeak = 10, MotorEc = 1.5, RatedLoad = 3 },
                new WheelReferenceSample { Part = "c", Type = WheelTypeName.FREE, Radius = 0.3, Brake = 1000, RatedLoad = 4 },
            };
            Assert.AreEqual("[KSP1PartMod] native: wheel reference: 3 KSP2 wheel part(s) (LEG 1, FREE 1, MOTORIZED 1), median radius 0.3 m, " +
                            "brake 1500 kN·m, lock 1000 kN·m, motor peak 10 kN·m at 1.5 EC/s, rated load 4 t", WheelReferenceStats.Line(samples));
            Assert.AreEqual("[KSP1PartMod] native: wheel reference: none read (no KSP2 part with Data_WheelBase)",
                WheelReferenceStats.Line(new WheelReferenceSample[0]));
        }
    }
}
