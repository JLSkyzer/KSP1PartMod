using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.B9;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9TankRuleTests
    {
        private static readonly B9TankTypes Types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox + B9Text.BdbMono));

        private static B9Resource Res(List<B9Resource> list, string name) => list.Single(r => r.Name == name);

        [Test] public void Tank_Lfo_Volume100()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\n}");
            var r = B9TankRule.Resources(st, Types.Find("bdbLFOX"), 100);
            CollectionAssert.AreEqual(new[] { "LiquidFuel", "Oxidizer" }, r.Select(x => x.Name));
            Assert.AreEqual(45, Res(r, "LiquidFuel").MaxAmount, 1e-9);
            Assert.AreEqual(45, Res(r, "LiquidFuel").Amount, 1e-9);
            Assert.AreEqual(55, Res(r, "Oxidizer").MaxAmount, 1e-9);
            Assert.AreEqual(55, Res(r, "Oxidizer").Amount, 1e-9);
        }

        [Test] public void Tank_SubtypeResource_MergedByName()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\nRESOURCE\n{\nname = Oxidizer\nunitsPerVolume = 0.6\n}\n}");
            var r = B9TankRule.Resources(st, Types.Find("bdbLFOX"), 100);
            Assert.AreEqual(2, r.Count, "merged, not added a second time");
            Assert.AreEqual(60, Res(r, "Oxidizer").MaxAmount, 1e-9);
            Assert.AreEqual(45, Res(r, "LiquidFuel").MaxAmount, 1e-9);
        }

        [Test] public void Tank_SubtypeResource_NewName_Added()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = LFO\ntankType = bdbLFOX\nRESOURCE\n{\nname = ElectricCharge\nunitsPerVolume = 2\n}\n}");
            var r = B9TankRule.Resources(st, Types.Find("bdbLFOX"), 100);
            CollectionAssert.AreEqual(new[] { "LiquidFuel", "Oxidizer", "ElectricCharge" }, r.Select(x => x.Name));
            Assert.AreEqual(200, Res(r, "ElectricCharge").MaxAmount, 1e-9);
        }

        [Test] public void Tank_SubtypeResource_NoTank_MaxAmountKept()
        {
            var st = B9Text.Subtype("SUBTYPE\n{\nname = Battery\nRESOURCE\n{\nname = ElectricCharge\nmaxAmount = 300\namount = 100\n}\n}");
            var r = B9TankRule.Resources(st, null, 100);
            Assert.AreEqual(300, Res(r, "ElectricCharge").MaxAmount, 1e-9);
            Assert.AreEqual(100, Res(r, "ElectricCharge").Amount, 1e-9);
        }

        [Test] public void Tank_NeitherUnitsNorMax_Dropped_Noted()
        {
            var notes = new List<string>();
            var st = B9Text.Subtype("SUBTYPE\n{\nname = X\nRESOURCE\n{\nname = Ore\n}\n}");
            Assert.IsEmpty(B9TankRule.Resources(st, null, 100, notes));
            Assert.AreEqual(1, notes.Count);
            StringAssert.Contains("Ore", notes[0]);
        }

        [Test] public void Tank_Fill_Precedence_ResourceSubtypeTank100()
        {
            var mono = Types.Find("bdbMono"); // tank percentFilled = 50
            var tankOnly = B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = M\n}"), mono, 10);
            Assert.AreEqual(5, tankOnly[0].Amount, 1e-9, "tank 50 %");

            var subtype = B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = M\npercentFilled = 20\n}"), mono, 10);
            Assert.AreEqual(2, subtype[0].Amount, 1e-9, "subtype over tank");

            var resource = B9TankRule.Resources(B9Text.Subtype(
                "SUBTYPE\n{\nname = M\npercentFilled = 20\nRESOURCE\n{\nname = MonoPropellant\npercentFilled = 70\n}\n}"), mono, 10);
            Assert.AreEqual(7, resource[0].Amount, 1e-9, "resource over subtype");

            var full = B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = L\n}"), Types.Find("bdbLFOX"), 10);
            Assert.AreEqual(4.5, full[0].Amount, 1e-9, "100 by default");
        }

        [Test] public void Tank_PercentFilledZero_Empty()
        {
            var notes = new List<string>();
            var r = B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = L\npercentFilled = 0\n}"), Types.Find("bdbLFOX"), 100, notes);
            Assert.AreEqual(0, r[0].Amount);
            Assert.AreEqual(45, r[0].MaxAmount, 1e-9);
            Assert.IsEmpty(notes, "0 is a plain empty tank");
        }

        [Test] public void Tank_ExplicitAmount_Wins()
        {
            var r = B9TankRule.Resources(B9Text.Subtype(
                "SUBTYPE\n{\nname = L\npercentFilled = 0\nRESOURCE\n{\nname = LiquidFuel\namount = 10\n}\n}"), Types.Find("bdbLFOX"), 100);
            Assert.AreEqual(10, Res(r, "LiquidFuel").Amount, 1e-9);
            Assert.AreEqual(45, Res(r, "LiquidFuel").MaxAmount, 1e-9);
            Assert.AreEqual(0, Res(r, "Oxidizer").Amount, 1e-9);
        }

        [Test] public void Tank_PercentFilledOne_ReadAsPercent_Noted()
        {
            var notes = new List<string>();
            var r = B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = L\npercentFilled = 1\n}"), Types.Find("bdbLFOX"), 100, notes);
            Assert.AreEqual(0.45, r[0].Amount, 1e-9, "1 %, not full");
            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual(B9Lines.Notes.PercentFilledSmall(1), notes[0]);
        }

        [Test] public void Tank_Structural_NoResources()
        {
            Assert.IsEmpty(B9TankRule.Resources(B9Text.Subtype("SUBTYPE\n{\nname = S\n}"), Types.Find("Structural"), 100));
        }
    }
}
