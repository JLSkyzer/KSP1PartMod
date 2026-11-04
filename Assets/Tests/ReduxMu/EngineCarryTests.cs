using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Engines;
using KSP1PartMod.Tests.Animation.Native.Engines;
using NUnit.Framework;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.0, review carry items of Tasks 1 and 2: the saved mode of a 0.15.x craft made to agree with the mode that
    /// runs (<see cref="EngineModeSync"/>), the line when a prefab rebuild gives other SEP values, Redux's exact engine module
    /// name rule.</summary>
    public class EngineCarryTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            EngineModes.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            EngineModes.Sink = null;
            IsruRuntime.PartNameForTests = null;
        }

        private static void Disarm()
        {
            new Harmony(EngineModes.HarmonyId).UnpatchAll(EngineModes.HarmonyId);
            new Harmony(SepEngines.HarmonyId).UnpatchAll(SepEngines.HarmonyId);
            new Harmony(EngineModeSync.HarmonyId).UnpatchAll(EngineModeSync.HarmonyId);
            EngineModes.Reset();
            SepEngines.Reset();
            EngineModeSync.Reset();
        }

        private static bool Arm(string json = "{}") =>
            EngineModes.TryApply(new Harmony(EngineModes.HarmonyId), AnimationSettings.Parse(json, out _));

        private string Log => string.Join(Environment.NewLine, _lines);

        private static Data_Engine Engine(int index, string active, params string[] ids)
        {
            var d = new Data_Engine { engineModes = ids.Select(id => new Data_Engine.EngineMode { engineID = id }).ToArray() };
            d.currentEngineModeIndex = index;
            d.activeEngineMode.SetValue(active);
            return d;
        }

        private static Ksp1ConfigNode PartNode(string text) => Ksp1ConfigNode.Parse(text).GetNodes("PART").First();

        private static string Lr91 => "PART\n{\nname = bluedog_LR91_3\nmass = 0.5\n" + EngineModeOrderTests.Engine("basicEngine", "thrustTransform", 88.7) +
                                      EngineModeOrderTests.Engine("vernier", "vernierTransform", 10) + "}\n";

        // ------------------------------------------------------------------ 1. saved mode of a 0.15.x craft

        [Test] public void Sync_SavedLastModuleId_ShowsTheModeThatRuns()
        {
            var d = Engine(0, "vernier", "basicEngine", "vernier");
            Assert.IsTrue(EngineModeSync.Sync(d, out var from, out var to));
            Assert.AreEqual("vernier", from);
            Assert.AreEqual("basicEngine", to);
            Assert.AreEqual("basicEngine", d.activeEngineMode.GetValue());
            Assert.AreEqual(0, d.currentEngineModeIndex, "the mode that runs is not changed");
            Assert.IsFalse(EngineModeSync.Sync(d, out _, out _), "agreement: nothing to do");
        }

        [Test] public void Sync_IndexOutOfRange_Nothing()
        {
            var d = Engine(5, "vernier", "basicEngine", "vernier");
            Assert.IsFalse(EngineModeSync.Sync(d, out _, out _));
            Assert.AreEqual("vernier", d.activeEngineMode.GetValue());
        }

        [Test] public void ModeSync_Resolves_ArmsOnlyWithEngineModes()
        {
            Assert.IsNotNull(EngineModeSync.FindOnInitialize());
            Assert.IsFalse(EngineModeSync.TryApply(new Harmony(EngineModeSync.HarmonyId)), "EngineModes not armed");
            Assert.IsTrue(Arm());
            Assert.IsTrue(EngineModeSync.TryApply(new Harmony(EngineModeSync.HarmonyId)));
        }

        [Test] public void AfterInitialize_OnlyOnPartsWeOrdered_OneLine()
        {
            Assert.IsTrue(Arm());
            EngineModes.FindOrdered().Invoke(null, new object[] { PartNode(Lr91) }); // the import orders bluedog_LR91_3
            var go = new GameObject("bluedog_LR91_3");
            var other = new GameObject("someOtherEngine");
            try
            {
                Module_Engine ours, theirs;
                try
                {
                    ours = go.AddComponent<Module_Engine>();
                    theirs = other.AddComponent<Module_Engine>();
                }
                catch (Exception e) { Assert.Ignore("Module_Engine cannot be added in EditMode: " + e.Message); return; }
                IsruRuntime.PartNameForTests = m => m.gameObject.name;
                ours.DataEngine = Engine(0, "vernier", "basicEngine", "vernier");
                theirs.DataEngine = Engine(0, "vernier", "basicEngine", "vernier");
                EngineModeSync.AfterInitialize(ours);
                EngineModeSync.AfterInitialize(ours);
                EngineModeSync.AfterInitialize(theirs);
                Assert.AreEqual("basicEngine", ours.DataEngine.activeEngineMode.GetValue());
                Assert.AreEqual("vernier", theirs.DataEngine.activeEngineMode.GetValue(), "a part we did not order is left alone");
                CollectionAssert.Contains(_lines, EngineSizeLines.ModeSynced("bluedog_LR91_3", "vernier", "basicEngine"));
                Assert.AreEqual(1, _lines.Count(l => l.Contains("saved mode")), Log);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        // ------------------------------------------------------------------ 3. SEP rebuild differs

        [Test] public void SepOutcome_Lines()
        {
            var part = EngineModeOrderTests.PartOf(SepEngineSetsTests.Sep25());
            var full = SepEngineSets.Compute(part, SepEngineSetsTests.Names());
            Assert.AreEqual("RBoosts 20, RMiddles13 10, RCenters13 3; mass +44.5 t", EngineSizeLines.SepOutcome(full));
            var none = SepEngineSets.Compute(part, new string[0]);
            StringAssert.StartsWith("refused (", EngineSizeLines.SepOutcome(none));
            Assert.AreEqual("[KSP1PartMod] engines: P: the prefab rebuild gives b, the first import gave a; the rebuild's values are written",
                EngineSizeLines.SepRebuildDiffers("P", "a", "b"));
        }

        private static void SetImportComplete(bool value) =>
            AccessTools.Property(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.IsComplete)).SetValue(null, value);

        [Test] public void Sep_RebuildWithOtherCounts_OneLine()
        {
            Assert.IsTrue(Arm());
            var part = PartNode(SepEngineSetsTests.Sep25());
            var core = new KSP.Sim.Definitions.PartCore
            {
                data = new KSP.Sim.Definitions.PartData(),
                modules = new List<KSP.Sim.Definitions.ModuleData>
                {
                    new Data_Engine { engineModes = new[] { "AllEngines", "MiddleInner", "CenterThree" }.Select(id => new Data_Engine.EngineMode { engineID = id }).ToArray() },
                },
            };
            var full = new GameObject("model");
            var fewer = new GameObject("model");
            try
            {
                foreach (var n in SepEngineSetsTests.Names()) new GameObject(n).transform.SetParent(full.transform);
                foreach (var n in SepEngineSetsTests.Names(outer: 16)) new GameObject(n).transform.SetParent(fewer.transform);
                SetImportComplete(false);
                SepEngines.Apply(part, core, full); // the first import
                SetImportComplete(true);
                SepEngines.Apply(part, core, full); // a rebuild with the same model: no line
                Assert.IsFalse(_lines.Any(l => l.Contains("prefab rebuild")), Log);
                SepEngines.Apply(part, core, fewer);
                SepEngines.Apply(part, core, fewer);
                Assert.AreEqual(1, _lines.Count(l => l.Contains("prefab rebuild")), Log);
                CollectionAssert.Contains(_lines, EngineSizeLines.SepRebuildDiffers("SEP_25_BOOSTER_CLUSTER",
                    "RBoosts 20, RMiddles13 10, RCenters13 3; mass +44.5 t", "RBoosts 16, RMiddles13 10, RCenters13 3; mass +39.5 t"));
                Assert.AreEqual(39.5, core.data.mass, 1e-9, "the rebuild's values are written");
            }
            finally
            {
                SetImportComplete(false);
                UnityEngine.Object.DestroyImmediate(full);
                UnityEngine.Object.DestroyImmediate(fewer);
            }
        }

        // ------------------------------------------------------------------ 2. and 4.

        [Test] public void B9DropLine_SaysItHoldsWhenTheSepValuesAreRefused() =>
            StringAssert.Contains("dropped even when the SEP values are later refused", EngineSizeLines.B9EngineEditDropped("P", "V"));

        [Test] public void IsEngineModule_AsRedux_NoTrim()
        {
            foreach (var name in new[] { "ModuleEngines", "moduleenginesfx", " ModuleEngines", "ModuleEnginesFX ", "ModuleEngine", null })
                Assert.AreEqual(Redux.Ksp1Import.Modules.Ksp1PartModuleImporterRegistry.IsEngineModuleName(name), EngineModeOrder.IsEngineModule(name), name ?? "null");
        }
    }
}
