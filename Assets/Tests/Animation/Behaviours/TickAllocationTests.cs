using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Task 8 ticks every machine every frame: Tick must not allocate. Unity's GC.Alloc recorder is used
    /// because Mono does not implement <c>GC.GetAllocatedBytesForCurrentThread</c>.</summary>
    public class TickAllocationTests
    {
        private static object _sink;

        [Test] public void Tick_AllocationCheck_DetectsAnAllocation() =>
            Assert.That(() => { _sink = new byte[64]; }, Is.AllocatingGCMemory());

        [Test] public void Tick_AllMachines_DoNotAllocate()
        {
            var toggle = new ToggleMachine(new AnimEntryDef { Kind = EntryKind.Toggle, Key = "t" }, 1f);
            var gear = new DeployMachine(new AnimEntryDef { Kind = EntryKind.Gear, Key = "g" }, 1f);
            var deploy = new DeployMachine(new AnimEntryDef { Kind = EntryKind.Deploy, Key = "d", Retractable = true }, 100f);
            var throttle = new ThrottleFollower(new AnimEntryDef { Kind = EntryKind.Throttle, Key = "f", ResponseSpeed = 0.5f, WeightOnOperational = true });
            var heat = new HeatScalar(new AnimEntryDef { Kind = EntryKind.Heat, Key = "h" });
            var color = new ColorRamp(new AnimEntryDef
            {
                Kind = EntryKind.Color, Key = "c", Curves = new[] { new float[] { 0, 0, float.NaN, float.NaN, 1, 1, 0, 0 } },
            });
            var group = new GroupMachine(new AnimEntryDef { Kind = EntryKind.Group, Key = "a", Clip = "d", SecondClip = "x", AlwaysActive = true }, 1f, 1f);
            var gate = new AeroGate(new AnimEntryDef { Kind = EntryKind.AeroGate, Key = "z" });
            toggle.Toggle(false); gear.Extend(false); deploy.Extend(false); color.Toggle(); group.Deploy(false);

            var i = 0;
            void Frames()
            {
                for (var k = 0; k < 100; k++, i++)
                {
                    toggle.Tick(0.02f); gear.Tick(0.02f); deploy.Tick(0.02f); throttle.Tick(0.02f, false, 1f, (i & 1) == 0);
                    heat.Tick(0.02f, 900f + i, 2000f); color.Tick(0.02f); group.Tick(0.02f, false); gate.Tick(i * 0.01f);
                    _ = toggle.Pose; _ = gear.Pose; _ = deploy.Pose; _ = throttle.Pose; _ = heat.Pose; _ = color.Color;
                    _ = group.DeployPose; _ = group.ActivePose;
                }
            }

            Frames(); // warm up (JIT)
            Assert.AreEqual(DeployState.Extending, deploy.State, "the measured Deploy is still moving");
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
        }

        /// <summary>0.10.0: the Group's active input (bound once) and the deploy bridge's state rule, 10 000 steps.</summary>
        [Test] public void Group_ActiveInput_AndBridgeRule_DoNotAllocate()
        {
            var running = false;
            var group = new GroupMachine(new AnimEntryDef { Kind = EntryKind.Group, Key = "a", Clip = "d", SecondClip = "x" }, 1f, 1f)
            {
                ActiveInput = () => running,
            };
            group.Deploy(false);
            var state = BridgeDeploy.Retracted;
            var i = 0;
            void Steps()
            {
                for (var k = 0; k < 10000; k++, i++)
                {
                    running = (i / 50 & 1) == 0;
                    group.Tick(0.02f, false);
                    _ = group.ActivePose;
                    state = IsruAnimRules.StateOf(group.Deployed, group.IsMoving, (i & 7) == 0);
                }
            }

            Steps(); // warm up (JIT)
            Assert.AreEqual(BridgeDeploy.Extended, state);
            Assert.That(Steps, Is.Not.AllocatingGCMemory());
        }
    }
}
