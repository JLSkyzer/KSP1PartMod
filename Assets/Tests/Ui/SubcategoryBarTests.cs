using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Placement;
using KSP1PartMod.Ui;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Ui
{
    public class SubcategoryBarTests
    {
        private static SubcategoryBarModel Model(params Ksp1Subcategory[] visibleParts) =>
            SubcategoryBarModel.Build(visibleParts);

        // ---------------------------------------------------------------- model

        [Test] public void Model_ListsOnlyTypesWithAVisiblePart_InEditorOrder()
        {
            var m = Model(Ksp1Subcategory.Robotics, Ksp1Subcategory.Engine, Ksp1Subcategory.Pods, Ksp1Subcategory.Engine);
            CollectionAssert.AreEqual(new[] { Ksp1Subcategory.Pods, Ksp1Subcategory.Engine, Ksp1Subcategory.Robotics },
                m.Types.Select(t => t.Type));
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, m.Types.Select(t => t.Count));
            Assert.AreEqual(4, m.Total);
            Assert.AreEqual(2, m.CountOf(Ksp1Subcategory.Engine));
            Assert.AreEqual(0, m.CountOf(Ksp1Subcategory.Cargo));
            Assert.IsTrue(m.Contains(Ksp1Subcategory.Robotics));
            Assert.IsFalse(m.Contains(Ksp1Subcategory.Cargo));
        }

        [Test] public void Model_HiddenWhenOneTypeOrNone()
        {
            Assert.IsFalse(Model().Show);
            Assert.IsFalse(Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Engine).Show);
            Assert.IsTrue(Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Pods).Show);
        }

        [Test] public void Model_Signature_ChangesWithCounts()
        {
            var a = Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Pods);
            var b = Model(Ksp1Subcategory.Pods, Ksp1Subcategory.Engine);
            var c = Model(Ksp1Subcategory.Pods, Ksp1Subcategory.Engine, Ksp1Subcategory.Engine);
            Assert.AreEqual(a.Signature, b.Signature);
            Assert.AreNotEqual(a.Signature, c.Signature);
            Assert.AreEqual("Pods 1, Engine 2", c.Signature);
        }

        [Test] public void Model_NullInput_Empty()
        {
            var m = SubcategoryBarModel.Build(null);
            Assert.AreEqual(0, m.Total);
            Assert.IsFalse(m.Show);
        }

        // ---------------------------------------------------------------- selection

        [Test] public void Selection_DefaultsToAll()
        {
            var s = new SubcategorySelection();
            Assert.IsNull(s.Get("Squad"));
            Assert.IsNull(s.Effective("Squad", Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Pods)));
        }

        [Test] public void Selection_RememberedPerModName_CaseInsensitive()
        {
            var s = new SubcategorySelection();
            s.Set("Squad", Ksp1Subcategory.Engine);
            s.Set("Bluedog_DB", Ksp1Subcategory.Pods);
            Assert.AreEqual(Ksp1Subcategory.Engine, s.Get("squad"));
            Assert.AreEqual(Ksp1Subcategory.Pods, s.Get("Bluedog_DB"));
            s.Set("Squad", null);
            Assert.IsNull(s.Get("Squad"));
            Assert.AreEqual(Ksp1Subcategory.Pods, s.Get("Bluedog_DB"));
        }

        [Test] public void Selection_EffectiveIsAll_WhenTheTypeHasNoVisiblePart_ButStaysRemembered()
        {
            var s = new SubcategorySelection();
            s.Set("Squad", Ksp1Subcategory.Robotics);
            Assert.IsNull(s.Effective("Squad", Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Pods)));
            Assert.AreEqual(Ksp1Subcategory.Robotics, s.Get("Squad"));
            Assert.AreEqual(Ksp1Subcategory.Robotics,
                s.Effective("Squad", Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Robotics)));
        }

        [Test] public void Selection_EffectiveIsAll_WhenTheBarIsHidden()
        {
            var s = new SubcategorySelection();
            s.Set("Squad", Ksp1Subcategory.Engine);
            Assert.IsNull(s.Effective("Squad", Model(Ksp1Subcategory.Engine)));
        }

        [Test] public void Selection_NullModName_IsSafe()
        {
            var s = new SubcategorySelection();
            s.Set(null, Ksp1Subcategory.Engine);
            Assert.IsNull(s.Get(null));
            Assert.IsNull(s.Effective(null, Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Pods)));
        }

        [Test] public void Keep_AllKeepsEverything_ElseOnlyTheType()
        {
            Assert.IsTrue(SubcategorySelection.Keep(Ksp1Subcategory.Engine, null));
            Assert.IsTrue(SubcategorySelection.Keep(Ksp1Subcategory.Engine, Ksp1Subcategory.Engine));
            Assert.IsFalse(SubcategorySelection.Keep(Ksp1Subcategory.Pods, Ksp1Subcategory.Engine));
        }

        [Test] public void Filter_KeepsOrderAndOnlyTheSelectedType()
        {
            var parts = new[] { "a", "b", "c", "d" };
            var subs = new Dictionary<string, Ksp1Subcategory>
            {
                ["a"] = Ksp1Subcategory.Engine, ["b"] = Ksp1Subcategory.Pods, ["c"] = Ksp1Subcategory.Engine, ["d"] = Ksp1Subcategory.Other,
            };
            CollectionAssert.AreEqual(new[] { "a", "c" }, SubcategorySelection.Filter(parts, p => subs[p], Ksp1Subcategory.Engine));
            CollectionAssert.AreEqual(parts, SubcategorySelection.Filter(parts, p => subs[p], null));
        }

        // ---------------------------------------------------------------- index

        private static Ksp1PartInfo Info(string name, string mod, string category) =>
            new Ksp1PartInfo(name, mod, null, category, null, false);

        [Test] public void Index_MapsPartNamesToTheirType()
        {
            var index = SubcategoryIndex.Build(new[]
            {
                Info("mk1pod", "Squad", "Pods"), Info("liquidEngine", "Squad", "Propulsion"),
                Info("hinge", "SquadExpansion", "Robotics"), Info("weird", "X", "CCK_Stuff"), null,
                Info(null, "X", "Pods"),
            });
            Assert.AreEqual(4, index.Count);
            Assert.AreEqual(Ksp1Subcategory.Pods, index.Get("mk1pod", null));
            Assert.AreEqual(Ksp1Subcategory.Engine, index.Get("liquidEngine", null));
            Assert.AreEqual(Ksp1Subcategory.Robotics, index.Get("hinge", "x Squad Utility KSP1"));
            Assert.AreEqual(Ksp1Subcategory.Other, index.Get("weird", null));
            CollectionAssert.AreEqual(new[] { Ksp1Subcategory.Pods, Ksp1Subcategory.Engine, Ksp1Subcategory.Robotics, Ksp1Subcategory.Other },
                index.TypesPresent);
        }

        [Test] public void Index_UnknownPart_FallsBackToTags()
        {
            var index = SubcategoryIndex.Build(new Ksp1PartInfo[0]);
            Assert.AreEqual(Ksp1Subcategory.FuelTank, index.Get("ghost", "tank Bluedog_DB FuelTank KSP1"));
            Assert.AreEqual(Ksp1Subcategory.Other, index.Get(null, null));
        }

        [Test] public void Index_DuplicateName_FirstWins()
        {
            var index = SubcategoryIndex.Build(new[] { Info("p", "A", "Pods"), Info("p", "B", "Engine") });
            Assert.AreEqual(1, index.Count);
            Assert.AreEqual(Ksp1Subcategory.Pods, index.Get("p", null));
        }

        // ---------------------------------------------------------------- log lines

        [Test] public void Log_TypesLine()
        {
            var m = Model(Ksp1Subcategory.Pods, Ksp1Subcategory.Engine, Ksp1Subcategory.Engine);
            Assert.AreEqual("[KSP1PartMod] ui: Squad: 3 visible part(s) in 2 type(s): Pods 1, Engine 2; bar shown",
                SubcategoryLog.TypesLine("Squad", m));
            Assert.AreEqual("[KSP1PartMod] ui: KRE: 2 visible part(s) in 1 type(s): Engine 2; bar hidden (fewer than 2 types)",
                SubcategoryLog.TypesLine("KRE", Model(Ksp1Subcategory.Engine, Ksp1Subcategory.Engine)));
        }

        [Test] public void Log_FilterLine()
        {
            var m = Model(Ksp1Subcategory.Pods, Ksp1Subcategory.Engine, Ksp1Subcategory.Engine);
            Assert.AreEqual("[KSP1PartMod] ui: Squad: filter Engine keeps 2 of 3 part(s)",
                SubcategoryLog.FilterLine("Squad", Ksp1Subcategory.Engine, m));
            Assert.AreEqual("[KSP1PartMod] ui: Squad: filter All keeps 3 of 3 part(s)",
                SubcategoryLog.FilterLine("Squad", null, m));
        }

        [Test] public void Log_IndexLine()
        {
            var index = SubcategoryIndex.Build(new[] { Info("a", "S", "Pods"), Info("b", "S", "Robotics") });
            Assert.AreEqual("[KSP1PartMod] ui: indexed 2 KSP1 part(s) in 2 type(s): Pods, Robotics",
                SubcategoryLog.IndexLine(index));
        }
    }
}
