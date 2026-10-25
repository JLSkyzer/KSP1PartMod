using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class AeroGateTests
    {
        private static AeroGate Gate(float deployedPosition) => new AeroGate(new AnimEntryDef
        {
            Kind = EntryKind.AeroGate, Key = "aero:0:0", ControllerModuleIndex = 0, DeployedPosition = deployedPosition,
        });

        [Test] public void AeroGate_BoosterWing_OpenAtZero() => Assert.IsTrue(Gate(deployedPosition: 0f).Tick(0f));

        [Test] public void AeroGate_BoosterWing_ClosedAtOne() => Assert.IsFalse(Gate(deployedPosition: 0f).Tick(1f));

        [Test] public void AeroGate_GridFin_OpenAtOne() => Assert.IsTrue(Gate(deployedPosition: 1f).Tick(1f));

        [Test] public void AeroGate_MidTravel_Closed() => Assert.IsFalse(Gate(deployedPosition: 1f).Tick(0.98f));

        [Test] public void AeroGate_WithinTolerance_Open() => Assert.IsTrue(Gate(deployedPosition: 1f).Tick(0.995f));

        [Test] public void AeroGate_Open_FollowsLastTick()
        {
            var g = Gate(1f); g.Tick(1f); g.Tick(0.5f); Assert.IsFalse(g.Open);
        }
    }
}
