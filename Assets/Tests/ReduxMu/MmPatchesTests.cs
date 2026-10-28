using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP1PartMod.Game;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>The ModuleManager glue against the referenced Ksp2Redux.dll (26w42c): every target resolves by name with the
    /// expected parameter types, and <c>Ksp1ConfigFinalizer.Finalize</c>, run in the editor on cfgs built in memory, gives
    /// MM's arithmetic, indices, safety net and BEFORE/AFTER rule with our patches, and Redux's exact result for plain
    /// lines.</summary>
    public class MmPatchesTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            _lines.Clear();
            MmPatches.Reset();
            MmPatches.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            new Harmony(MmPatches.HarmonyId).UnpatchAll(MmPatches.HarmonyId);
            MmPatches.Reset();
            MmPatches.Sink = null;
        }

        private static void Arm(MmPatches.Switches switches = null) =>
            MmPatches.TryApply(new Harmony(MmPatches.HarmonyId), switches ?? new MmPatches.Switches());

        private static void Disarm() => new Harmony(MmPatches.HarmonyId).UnpatchAll(MmPatches.HarmonyId);

        // ------------------------------------------------------------------ binding

        [Test] public void TryBind_ResolvesEveryMemberWithTheExpectedTypes()
        {
            var a = MmAccess.TryBind(out var missing);
            Assert.AreEqual("", string.Join(", ", missing));
            Assert.IsTrue(a.SafetyNetReady && a.BeforeAfterReady && a.ValuesReady && a.NodeIndexReady);

            Assert.AreEqual(typeof(Ksp1ConfigFinalizer), a.Finalizer);
            CollectionAssert.AreEqual(new[] { typeof(IReadOnlyList<Ksp1ConfigFile>), typeof(Ksp1ImportReport) }, Types(a.Finalize));
            Assert.AreEqual(typeof(Ksp1FinalizedConfigDatabase), a.Finalize.ReturnType);
            CollectionAssert.AreEqual(new[] { typeof(string), typeof(string), typeof(HashSet<string>) }, Types(a.ModIsAvailable));
            Assert.AreEqual(typeof(bool), a.ModIsAvailable.ReturnType);
            CollectionAssert.AreEqual(new[] { typeof(Ksp1ConfigNode), typeof(Ksp1ConfigFile), typeof(HashSet<string>), typeof(Ksp1ModuleManagerReport) },
                Types(a.CopyWithSatisfiedNeeds));
            Assert.AreEqual(a.MmNode, a.CopyWithSatisfiedNeeds.ReturnType);
            CollectionAssert.AreEqual(new[] { a.MmNode, a.MmNode, typeof(Ksp1ModuleManagerReport) }, Types(a.ApplyNodeContents));
            CollectionAssert.AreEqual(new[] { a.MmNode, a.MmNode, typeof(Ksp1ModuleManagerReport) }, Types(a.ApplyChildPatch));
            CollectionAssert.AreEqual(new[] { a.MmNode, a.NameDescriptor }, Types(a.NodeMatches));
            CollectionAssert.AreEqual(new[] { typeof(string) }, Types(a.ParseName));
            Assert.IsTrue(a.MmValue.IsValueType);
            Assert.AreEqual(a.MmNode, a.Clone.ReturnType);
            foreach (var m in new[] { a.ModIsAvailable, a.CopyWithSatisfiedNeeds, a.ApplyNodeContents, a.ApplyChildPatch, a.NodeMatches })
                Assert.IsFalse(m.IsPublic, m.Name + " is private in 26w42c");
        }

        private static Type[] Types(System.Reflection.MethodBase m) => m.GetParameters().Select(p => p.ParameterType).ToArray();

        private static object NewNode(MmAccess a, string name, params string[] pairs)
        {
            var node = Activator.CreateInstance(a.MmNode, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic, null, new object[] { name }, null);
            for (var i = 0; i + 1 < pairs.Length; i += 2) a.AddValue(node, pairs[i], pairs[i + 1]);
            return node;
        }

        [Test] public void Accessors_ReadAndWriteTheNodes()
        {
            var a = MmAccess.TryBind(out _);
            var node = NewNode(a, "PART", "name", "p", "mass", "0.5");
            Assert.AreEqual("PART", a.NodeName(node));
            Assert.AreEqual(2, a.Values(node).Count);
            Assert.AreEqual("mass", a.ValueName(node, 1));
            Assert.AreEqual("0.5", a.ValueRaw(node, 1));
            a.Nodes(node).Add(NewNode(a, "MODULE", "name", "ModuleRCS"));
            Assert.AreEqual(1, a.Nodes(node).Count);
            var copy = a.CallClone(node);
            Assert.AreNotSame(node, copy);
            Assert.AreEqual(1, a.Nodes(copy).Count);
            var descriptor = a.CallParseName("@MODULE[ModuleR*]:HAS[#name[ModuleRCS]]");
            Assert.AreEqual(MmCommand.Edit, a.Command(descriptor));
            Assert.AreEqual("MODULE", a.BaseNameOf(descriptor));
            Assert.IsTrue(a.CallNodeMatches(a.Nodes(node)[0], descriptor));
            Assert.IsFalse(a.CallNodeMatches(node, descriptor));
        }

        [Test] public void PreScan_OfAPlainPatch_DoesNotAllocate()
        {
            Arm();
            var a = MmAccess.TryBind(out _);
            var patch = NewNode(a, "@PART[p]", "@mass", "1", "%cost", "2", "title", "x", "!desc", "delete", "@tags", "a b");
            Assert.IsFalse(MmPatches.MightNeedUs(patch));
            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    if (MmPatches.MightNeedUs(patch)) throw new Exception("unexpected");
            }, Is.Not.AllocatingGCMemory());
        }

        [TestCase("PART", true)]
        [TestCase(" PART:NEEDS[X] ", true)]
        [TestCase("part", true)]
        [TestCase("@PART[x]", false)]
        [TestCase("+PART[x]:NEEDS[Y]", true)]
        [TestCase("+ PART[x]", true)]
        [TestCase("+MODULE[x]", false)]
        [TestCase("PARTS", false)]
        [TestCase("RESOURCE_DEFINITION", false)]
        public void IsPartRootName(string name, bool expected) => Assert.AreEqual(expected, MmPatches.IsPartRootName(name));

        // ------------------------------------------------------------------ Finalize, in memory

        private const string Part =
            "PART\n{\nname = p1\nmass = 0.5\ncost = 100\ncategory = Utility\nvolume = 1\nvolume = 2\nvolume = 3\nnode_stack_top = 0, 1, 0\n" +
            "MODULE\n{\nname = ModuleEnginesFX\nmaxThrust = 10\n}\n" +
            "MODULE\n{\nname = ModuleEnginesFX\nmaxThrust = 20\n}\n" +
            "MODULE\n{\nname = ModuleRCS\n}\n}\n";

        private static Ksp1ConfigFile File(string mod, string text) =>
            new Ksp1ConfigFile(new Ksp1ModRoot("Z:/no-such-import-root", "Z:/no-such-import-root"), mod + "/x.cfg", mod,
                Ksp1ConfigNode.Parse(text));

        private static Ksp1ConfigNode Run(params Ksp1ConfigFile[] files)
        {
            var db = Ksp1ConfigFinalizer.Finalize(files, new Ksp1ImportReport());
            Assert.AreEqual(0, db.ModuleManagerReport.ErrorCount, "Redux reported an error");
            return db.Files.SelectMany(f => f.Node.Nodes).Single(n => n.Name == "PART" && n.GetValue("name") == "p1");
        }

        private static List<string> Flat(Ksp1ConfigNode node, string path = "")
        {
            var list = new List<string>();
            var here = path + "/" + node.Name;
            foreach (var v in node.Values) list.Add(here + ":" + v.Name + "=" + v.RawValue);
            foreach (var n in node.Nodes) list.AddRange(Flat(n, here));
            return list;
        }

        private static string[] Thrusts(Ksp1ConfigNode part) =>
            part.GetNodes("MODULE").Where(m => m.GetValue("name") == "ModuleEnginesFX").Select(m => m.GetValue("maxThrust")).ToArray();

        [Test] public void Finalize_RunsInTheEditor_WithoutOurPatches()
        {
            var part = Run(File("Squad", Part));
            Assert.AreEqual("0.5", part.GetValue("mass"));
        }

        [Test] public void Arithmetic_AndIndices_AreApplied()
        {
            Arm();
            var part = Run(File("Squad", Part), File("Mine",
                "@PART[p1]\n{\n@mass *= 2\n@cost += 50\n@volume,1 = 5\n@volume,-1 /= 4\n" +
                "@MODULE[ModuleEngines*],1\n{\n@maxThrust = 1\n}\n}\n"));
            Assert.AreEqual("1", part.GetValue("mass"));
            Assert.AreEqual("150", part.GetValue("cost"));
            CollectionAssert.AreEqual(new[] { "1", "5", "0.75" }, part.GetValues("volume").ToArray());
            CollectionAssert.AreEqual(new[] { "10", "1" }, Thrusts(part));
            var c = MmPatches.Counters;
            Assert.AreEqual(1, c.ArithmeticMul);
            Assert.AreEqual(1, c.ArithmeticAdd);
            Assert.AreEqual(1, c.ArithmeticDiv);
            Assert.AreEqual(2, c.IndexValueEdits);
            Assert.AreEqual(1, c.IndexNodeEdits);
            Assert.IsFalse(MmPatches.Broken);
            CollectionAssert.Contains(_lines, MmLines.Arithmetic(c));
            CollectionAssert.Contains(_lines, MmLines.Index(c));
        }

        [Test] public void WithoutOurPatches_ReduxLosesTheOperatorAndWidensTheIndex()
        {
            var part = Run(File("Squad", Part), File("Mine",
                "@PART[p1]\n{\n@mass *= 2\n@volume,1 = 5\n@MODULE[ModuleEngines*],1\n{\n@maxThrust = 1\n}\n}\n"));
            Assert.AreEqual("0.5", part.GetValue("mass"));
            CollectionAssert.AreEqual(new[] { "5", "5", "5" }, part.GetValues("volume").ToArray());
            CollectionAssert.AreEqual(new[] { "1", "1" }, Thrusts(part));
        }

        [Test] public void NodeIndices_DeleteAndCopyOnlyThePickedNode()
        {
            Arm();
            var part = Run(File("Squad", Part), File("Mine",
                "@PART[p1]\n{\n!MODULE[ModuleEnginesFX],0\n{\n}\n+MODULE[ModuleEnginesFX],-1\n{\n@maxThrust = 30\n}\n}\n"));
            CollectionAssert.AreEqual(new[] { "20", "30" }, Thrusts(part));
            Assert.AreEqual(1, part.GetNodes("MODULE").Count(m => m.GetValue("name") == "ModuleRCS"));
        }

        [Test] public void IndexOutOfRange_ChangesNothing_AndIsCounted()
        {
            Arm();
            var part = Run(File("Squad", Part), File("Mine",
                "@PART[p1]\n{\n@volume,7 = 9\n@MODULE[ModuleEnginesFX],5\n{\n@maxThrust = 1\n}\n}\n"));
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, part.GetValues("volume").ToArray());
            CollectionAssert.AreEqual(new[] { "10", "20" }, Thrusts(part));
            Assert.AreEqual(2, MmPatches.Counters.IndexOutOfRange);
        }

        [Test] public void VectorComponentEdit_IsSkipped_WhereReduxWouldOverwriteTheVector()
        {
            const string patch = "@PART[p1]\n{\n@node_stack_top[1,,] *= 2\n}\n";
            var redux = Run(File("Squad", Part), File("Mine", patch));
            Assert.AreEqual("2", redux.GetValue("node_stack_top"), "Redux writes the operand over the vector");
            Arm();
            var ours = Run(File("Squad", Part), File("Mine", patch));
            Assert.AreEqual("0, 1, 0", ours.GetValue("node_stack_top"));
            Assert.AreEqual(1, MmPatches.Counters.VectorComponentSkipped);
            CollectionAssert.Contains(_lines, MmLines.VectorComponentsSkipped(1));
        }

        [Test] public void Variables_AreCountedOncePerValue()
        {
            Arm();
            Run(File("Squad", Part), File("Mine", "@PART[*]\n{\n@cost = #$mass$\n@title = #1.0 $cost$\n}\n"));
            Assert.AreEqual(2, MmPatches.Counters.VariableValues);
            CollectionAssert.Contains(_lines, MmLines.Variables(2));
        }

        // ------------------------------------------------------------------ parity

        private const string PlainPatch =
            "@PART[p1]\n{\n@cost = 7\n%newkey = 1\n%cost = 8\ntitle = added\n!volume = delete\n@missing = 3\n@a,b = 4\n" +
            "+extra = 1\n&extra2 = 2\n-category = delete\n@cost:HAS[#name[p1]] = 11\n@Cost = 12\n@node_stack_top[1] = 9\n%odd[1 = 9\n" +
            "@MODULE[ModuleEnginesFX]\n{\n@maxThrust = 7\n}\n+MODULE[ModuleRCS]\n{\n@name = ModuleRCS2\n}\n" +
            "%MODULE[ModuleNew]\n{\nx = 1\n}\n!MODULE[ModuleRCS]\n{\n}\n}\n";

        [Test] public void PlainPatch_SameResultWithAndWithoutOurPatches()
        {
            var redux = Flat(Run(File("Squad", Part), File("Mine", PlainPatch)));
            Arm();
            var ours = Flat(Run(File("Squad", Part), File("Mine", PlainPatch)));
            Assert.IsFalse(MmPatches.Broken);
            CollectionAssert.AreEqual(redux, ours);
        }

        [Test] public void PlainLines_InAPatchWeTakeOver_GiveReduxsResult()
        {
            var redux = Flat(Run(File("Squad", Part), File("Mine", PlainPatch)));
            Arm();
            var withOperator = PlainPatch.Replace("@PART[p1]\n{\n", "@PART[p1]\n{\n@mass *= 2\n");
            var ours = Flat(Run(File("Squad", Part), File("Mine", withOperator)));
            Assert.AreEqual(1, MmPatches.Counters.ArithmeticMul, "the patch was taken over");
            var expected = redux.Select(l => l == "/PART:mass=0.5" ? "/PART:mass=1" : l).ToList();
            CollectionAssert.AreEqual(expected, ours);
        }

        [Test] public void ArithmeticOff_LeavesTheLineToRedux()
        {
            Arm(new MmPatches.Switches { Arithmetic = false });
            var part = Run(File("Squad", Part), File("Mine", "@PART[p1]\n{\n@mass *= 2\n@volume,1 = 5\n}\n"));
            Assert.AreEqual("0.5", part.GetValue("mass"));
            CollectionAssert.AreEqual(new[] { "1", "5", "3" }, part.GetValues("volume").ToArray());
            CollectionAssert.Contains(_lines, MmLines.Armed(true, false, true, false, true));
        }

        // ------------------------------------------------------------------ safety net and BEFORE/AFTER

        private static Ksp1ConfigFile[] NeedsFiles() => new[]
        {
            File("Squad", Part),
            File("RealFuels", "RF_DUMMY\n{\nx = 1\n}\n@PART[p1]\n{\n@cost = 9\n}\n"),
            File("Mine", "@PART[p1]:NEEDS[RealFuels]\n{\n%title = rf\n}\n@PART[p1]:NEEDS[!RealFuels]\n{\n%description = norf\n}\n" +
                         "@PART[p1]:FOR[RealFuels]\n{\n%tags = rf\n}\n"),
        };

        [Test] public void SafetyNet_BlockedPluginFolder_DoesNotSatisfyNeeds()
        {
            var redux = Run(NeedsFiles());
            Assert.AreEqual("rf", redux.GetValue("title"), "Redux: the folder makes NEEDS true");
            Assert.AreEqual("9", redux.GetValue("cost"));

            Arm();
            var ours = Run(NeedsFiles());
            Assert.IsNull(ours.GetValue("title"));
            Assert.AreEqual("norf", ours.GetValue("description"));
            Assert.AreEqual("100", ours.GetValue("cost"), "a patch from the blocked folder is dropped");
            Assert.IsNull(ours.GetValue("tags"), "FOR of a blocked name is dropped");
            Assert.IsNotNull(_lines.FirstOrDefault(l => l.StartsWith("[KSP1PartMod] mm: blocked plugin names: RealFuels,")), string.Join("\n", _lines));
            CollectionAssert.Contains(_lines, MmLines.BlockedName("RealFuels", 2, 1, 0, 1));
        }

        [Test] public void SafetyNetOff_KeepsReduxsAnswer()
        {
            Arm(new MmPatches.Switches { SafetyNet = false });
            var part = Run(NeedsFiles());
            Assert.AreEqual("rf", part.GetValue("title"));
            Assert.IsNull(part.GetValue("description"));
        }

        [Test] public void BeforeAfter_OfAnAbsentMod_IsDropped_ForNamesCountAsPresent()
        {
            var files = new[]
            {
                File("Squad", Part),
                File("Mine", "@PART[p1]:AFTER[RemoteTech]\n{\n%title = rt\n}\n@PART[p1]:FOR[zzMine]\n{\n@cost = 1\n}\n" +
                             "@PART[p1]:AFTER[zzMine]\n{\n@cost *= 3\n}\n@PART[p1]:LAST[RemoteTech]\n{\n%description = last\n}\n"),
            };
            Arm();
            var part = Run(files);
            Assert.IsNull(part.GetValue("title"));
            Assert.AreEqual("3", part.GetValue("cost"));
            Assert.AreEqual("last", part.GetValue("description"), "LAST is never dropped");
            CollectionAssert.Contains(_lines, MmLines.BeforeAfterAbsent(new[] { new KeyValuePair<string, int>("RemoteTech", 1) }));
        }

        [Test] public void Unpatched_RunsReduxAgain()
        {
            Arm();
            Run(NeedsFiles());
            Disarm();
            var part = Run(NeedsFiles());
            Assert.AreEqual("rf", part.GetValue("title"));
        }

        [Test] public void PlainPatch_CoversEveryReduxForm()
        {
            var part = Run(File("Squad", Part), File("Mine", PlainPatch));
            Assert.AreEqual("1", part.GetValue("extra"));
            Assert.AreEqual("2", part.GetValue("extra2"));
            Assert.IsNull(part.GetValue("category"));
            Assert.AreEqual("9", part.GetValue("odd[1"), "an unclosed bracket stays in the key");
        }

        [Test] public void FailedWriteBack_LeavesTheNodeAsReduxFoundIt()
        {
            const string patch = "@PART[p1]\n{\n@mass *= 2\n@cost = 7\n%newkey = 1\n}\n";
            var redux = Flat(Run(File("Squad", Part), File("Mine", patch)));
            Arm();
            MmPatches.FailWriteBackAfter = 1;
            var ours = Flat(Run(File("Squad", Part), File("Mine", patch)));
            Assert.IsTrue(MmPatches.Broken);
            Assert.IsNotNull(_lines.FirstOrDefault(l => l.StartsWith("[KSP1PartMod] mm: patch failed, stopped for this session:")));
            CollectionAssert.AreEqual(redux, ours, "restored, then Redux's own code ran");
        }

        [Test] public void AutomaticRule_NeverBlocksGameDataItself_AndCountsPartCopies()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ksp1partmod-mm-" + Guid.NewGuid().ToString("N"));
            try
            {
                void Touch(string rel)
                {
                    var path = System.IO.Path.Combine(root, rel);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.File.WriteAllText(path, "");
                }
                Touch("GameData/ModuleManager.4.2.3.dll");
                Touch("GameData/SomePlugin/Plugins/SomePlugin.dll");
                Touch("GameData/CopiesOnly/Plugins/CopiesOnly.dll");
                Arm();
                MmPatches.ImportRootsOverride = () => new[] { root };
                var db = Ksp1ConfigFinalizer.Finalize(new[]
                {
                    File("Squad", Part),
                    File("GameData", "@PART[p1]\n{\n%mine = 1\n}\n"),
                    File("SomePlugin", "@PART[p1]\n{\n%plugin = 1\n}\n"),
                    File("CopiesOnly", "+PART[p1]\n{\n@name = p2\n}\n"),
                }, new Ksp1ImportReport());
                var parts = db.Files.SelectMany(f => f.Node.Nodes).Where(n => n.Name == "PART").ToList();
                var p1 = parts.Single(n => n.GetValue("name") == "p1");
                Assert.AreEqual("1", p1.GetValue("mine"), "the player's loose GameData patch is kept");
                Assert.IsNull(p1.GetValue("plugin"), "a DLL folder without parts is blocked");
                Assert.AreEqual(1, parts.Count(n => n.GetValue("name") == "p2"), "a folder of +PART copies has parts");
                var blocked = _lines.Single(l => l.StartsWith("[KSP1PartMod] mm: blocked plugin names:"));
                StringAssert.Contains("SomePlugin", blocked);
                StringAssert.DoesNotContain("GameData", blocked);
                StringAssert.DoesNotContain("CopiesOnly", blocked);
                StringAssert.Contains("automatic 1;", blocked);
            }
            finally
            {
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            }
        }
    }
}
