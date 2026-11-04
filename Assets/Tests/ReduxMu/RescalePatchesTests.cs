using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.IO;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Rescale;
using KSP1PartMod.Tests.Animation;
using Newtonsoft.Json;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.0, Task 4, against the referenced Ksp2Redux.dll (26w42c): KSP1's default rescale, the legacy node scale and
    /// the MODEL position (<see cref="RescalePatches"/>), and the census of the installed cfgs.</summary>
    public class RescalePatchesTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            RescalePatches.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            RescalePatches.Sink = null;
        }

        private static void Disarm()
        {
            foreach (var id in new[] { RescalePatches.DefaultId, RescalePatches.NodeScaleId, RescalePatches.ModelPositionId, RescalePatches.LinesId, MmPatches.HarmonyId })
                new Harmony(id).UnpatchAll(id);
            RescalePatches.Reset();
            MmPatches.Reset();
            MmPatches.Sink = null;
        }

        private static AnimationSettings Settings(string json = "{}") => AnimationSettings.Parse(json, out _);

        private static void ArmAll(string json = "{}")
        {
            var s = Settings(json);
            RescalePatches.TryApplyDefault(new Harmony(RescalePatches.DefaultId), s);
            RescalePatches.TryApplyNodeScale(new Harmony(RescalePatches.NodeScaleId), s);
            RescalePatches.TryApplyModelPosition(new Harmony(RescalePatches.ModelPositionId), s);
        }

        private string Log => string.Join(Environment.NewLine, _lines);

        /// <summary>The LV-T30's shape (KSP1 stock <c>liquidEngine</c>): legacy <c>mesh</c>, <c>scale = 0.1</c> before the nodes,
        /// no <c>rescaleFactor</c>.</summary>
        internal const string LvT30 = "PART\n{\nname = liquidEngine\nmodule = Part\nmesh = model.mu\nscale = 0.1\n" +
                                      "node_stack_top = 0.0, 7.21461, 0.0, 0.0, 1.0, 0.0\nnode_stack_bottom = 0.0, -6.9685, 0.0, 0.0, -1.0, 0.0\n" +
                                      "attachRules = 1,0,1,0,0\nmass = 1.25\n}\n";

        private static Ksp1ConfigNode PartNode(string text) => Ksp1ConfigNode.Parse(text).GetNodes("PART").First();

        private static List<AttachNodeDefinition> Parse(Ksp1ConfigNode part)
        {
            part.TryGetFloat("rescaleFactor", out var factor, 1f); // as Redux's GetRescaleFactor
            return (List<AttachNodeDefinition>)RescalePatches.FindParseAttachNodes().Invoke(null, new object[] { part, factor });
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Members_Resolve()
        {
            Assert.IsNotNull(RescalePatches.FindTryTranslatePart());
            Assert.IsNotNull(RescalePatches.FindRebuild());
            Assert.IsNotNull(RescalePatches.FindAddValue());
            Assert.IsNotNull(RescalePatches.FindParseAttachNodes());
            Assert.IsNotNull(RescalePatches.FindApplyModelNodeTransform());
        }

        [Test] public void KeysOff_ArmNothing()
        {
            ArmAll("{\"ksp1_default_rescale\": false, \"ksp1_legacy_node_scale\": false, \"ksp1_model_position_rescale\": false}");
            CollectionAssert.AreEqual(new[]
            {
                EngineSizeLines.RescaleOff("ksp1_default_rescale"), EngineSizeLines.RescaleOff("ksp1_legacy_node_scale"),
                EngineSizeLines.RescaleOff("ksp1_model_position_rescale"),
            }, _lines);
            var ids = new[] { RescalePatches.DefaultId, RescalePatches.NodeScaleId, RescalePatches.ModelPositionId, RescalePatches.LinesId };
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Any(ids.Contains) == true));
            var part = PartNode(LvT30);
            Assert.AreEqual(7.21461, Parse(part)[0].position.y, 1e-5, "0.15.1: scale ignored, factor 1");
        }

        [Test] public void Armed_Lines()
        {
            ArmAll();
            CollectionAssert.AreEqual(new[]
            {
                EngineSizeLines.RescaleArmed("ksp1_default_rescale"), EngineSizeLines.RescaleArmed("ksp1_legacy_node_scale"),
                EngineSizeLines.RescaleArmed("ksp1_model_position_rescale"),
            }, _lines);
        }

        // ------------------------------------------------------------------ default rescale

        [Test] public void Default_AddedOnce_ToTheRealNode()
        {
            ArmAll();
            var part = PartNode(LvT30);
            RescalePatches.AddDefault(part, true);
            RescalePatches.AddDefault(part, true);
            Assert.AreEqual(new[] { "1.25" }, part.GetValues("rescaleFactor").ToArray(), "second call: nothing");
            part.TryGetFloat("rescaleFactor", out var f, 1f);
            Assert.AreEqual(1.25f, f, "Redux reads it");
        }

        [Test] public void Default_PartWithAFactor_Excluded_Untouched()
        {
            ArmAll();
            var own = PartNode("PART\n{\nname = own\nrescaleFactor = 1\n}\n");
            var eva = PartNode("PART\n{\nname = kerbalEVA\nmodule = KerbalEVA\n}\n");
            var flag = PartNode("PART\n{\nname = flag\nmodule = FlagSite\n}\n");
            foreach (var p in new[] { own, eva, flag }) RescalePatches.AddDefault(p, true);
            Assert.AreEqual(new[] { "1" }, own.GetValues("rescaleFactor").ToArray());
            Assert.IsNull(eva.GetValue("rescaleFactor"));
            Assert.IsNull(flag.GetValue("rescaleFactor"));
            RescalePatches.AddDefault(PartNode(LvT30), true);
            RescalePatches.AfterImportComplete();
            CollectionAssert.Contains(_lines, "[KSP1PartMod] rescale: 1 part(s) without rescaleFactor set to 1.25, 2 excluded (KerbalEVA, FlagSite)");
        }

        [Test] public void Default_Prefix_ThroughHarmony_OnTryTranslatePart() => WithTranslator(() =>
        {
            ArmAll();
            var core = Translate(LvT30, out var part);
            Assert.AreEqual("1.25", part.GetValue("rescaleFactor"));
            var top = core.data.attachNodes.Single(n => n.nodeID == "top");
            var bottom = core.data.attachNodes.Single(n => n.nodeID == "bottom");
            Assert.AreEqual(7.21461 * 0.1 * 1.25, top.position.y, 1e-4, "7.21461 x 0.1 x 1.25 = 0.9018 m");
            Assert.AreEqual(0.9018, top.position.y, 1e-4);
            Assert.AreEqual(-6.9685 * 0.1 * 1.25, bottom.position.y, 1e-4);
        });

        // ------------------------------------------------------------------ legacy node scale

        [Test] public void NodeScale_TopIs0p9018_AndTwiceTheSame()
        {
            ArmAll();
            var part = PartNode(LvT30);
            RescalePatches.AddDefault(part, true);
            for (var run = 0; run < 2; run++) // each parse starts from the cfg
                Assert.AreEqual(0.9018, Parse(part)[0].position.y, 1e-4, "run " + run);
        }

        [Test] public void NodeScale_ScaleAfterTheNodes_NoChange()
        {
            ArmAll("{\"ksp1_default_rescale\": false}");
            var part = PartNode("PART\n{\nname = late\nnode_stack_top = 0, 2, 0, 0, 1, 0\nscale = 0.1\n}\n");
            Assert.AreEqual(2.0, Parse(part)[0].position.y, 1e-6);
        }

        [Test] public void NodeScale_CountMismatch_Skipped_OneLine()
        {
            ArmAll("{\"ksp1_default_rescale\": false}");
            var part = PartNode("PART\n{\nname = broken\nscale = 0.1\nnode_stack_top = 0, 2, 0\nnode_stack_bottom = 0, -2, 0, 0, -1, 0\n}\n");
            Assert.AreEqual(-2.0, Parse(part).Single().position.y, 1e-6, "Redux dropped the short value: positions left as Redux made them");
            Parse(part);
            Assert.AreEqual(1, _lines.Count(l => l == EngineSizeLines.NodeCountMismatch("broken", 2, 1)), Log);
        }

        // ------------------------------------------------------------------ MODEL position

        [Test] public void ModelPosition_TimesRescaleFactor()
        {
            ArmAll();
            var apply = RescalePatches.FindApplyModelNodeTransform();
            var go = new GameObject("m");
            try
            {
                var model = Ksp1ConfigNode.Parse("MODEL\n{\nmodel = x\nposition = 0, 1, 2\n}\n").GetNodes("MODEL").First();
                apply.Invoke(null, new object[] { go.transform, model, 0.625f });
                Assert.AreEqual(new Vector3(0, 0.625f, 1.25f), go.transform.localPosition);
                Assert.AreEqual(new Vector3(0.625f, 0.625f, 0.625f), go.transform.localScale, "Redux's scale untouched");
                apply.Invoke(null, new object[] { go.transform, model, 0.625f });
                Assert.AreEqual(new Vector3(0, 0.625f, 1.25f), go.transform.localPosition, "absolute: twice the same");

                go.transform.localPosition = new Vector3(5, 5, 5);
                var noPosition = Ksp1ConfigNode.Parse("MODEL\n{\nmodel = x\n}\n").GetNodes("MODEL").First();
                apply.Invoke(null, new object[] { go.transform, noPosition, 0.625f });
                Assert.AreEqual(new Vector3(3.125f, 3.125f, 3.125f), go.transform.localPosition, "no position: the .mu root's, scaled as KSP1");
                go.transform.localPosition = Vector3.zero;
                apply.Invoke(null, new object[] { go.transform, noPosition, 0.625f });
                Assert.AreEqual(Vector3.zero, go.transform.localPosition, "zero stays zero, not counted");

                apply.Invoke(null, new object[] { go.transform, model, 1f });
                Assert.AreEqual(new Vector3(0, 1, 2), go.transform.localPosition, "factor 1: Redux's");
                RescalePatches.AfterImportComplete();
                CollectionAssert.Contains(_lines, "[KSP1PartMod] rescale: MODEL position x rescaleFactor on 3 model(s)");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ through Redux's translator

        private static void WithTranslator(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Ksp1ImportedPartRegistry.Clear();
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                test();
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
                JsonConvert.DefaultSettings = previous;
            }
        }

        private static PartCore Translate(string text, out Ksp1ConfigNode part)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(text);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Squad/Parts/engine.cfg", "Squad", node);
            part = node.GetNodes("PART").First();
            Ksp1ImportedPartRegistry.BeginImport("rescale-test");
            Assert.IsTrue(new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out var core, out _));
            return core;
        }

        // ------------------------------------------------------------------ installed cfgs

        /// <summary>The installed <c>mods/KSP1</c> set through Redux's finalizer with our ModuleManager patches at their
        /// defaults (as in game): the parts each rule takes (census of plan (c)).</summary>
        [Test] public void Census_Installed_19Rescaled_7Excluded_13Scale()
        {
            var root = InstalledKsp1.Root;
            InstalledKsp1.AllCfg(); // ignores the test when the install is absent
            MmPatches.Sink = _ => { };
            MmPatches.ImportRootsOverride = () => new[] { root };
            MmPatches.TryApply(new Harmony(MmPatches.HarmonyId), new MmPatches.Switches());
            var report = new Ksp1ImportReport();
            var files = Ksp1PartModDiscovery.LoadConfigFiles(report, true, new[] { new Ksp1ModRoot(root, root) });
            var db = Ksp1ConfigFinalizer.Finalize(files, report);
            var rescaled = new SortedSet<string>(StringComparer.Ordinal);
            var excluded = new SortedSet<string>(StringComparer.Ordinal);
            var scaled = new SortedSet<string>(StringComparer.Ordinal);
            var wheels = new SortedSet<string>(StringComparer.Ordinal);
            var models = new SortedSet<string>(StringComparer.Ordinal);
            int scaledNodes = 0, rescaledNodes = 0;
            foreach (var part in db.Files.SelectMany(f => f.Node.Nodes).Where(n => n.Name == "PART"))
            {
                var name = part.GetValue("name") ?? "";
                var a = new Ksp1NodeAdapter(part);
                var missing = RescaleRule.MissingDefault(a);
                if (missing.HasValue) rescaled.Add(name);
                else if (part.GetValue("rescaleFactor") == null && RescaleRule.IsExcluded(a)) excluded.Add(name);
                if (RescaleRule.HasNodeScale(a))
                {
                    scaled.Add(name);
                    scaledNodes += RescaleRule.NodeScale(a).Count(p => p.Value != 1f);
                }
                if (missing.HasValue) rescaledNodes += RescaleRule.NodeScale(a).Count;
                part.TryGetFloat("rescaleFactor", out var factor, missing ?? 1f);
                if (factor != 1f && part.GetNodes("MODULE").Any(m => m.GetValue("name") == "ModuleWheelBase")) wheels.Add(name + " x" + factor.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (factor != 1f && part.GetNodes("MODEL").Any(m => m.GetValue("position") != null)) models.Add(name);
            }
            TestContext.WriteLine($"rescale census (installed): default 1.25 on {rescaled.Count}: {string.Join(", ", rescaled)}");
            TestContext.WriteLine($"excluded {excluded.Count}: {string.Join(", ", excluded)}");
            TestContext.WriteLine($"node scale {scaled.Count}: {string.Join(", ", scaled)}");
            TestContext.WriteLine($"nodes moved: {scaledNodes} by a scale, {rescaledNodes} on the parts set to 1.25");
            TestContext.WriteLine($"wheels with rescaleFactor != 1 {wheels.Count}: {string.Join(", ", wheels)}");
            TestContext.WriteLine($"MODEL position x rescaleFactor {models.Count}: {string.Join(", ", models)}");
            Assert.AreEqual(19, rescaled.Count, "parts without rescaleFactor");
            Assert.AreEqual(7, excluded.Count, "KerbalEVA and FlagSite");
            // The plan counted 14: bluedog_X15_VentralFin's scale comes after its only node, so KSP1 does not apply it.
            Assert.AreEqual(13, scaled.Count, "parts with a scale before their nodes");
        }
    }
}
