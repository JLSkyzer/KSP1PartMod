using System;
using System.Linq;
using KSP1PartMod.Ui;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Ui
{
    public class Ksp1SubcategoriesTests
    {
        [TestCase("Pods", Ksp1Subcategory.Pods)]
        [TestCase("FuelTank", Ksp1Subcategory.FuelTank)]
        [TestCase("Fuel Tank", Ksp1Subcategory.FuelTank)]
        [TestCase("fueltank", Ksp1Subcategory.FuelTank)]
        [TestCase("Propulsion", Ksp1Subcategory.Engine)]
        [TestCase("Engine", Ksp1Subcategory.Engine)]
        [TestCase("Control", Ksp1Subcategory.Control)]
        [TestCase("Structural", Ksp1Subcategory.Structural)]
        [TestCase("Coupling", Ksp1Subcategory.Coupling)]
        [TestCase("Payload", Ksp1Subcategory.Payload)]
        [TestCase("Aero", Ksp1Subcategory.Aero)]
        [TestCase("Ground", Ksp1Subcategory.Ground)]
        [TestCase("Thermal", Ksp1Subcategory.Thermal)]
        [TestCase("Electrical", Ksp1Subcategory.Electrical)]
        [TestCase("Communication", Ksp1Subcategory.Communication)]
        [TestCase("Science", Ksp1Subcategory.Science)]
        [TestCase("Utility", Ksp1Subcategory.Utility)]
        [TestCase("Robotics", Ksp1Subcategory.Robotics)]
        [TestCase("Cargo", Ksp1Subcategory.Cargo)]
        [TestCase("  structural  ", Ksp1Subcategory.Structural)]
        [TestCase("Engine // comment", Ksp1Subcategory.Engine)]
        [TestCase("none", Ksp1Subcategory.Other)]
        [TestCase("CCK_Custom", Ksp1Subcategory.Other)]
        [TestCase("", Ksp1Subcategory.Other)]
        [TestCase(null, Ksp1Subcategory.Other)]
        public void Normalize_MapsKsp1Categories(string raw, Ksp1Subcategory expected)
        {
            Assert.AreEqual(expected, Ksp1Subcategories.Normalize(raw));
        }

        [Test] public void Order_IsKsp1EditorOrder()
        {
            CollectionAssert.AreEqual(new[]
            {
                Ksp1Subcategory.Pods, Ksp1Subcategory.FuelTank, Ksp1Subcategory.Engine, Ksp1Subcategory.Control,
                Ksp1Subcategory.Structural, Ksp1Subcategory.Coupling, Ksp1Subcategory.Payload, Ksp1Subcategory.Aero,
                Ksp1Subcategory.Ground, Ksp1Subcategory.Thermal, Ksp1Subcategory.Electrical,
                Ksp1Subcategory.Communication, Ksp1Subcategory.Science, Ksp1Subcategory.Utility,
                Ksp1Subcategory.Robotics, Ksp1Subcategory.Cargo, Ksp1Subcategory.Other,
            }, Ksp1Subcategories.Ordered);
            Assert.AreEqual(Enum.GetValues(typeof(Ksp1Subcategory)).Length, Ksp1Subcategories.Ordered.Count);
        }

        [TestCase("lander Squad Engine KSP1", Ksp1Subcategory.Engine)]
        [TestCase("Bluedog_DB FuelTank KSP1", Ksp1Subcategory.FuelTank)]
        [TestCase("ksp1", Ksp1Subcategory.Other)]
        [TestCase("tank fuel", Ksp1Subcategory.Other)]
        [TestCase(null, Ksp1Subcategory.Other)]
        public void FromTags_ReadsTheTokenBeforeKsp1(string tags, Ksp1Subcategory expected)
        {
            Assert.AreEqual(expected, Ksp1Subcategories.FromTags(tags));
        }

        [Test] public void Resolve_PrefersTheRawCategory()
        {
            Assert.AreEqual(Ksp1Subcategory.Robotics, Ksp1Subcategories.Resolve("Robotics", "x Squad Utility KSP1"));
            Assert.AreEqual(Ksp1Subcategory.Utility, Ksp1Subcategories.Resolve(null, "x Squad Utility KSP1"));
            Assert.AreEqual(Ksp1Subcategory.Other, Ksp1Subcategories.Resolve(null, null));
        }

        [Test] public void StockIcon_SameNameForTheFourteenStockTypes()
        {
            foreach (var sub in Ksp1Subcategories.Ordered.Take(14))
                CollectionAssert.AreEqual(new[] { sub.ToString() }, Ksp1Subcategories.StockIconCategories(sub), sub.ToString());
            CollectionAssert.AreEqual(new[] { "Storage", "Payload" }, Ksp1Subcategories.StockIconCategories(Ksp1Subcategory.Cargo));
            CollectionAssert.IsEmpty(Ksp1Subcategories.StockIconCategories(Ksp1Subcategory.Robotics));
            CollectionAssert.IsEmpty(Ksp1Subcategories.StockIconCategories(Ksp1Subcategory.Other));
        }

        [Test] public void PartIcon_ForTypesWithoutAStockSprite()
        {
            Assert.IsTrue(Ksp1Subcategories.PrefersPartIcon(Ksp1Subcategory.Robotics));
            Assert.IsTrue(Ksp1Subcategories.PrefersPartIcon(Ksp1Subcategory.Other));
            Assert.IsFalse(Ksp1Subcategories.PrefersPartIcon(Ksp1Subcategory.Cargo));
            Assert.IsFalse(Ksp1Subcategories.PrefersPartIcon(Ksp1Subcategory.Engine));
        }

        [Test] public void LocKey_StockKeysOnly()
        {
            Assert.AreEqual("VAB/PartsPicker/Engines", Ksp1Subcategories.LocKey(Ksp1Subcategory.Engine));
            Assert.AreEqual("VAB/PartsPicker/Fuel Tanks", Ksp1Subcategories.LocKey(Ksp1Subcategory.FuelTank));
            Assert.AreEqual("VAB/PartsPicker/Structures", Ksp1Subcategories.LocKey(Ksp1Subcategory.Structural));
            Assert.AreEqual("VAB/PartsPicker/Aerodynamics", Ksp1Subcategories.LocKey(Ksp1Subcategory.Aero));
            Assert.AreEqual("VAB/PartsPicker/Payloads", Ksp1Subcategories.LocKey(Ksp1Subcategory.Payload));
            foreach (var sub in Ksp1Subcategories.Ordered.Take(14))
                Assert.IsNotNull(Ksp1Subcategories.LocKey(sub), sub.ToString());
            Assert.IsNull(Ksp1Subcategories.LocKey(Ksp1Subcategory.Robotics));
            Assert.IsNull(Ksp1Subcategories.LocKey(Ksp1Subcategory.Cargo));
            Assert.IsNull(Ksp1Subcategories.LocKey(Ksp1Subcategory.Other));
        }

        [Test] public void Label_TranslationFirstThenKsp1Name()
        {
            Assert.AreEqual("Moteurs", Ksp1Subcategories.Label(Ksp1Subcategory.Engine, "fr", k => k == "VAB/PartsPicker/Engines" ? "Moteurs" : null));
            Assert.AreEqual("Engines", Ksp1Subcategories.Label(Ksp1Subcategory.Engine, "en", k => null));
            Assert.AreEqual("Engines", Ksp1Subcategories.Label(Ksp1Subcategory.Engine, "en", k => "  "));
            Assert.AreEqual("Robotics", Ksp1Subcategories.Label(Ksp1Subcategory.Robotics, "en", k => null));
            Assert.AreEqual("Robotique", Ksp1Subcategories.Label(Ksp1Subcategory.Robotics, "fr", k => null));
            Assert.AreEqual("Robotique", Ksp1Subcategories.Label(Ksp1Subcategory.Robotics, "fr-FR", k => null));
            Assert.AreEqual("Autres", Ksp1Subcategories.Label(Ksp1Subcategory.Other, "fr", k => null));
            Assert.AreEqual("Cargo", Ksp1Subcategories.Label(Ksp1Subcategory.Cargo, null, null));
        }

        [Test] public void AllLabel_FrenchOrEnglish()
        {
            Assert.AreEqual("Tout", Ksp1Subcategories.AllLabel("fr"));
            Assert.AreEqual("All", Ksp1Subcategories.AllLabel("en"));
            Assert.AreEqual("All", Ksp1Subcategories.AllLabel(null));
        }
    }
}
