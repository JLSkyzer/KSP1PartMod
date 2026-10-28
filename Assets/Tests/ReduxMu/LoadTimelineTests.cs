using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Game;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Perf;
using KSP1PartMod.Native;
using KSP1PartMod.Perf;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c): the load timeline's members resolve by name with the parameter
    /// types used, its patches are applied, a real <c>TryTranslatePart</c> is timed, and our importers are wrapped only while
    /// it is armed.</summary>
    public class LoadTimelineTests
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _reports = new List<string>();

        [SetUp] public void SetUp()
        {
            LoadTimeline.Reset();
            Ksp1ImportedPartRegistry.Clear();
            _lines.Clear();
            _reports.Clear();
            LoadTimeline.Sink = _lines.Add;
            LoadTimeline.ReportWriter = _reports.Add;
        }

        [TearDown] public void TearDown()
        {
            LoadTimeline.Reset();
            LoadTimeline.Sink = null;
            LoadTimeline.ReportWriter = PerfReportWriter.Write;
            new Harmony(NativeModules.HarmonyId).UnpatchAll(NativeModules.HarmonyId);
            NativeModules.Reset();
            NativeModules.Sink = null;
            Ksp1ImportedPartRegistry.Clear();
        }

        private static AnimationSettings Settings(string json = "{}") => AnimationSettings.Parse(json, out _);

        private void Arm(string json = "{}") => LoadTimeline.Arm(new Harmony(LoadTimeline.HarmonyId), Settings(json));

        // ------------------------------------------------------------------ resolution

        [Test] public void EveryReduxMember_ResolvesWithItsParameterTypes()
        {
            var members = LoadTimeline.Resolve();
            Assert.AreEqual(24, members.Count);
            var missing = members.Where(m => !m.Found).Select(m => m.Label).ToList();
            CollectionAssert.IsEmpty(missing, string.Join(", ", missing));
            foreach (var m in members)
            {
                if (m.Prefix != null) Assert.IsNotNull(AccessTools.Method(typeof(LoadTimeline), m.Prefix), m.Prefix);
                if (m.Postfix != null) Assert.IsNotNull(AccessTools.Method(typeof(LoadTimeline), m.Postfix), m.Postfix);
            }
            // Every phase of research (b) is covered.
            var phases = new HashSet<PerfPhase>(members.Select(m => m.Phase));
            foreach (var p in new[]
                     {
                         PerfPhase.CacheKey, PerfPhase.ConfigGet, PerfPhase.CfgRead, PerfPhase.MmFinalize, PerfPhase.TankCatalog,
                         PerfPhase.PlumeCatalog, PerfPhase.LocalizationCatalog, PerfPhase.ImportWall, PerfPhase.Translate,
                         PerfPhase.ReduxImporters, PerfPhase.Prefab, PerfPhase.MuRead, PerfPhase.Textures, PerfPhase.ReportLines,
                         PerfPhase.LocalizationRegister, PerfPhase.Icons, PerfPhase.Release, PerfPhase.Rebuild,
                     })
                Assert.IsTrue(phases.Contains(p), p.ToString());
        }

        [Test] public void FlowAndRebuildNameMembers_Resolve()
        {
            var x = LoadTimeline.ResolveExtras();
            CollectionAssert.IsEmpty(x.Missing, string.Join(", ", x.Missing));
            Assert.AreEqual(typeof(float), x.FlowStartedOnRealtime.FieldType);
            Assert.AreEqual(typeof(string), x.EntryPartName.ReturnType);
            Assert.AreEqual("OnActionResolve", x.OnActionResolve.Name);
        }

        [Test] public void Armed_PatchesEveryMember_AndSaysSo()
        {
            Arm();
            Assert.IsTrue(LoadTimeline.IsArmed);
            Assert.IsTrue(PerfClock.Enabled);
            var armed = _lines.Single(l => l.Contains("perf: armed:"));
            StringAssert.StartsWith("[KSP1PartMod] perf: armed: load timeline on (24 of 24 Redux phase(s) timed), memory lines on, " +
                                    "report file on, frame cost off (window 10 s); timer overhead ~", armed);
            Assert.IsFalse(_lines.Any(l => l.Contains("not found")), string.Join("\n", _lines));
            var patched = Harmony.GetAllPatchedMethods()
                .Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(LoadTimeline.HarmonyId) == true).ToList();
            Assert.AreEqual(25, patched.Count); // 24 Redux members and SequentialFlow.OnActionResolve
            var finalized = patched.Where(m => Harmony.GetPatchInfo(m).Finalizers.Any(p => p.owner == LoadTimeline.HarmonyId))
                .Select(m => m.Name).OrderBy(n => n).ToList();
            CollectionAssert.AreEqual(new[] { "RebuildPrefab", "TryTranslatePart" }, finalized);
            foreach (var m in patched)
            {
                var info = Harmony.GetPatchInfo(m);
                foreach (var p in info.Prefixes.Where(p => p.owner == LoadTimeline.HarmonyId))
                {
                    Assert.AreEqual(Priority.First, p.priority, m.Name);
                    Assert.AreEqual(typeof(void), p.PatchMethod.ReturnType, m.Name + ": a timing prefix never skips the original");
                }
                // The end of the import times nothing: it runs first, so the memory moment and the report see its summary.
                foreach (var p in info.Postfixes.Where(p => p.owner == LoadTimeline.HarmonyId))
                    Assert.AreEqual(m.Name == "MarkImportComplete" ? Priority.First : Priority.Last, p.priority, m.Name);
                Assert.IsFalse(info.Transpilers.Any(p => p.owner == LoadTimeline.HarmonyId));
                foreach (var p in info.Finalizers.Where(p => p.owner == LoadTimeline.HarmonyId))
                    Assert.AreEqual(typeof(void), p.PatchMethod.ReturnType, m.Name + ": a void finalizer leaves the exception as is");
            }
        }

        [Test] public void Off_NoPatch_AndTheLineSaysOff()
        {
            Arm("{\"perf_load_timeline\": false}");
            Assert.IsFalse(LoadTimeline.IsArmed);
            Assert.IsFalse(PerfClock.Enabled);
            StringAssert.StartsWith("[KSP1PartMod] perf: armed: load timeline off (0 of 24 Redux phase(s) timed), ", _lines.Single());
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(LoadTimeline.HarmonyId) == true));
        }

        // ------------------------------------------------------------------ a real translation

        [Test] public void TryTranslatePart_IsTimed_AndKeptAsSlowest()
        {
            LogAssert.ignoreFailingMessages = true; // Redux's report lines (fallback geometry) are not ours to judge here
            Arm();
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse("PART\n{\nname = perfTimelinePart\nmodule = Part\nmass = 0.1\ncategory = Utility\n}\n");
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/perf.cfg", "Test", node);
            var part = node.GetNodes("PART").First();
            // IOProvider's settings come from a RuntimeInitializeOnLoadMethod that EditMode does not run: without them the
            // translation's JSON round trip fails when this fixture runs alone.
            var previousJson = Newtonsoft.Json.JsonConvert.DefaultSettings;
            try
            {
                try { KSP.IO.IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                Ksp1ImportedPartRegistry.BeginImport("perf-timeline-test");
                var ok = new Ksp1PartTranslator(null, null).TryTranslatePart(file, part, new Ksp1ImportReport(), out var core, out _);
                Assert.IsTrue(ok);
                Assert.IsNotNull(core);
                var translate = PerfClock.Clock.Get(PerfPhase.Translate);
                Assert.AreEqual(1, translate.Calls);
                Assert.Greater(translate.Ticks, 0);
                Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.Prefab).Calls, "BuildPrefab is nested in the translation");
                var slowest = LoadTimeline.SlowestOfImport;
                Assert.AreEqual(1, slowest.Length);
                Assert.AreEqual("perfTimelinePart", slowest[0].Name);
                Assert.AreEqual(translate.Ticks, slowest[0].TotalTicks);
                Assert.Greater(slowest[0].PrefabTicks, 0);
                Assert.LessOrEqual(slowest[0].PrefabTicks, slowest[0].TotalTicks);
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
                Newtonsoft.Json.JsonConvert.DefaultSettings = previousJson;
            }
        }

        [Test] public void OutsideAnImport_MainThreadPhasesDoNotCount()
        {
            Arm();
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse("PART\n{\nname = perfNoImport\n}\n");
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/perf.cfg", "Test", node);
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Ksp1ModelImporter_BuildPrefab(file, node.GetNodes("PART").First());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.Prefab).Calls);
        }

        private static void Ksp1ModelImporter_BuildPrefab(Ksp1ConfigFile file, Ksp1ConfigNode part)
        {
            var core = new PartCore { data = new PartData { partName = "perfNoImport" } };
            var go = Redux.Ksp1Import.Model.Ksp1ModelImporter.BuildPrefab(file, part, core, new Ksp1ImportReport());
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ throwing originals

        private static Exception Thrown(Action call)
        {
            try
            {
                call();
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                return e.InnerException;
            }
            catch (Exception e)
            {
                return e;
            }
            return null;
        }

        /// <summary>The method of the innermost frame of a trace (<c>at Type.Method (…)</c> → <c>Type.Method</c>).</summary>
        private static string TopFrame(Exception e)
        {
            var first = (e.StackTrace ?? "").Split('\n')[0].Trim();
            if (first.StartsWith("at ")) first = first.Substring(3);
            var paren = first.IndexOf(" (", StringComparison.Ordinal);
            if (paren < 0) paren = first.IndexOf('(');
            return paren > 0 ? first.Substring(0, paren).Trim() : first;
        }

        /// <summary>A real import error keeps its origin under the timing finalizer: the innermost frame of the unpatched trace
        /// is still in the patched one (Harmony must not re-throw with <c>throw ex</c>, which resets the trace on Mono).</summary>
        private static void AssertTraceKept(Exception before, Exception after, string type)
        {
            TestContext.Out.WriteLine("unpatched:\n" + before.StackTrace + "\npatched:\n" + after.StackTrace);
            StringAssert.Contains(type, before.StackTrace);
            StringAssert.Contains(type, after.StackTrace);
            // The patched method is a new dynamic method each time (_Patch0, _Patch3…): compare without that suffix.
            var top = System.Text.RegularExpressions.Regex.Replace(TopFrame(before), @"_Patch\d+", "");
            Assert.IsNotEmpty(top);
            StringAssert.Contains(top, System.Text.RegularExpressions.Regex.Replace(after.StackTrace, @"_Patch\d+", ""),
                "the original innermost frame is still there");
        }

        /// <summary>Thrown two calls deep, so that a trace reset by a <c>throw ex</c> re-throw would lose <see cref="Inner"/>.</summary>
        public static class Thrower
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Outer() => Inner();

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static void Inner() => throw new InvalidOperationException("deep");
        }

        /// <summary>The timing finalizers' shape (a <c>void</c> finalizer that only clears a flag, as <c>TranslateDone</c> and
        /// <c>RebuildDone</c>) under the game's Harmony: an exception thrown deeper keeps its innermost frame.</summary>
        [Test] public void TimingFinalizer_KeepsTheTraceOfADeepException()
        {
            const string id = "KSP1PartMod.Tests.TraceKept";
            var harmony = new Harmony(id);
            try
            {
                var before = Thrown(Thrower.Outer);
                StringAssert.Contains(nameof(Thrower.Inner), before.StackTrace);
                harmony.Patch(AccessTools.Method(typeof(Thrower), nameof(Thrower.Outer)),
                    finalizer: new HarmonyMethod(typeof(LoadTimeline), "TranslateDone"));
                harmony.Patch(AccessTools.Method(typeof(Thrower), nameof(Thrower.Outer)),
                    finalizer: new HarmonyMethod(typeof(LoadTimeline), "RebuildDone"));
                var after = Thrown(Thrower.Outer);
                TestContext.Out.WriteLine("unpatched:\n" + before.StackTrace + "\npatched:\n" + after.StackTrace);
                Assert.AreSame(typeof(InvalidOperationException), after.GetType());
                StringAssert.Contains(nameof(Thrower.Inner), after.StackTrace, "the frame where the exception was thrown is kept");
            }
            finally
            {
                harmony.UnpatchAll(id);
            }
        }

        private static void Rebuild(object entry) =>
            AccessTools.Method(typeof(Ksp1ImportedPartRegistry), "RebuildPrefab").Invoke(null, new[] { entry });

        [Test] public void ThrowingRebuild_ClearsTheRebuildFlag_AndKeepsTheException()
        {
            var before = Thrown(() => Rebuild(null)); // Redux's own exception, unpatched
            Assert.IsNotNull(before);
            Arm();
            var after = Thrown(() => Rebuild(null));
            Assert.IsNotNull(after);
            Assert.AreEqual(before.GetType(), after.GetType());
            Assert.AreEqual(before.Message, after.Message);
            AssertTraceKept(before, after, nameof(Ksp1ImportedPartRegistry));
            Assert.IsFalse(PerfClock.InRebuild, "the finalizer clears the flag the prefix set");
            PerfClock.NoteTicks(PerfPhase.AnimationStep, 5);
            Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.AnimationStep).Calls, "later notes are not dropped");
        }

        [Test] public void ThrowingTranslate_ClearsThePartFlag_AndKeepsTheException()
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/perf.cfg", "Test", Ksp1ConfigNode.Parse("PART { }"));
            Action call = () => new Ksp1PartTranslator(null, null).TryTranslatePart(file, null, new Ksp1ImportReport(), out _, out _);
            var before = Thrown(call);
            Assert.IsNotNull(before);
            Arm();
            var after = Thrown(call);
            Assert.AreEqual(before.GetType(), after?.GetType());
            Assert.AreEqual(before.Message, after.Message);
            AssertTraceKept(before, after, nameof(Ksp1PartTranslator));
            Assert.IsFalse(LoadTimeline.InPart);
        }

        [Test] public void BeginImport_ResetsTheMainThreadPhases_ButKeepsTheWorkerOnes()
        {
            Arm();
            PerfClock.NoteTicks(PerfPhase.Translate, 10);
            PerfClock.NoteTicks(PerfPhase.CacheKey, 10);
            try
            {
                Ksp1ImportedPartRegistry.BeginImport("perf-reset-test");
                Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.Translate).Calls);
                Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.CacheKey).Calls);
            }
            finally
            {
                Ksp1ImportedPartRegistry.Clear();
            }
        }

        // ------------------------------------------------------------------ our importers

        [Test] public void OurImporters_WrappedOnlyWhileArmed()
        {
            NativeModules.Sink = _ => { };
            Assert.IsTrue(NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), Settings()));
            var importers = Importers(new Ksp1PartModuleImporterRegistry());
            Assert.IsInstanceOf<CrossfeedImporter>(importers[CrossfeedMapping.Ksp1Module]);

            Arm();
            importers = Importers(new Ksp1PartModuleImporterRegistry());
            var timed = importers[CrossfeedMapping.Ksp1Module] as TimedImporter;
            Assert.IsNotNull(timed);
            Assert.IsInstanceOf<CrossfeedImporter>(timed.Inner);
        }

        private static IDictionary<string, IKsp1PartModuleImporter> Importers(Ksp1PartModuleImporterRegistry registry) =>
            (IDictionary<string, IKsp1PartModuleImporter>)NativeModules.Resolve().Importers.GetValue(registry);

        private sealed class FakeImporter : IKsp1PartModuleImporter
        {
            public bool Result;
            public SerializedPartModule Module;
            public Exception Throw;

            public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
            {
                serializedModule = Module;
                if (Throw != null) throw Throw;
                return Result;
            }
        }

        [Test] public void TimedImporter_ReturnsWhatTheInnerReturns()
        {
            PerfClock.Enabled = true;
            var module = new SerializedPartModule { Name = "PartComponentModule_Test", BehaviourType = typeof(string) };
            var yes = new TimedImporter(new FakeImporter { Result = true, Module = module });
            Assert.IsTrue(yes.TryImport(null, null, out var got));
            Assert.AreEqual("PartComponentModule_Test", got.Name);
            Assert.AreEqual(typeof(string), got.BehaviourType);

            var no = new TimedImporter(new FakeImporter { Result = false });
            Assert.IsFalse(no.TryImport(null, null, out var none));
            Assert.IsNull(none.Name);
            Assert.AreEqual(2, PerfClock.Clock.Get(PerfPhase.OurImporters).Calls);
        }

        [Test] public void TimedImporter_LetsTheExceptionThrough()
        {
            PerfClock.Enabled = true;
            var thrown = new InvalidOperationException("inner");
            var timed = new TimedImporter(new FakeImporter { Throw = thrown });
            var caught = Assert.Throws<InvalidOperationException>(() => timed.TryImport(null, null, out _));
            Assert.AreSame(thrown, caught);
            Assert.AreEqual(1, PerfClock.Clock.Get(PerfPhase.OurImporters).Calls);
        }

        // ------------------------------------------------------------------ game states

        [Test] public void GameStates_GameStartCampaignLoadAndOurs()
        {
            Arm();
            _lines.Clear();
            PerfClock.NoteTicks(PerfPhase.AnimationStep, PerfClock.Frequency / 1000 * 3);
            LoadTimeline.OnState(GameState.MainMenu, GameState.WarmUpLoading, 42f);
            LoadTimeline.OnState(GameState.Loading, GameState.MainMenu, 50f);
            LoadTimeline.OnState(GameState.KerbalSpaceCenter, GameState.Loading, 80.5f);
            LoadTimeline.OnState(GameState.MainMenu, GameState.KerbalSpaceCenter, 90f);
            LoadTimeline.OnState(GameState.Loading, GameState.MainMenu, 100f);
            LoadTimeline.OnState(GameState.VehicleAssemblyBuilder, GameState.Loading, 110f);

            Assert.AreEqual("[KSP1PartMod] perf: game start: main menu after 42.0 s", _lines[0]);
            Assert.AreEqual("[KSP1PartMod] perf: campaign load: 30.5 s from Loading to KerbalSpaceCenter", _lines[1]);
            StringAssert.StartsWith("[KSP1PartMod] perf: ours: mu fix —, shroud import —, B9 —, native importers —, science scan —, " +
                                    "tracks read — (worker), animation step 3 ms, placement —", _lines[2]);
            Assert.AreEqual("[KSP1PartMod] perf: campaign load: 10.0 s from Loading to VehicleAssemblyBuilder", _lines[3]);
            StringAssert.StartsWith("[KSP1PartMod] perf: ours: mu fix —", _lines[4]);
            StringAssert.Contains("animation step —", _lines[4], "our phases are reset after each ours line");
            Assert.AreEqual(5, _lines.Count, "the rebuild line belongs to the memory moments of Ksp1Textures");
            Assert.AreEqual(42.0, LoadTimeline.MainMenuAfterSeconds);
            Assert.AreEqual(2, LoadTimeline.CampaignLoads.Count);
            // The report is rewritten after each ours line: it has this load's figures whatever the message order.
            CollectionAssert.AreEqual(new[] { "end of the campaign load", "end of the campaign load" }, _reports);
        }

        [Test] public void Rebuilds_AreResetAfterTheirLine()
        {
            Arm();
            PerfClock.NoteTicks(PerfPhase.Rebuild, PerfClock.Frequency / 100);
            LoadTimeline.WriteRebuilds("VAB entry");
            LoadTimeline.WriteRebuilds("flight entry");
            var lines = _lines.Where(l => l.Contains("rebuilds since")).ToList();
            StringAssert.StartsWith("[KSP1PartMod] perf: rebuilds since game start: 1 prefab rebuild(s) in 10 ms", lines[0]);
            StringAssert.StartsWith("[KSP1PartMod] perf: rebuilds since VAB entry: 0 prefab rebuild(s) in 0 ms", lines[1]);
            Assert.AreEqual(0, PerfClock.Clock.Get(PerfPhase.Rebuild).Calls);
            Assert.AreEqual(2, LoadTimeline.Rebuilds.Count);
        }
    }
}
