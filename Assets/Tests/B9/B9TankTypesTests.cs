using System.Linq;
using KSP1PartMod.B9;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9TankTypesTests
    {
        [Test] public void TankTypes_BdbType_IsRead()
        {
            var types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox));
            var t = types.Find("bdbLFOX");
            Assert.IsNotNull(t);
            Assert.AreEqual(0.000625, t.TankMass.Value, 1e-12);
            Assert.AreEqual(0.7, t.TankCost.Value, 1e-12);
            Assert.IsNull(t.PercentFilled);
            CollectionAssert.AreEqual(new[] { "LiquidFuel", "Oxidizer" }, t.Resources.Select(r => r.Name));
            Assert.AreEqual(0.45, t.Resources[0].UnitsPerVolume.Value, 1e-12);
            Assert.AreEqual(0.55, t.Resources[1].UnitsPerVolume.Value, 1e-12);
            Assert.AreSame(t, types.Find("BDBLFOX"), "names ignore case");
            Assert.IsNull(types.Find("bdbLH2"));
        }

        [Test] public void TankTypes_OnlyB9TankTypeRoots()
        {
            var types = B9TankTypes.From(B9Text.Roots("PART\n{\nname = x\n}\n" + B9Text.BdbMono + "RESOURCE_DEFINITION\n{\nname = y\n}"));
            Assert.IsNotNull(types.Find("bdbMono"));
            Assert.AreEqual(50, types.Find("bdbMono").PercentFilled.Value, 1e-12);
            Assert.IsNull(types.Find("x"));
            Assert.IsNull(types.Find("y"));
        }

        [Test] public void TankTypes_Duplicate_KeepsTheFirst_Counted()
        {
            var second = B9Text.BdbLfox.Replace("0.000625", "0.5");
            var types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox + second.Replace("bdbLFOX", "bdblfox")));
            Assert.AreEqual(0.000625, types.Find("bdbLFOX").TankMass.Value, 1e-12);
            CollectionAssert.AreEqual(new[] { "bdblfox" }, types.Duplicates);
        }

        [Test] public void TankTypes_Structural_AddedWhenAbsent_Empty()
        {
            var types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox));
            var s = types.Find("Structural");
            Assert.IsNotNull(s);
            Assert.AreEqual(0, s.TankMass.Value);
            Assert.AreEqual(0, s.TankCost.Value);
            Assert.AreEqual(0, s.Resources.Count);
            Assert.AreEqual(2, types.Count);
        }

        [Test] public void TankTypes_Structural_FromFileWins()
        {
            var text = "B9_TANK_TYPE\n{\nname = Structural\ntankMass = 0.1\n}";
            Assert.AreEqual(0.1, B9TankTypes.From(B9Text.Roots(text)).Find("structural").TankMass.Value, 1e-12);
        }

        [Test] public void TankTypes_BuiltIns_ResourceOnly_FileWins()
        {
            var builtIns = new[]
            {
                B9TankType.ResourceOnly("LFO", new[] { new B9ResourceDef { Name = "LiquidFuel", UnitsPerVolume = 0.45 } }),
                B9TankType.ResourceOnly("bdbLFOX", new B9ResourceDef[0]),
            };
            var types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox), builtIns);
            var lfo = types.Find("LFO");
            Assert.IsNotNull(lfo);
            Assert.IsTrue(lfo.BuiltIn);
            Assert.IsNull(lfo.TankMass, "Redux's built-ins carry no tankMass");
            Assert.AreEqual(0.000625, types.Find("bdbLFOX").TankMass.Value, 1e-12, "a file definition wins");
            Assert.IsEmpty(types.Duplicates, "a built-in under a file name is not a duplicate");
        }

        [Test] public void TankTypes_NullOrEmpty_OnlyStructural()
        {
            Assert.AreEqual(1, B9TankTypes.From(null).Count);
            Assert.IsNull(B9TankTypes.From(null).Find(null));
        }
    }
}
