using System.Collections.Generic;
using KSP1PartMod.B9;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9MassRuleTests
    {
        private static readonly B9TankTypes Types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox),
            new[] { B9TankType.ResourceOnly("LFO", new B9ResourceDef[0]) });

        [Test] public void Mass_AddedMassOnly()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = Heavy\naddedMass = 0.125\naddedCost = 40\n}");
            Assert.AreEqual(0.125, B9MassRule.Delta(st, null, null), 1e-12);
            Assert.AreEqual(40, B9MassRule.Cost(st, null, null), 1e-12);
        }

        [Test] public void Mass_TankOnly()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\n}");
            Assert.AreEqual(0.0625, B9MassRule.Delta(st, Types.Find("bdbLFOX"), 100), 1e-12);
            Assert.AreEqual(70, B9MassRule.Cost(st, Types.Find("bdbLFOX"), 100), 1e-9);
        }

        [Test] public void Mass_Both()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\naddedMass = -0.01\naddedCost = 5\n}");
            Assert.AreEqual(0.0525, B9MassRule.Delta(st, Types.Find("bdbLFOX"), 100), 1e-12);
            Assert.AreEqual(75, B9MassRule.Cost(st, Types.Find("bdbLFOX"), 100), 1e-9);
        }

        [Test] public void Mass_TankWithoutTankMass_Zero_Noted()
        {
            var notes = new List<string>();
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = LFO\naddedMass = 0.1\n}");
            Assert.AreEqual(0.1, B9MassRule.Delta(st, Types.Find("LFO"), 100, notes), 1e-12);
            CollectionAssert.AreEqual(new[] { B9Lines.Notes.TankWithoutMass("LFO") }, notes);
        }

        [Test] public void Mass_Structural_Zero_NotNoted()
        {
            var notes = new List<string>();
            var st = B9Text.Subtype("SUBTYPE\n{\nname = S\ntankType = Structural\n}");
            Assert.AreEqual(0, B9MassRule.Delta(st, Types.Find("Structural"), 100, notes));
            Assert.IsEmpty(notes);
        }

        [Test] public void Mass_TankAndNoVolume_AddedMassOnly_Noted()
        {
            var notes = new List<string>();
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\naddedMass = 0.2\n}");
            Assert.AreEqual(0.2, B9MassRule.Delta(st, Types.Find("bdbLFOX"), null, notes), 1e-12);
            CollectionAssert.AreEqual(new[] { B9Lines.Notes.NoVolumeForDryMass("LFO", "bdbLFOX") }, notes);
        }

        [Test] public void Mass_UnparseableAddedMass_Zero_Noted()
        {
            var notes = new List<string>();
            var st = B9Text.Subtype("SUBTYPE\n{\nname = X\naddedMass = #$../m$\n}");
            Assert.AreEqual(0, B9MassRule.Delta(st, null, null, notes));
            CollectionAssert.AreEqual(new[] { B9Lines.Notes.Unparseable("X", "addedMass", "#$../m$") }, notes);
        }
    }
}
