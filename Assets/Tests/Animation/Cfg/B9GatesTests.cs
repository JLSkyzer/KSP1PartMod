using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    public class B9GatesTests
    {
        // ---- cfg builders ----

        private static IReadOnlyList<IKsp1Node> Mods(params string[] modules) =>
            CfgText.Parse(string.Concat(modules)).GetNodes("MODULE").ToList();

        private static string Module(string body) => "MODULE\n{\n" + body + "\n}\n";

        private static string DeployEngine(string clip) => Module("name = ModuleDeployableEngine\nEngineAnimationName = " + clip);

        private static string EnginesFx() => Module("name = ModuleEnginesFX\nengineID = basic");

        /// <summary>A ModuleB9PartSwitch. Without subtypes it gets one visual subtype <c>s</c>.</summary>
        private static string B9(string moduleId = null, string desc = null, string current = null, string defaultSubtype = null,
            params string[] subtypes)
        {
            var body = "name = ModuleB9PartSwitch\n";
            if (moduleId != null) body += "moduleID = " + moduleId + "\n";
            if (desc != null) body += "switcherDescription = " + desc + "\n";
            if (current != null) body += "currentSubtype = " + current + "\n";
            if (defaultSubtype != null) body += "defaultSubtype = " + defaultSubtype + "\n";
            body += subtypes.Length == 0 ? Subtype("s") : string.Concat(subtypes);
            return Module(body);
        }

        /// <summary>A subtype with a <c>transform</c> (visual data).</summary>
        private static string Subtype(string name, params string[] overrides) =>
            Sub("name = " + name + "\ntransform = " + name + "_t\n", overrides);

        /// <summary>A subtype without visual data.</summary>
        private static string Plain(string name, params string[] overrides) => Sub("name = " + name + "\n", overrides);

        private static string Sub(string head, params string[] overrides) => "SUBTYPE\n{\n" + head + string.Concat(overrides) + "}\n";

        private static string Override(string identifier, bool active = true, bool data = false) =>
            "MODULE\n{\n" + identifier + (active ? "" : "moduleActive = false\n") +
            (data ? "DATA\n{\nWaitForAnimation = 0.5\n}\n" : "") + "}\n";

        private static string Id(string name, string EngineAnimationName = null) =>
            "IDENTIFIER\n{\nname = " + name + "\n" + (EngineAnimationName != null ? "EngineAnimationName = " + EngineAnimationName + "\n" : "") + "}\n";

        private static IKsp1Node IdNode(string name, string EngineAnimationName = null) =>
            CfgText.Parse(Id(name, EngineAnimationName)).GetNodes("IDENTIFIER").Single();

        private static List<AnimEntryDef> EntriesOf(IReadOnlyList<IKsp1Node> mods) =>
            mods.Select((m, i) => EntryParser.Parse(m, i, i)).Where(e => e != null).ToList();

        private static IReadOnlyList<string> Apply(IReadOnlyList<IKsp1Node> mods, params int[] reduxOwned) =>
            B9GatePlanner.Apply(mods, EntriesOf(mods), new HashSet<int>(reduxOwned));

        private static List<AnimEntryDef> Entries(IReadOnlyList<IKsp1Node> mods, params int[] reduxOwned)
        {
            var entries = EntriesOf(mods);
            B9GatePlanner.Apply(mods, entries, new HashSet<int>(reduxOwned));
            return entries;
        }

        private static IReadOnlyList<IKsp1Node> TwoNozzles(string b9) => Mods(DeployEngine("NozzleA"), DeployEngine("NozzleB"), b9);

        // ---- set ids (Redux 26w42c, Ksp1B9PartSwitchVariantBuilder) ----

        // The second switch has no visual data: not a V-SwiFT set (null), and it does not advance k, so the third is
        // B9Switch1, not B9Switch2.
        [Test] public void B9_SetIds_FollowReduxRule() =>
            CollectionAssert.AreEqual(new[] { "engineSwitch", null, "B9Switch1" },
                B9GatePlanner.SetIds(Mods(B9(moduleId: "engineSwitch"), B9(desc: "Tank Type", subtypes: Plain("p")), B9())));

        [Test] public void B9_SetIds_SwitcherDescriptionWhenNoModuleId() =>
            CollectionAssert.AreEqual(new[] { "Tank Type" }, B9GatePlanner.SetIds(Mods(B9(desc: " Tank Type "))));

        [Test] public void B9_SetIds_DuplicateMadeUnique() =>
            Assert.AreEqual(2, B9GatePlanner.SetIds(Mods(B9(moduleId: "a"), B9(moduleId: "a"))).Distinct().Count());

        [Test] public void B9_SetIds_DuplicateIgnoringCase_GetsSuffix() =>
            CollectionAssert.AreEqual(new[] { "a", "A_2", "a_3" },
                B9GatePlanner.SetIds(Mods(B9(moduleId: "a"), B9(moduleId: "A"), B9(moduleId: "a"))));

        // A NODE without a position is visual data (the set is built, its id reserved) but builds no transformer: the
        // set is dropped and k does not advance.
        [Test] public void B9_SetIds_DroppedSetReservesIdButNotIndex() =>
            CollectionAssert.AreEqual(new[] { null, "a_2", "B9Switch1" },
                B9GatePlanner.SetIds(Mods(B9(moduleId: "a", subtypes: Sub("name = n\nNODE\n{\nname = top\n}\n")), B9(moduleId: "a"), B9())));

        [Test] public void B9_SetIds_NodeWithPosition_IsASet() =>
            CollectionAssert.AreEqual(new[] { "n" },
                B9GatePlanner.SetIds(Mods(B9(moduleId: "n", subtypes: Sub("name = x\nNODE\n{\nname = bottom\nposition = 0, -0.4, 0\n}\n")))));

        [Test] public void B9_SetIds_TextureNode_IsASet() =>
            CollectionAssert.AreEqual(new[] { "t", "u" }, B9GatePlanner.SetIds(Mods(
                B9(moduleId: "t", subtypes: Sub("name = x\nTEXTURE\n{\ntexture = a/b\n}\n")),
                B9(moduleId: "u", subtypes: Sub("name = x\nTEXTURE:emissive\n{\n_EmissiveMap = a/c\n}\n")))));

        private static string Jettison(string names, string ignoreNodes = null) =>
            Module("name = ModuleJettison\njettisonName = " + names + (ignoreNodes != null ? "\nignoreNodes = " + ignoreNodes : ""));

        // A switch whose only transforms are shroud names (Redux's ignored set, from ModuleJettison jettisonName,
        // ignoring case) is built (id reserved) then dropped: k does not advance.
        [Test] public void B9_SetIds_ShroudOnlyTransforms_DroppedButReserved() =>
            CollectionAssert.AreEqual(new[] { null, "s_2", "B9Switch1" }, B9GatePlanner.SetIds(Mods(
                Jettison("fairingA, Fairing_B"),
                B9(moduleId: "s", subtypes: new[] { Sub("name = a\ntransform = FAIRINGA\n"), Sub("name = b\ntransform = fairing_b\n") }),
                B9(moduleId: "s"), B9())));

        [Test] public void B9_SetIds_ShroudWithIgnoreNodes_NotIgnored() =>
            CollectionAssert.AreEqual(new[] { "s" }, B9GatePlanner.SetIds(Mods(
                Jettison("fairingA", ignoreNodes: "True"), B9(moduleId: "s", subtypes: new[] { Sub("name = a\ntransform = fairingA\n") }))));

        [Test] public void B9_SetIds_ShroudPlusOtherTransform_Kept() =>
            CollectionAssert.AreEqual(new[] { "s" }, B9GatePlanner.SetIds(Mods(
                Jettison("fairingA"), B9(moduleId: "s", subtypes: new[] { Sub("name = a\ntransform = fairingA\ntransform = bell\n") }))));

        [Test] public void B9_SetIds_NoSubtype_NotASet() =>
            CollectionAssert.AreEqual(new string[] { null }, B9GatePlanner.SetIds(Mods(Module("name = ModuleB9PartSwitch\nmoduleID = x"))));

        [Test] public void B9_VariantIds_NameElseTitleElseIndex_Unique() =>
            CollectionAssert.AreEqual(new[] { "a", "T", "Subtype2", "a_2", "A_3" }, B9GatePlanner.VariantIds(Mods(B9(subtypes: new[]
            {
                Subtype("a"), Sub("title = T\ntransform = x\n"), Sub("transform = y\n"), Subtype("a"), Subtype("A"),
            })))[0]);

        // ---- default subtype (Redux SelectDefaultSubtype) ----

        private static string DefaultOf(string b9) => B9GatePlanner.DefaultSubtype(Mods(b9)[0]);

        [Test] public void B9_DefaultSubtype_CurrentElseDefaultElseHighestPriority()
        {
            Assert.AreEqual("b", DefaultOf(B9(current: "b", subtypes: new[] { Subtype("a"), Subtype("b") })));
            Assert.AreEqual("b", DefaultOf(B9(current: "B", subtypes: new[] { Subtype("a"), Subtype("b") })), "matched ignoring case");
            Assert.AreEqual("a", DefaultOf(B9(subtypes: new[] { Subtype("a"), Subtype("b") })), "no priority: the first");
            Assert.AreEqual("b", DefaultOf(B9(defaultSubtype: "b", subtypes: new[] { Subtype("a"), Subtype("b") })));
            Assert.AreEqual("c", DefaultOf(B9(current: "zz", subtypes: new[]
            {
                Sub("name = a\ndefaultSubtypePriority = 1\ntransform = t\n"), Sub("name = b\ndefaultSubtypePriority = 2\ntransform = t\n"),
                Sub("name = c\ndefaultSubtypePriority = 5\ntransform = t\n"), Sub("name = d\ndefaultSubtypePriority = 5\ntransform = t\n"),
            })), "unknown current: highest priority, ties to the first");
        }

        // ---- IDENTIFIER matching ----

        [Test] public void B9_Identifier_MatchesByNameAndEveryOtherValue() =>
            CollectionAssert.AreEqual(new[] { 1 }, B9GatePlanner.Match(Mods(DeployEngine("NozzleA"), DeployEngine("NozzleB")),
                IdNode("ModuleDeployableEngine", EngineAnimationName: "NozzleB")));

        [Test] public void B9_Identifier_BareName_MatchesEveryModuleOfThatName() =>
            CollectionAssert.AreEqual(new[] { 0, 2 }, B9GatePlanner.Match(Mods(DeployEngine("a"), EnginesFx(), DeployEngine("b")),
                IdNode("ModuleDeployableEngine")));

        [Test] public void B9_Identifier_BareName_TwoModules_IgnoredWithNote() =>
            StringAssert.Contains("IDENTIFIER matches 2 module(s), ignored",
                Apply(Mods(DeployEngine("a"), DeployEngine("b"), B9(subtypes: Subtype("s", Override(Id("ModuleDeployableEngine"), active: false))))).Single());

        [Test] public void B9_Identifier_NoMatchOnOurModuleName_Noted() =>
            StringAssert.Contains("IDENTIFIER matches 0 module(s), ignored",
                Apply(TwoNozzles(B9(subtypes: Subtype("s", Override(Id("ModuleDeployableEngine", "NozzleC"), active: false))))).Single());

        // ---- gates ----

        [Test] public void B9_ModuleActiveFalse_AddsGate()
        {
            var e = Entries(TwoNozzles(B9(moduleId: "engineSwitch", subtypes: new[]
            {
                Subtype("RL10A", Override(Id("ModuleDeployableEngine", "NozzleB"), active: false)),
                Subtype("RL10B", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false)),
                Subtype("Fixed", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false), Override(Id("ModuleDeployableEngine", "NozzleB"), active: false)),
            })));
            var g = e[1].Gates.Single();
            Assert.AreEqual("engineSwitch", g.SetId);
            CollectionAssert.AreEqual(new[] { "RL10A", "Fixed" }, g.InactiveIn);
            CollectionAssert.AreEqual(new[] { "RL10A", "RL10B", "Fixed" }, g.SubtypeIds);
            Assert.AreEqual("RL10A", g.DefaultSubtype);
            CollectionAssert.AreEqual(new[] { "RL10B", "Fixed" }, e[0].Gates.Single().InactiveIn);
        }

        [Test] public void B9_TwoSwitches_TwoGates()
        {
            var e = Entries(TwoNozzles(B9(moduleId: "x", subtypes: Subtype("a", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false))))
                .Concat(Mods(B9(moduleId: "y", subtypes: Subtype("b", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false))))).ToList());
            CollectionAssert.AreEqual(new[] { "x", "y" }, e[0].Gates.Select(g => g.SetId));
            Assert.IsNull(e[1].Gates);
        }

        [Test] public void B9_NoVisualData_GateHasNoSetId_KeepsDefault()
        {
            var g = Entries(TwoNozzles(B9(moduleId: "x", current: "b", subtypes: new[]
            {
                Plain("a", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false)), Plain("b"),
            })))[0].Gates.Single();
            Assert.AreEqual(((string)null, "b"), (g.SetId, g.DefaultSubtype));
        }

        [Test] public void B9_Apply_Twice_SameGates()
        {
            var mods = TwoNozzles(B9(moduleId: "x", subtypes: new[]
            {
                Subtype("a", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false)), Subtype("b"),
            }));
            var entries = EntriesOf(mods);
            var first = B9GatePlanner.Apply(mods, entries, new HashSet<int>());
            var second = B9GatePlanner.Apply(mods, entries, new HashSet<int>());
            CollectionAssert.AreEqual(first, second);
            var g = entries[0].Gates.Single();
            Assert.AreEqual("x", g.SetId);
            CollectionAssert.AreEqual(new[] { "a" }, g.InactiveIn);
            Assert.IsNull(entries[1].Gates);
        }

        [Test] public void B9_ModuleActiveTrue_NoGate() =>
            Assert.IsNull(Entries(TwoNozzles(B9(subtypes: Subtype("s", Override(Id("ModuleDeployableEngine", "NozzleA"))))))[0].Gates);

        [Test] public void B9_DataOverride_LoggedIgnored() =>
            StringAssert.Contains("DATA ignored",
                Apply(TwoNozzles(B9(subtypes: Subtype("s", Override(Id("ModuleDeployableEngine", "NozzleA"), data: true))))).Single());

        [Test] public void B9_OverrideOnModuleNotOurs_Silent()
        {
            var mods = Mods(EnginesFx(), DeployEngine("a"),
                B9(subtypes: Subtype("s", Override(Id("ModuleEnginesFX"), active: false, data: true))));
            var entries = EntriesOf(mods);
            CollectionAssert.IsEmpty(B9GatePlanner.Apply(mods, entries, new HashSet<int>()));
            Assert.IsNull(entries.Single().Gates);
        }

        [Test] public void B9_ReduxOwnedModule_NotGated()
        {
            var mods = TwoNozzles(B9(subtypes: Subtype("s", Override(Id("ModuleDeployableEngine", "NozzleA"), active: false))));
            var entries = EntriesOf(mods);
            StringAssert.Contains("left to Redux, not gated", B9GatePlanner.Apply(mods, entries, new HashSet<int> { 0 }).Single());
            Assert.IsNull(entries[0].Gates);
        }

        [Test] public void B9_Apply_NullsAndEmpties_NoThrow()
        {
            CollectionAssert.IsEmpty(B9GatePlanner.Apply(null, null, null));
            CollectionAssert.IsEmpty(B9GatePlanner.Apply(Mods(B9()), new List<AnimEntryDef>(), null));
        }
    }
}
