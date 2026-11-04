using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Sizes;
using Newtonsoft.Json;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.0, Task 3, against the referenced Ksp2Redux.dll (26w42c): size labels from the stack nodes Redux measures
    /// (<see cref="SizeFromNodes"/>). A key snapshot of the installed cfgs is not possible here: the sizes come from the models,
    /// so the census comes from the game's log.</summary>
    public class SizeFromNodesTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            SizeFromNodes.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            SizeFromNodes.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(SizeFromNodes.HarmonyId).UnpatchAll(SizeFromNodes.HarmonyId);
            SizeFromNodes.Reset();
        }

        private static bool Arm(string json = "{}") =>
            SizeFromNodes.TryApply(new Harmony(SizeFromNodes.HarmonyId), AnimationSettings.Parse(json, out _));

        private string Log => string.Join(Environment.NewLine, _lines);

        private static AttachNodeDefinition Node(string id, AttachNodeType type, string sizeKey, int size = 1)
        {
            AttachNodeDefinition.Defaults(out var d);
            d.nodeID = id;
            d.nodeType = type;
            d.sizeKey = sizeKey;
            d.size = size;
            return d;
        }

        private static PartCore Core(string reduxKey, params AttachNodeDefinition[] nodes) => new PartCore
        {
            data = new PartData
            {
                partName = "p",
                sizeKey = reduxKey,
                sizeCategory = (MetaAssemblySizeFilterType)Enum.Parse(typeof(MetaAssemblySizeFilterType), PartSizeRule.CategoryOf(reduxKey)),
                attachNodes = nodes.ToList(),
                serializedPartModules = new List<SerializedPartModule>(),
            },
        };

        private static Ksp1ConfigNode Part(string name, string bulkhead) =>
            Ksp1ConfigNode.Parse("PART\n{\nname = " + name + (bulkhead == null ? "" : "\nbulkheadProfiles = " + bulkhead) + "\n}\n").GetNodes("PART").First();

        // ------------------------------------------------------------------ arming

        [Test] public void Members_Resolve() => Assert.IsNotNull(SizeFromNodes.FindMapSize());

        [Test] public void KeyOff_ArmsNothing()
        {
            Assert.IsFalse(Arm("{\"ksp1_size_from_nodes\": false}"));
            CollectionAssert.AreEqual(new[] { EngineSizeLines.SizeOff }, _lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(SizeFromNodes.HarmonyId) == true));
        }

        [Test] public void Armed_Line()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, EngineSizeLines.SizeArmed);
        }

        // ------------------------------------------------------------------ the rule on built node lists

        [Test] public void StackKeys_FromRealNodes_LargestStackWins_SurfaceIgnored()
        {
            var nodes = new List<AttachNodeDefinition>
            {
                Node("top", AttachNodeType.Stack, "SM"), Node("bottom", AttachNodeType.Stack, "MD"), Node("srfAttach", AttachNodeType.Surface, "2XL"),
            };
            var d = PartSizeRule.Decide("SM", false, "size1", SizeFromNodes.StackKeys(nodes));
            Assert.AreEqual("MD", d.Key);
            Assert.AreEqual(SizeSource.Nodes, d.Source);
            Assert.AreEqual(2, d.Steps);
        }

        [Test] public void StackKeys_NoSizeKey_UsesTheLegacySize()
        {
            var nodes = new List<AttachNodeDefinition> { Node("top", AttachNodeType.Stack, null, 2) };
            Assert.AreEqual(PartSizeRegistry.GetAttachNodeSizeKey(null, 2), SizeFromNodes.StackKeys(nodes).Single().sizeKey);
        }

        [Test] public void Apply_StackPart_Relabelled_TwiceTheSame()
        {
            Arm();
            var core = Core("SM", Node("top", AttachNodeType.Stack, "SM"), Node("bottom", AttachNodeType.Stack, "LG"));
            var part = Part("stackPart", "size1");
            for (var run = 0; run < 2; run++) // the prefab rebuild runs the postfix again
            {
                SizeFromNodes.Apply("Test", part, core);
                Assert.AreEqual("LG", core.data.sizeKey, "run " + run);
                Assert.AreEqual(MetaAssemblySizeFilterType.L, core.data.sizeCategory, "run " + run);
            }
        }

        [Test] public void Apply_SurfaceOnly_UnknownToken_Read()
        {
            Assert.IsTrue(Arm());
            var core = Core("SM", Node("srfAttach", AttachNodeType.Surface, "SM"));
            SizeFromNodes.Apply("Test", Part("tokenPart", "size3p4, srf"), core);
            Assert.AreEqual("LG+", core.data.sizeKey);
            Assert.AreEqual(MetaAssemblySizeFilterType.LPLUS, core.data.sizeCategory);
        }

        [Test] public void Apply_SurfaceOnly_TokenReduxKnows_Kept()
        {
            Assert.IsTrue(Arm());
            var core = Core("MD", Node("srfAttach", AttachNodeType.Surface, "SM"));
            SizeFromNodes.Apply("Test", Part("knownPart", "size2, size3p4"), core);
            Assert.AreEqual("MD", core.data.sizeKey, "Redux did not fall back");
        }

        [Test] public void Apply_NoNodeNoToken_Kept()
        {
            Assert.IsTrue(Arm());
            var core = Core("SM");
            SizeFromNodes.Apply("Test", Part("bare", "srf"), core);
            Assert.AreEqual("SM", core.data.sizeKey);
            Assert.AreEqual(MetaAssemblySizeFilterType.S, core.data.sizeCategory);
        }

        [Test] public void Summary_PerMod_Moves_AndDocking()
        {
            Assert.IsTrue(Arm());
            var dock = Core("SM", Node("top", AttachNodeType.Stack, "MD"));
            dock.data.serializedPartModules.Add(new SerializedPartModule { Name = "PartComponentModule_DockingNode", BehaviourType = typeof(Module_DockingNode) });
            SizeFromNodes.Apply("ModA", Part("dockPort", "size1"), dock);
            SizeFromNodes.Apply("ModA", Part("dockPort", "size1"), dock); // counted once by name
            SizeFromNodes.Apply("ModA", Part("same", "size1"), Core("SM", Node("top", AttachNodeType.Stack, "SM")));
            SizeFromNodes.Apply("ModB", Part("tok", "size000"), Core("SM", Node("srfAttach", AttachNodeType.Surface, "SM")));
            SizeFromNodes.AfterImportComplete();
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] size: ModA: 1 from nodes, 0 from tokens, 1 unchanged; 1 moved by 2+ steps",
                "[KSP1PartMod] size: ModB: 0 from nodes, 1 from tokens, 0 unchanged; 1 moved by 2+ steps",
                "[KSP1PartMod] size: largest moves: tok SM -> XXS (3), dockPort SM -> MD (2)",
                "[KSP1PartMod] size: docking parts relabelled: dockPort SM -> MD",
            }, _lines.Skip(1).ToArray(), Log);
            _lines.Clear();
            SizeFromNodes.AfterImportComplete();
            CollectionAssert.IsEmpty(_lines, "a cached reload logs nothing");
        }

        // ------------------------------------------------------------------ through Redux's translator

        private static void WithGameSettings(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                test();
            }
            finally { JsonConvert.DefaultSettings = previous; }
        }

        private static PartCore Translate(string text, out string rawJson)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(text);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/size.cfg", "Test", node);
            Ksp1ImportedPartRegistry.BeginImport("size-test");
            Assert.IsTrue(new Ksp1PartTranslator(null, null).TryTranslatePart(file, node.GetNodes("PART").First(), new Ksp1ImportReport(), out var core, out rawJson));
            return core;
        }

        [Test] public void Translator_StackPart_LabelIsTheLargestStackNode_InTheJson() => WithGameSettings(() =>
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Ksp1ImportedPartRegistry.Clear();
            try
            {
                Assert.IsTrue(Arm());
                var core = Translate("PART\n{\nname = sizeStack\nmass = 1\nbulkheadProfiles = size0\nnode_stack_top = 0, 0.5, 0, 0, 1, 0, 3\n" +
                                     "node_stack_bottom = 0, -0.5, 0, 0, -1, 0, 0\nnode_attach = 0.3, 0, 0, 1, 0, 0, 1\n}\n", out var json);
                var expected = PartSizeRule.FromStackNodes(SizeFromNodes.StackKeys(core.data.attachNodes));
                Assert.IsNotNull(expected, Log);
                Assert.AreEqual(expected, core.data.sizeKey, Log);
                Assert.AreEqual(PartSizeRule.CategoryOf(expected), core.data.sizeCategory.ToString());
                var back = IOProvider.FromJson<PartCore>(json);
                Assert.AreEqual(expected, back.data.sizeKey, "written before the part JSON");
                TestContext.WriteLine("Redux XS -> " + expected + "; nodes " + string.Join(", ", core.data.attachNodes.Select(n => n.nodeID + " " + n.sizeKey)));
            }
            finally
            {
                foreach (var e in Ksp1ImportedPartRegistry.ImportedParts.ToList())
                {
                    if (e.Prefab != null) UnityEngine.Object.DestroyImmediate(e.Prefab);
                    e.Prefab = null;
                }
                Ksp1ImportedPartRegistry.Clear();
                LogAssert.ignoreFailingMessages = false;
            }
        });
    }
}
