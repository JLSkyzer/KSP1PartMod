using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>Structural nodes (0.17.0): the parser, the visibility rule, the mesh toggle, the planner wiring and a census of the
    /// installed cfg files. The cfg texts are written here, in the shape of the stock fairing and the BDB truss bases.</summary>
    public class StructureTests
    {
        private static IReadOnlyList<IKsp1Node> Mods(params string[] modules) =>
            CfgText.Parse(string.Concat(modules)).GetNodes("MODULE").ToList();

        private static string Module(string body) => "MODULE\n{\n" + body + "\n}\n";

        private static string Node(string root, string nodes) =>
            Module("name = ModuleStructuralNode\nrootObject = " + root + "\nattachNodeNames = " + nodes);

        private static string Toggle(string meshName = "Truss Structure") =>
            Module("name = ModuleStructuralNodeToggle\nMeshMenuName = " + meshName + "\nNodeMenuName = Interstage Nodes\nshowNodes = false");

        private static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> mods, bool structures = true) =>
            OwnershipPlanner.Plan(mods, false, new HashSet<string>(), false, false, null, true, true, structures: structures);

        // ---- parser ----

        [Test] public void Parser_ReadsRootAndNodes()
        {
            var e = StructureParser.Parse(Mods(Node("Cap1", "interstage01a, interstage01b"))[0], 4, 0);
            Assert.AreEqual(EntryKind.Structure, e.Kind);
            Assert.AreEqual(4, e.ModuleIndex);
            CollectionAssert.AreEqual(new[] { "Cap1" }, e.TransformNames);
            CollectionAssert.AreEqual(new[] { "interstage01a", "interstage01b" }, e.TargetNames);
            Assert.AreEqual("structure:Cap1:0", e.Key);
            Assert.IsFalse(e.MeshToggle);
        }

        [Test] public void Parser_Defaults_AreKsp1s()
        {
            var e = StructureParser.Parse(Mods(Module("name = ModuleStructuralNode"))[0], 0, 0);
            CollectionAssert.AreEqual(new[] { "Fairing" }, e.TransformNames);
            CollectionAssert.AreEqual(new[] { "bottom" }, e.TargetNames);
        }

        [Test] public void Parser_AnotherModule_IsNull() =>
            Assert.IsNull(StructureParser.Parse(Mods(Module("name = ModuleJettison\njettisonName = X"))[0], 0, 0));

        [Test] public void MeshToggleName_NeedsBothModules()
        {
            Assert.AreEqual("Truss Structure", StructureParser.MeshToggleName(Mods(Node("Cap1", "a"), Toggle())));
            Assert.AreEqual(StructureParser.DefaultMenuName, StructureParser.MeshToggleName(Mods(Node("Cap1", "a"), Toggle(""))));
            Assert.IsNull(StructureParser.MeshToggleName(Mods(Node("Cap1", "a"))), "no toggle module");
            Assert.IsNull(StructureParser.MeshToggleName(Mods(Toggle())), "nothing to hide");
        }

        // ---- rule and machine ----

        [TestCase(true, false, true, ExpectedResult = true)]
        [TestCase(false, false, true, ExpectedResult = false)]
        [TestCase(true, true, true, ExpectedResult = false)]
        [TestCase(true, false, false, ExpectedResult = false)]
        public bool Rule_ShownOnlyWhenAttachedVisibleAndActive(bool attached, bool hidden, bool active) =>
            StructureRule.Visible(attached, hidden, active);

        [Test] public void Machine_StepReportsChangesOnly()
        {
            var m = new StructureMachine(StructureParser.Parse(Mods(Node("Cap1", "a"))[0], 0, 0));
            Assert.IsFalse(m.Step(false, true));
            Assert.AreEqual(StructureChange.None, m.LastChange, "the first step is no change");
            Assert.IsTrue(m.Step(true, true));
            Assert.AreEqual(StructureChange.Shown, m.LastChange);
            Assert.IsTrue(m.Step(true, true));
            Assert.AreEqual(StructureChange.None, m.LastChange);
            Assert.IsFalse(m.Step(false, true));
            Assert.AreEqual(StructureChange.Hidden, m.LastChange);
        }

        [Test] public void Machine_MeshToggle_HidesAndSaves()
        {
            var def = StructureParser.Parse(Mods(Node("Cap1", "a"))[0], 0, 0);
            var m = new StructureMachine(def);
            Assert.IsTrue(m.SetMeshHidden(true));
            Assert.IsFalse(m.SetMeshHidden(true), "no change the second time");
            Assert.IsFalse(m.Step(true, true), "hidden by the player even when attached");
            var saved = new Dictionary<string, float>();
            m.Save(saved);
            Assert.IsFalse(m.Changed);
            var again = new StructureMachine(def);
            again.Load(saved);
            Assert.IsTrue(again.MeshHidden);
            Assert.IsFalse(again.Changed);
        }

        // ---- planner ----

        [Test] public void Planner_OneEntryPerModule_ToggleOnTheFirst()
        {
            var plan = Plan(Mods(Node("Cap1", "a,b"), Node("Cap2", "c,d"), Node("Truss1", "a,b,c,d"), Toggle()));
            var entries = plan.Entries.Where(e => e.Kind == EntryKind.Structure).ToList();
            Assert.AreEqual(3, entries.Count);
            Assert.IsTrue(entries[0].MeshToggle);
            Assert.AreEqual("Truss Structure", entries[0].StartLabel);
            Assert.IsFalse(entries[1].MeshToggle);
            Assert.IsFalse(entries[2].MeshToggle);
        }

        [Test] public void Planner_NoToggleModule_NoButton()
        {
            var plan = Plan(Mods(Node("Cap1", "a")));
            Assert.IsFalse(plan.Entries.Single(e => e.Kind == EntryKind.Structure).MeshToggle);
        }

        [Test] public void Planner_SwitchOff_NoEntry() =>
            Assert.IsEmpty(Plan(Mods(Node("Cap1", "a"), Toggle()), structures: false).Entries.Where(e => e.Kind == EntryKind.Structure));

        [Test] public void Planner_SameRootTwice_GetsOrdinals()
        {
            var keys = Plan(Mods(Node("Cap1", "a"), Node("Cap1", "b"))).Entries.Select(e => e.Key).ToArray();
            CollectionAssert.AreEqual(new[] { "structure:Cap1:0", "structure:Cap1:1" }, keys);
        }

        [Test] public void Planner_SkipModules_DropsTheEntry()
        {
            var plan = OwnershipPlanner.Plan(Mods(Node("Cap1", "a")), false, new HashSet<string>(), false, false,
                new HashSet<string> { "ModuleStructuralNode" }, true, true, structures: true);
            Assert.IsEmpty(plan.Entries.Where(e => e.Kind == EntryKind.Structure));
        }

        // ---- labels and kinds ----

        [Test] public void Labels_NameTheNextAction()
        {
            Assert.AreEqual("Hide Truss Structure", StructureLines.Button("Truss Structure", false));
            Assert.AreEqual("Show Truss Structure", StructureLines.Button("Truss Structure", true));
            Assert.AreEqual("Hide structure", StructureLines.Button(null, false));
        }

        [Test] public void Controls_EditorOnly_AndOnlyOnTheToggleEntry()
        {
            Assert.IsTrue(ControlLabels.HasButton(EntryKind.Structure));
            Assert.IsFalse(ControlLabels.HasAction(EntryKind.Structure), "KSP1's toggles are editor-only fields");
            var withToggle = new AnimEntryDef { Kind = EntryKind.Structure, MeshToggle = true };
            var without = new AnimEntryDef { Kind = EntryKind.Structure };
            Assert.IsTrue(ControlLabels.Allowed(withToggle, true));
            Assert.IsFalse(ControlLabels.Allowed(withToggle, false), "not in flight");
            Assert.IsFalse(ControlLabels.Allowed(without, true));
        }

        [Test] public void Kind_HasBinder_AndIsLast()
        {
            Assert.IsTrue(RuntimeKinds.HasBinder(EntryKind.Structure));
            Assert.AreEqual(EntryKind.Structure, System.Enum.GetValues(typeof(EntryKind)).Cast<EntryKind>().Max(), "appended last: no older value moves");
        }

        // ---- census of the installed game ----

        [Test] public void Census_InstalledCfg_AllStructuralNodesParse()
        {
            var files = InstalledKsp1.AllCfg().Where(f => File.ReadAllText(f).Contains("ModuleStructuralNode")).ToArray();
            if (files.Length == 0) Assert.Ignore("no KSP1 cfg files installed");
            var entries = 0;
            var withToggle = 0;
            foreach (var f in files)
            {
                var text = File.ReadAllText(f);
                var root = CfgText.Parse(text);
                foreach (var part in root.GetNodes("PART"))
                {
                    var mods = part.GetNodes("MODULE").ToList();
                    var plan = Plan(mods);
                    var structure = plan.Entries.Where(e => e.Kind == EntryKind.Structure).ToList();
                    entries += structure.Count;
                    if (structure.Any(e => e.MeshToggle)) withToggle++;
                    foreach (var e in structure)
                    {
                        Assert.IsNotEmpty(e.TransformNames[0], f);
                        Assert.IsNotEmpty(e.TargetNames, f);
                    }
                }
            }
            Assert.Greater(entries, 300, "about 470 module entries in the installed set (2026-11-20)");
            Assert.Greater(withToggle, 20, "BDB truss bases carry the mesh toggle");
            TestContext.WriteLine($"structure census: {files.Length} cfg file(s), {entries} entry(ies), {withToggle} part(s) with the mesh toggle");
        }
    }
}
