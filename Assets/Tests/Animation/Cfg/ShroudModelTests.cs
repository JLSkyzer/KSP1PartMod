using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Injection;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>Engine shrouds (0.7.0, Task 1): the parser, the variant rules, the ownership rules and the planner wiring.
    /// The cfg texts are written here, in the shape of the stock RE-I2 engine; no game file is copied.</summary>
    public class ShroudModelTests
    {
        private static IReadOnlyList<IKsp1Node> Mods(params string[] modules) =>
            CfgText.Parse(string.Concat(modules)).GetNodes("MODULE").ToList();

        private static string Module(string body) => "MODULE\n{\n" + body + "\n}\n";

        private static string Jettison(string body) => Module("name = ModuleJettison\n" + body);

        private const string Re12Jettison =
            "jettisonName = \tShroud1p5x3,Shroud2x3\nbottomNodeName = bottom\nisFairing = True\njettisonedObjectMass = 0.1\njettisonForce = 5\njettisonDirection = 0 0 1\n";

        private static string Variant(string name, params string[] gameObjects) =>
            "VARIANT\n{\nname = " + name + "\nGAMEOBJECTS\n{\n" + string.Concat(gameObjects.Select(g => g + "\n")) + "}\n}\n";

        private static string StockVariants(string head, params string[] variants) =>
            Module("name = ModulePartVariants\n" + head + string.Concat(variants));

        /// <summary>The variant module of the stock RE-I2: <c>Shroud</c> (the big shroud) and <c>Bare</c>.</summary>
        private static string Re12Variants() => StockVariants("baseVariant = Shroud\n",
            Variant("Shroud", "125 = false", "250 = true", "Shroud1p5x3 = false", "Shroud2x3 = true"),
            Variant("Bare", "125 = true", "250 = false", "Shroud1p5x3 = true", "Shroud2x3 = false"));

        private static string B9(string head, params string[] subtypes) =>
            Module("name = ModuleB9PartSwitch\n" + head + string.Concat(subtypes));

        private static string Sub(string name, params string[] transforms) =>
            "SUBTYPE\n{\nname = " + name + "\n" + string.Concat(transforms.Select(t => "transform = " + t + "\n")) + "}\n";

        // ---- ShroudParser ----

        [Test] public void Parser_Re12Module_ReadsEveryField()
        {
            var e = ShroudParser.Parse(Mods(Jettison(Re12Jettison))[0], 3, 0);
            Assert.AreEqual(EntryKind.Shroud, e.Kind);
            Assert.AreEqual(3, e.ModuleIndex);
            CollectionAssert.AreEqual(new[] { "Shroud1p5x3", "Shroud2x3" }, e.TransformNames);
            Assert.AreEqual("bottom", e.NodeName);
            Assert.IsTrue(e.IsFairing);
            Assert.IsFalse(e.CheckBottomNode);
            Assert.IsTrue(e.DecoupleEnabled);
            Assert.IsFalse(e.ManualJettison);
            Assert.IsFalse(e.IgnoreNodes);
            Assert.IsTrue(e.AllowShroudToggle);
            Assert.AreEqual("shroud:Shroud1p5x3:0", e.Key);
        }

        [Test] public void Parser_NotFairing_WithCheckBottomNode()
        {
            var e = ShroudParser.Parse(Mods(Jettison("jettisonName = Fairing\nisFairing = False\ncheckBottomNode = True\nmanualJettison = true\n" +
                                                     "decoupleEnabled = false\nallowShroudToggle = false\nbottomNodeName = bottom2\n"))[0], 0, 0);
            Assert.IsFalse(e.IsFairing);
            Assert.IsTrue(e.CheckBottomNode);
            Assert.IsTrue(e.ManualJettison);
            Assert.IsFalse(e.DecoupleEnabled);
            Assert.IsFalse(e.AllowShroudToggle);
            Assert.AreEqual("bottom2", e.NodeName);
        }

        [Test] public void Parser_IgnoreNodes_IsAnEntryButNotConvertible()
        {
            var m = Mods(Jettison("jettisonName = Fairing\nignoreNodes = true\n"))[0];
            var e = ShroudParser.Parse(m, 0, 0);
            Assert.IsNotNull(e);
            Assert.IsTrue(e.IgnoreNodes);
            Assert.IsFalse(ShroudParser.Convertible(m));
        }

        [Test] public void Convertible_FollowsRedux()
        {
            Assert.IsTrue(ShroudParser.Convertible(Mods(Jettison(Re12Jettison))[0]));
            Assert.IsTrue(ShroudParser.Convertible(Mods(Jettison("jettisonName = A\nignoreNodes = false\n"))[0]));
            Assert.IsTrue(ShroudParser.Convertible(Mods(Jettison("jettisonName = A\nbottomNodeName =  \n"))[0]), "a blank node name falls back to bottom");
            Assert.IsFalse(ShroudParser.Convertible(Mods(Jettison("isFairing = True\n"))[0]), "no names");
            Assert.IsFalse(ShroudParser.Convertible(Mods(Module("name = ModuleEnginesFX\njettisonName = A"))[0]), "not a ModuleJettison");
            Assert.IsFalse(ShroudParser.Convertible(null));
        }

        [Test] public void Parser_BlankNodeName_FallsBackToBottom() =>
            Assert.AreEqual("bottom", ShroudParser.Parse(Mods(Jettison("jettisonName = A\nbottomNodeName =  \n"))[0], 0, 0).NodeName);

        [Test] public void Parser_NamesSplitTrimmedEmptiesDropped()
        {
            var e = ShroudParser.Parse(Mods(Jettison("jettisonName = A , ,B,, C \n"))[0], 0, 0);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, e.TransformNames);
        }

        [Test] public void Parser_MissingOrBlankJettisonName_NoEntry()
        {
            Assert.IsNull(ShroudParser.Parse(Mods(Jettison("isFairing = True\n"))[0], 0, 0));
            Assert.IsNull(ShroudParser.Parse(Mods(Jettison("jettisonName =  , \n"))[0], 0, 0));
            Assert.IsNull(ShroudParser.Parse(null, 0, 0));
        }

        [Test] public void Parser_ModuleNameCaseInsensitive_OtherModulesIgnored()
        {
            Assert.IsNotNull(ShroudParser.Parse(Mods(Module("name = modulejettison\njettisonName = A"))[0], 0, 0));
            Assert.IsNull(ShroudParser.Parse(Mods(Module("name = ModuleAnimateGeneric\njettisonName = A"))[0], 0, 0));
        }

        [Test] public void Planner_TwoJettisonModules_TwoEntriesWithDistinctKeys()
        {
            var mods = Mods(Jettison("jettisonName = A\n"), Jettison("jettisonName = A\nisFairing = False\n"));
            var plan = OwnershipPlanner.Plan(mods, false, new HashSet<string>(), false, false, null, true, true, shrouds: true);
            var shrouds = plan.Entries.Where(e => e.Kind == EntryKind.Shroud).ToList();
            Assert.AreEqual(2, shrouds.Count);
            CollectionAssert.AreEqual(new[] { "shroud:A:0", "shroud:A:1" }, shrouds.Select(e => e.Key));
            CollectionAssert.AreEqual(new[] { 0, 1 }, shrouds.Select(e => e.ModuleIndex));
        }

        // ---- VariantSwitches ----

        [Test] public void Stock_NoModuleId_PartVariant0()
        {
            var sw = VariantSwitches.All(Mods(Re12Variants(), Jettison(Re12Jettison))).Single();
            Assert.IsTrue(sw.IsStock);
            Assert.AreEqual("PartVariant0", sw.SetId);
            CollectionAssert.AreEqual(new[] { "Shroud", "Bare" }, sw.VariantIds);
            Assert.AreEqual("Shroud", sw.DefaultSubtype);
        }

        [Test] public void Stock_DefaultFromBaseVariant()
        {
            var bare = StockVariants("baseVariant = Bare\n", Variant("Shroud", "a = true"), Variant("Bare", "b = true"));
            Assert.AreEqual("Bare", VariantSwitches.All(Mods(bare)).Single().DefaultSubtype);
            var current = StockVariants("currentVariant = bare\n", Variant("Shroud", "a = true"), Variant("Bare", "b = true"));
            Assert.AreEqual("Bare", VariantSwitches.All(Mods(current)).Single().DefaultSubtype);
            var none = StockVariants("", Variant("Shroud", "a = true"), Variant("Bare", "b = true"));
            Assert.AreEqual("Shroud", VariantSwitches.All(Mods(none)).Single().DefaultSubtype);
            var unknown = StockVariants("baseVariant = Nope\n", Variant("Shroud", "a = true"), Variant("Bare", "b = true"));
            Assert.AreEqual("Shroud", VariantSwitches.All(Mods(unknown)).Single().DefaultSubtype);
        }

        [Test] public void Stock_SameDisplayName_MadeUnique()
        {
            var a = StockVariants("displayName = X\n", Variant("v", "t = true"));
            var b = StockVariants("displayName = X\n", Variant("v", "u = true"));
            CollectionAssert.AreEqual(new[] { "X", "X_2" }, VariantSwitches.All(Mods(a, b)).Select(s => s.SetId));
        }

        [Test] public void Stock_IdRule_ModuleIdThenDisplayNameThenThemeName()
        {
            var sets = VariantSwitches.All(Mods(
                StockVariants("moduleID = mid\ndisplayName = d\nthemeName = t\n", Variant("v", "a = true")),
                StockVariants("displayName = d2\nthemeName = t\n", Variant("v", "a = true")),
                StockVariants("themeName = t3\n", Variant("v", "a = true")),
                StockVariants("", Variant("v", "a = true"))));
            CollectionAssert.AreEqual(new[] { "mid", "d2", "t3", "PartVariant3" }, sets.Select(s => s.SetId));
        }

        [Test] public void Stock_VariantIds_NameThenDisplayNameThenThemeNameThenIndex_Unique()
        {
            var m = StockVariants("",
                "VARIANT\n{\nname = A\n}\nVARIANT\n{\ndisplayName = B\n}\nVARIANT\n{\nthemeName = C\n}\nVARIANT\n{\n}\n" +
                "VARIANT\n{\nname = a\nGAMEOBJECTS\n{\nz = true\n}\n}\n");
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "Variant3", "a_2" }, VariantSwitches.All(Mods(m)).Single().VariantIds);
        }

        [Test] public void B9_ReservesAnIdEvenWhenDropped()
        {
            // A B9 set whose only transforms are shroud names: it has visual data, so its id is reserved, but Redux builds
            // no transformer for it and drops the set (SetId null). A stock set of the same id is then made unique.
            var b9 = B9("moduleID = Sw\n", Sub("one", "Shroud2x3"), Sub("two", "Shroud1p5x3"));
            var stock = StockVariants("moduleID = Sw\n", Variant("v", "a = true"));
            var all = VariantSwitches.All(Mods(b9, stock, Jettison(Re12Jettison)));
            Assert.IsFalse(all[0].IsStock);
            Assert.IsNull(all[0].SetId);
            Assert.AreEqual("Sw_2", all[1].SetId);
        }

        [Test] public void B9_SetsComeBeforeStockSets_WhateverTheModuleOrder()
        {
            var b9 = B9("moduleID = Sw\n", Sub("one", "T1"), Sub("two", "T2"));
            var stock = StockVariants("moduleID = Sw\n", Variant("v", "a = true"));
            var all = VariantSwitches.All(Mods(stock, b9));
            Assert.IsFalse(all[0].IsStock);
            Assert.AreEqual("Sw", all[0].SetId);
            Assert.AreEqual("Sw_2", all[1].SetId);
        }

        [Test] public void Stock_OnlyAShroudEffect_IsDropped_SetIdNull_ReservesItsId()
        {
            var dropped = StockVariants("", Variant("Shroud", "Shroud2x3 = true", "Shroud1p5x3 = false"),
                Variant("Bare", "Shroud2x3 = false", "Shroud1p5x3 = true"));
            var kept = StockVariants("", Variant("v", "real = true"));
            var next = StockVariants("", Variant("v", "real = true"));
            var all = VariantSwitches.All(Mods(dropped, kept, next, Jettison(Re12Jettison)));
            Assert.IsNull(all[0].SetId);
            // The dropped set reserved "PartVariant0" and did not advance the counter: the next set asks for the same id
            // and gets the suffix; the counter then moves on.
            Assert.AreEqual("PartVariant0_2", all[1].SetId);
            Assert.AreEqual("PartVariant1", all[2].SetId);
        }

        [Test] public void Stock_TextureOnlyVariant_IsKept()
        {
            var m = StockVariants("", "VARIANT\n{\nname = A\nTEXTURE\n{\nmainTextureURL = Some/Tex\n}\n}\n");
            Assert.AreEqual("PartVariant0", VariantSwitches.All(Mods(m)).Single().SetId);
        }

        [Test] public void Stock_FalseGameObjectsAndUnrelatedModules_AreNotSets()
        {
            var m = StockVariants("", Variant("v", "real = false"));
            Assert.IsNull(VariantSwitches.All(Mods(m)).Single().SetId);
            Assert.AreEqual(0, VariantSwitches.All(Mods(Jettison(Re12Jettison))).Count);
            Assert.AreEqual(0, VariantSwitches.All(null).Count);
        }

        [Test] public void IgnoreNodesShroud_IsNotInTheIgnoredSet_SoItsGameObjectCounts()
        {
            // Redux ignores only convertible shrouds: with ignoreNodes = true the name is an ordinary transform.
            var set = StockVariants("", Variant("v", "Fairing = true"));
            var jet = Jettison("jettisonName = Fairing\nignoreNodes = true\n");
            Assert.AreEqual("PartVariant0", VariantSwitches.All(Mods(set, jet)).Single().SetId);
        }

        [Test] public void B9GatePlanner_StillSeesOnlyItsOwnSwitches()
        {
            var b9 = B9("moduleID = Sw\n", Sub("one", "T1"), Sub("two", "T2"));
            var mods = Mods(Re12Variants(), b9, Jettison(Re12Jettison));
            CollectionAssert.AreEqual(new[] { "Sw" }, B9GatePlanner.SetIds(mods));
            Assert.AreEqual(1, B9GatePlanner.VariantIds(mods).Count);
        }

        // ---- ShroudVariantPlanner ----

        [Test] public void Gates_Re12_PerNameInactiveVariants()
        {
            var gates = ShroudVariantPlanner.Gates(Mods(Re12Variants(), Jettison(Re12Jettison)), new[] { "Shroud1p5x3", "Shroud2x3" });
            Assert.AreEqual(2, gates.Length);
            var small = gates[0].Single();
            CollectionAssert.AreEqual(new[] { "Shroud" }, small.InactiveIn);
            Assert.AreEqual("PartVariant0", small.SetId);
            Assert.AreEqual("Shroud", small.DefaultSubtype);
            CollectionAssert.AreEqual(new[] { "Shroud", "Bare" }, small.SubtypeIds);
            var big = gates[1].Single();
            CollectionAssert.AreEqual(new[] { "Bare" }, big.InactiveIn);
            Assert.AreEqual("PartVariant0", big.SetId);
        }

        [Test] public void Gates_NameNoSetMentions_GetsNoGate()
        {
            var gates = ShroudVariantPlanner.Gates(Mods(Re12Variants(), Jettison(Re12Jettison)), new[] { "Other", "Shroud2x3" });
            Assert.AreEqual(0, gates[0].Length);
            Assert.AreEqual(1, gates[1].Length);
            Assert.AreEqual(0, ShroudVariantPlanner.Gates(Mods(Jettison(Re12Jettison)), new[] { "Shroud2x3" })[0].Length);
            Assert.AreEqual(0, ShroudVariantPlanner.Gates(null, new[] { "a" })[0].Length);
            Assert.AreEqual(0, ShroudVariantPlanner.Gates(Mods(), null).Length);
        }

        [Test] public void Gates_B9SubtypeTransform_EnablesTheNameInThatSubtype()
        {
            var b9 = B9("moduleID = Cap\n", Sub("Open", "Other"), Sub("Closed", "Fairing", "Other"));
            var gate = ShroudVariantPlanner.Gates(Mods(b9, Jettison("jettisonName = Fairing\n")), new[] { "Fairing" })[0].Single();
            Assert.AreEqual("Cap", gate.SetId);
            CollectionAssert.AreEqual(new[] { "Open" }, gate.InactiveIn);
            Assert.AreEqual("Open", gate.DefaultSubtype);
        }

        [Test] public void Gates_B9SetDroppedByRedux_KeepsAGateWithNullSetId()
        {
            var b9 = B9("moduleID = Cap\n", Sub("Open", "Fairing"), Sub("Closed", "Fairing2"));
            var gate = ShroudVariantPlanner.Gates(Mods(b9, Jettison("jettisonName = Fairing,Fairing2\n")), new[] { "Fairing" })[0].Single();
            Assert.IsNull(gate.SetId);
            Assert.AreEqual("Open", gate.DefaultSubtype);
            CollectionAssert.AreEqual(new[] { "Closed" }, gate.InactiveIn);
        }

        [Test] public void Gates_NamesCompareIgnoringCase_FalseValuesDisable()
        {
            var m = StockVariants("", Variant("On", "shroud2X3 = TRUE"), Variant("Off", "SHROUD2x3 = false"), Variant("Silent", "x = true"));
            var gate = ShroudVariantPlanner.Gates(Mods(m, Jettison(Re12Jettison)), new[] { "Shroud2x3" })[0].Single();
            CollectionAssert.AreEqual(new[] { "Off", "Silent" }, gate.InactiveIn);
        }

        // ---- ShroudOwnership ----

        private static AnimEntryDef Shroud(int module, params string[] names) =>
            new AnimEntryDef { Kind = EntryKind.Shroud, ModuleIndex = module, TransformNames = names, Key = "shroud:" + names[0] + ":0" };

        private static AnimEntryDef Hider(int module, params string[] names) =>
            new AnimEntryDef { Kind = EntryKind.MeshHider, ModuleIndex = module, TransformNames = names, Key = "meshhider::0" };

        [Test] public void TrimMeshHiders_DropsShroudNames_OneNotePerName()
        {
            var entries = new List<AnimEntryDef> { Shroud(1, "Shroud2x3", "Shroud1p5x3"), Hider(2, "Wheel", "shroud2x3") };
            var notes = ShroudOwnership.TrimMeshHiders(entries);
            CollectionAssert.AreEqual(new[] { "Wheel" }, entries[1].TransformNames);
            Assert.AreEqual(1, notes.Count);
            StringAssert.Contains("shroud2x3", notes[0]);
            StringAssert.Contains("MODULE[2]", notes[0]);
        }

        [Test] public void TrimMeshHiders_EmptyHiderIsRemoved()
        {
            var entries = new List<AnimEntryDef> { Shroud(1, "A", "B"), Hider(2, "A", "B") };
            var notes = ShroudOwnership.TrimMeshHiders(entries);
            Assert.AreEqual(2, notes.Count);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(EntryKind.Shroud, entries[0].Kind);
        }

        [Test] public void TrimMeshHiders_NoShroud_OrOtherNames_ChangesNothing()
        {
            var entries = new List<AnimEntryDef> { Hider(2, "A") };
            Assert.AreEqual(0, ShroudOwnership.TrimMeshHiders(entries).Count);
            Assert.AreEqual(1, entries.Count);
            entries.Add(Shroud(3, "B"));
            Assert.AreEqual(0, ShroudOwnership.TrimMeshHiders(entries).Count);
            CollectionAssert.AreEqual(new[] { "A" }, entries[0].TransformNames);
            Assert.AreEqual(0, ShroudOwnership.TrimMeshHiders(null).Count);
        }

        // ---- ShroudImportRule ----

        [Test] public void RootsToHide_OutermostMatchesOnly()
        {
            var paths = new[] { "model", "model/Shroud2x3", "model/Shroud2x3/Shroud1p5x3", "model/body", "model/body/Shroud1p5x3" };
            CollectionAssert.AreEqual(new[] { "model/Shroud2x3", "model/body/Shroud1p5x3" },
                ShroudImportRule.RootsToHide(new[] { "Shroud1p5x3", "Shroud2x3" }, paths));
        }

        [Test] public void RootsToHide_CaseInsensitive_NoNames_NoPaths_WholeSegmentsOnly()
        {
            CollectionAssert.AreEqual(new[] { "a/SHROUD" }, ShroudImportRule.RootsToHide(new[] { "shroud" }, new[] { "a", "a/SHROUD" }));
            Assert.AreEqual(0, ShroudImportRule.RootsToHide(new string[0], new[] { "a/b" }).Count);
            Assert.AreEqual(0, ShroudImportRule.RootsToHide(new[] { "b" }, null).Count);
            Assert.AreEqual(0, ShroudImportRule.RootsToHide(new[] { "b" }, new[] { "a/bb", "b/c" }).Count);
        }

        [Test] public void RootsToHide_ADuplicatePath_IsReturnedOnce() =>
            CollectionAssert.AreEqual(new[] { "m/A" }, ShroudImportRule.RootsToHide(new[] { "A" }, new[] { "m/A", "m/A" }));

        // ---- planner wiring ----

        private static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> mods, bool shrouds = true, bool b9 = true) =>
            OwnershipPlanner.Plan(mods, false, new HashSet<string>(), false, false, null, true, b9, shrouds: shrouds);

        [Test] public void Planner_Re12_ShroudEntryWithNameGates()
        {
            var plan = Plan(Mods(Re12Variants(), Jettison(Re12Jettison)));
            var e = plan.Entries.Single(x => x.Kind == EntryKind.Shroud);
            Assert.AreEqual(1, e.ModuleIndex);
            Assert.AreEqual(2, e.NameGates.Length);
            CollectionAssert.AreEqual(new[] { "Shroud" }, e.NameGates[0].Single().InactiveIn);
            CollectionAssert.AreEqual(new[] { "Bare" }, e.NameGates[1].Single().InactiveIn);
        }

        [Test] public void Planner_ShroudsOff_NoShroudEntry()
        {
            var plan = Plan(Mods(Re12Variants(), Jettison(Re12Jettison)), shrouds: false);
            Assert.IsFalse(plan.Entries.Any(x => x.Kind == EntryKind.Shroud));
        }

        [Test] public void Planner_ShroudNamesLeaveTheMeshHider()
        {
            var hider = Module("name = ModuleBDBDeployableMeshHider\ntransformName = Shroud2x3\ntransformName = Wheel\n");
            var plan = Plan(Mods(Jettison(Re12Jettison), hider));
            CollectionAssert.AreEqual(new[] { "Wheel" }, plan.Entries.Single(x => x.Kind == EntryKind.MeshHider).TransformNames);
            Assert.IsTrue(plan.Notes.Any(n => n.Contains("Shroud2x3")));
        }

        [Test] public void Planner_B9ModuleActiveFalse_GatesTheShroudEntry()
        {
            var b9 = B9("moduleID = Cap\n",
                "SUBTYPE\n{\nname = Open\ntransform = T1\n}\n" +
                "SUBTYPE\n{\nname = Closed\ntransform = T2\nMODULE\n{\nIDENTIFIER\n{\nname = ModuleJettison\n}\nmoduleActive = false\n}\n}\n");
            var plan = Plan(Mods(b9, Jettison("jettisonName = Fairing\n")));
            var e = plan.Entries.Single(x => x.Kind == EntryKind.Shroud);
            Assert.AreEqual(1, e.Gates.Length);
            CollectionAssert.AreEqual(new[] { "Closed" }, e.Gates[0].InactiveIn);
        }

        [Test] public void Planner_SkipModules_DropsTheShroudEntry()
        {
            var plan = OwnershipPlanner.Plan(Mods(Jettison(Re12Jettison)), false, new HashSet<string>(), false, false,
                new HashSet<string> { "ModuleJettison" }, true, true, shrouds: true);
            Assert.IsFalse(plan.Entries.Any(x => x.Kind == EntryKind.Shroud));
        }

        [Test] public void Planner_ShroudKeyDoesNotMoveOtherKeys()
        {
            var anim = Module("name = ModuleAnimateGeneric\nanimationName = door\n");
            var without = Plan(Mods(anim, Jettison(Re12Jettison)), shrouds: false).Entries.Select(e => e.Key).ToList();
            var with = Plan(Mods(anim, Jettison(Re12Jettison))).Entries.Where(e => e.Kind != EntryKind.Shroud).Select(e => e.Key).ToList();
            CollectionAssert.AreEqual(without, with);
        }

        [Test] public void SummaryLine_CountsShroudEntries()
        {
            var plan = Plan(Mods(Jettison(Re12Jettison)));
            StringAssert.Contains("Shroud 1", ModuleInjection.SummaryLine("Squad", new[] { plan }));
        }

        [Test] public void EntryKind_Shroud_IsAppendedLast() =>
            Assert.AreEqual(EntryKind.Shroud, System.Enum.GetValues(typeof(EntryKind)).Cast<EntryKind>().Max());

        [Test] public void NewFields_HaveKsp1Defaults()
        {
            var e = new AnimEntryDef();
            Assert.AreEqual("bottom", e.NodeName);
            Assert.IsTrue(e.IsFairing && e.DecoupleEnabled && e.AllowShroudToggle);
            Assert.IsFalse(e.CheckBottomNode || e.ManualJettison || e.IgnoreNodes);
            Assert.IsNull(e.NameGates);
        }
    }
}
