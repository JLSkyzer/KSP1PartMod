using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Unity's GC.Alloc recorder: <c>GC.GetAllocatedBytesForCurrentThread</c> reads 0 under Mono.</summary>
    public class PluginAllocationTests
    {
        [Test] public void PluginMachines_10000Frames_AllocateZero()
        {
            var deploy = new EngineDeployMachine(new AnimEntryDef { Kind = EntryKind.EngineDeploy, Key = "e", PersistDeployed = true }, 1f);
            var mode = new EngineModeMachine(new AnimEntryDef { Kind = EntryKind.EngineMode, Key = "m", ModeAtTimeZero = "A" }, 1f);
            var dec = new DecouplerClipMachine(new AnimEntryDef { Kind = EntryKind.DecouplerClip, Key = "d" }, 1f);
            var spring = new AccelSpring(new AnimEntryDef { Kind = EntryKind.AccelSpring, Key = "s", SpringK = 40f, SpringDamping = 2f, AnchorDeployed = 0.5f }, 1f);
            var constrained = new[] { true, true, false };
            var scales = new[] { 0.5f, 1f, 1f };
            var sink = 0f;
            var i = 0;
            void Frames()
            {
                for (var k = 0; k < 10000; k++, i++)
                {
                    var on = (i / 50 & 1) == 0;
                    deploy.Tick(0.02f, false, on, 1f);
                    mode.Tick(0.02f, false, on ? "B" : "A", 1f);
                    dec.Tick(0.02f, k % 3000 > 1500, on, 1f);
                    spring.Tick(0.02f, on ? 9f : float.NaN);
                    float x = 1, y = 2, z = 3;
                    AxisBlend.Blend(ref x, ref y, ref z, 9, 8, 7, constrained, scales);
                    sink += deploy.Pose.NormalizedTime + mode.Pose.NormalizedTime + dec.Pose.NormalizedTime
                            + spring.Pose.NormalizedTime + RcsScalar.Of(i, 100f, 1f) + x + y + z
                            + (MeshHiderRule.Hidden(on, !on) ? 1f : 0f);
                    _ = deploy.Changed; _ = deploy.Deployed; _ = dec.Changed;
                }
            }

            Frames(); // warm up (JIT)
            Assert.That(Frames, Is.Not.AllocatingGCMemory());
            Assert.IsFalse(float.IsNaN(sink));
        }
    }
}
