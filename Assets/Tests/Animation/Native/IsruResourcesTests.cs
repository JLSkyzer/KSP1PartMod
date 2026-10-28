using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="IsruResources"/>: the injected mapper, converter, database and minable set (plan (g)).</summary>
    public class IsruResourcesTests
    {
        [Test] public void Ore_MapsToCarbonRegolith_ByMass()
        {
            var r = IsruFakes.Table();
            var (name, rate) = r.MappedRate("Ore", 0.5);
            Assert.AreEqual("CarbonRegolith", name);
            Assert.AreEqual(0.025, rate, 1e-12, "0.5 units of 0.05 t = 0.025 t = 0.025 CarbonRegolith units of 1 t");
            Assert.IsTrue(r.IsMinable(name));
            Assert.IsTrue(r.IsKnown(name));
        }

        [Test] public void LiquidFuel_MapsToMethane_NotMinable()
        {
            var r = IsruFakes.Table();
            var (name, rate) = r.MappedRate("LiquidFuel", 0.45);
            Assert.AreEqual("Methane", name);
            Assert.AreEqual(0.9, rate, 1e-12);
            Assert.IsFalse(r.IsMinable(name));
            Assert.IsTrue(r.IsKnown(name));
        }

        [Test] public void ElectricCharge_HasDensity0_Unchanged()
        {
            var (name, rate) = IsruFakes.Table().MappedRate("ElectricCharge", 30);
            Assert.AreEqual("ElectricCharge", name);
            Assert.AreEqual(30, rate);
        }

        [Test] public void UnknownResource_KeepsItsName_NotKnown_AmountUnchanged()
        {
            var r = IsruFakes.Table();
            var (name, rate) = r.MappedRate("Karbonite", 2);
            Assert.AreEqual("Karbonite", name);
            Assert.AreEqual(2, rate);
            Assert.IsFalse(r.IsKnown(name));
            Assert.IsFalse(r.IsMinable(name));
        }

        [Test] public void TestRocks_KnownButNotMinable()
        {
            var r = IsruFakes.Table("TestRocks");
            Assert.AreEqual("TestRocks", r.Map("Ore"));
            Assert.IsTrue(r.IsKnown("TestRocks"));
            Assert.IsFalse(r.IsMinable("TestRocks"));
        }

        [Test] public void NullDelegates_IdentityAndEverythingKnown()
        {
            var r = new IsruResources(null, null, null, null);
            Assert.AreEqual("Ore", r.Map("Ore"));
            Assert.AreEqual(("Ore", 3.0), r.MappedRate("Ore", 3));
            Assert.IsTrue(r.IsKnown("Anything"), "no database: every name is taken as known");
            Assert.IsFalse(r.IsMinable("Water"), "no minable set: nothing minable");
            Assert.IsNaN(r.MassPerUnit("Water"));
        }

        [Test] public void MapperReturningNullOrEmpty_KeepsTheKsp1Name_ConverterNaNKeepsTheAmount()
        {
            var r = new IsruResources(n => n == "A" ? null : "", (s, t, a) => double.NaN, n => true, new[] { "A" });
            Assert.AreEqual("A", r.Map("A"));
            Assert.AreEqual("B", r.Map("B"));
            Assert.AreEqual(5, r.MappedRate("A", 5).Rate, "a converter that cannot convert leaves the amount, as Redux does");
            Assert.IsTrue(r.IsMinable("A"));
        }

        [Test] public void MinableSet_IsOrdinal_AndListed()
        {
            var r = IsruFakes.Table();
            Assert.IsFalse(r.IsMinable("water"));
            CollectionAssert.AreEquivalent(IsruFakes.Minable, r.Minable);
        }
    }
}
