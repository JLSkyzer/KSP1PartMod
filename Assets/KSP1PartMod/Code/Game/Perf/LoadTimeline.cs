using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using KSP.Game;
using KSP.Game.Flow;
using KSP.Messages;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Perf;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Model;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Perf
{
    /// <summary>The load timeline (0.13.0, plan Task 3): times the phases of Redux's KSP1 import, our importers and our load
    /// steps, and writes one summary line per group.
    /// <list type="bullet">
    /// <item>Each Redux member of research (b) is resolved by name; a missing one logs one line and only its phase is not
    /// timed. Its patches are a <c>void</c> prefix that keeps a timestamp in <c>__state</c> (<c>Priority.First</c>) and a
    /// postfix that adds the elapsed ticks (<c>Priority.Last</c>); some phases also have a <c>void</c> finalizer that only
    /// clears a flag the prefix set, and the exception passes unchanged: no argument, result or control flow changes.</item>
    /// <item>The worker phases (configuration database, catalogs) add from Redux's <c>Task.Run</c> thread through the
    /// lock-free <see cref="PerfClock"/>, and nothing logs from there: the lines are written at <c>MarkImportComplete</c>.</item>
    /// <item>Main-thread phases count only while an import is open (<c>BeginImport</c> → <c>MarkImportComplete</c>); inside a
    /// later <c>RebuildPrefab</c> they feed the rebuild figures instead.</item>
    /// <item>A timing patch that throws stops the whole timeline for the session (<c>perf: load: stopped</c>); Redux's code
    /// runs on as is.</item>
    /// </list>
    /// Armed in <c>OnPreInitialized</c> right after the settings are read, only with <c>perf_load_timeline</c>.</summary>
    internal static class LoadTimeline
    {
        public const string HarmonyId = "KSP1PartMod.LoadTimeline";
        public const int MaxFlowSteps = 500;
        public const int MaxRebuildRecords = 100;
        private const int OverheadTimestamps = 10001;

        /// <summary>One Redux member to time: its label in the log, its phase and the patch methods.</summary>
        internal sealed class Member
        {
            public string Label;
            public PerfPhase Phase;
            public MethodBase Method;
            public string Prefix, Postfix;
            /// <summary>A <c>void</c> finalizer that only clears a flag the prefix set (the exception passes unchanged).</summary>
            public string Finalizer;
            /// <summary><see cref="Priority.Last"/> for a timing postfix (it includes our other patches' cost); the end of the
            /// import, which times nothing of its own, runs first so that every later postfix (the memory moment, the report
            /// file) already sees the import summary.</summary>
            public int PostfixPriority = HarmonyLib.Priority.Last;
            public bool Found => Method != null;
        }

        /// <summary>The members that are not a phase: the flow-step postfix, its field, the rebuilds' part name.</summary>
        internal sealed class Extras
        {
            public MethodBase OnActionResolve;
            public FieldInfo FlowStartedOnRealtime;
            public MethodInfo EntryPartName;
            public readonly List<string> Missing = new List<string>();
        }

        // ------------------------------------------------------------------ state

        /// <summary>Tests: receives every line instead of the game log.</summary>
        internal static Action<string> Sink;

        /// <summary>Rewrites the perf report (<see cref="PerfReportWriter.Write"/>; tests replace it). Called after the
        /// <c>ours</c> line, so that the report written at a VAB or flight entry has this load's figures whatever the order in
        /// which the game delivers its state message to us and to <c>Ksp1Textures</c>.</summary>
        internal static Action<string> ReportWriter = PerfReportWriter.Write;

        private static volatile bool _stopped;
        private static int _stopOnce;
        private static string _pendingStop;
        private static int _mainThread = -1;
        private static bool _armed, _wrapImporters;
        private static AccessTools.FieldRef<SequentialFlow, float> _flowStarted;
        private static MethodInfo _entryPartName;

        // The import (main thread)
        private static bool _importOpen;
        private static long _wallStart;
        private static int _frameStart, _parts;
        private static long _cfgFiles; // Interlocked: worker
        private static SlowestParts _slowest = new SlowestParts(10);

        // The part being translated (main thread)
        private static bool _inPart;
        private static long _partPrefab, _partTextures;

        // Rebuilds since the last moment (main thread)
        private static SlowestParts _rebuildSlowest = new SlowestParts(10);
        private static long _rebuildTextures, _rebuildTextureLoads, _rebuildPartPrefab, _rebuildPartTextures;
        private static string _lastMoment = "game start";

        // Game states
        private static float _loadingStart = -1f;
        private static bool _mainMenuSeen;

        // ------------------------------------------------------------------ kept for the report (Task 4)

        internal static ImportSummary LastImport { get; private set; }
        internal static OurSteps LastOurs { get; private set; }
        internal static readonly List<RebuildRecord> Rebuilds = new List<RebuildRecord>();
        internal static readonly List<FlowStep> FlowSteps = new List<FlowStep>();
        internal static readonly List<string> CampaignLoads = new List<string>();
        internal static double? MainMenuAfterSeconds { get; private set; }

        /// <summary>The timeline is armed and has not stopped.</summary>
        internal static bool IsArmed => _armed && !_stopped;

        // ------------------------------------------------------------------ resolution

        /// <summary>Every Redux member of research (b), resolved by name against the loaded game (null when missing).</summary>
        internal static List<Member> Resolve()
        {
            var database = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1FinalizedConfigDatabase");
            var discovery = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1PartModDiscovery");
            var finalizer = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ConfigFinalizer");
            var tanks = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1B9TankCatalog");
            var plumes = AccessTools.TypeByName("Redux.Ksp1Import.Model.Ksp1EnginePlumeCatalog");
            var localization = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1LocalizationCatalog");
            var registry = AccessTools.TypeByName("Redux.Ksp1Import.Assets.Ksp1ImportedPartRegistry");
            var translator = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1PartTranslator");
            var importers = AccessTools.TypeByName("Redux.Ksp1Import.Modules.Ksp1PartModuleImporterRegistry");
            var model = AccessTools.TypeByName("Redux.Ksp1Import.Model.Ksp1ModelImporter");
            var mu = AccessTools.TypeByName("Redux.Ksp1Import.Model.Ksp1MuReader");
            var loader = AccessTools.TypeByName("Redux.Ksp1Import.Model.Ksp1TextureLoader");
            var report = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ImportReport");
            var builder = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1PartLocalizationBuilder");
            var flowAction = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ImportPartsFlowAction");

            var roots = typeof(IEnumerable<Ksp1ModRoot>);
            var files = typeof(IEnumerable<Ksp1ConfigFile>);
            var module = typeof(SerializedPartModule).MakeByRefType();
            const string begin = nameof(Begin);
            return new List<Member>
            {
                // BuildCacheKey is private: it is the directory walk behind GetCacheKey and both Get calls.
                M("Ksp1FinalizedConfigDatabase.BuildCacheKey(IEnumerable<Ksp1ModRoot>)", PerfPhase.CacheKey, database, "BuildCacheKey",
                    new[] { roots }, begin, nameof(EndCacheKey)),
                M("Ksp1FinalizedConfigDatabase.Get(Ksp1ImportReport, IReadOnlyList<Ksp1ModRoot>)", PerfPhase.ConfigGet, database, "Get",
                    new[] { typeof(Ksp1ImportReport), typeof(IReadOnlyList<Ksp1ModRoot>) }, begin, nameof(EndConfigGet)),
                M("Ksp1PartModDiscovery.LoadConfigFiles(Ksp1ImportReport, bool, IEnumerable<Ksp1ModRoot>)", PerfPhase.CfgRead, discovery,
                    "LoadConfigFiles", new[] { typeof(Ksp1ImportReport), typeof(bool), roots }, begin, nameof(EndCfgRead)),
                M("Ksp1ConfigFinalizer.Finalize(IReadOnlyList<Ksp1ConfigFile>, Ksp1ImportReport)", PerfPhase.MmFinalize, finalizer,
                    "Finalize", new[] { typeof(IReadOnlyList<Ksp1ConfigFile>), typeof(Ksp1ImportReport) }, begin, nameof(EndMmFinalize)),
                M("Ksp1B9TankCatalog.FromConfigFiles", PerfPhase.TankCatalog, tanks, "FromConfigFiles", new[] { files }, begin,
                    nameof(EndTankCatalog)),
                M("Ksp1EnginePlumeCatalog.FromConfigFiles", PerfPhase.PlumeCatalog, plumes, "FromConfigFiles", new[] { files }, begin,
                    nameof(EndPlumeCatalog)),
                M("Ksp1LocalizationCatalog.FromFiles", PerfPhase.LocalizationCatalog, localization, "FromFiles", new[] { files }, begin,
                    nameof(EndLocalizationCatalog)),
                M("Ksp1ImportedPartRegistry.BeginImport(string)", PerfPhase.ImportWall, registry, "BeginImport", new[] { typeof(string) },
                    nameof(BeforeBeginImport), null),
                M("Ksp1ImportedPartRegistry.MarkImportComplete(Ksp1EnginePlumeCatalog)", PerfPhase.ImportWall, registry,
                    "MarkImportComplete", new[] { typeof(Ksp1EnginePlumeCatalog) }, null, nameof(AfterMarkImportComplete),
                    postfixPriority: Priority.First),
                M("Ksp1PartTranslator.TryTranslatePart", PerfPhase.Translate, translator, "TryTranslatePart",
                    new[] { typeof(Ksp1ConfigFile), typeof(Ksp1ConfigNode), typeof(Ksp1ImportReport), typeof(PartCore).MakeByRefType(),
                        typeof(string).MakeByRefType() }, nameof(BeforeTranslate), nameof(AfterTranslate), nameof(TranslateDone)),
                M("Ksp1PartModuleImporterRegistry.TryImport", PerfPhase.ReduxImporters, importers, "TryImport",
                    new[] { typeof(Ksp1ConfigNode), typeof(Ksp1PartImportContext), module }, begin, nameof(EndReduxImporters)),
                M("Ksp1PartModuleImporterRegistry.TryImportEngineModes", PerfPhase.ReduxImporters, importers, "TryImportEngineModes",
                    new[] { typeof(IReadOnlyList<Ksp1ConfigNode>), typeof(Ksp1PartImportContext), module }, begin, nameof(EndReduxImporters)),
                M("Ksp1ModelImporter.BuildPrefab", PerfPhase.Prefab, model, "BuildPrefab",
                    new[] { typeof(Ksp1ConfigFile), typeof(Ksp1ConfigNode), typeof(PartCore), typeof(Ksp1ImportReport) }, begin, nameof(EndPrefab)),
                M("Ksp1MuReader.Read(string)", PerfPhase.MuRead, mu, "Read", new[] { typeof(string) }, begin, nameof(EndMuRead)),
                M("Ksp1TextureLoader.LoadTexture", PerfPhase.Textures, loader, "LoadTexture",
                    new[] { typeof(string), typeof(string), typeof(string), typeof(Ksp1TextureLoader.TextureUsage) }, begin, nameof(EndTextures)),
                M("Ksp1ImportReport.Info", PerfPhase.ReportLines, report, "Info", new[] { typeof(string) }, begin, nameof(EndReportLine)),
                M("Ksp1ImportReport.Important", PerfPhase.ReportLines, report, "Important", new[] { typeof(string) }, begin, nameof(EndReportLine)),
                M("Ksp1ImportReport.Warn", PerfPhase.ReportLines, report, "Warn", new[] { typeof(string) }, begin, nameof(EndReportWarning)),
                M("Ksp1ImportReport.Error", PerfPhase.ReportLines, report, "Error", new[] { typeof(string), typeof(Exception) }, begin,
                    nameof(EndReportWarning)),
                M("Ksp1PartLocalizationBuilder.Register", PerfPhase.LocalizationRegister, builder, "Register", Type.EmptyTypes, begin,
                    nameof(EndLocalizationRegister)),
                M("Ksp1ImportedPartRegistry.GenerateMissingIcons", PerfPhase.Icons, registry, "GenerateMissingIcons", Type.EmptyTypes, begin,
                    nameof(EndIcons)),
                M("Ksp1ImportedPartRegistry.ReleaseResidentPrefabs", PerfPhase.Release, registry, "ReleaseResidentPrefabs", Type.EmptyTypes,
                    begin, nameof(EndRelease)),
                M("Ksp1ImportedPartRegistry.RebuildPrefab(Entry)", PerfPhase.Rebuild, registry, "RebuildPrefab",
                    new[] { typeof(Ksp1ImportedPartRegistry.Entry) }, nameof(BeforeRebuild), nameof(AfterRebuild), nameof(RebuildDone)),
                M("Ksp1ImportPartsFlowAction.RestoreCachedImport(PartProvider)", PerfPhase.ImportWall, flowAction, "RestoreCachedImport",
                    new[] { typeof(PartProvider) }, begin, nameof(AfterRestoreCachedImport)),
            };
        }

        private static Member M(string label, PerfPhase phase, Type owner, string name, Type[] args, string prefix, string postfix,
            string finalizer = null, int postfixPriority = Priority.Last) =>
            new Member
            {
                Label = label,
                Phase = phase,
                Method = owner == null ? null : AccessTools.DeclaredMethod(owner, name, args),
                Prefix = prefix,
                Postfix = postfix,
                Finalizer = finalizer,
                PostfixPriority = postfixPriority,
            };

        /// <summary>The flow-step members and the rebuild part name.</summary>
        internal static Extras ResolveExtras()
        {
            var x = new Extras();
            var flow = typeof(SequentialFlow);
            x.OnActionResolve = AccessTools.DeclaredMethod(flow, "OnActionResolve", new[] { typeof(FlowAction) });
            if (x.OnActionResolve == null) x.Missing.Add("SequentialFlow.OnActionResolve(FlowAction)");
            x.FlowStartedOnRealtime = AccessTools.DeclaredField(flow, "_flowStartedOnRealtime");
            if (x.FlowStartedOnRealtime == null || x.FlowStartedOnRealtime.IsStatic || x.FlowStartedOnRealtime.FieldType != typeof(float))
            {
                x.FlowStartedOnRealtime = null;
                x.Missing.Add("SequentialFlow._flowStartedOnRealtime");
            }
            x.EntryPartName = AccessTools.PropertyGetter(typeof(Ksp1ImportedPartRegistry.Entry), "PartName");
            if (x.EntryPartName == null || x.EntryPartName.ReturnType != typeof(string))
            {
                x.EntryPartName = null;
                x.Missing.Add("Ksp1ImportedPartRegistry.Entry.PartName");
            }
            return x;
        }

        // ------------------------------------------------------------------ arming

        /// <summary>Writes the <c>perf: armed</c> line and, with <c>perf_load_timeline</c>, patches every member found.</summary>
        public static void Arm(Harmony harmony, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            var overheadNs = 0.0;
            try
            {
                // The mean, not the median: a timestamp costs less than one tick of a 10 MHz counter, so the median reads 0.
                var ticks = TimerOverhead.Mean(Stopwatch.GetTimestamp, OverheadTimestamps);
                overheadNs = ticks * 1e9 / Stopwatch.Frequency;
            }
            catch (Exception)
            {
                // The estimate is information only.
            }

            var members = Resolve();
            if (!settings.PerfLoadTimeline)
            {
                Info(PerfLines.Armed(false, 0, members.Count, null, settings.PerfMemoryLines, settings.PerfReportFile, settings.PerfCounter,
                    settings.PerfWindowSeconds, overheadNs));
                return;
            }

            _slowest = new SlowestParts(Math.Max(1, settings.PerfTopN));
            _rebuildSlowest = new SlowestParts(Math.Max(1, settings.PerfTopN));
            var notFound = new List<string>();
            var timed = 0;
            foreach (var m in members)
            {
                if (!m.Found)
                {
                    notFound.Add(m.Label);
                    Warn(PerfLines.MemberNotFound(m.Label, m.Phase));
                    continue;
                }
                try
                {
                    harmony.Patch(m.Method,
                        prefix: m.Prefix == null ? null : new HarmonyMethod(typeof(LoadTimeline), m.Prefix) { priority = Priority.First },
                        postfix: m.Postfix == null ? null : new HarmonyMethod(typeof(LoadTimeline), m.Postfix) { priority = m.PostfixPriority },
                        finalizer: m.Finalizer == null ? null : new HarmonyMethod(typeof(LoadTimeline), m.Finalizer));
                    timed++;
                }
                catch (Exception e)
                {
                    notFound.Add(m.Label);
                    Warn(PerfLines.MemberNotFound(m.Label + " (patch failed: " + e.GetType().Name + ": " + e.Message + ")", m.Phase));
                }
            }

            var extras = ResolveExtras();
            foreach (var missing in extras.Missing)
            {
                notFound.Add(missing);
                Warn(PerfLines.MemberNotFound(missing, missing.Contains("Entry") ? "rebuild part names" : "flow steps"));
            }
            _entryPartName = extras.EntryPartName;
            if (extras.OnActionResolve != null && extras.FlowStartedOnRealtime != null)
            {
                try
                {
                    _flowStarted = AccessTools.FieldRefAccess<SequentialFlow, float>(extras.FlowStartedOnRealtime);
                    harmony.Patch(extras.OnActionResolve,
                        postfix: new HarmonyMethod(typeof(LoadTimeline), nameof(AfterFlowResolve)) { priority = Priority.Last });
                }
                catch (Exception e)
                {
                    _flowStarted = null;
                    notFound.Add("SequentialFlow.OnActionResolve");
                    Warn(PerfLines.MemberNotFound("SequentialFlow.OnActionResolve (patch failed: " + e.GetType().Name + ": " + e.Message + ")",
                        "flow steps"));
                }
            }

            _armed = true;
            _wrapImporters = true;
            PerfClock.Enabled = true;
            Info(PerfLines.Armed(true, timed, members.Count, notFound, settings.PerfMemoryLines, settings.PerfReportFile, settings.PerfCounter,
                settings.PerfWindowSeconds, overheadNs));
        }

        /// <summary>Our importer, timed while the timeline is armed (<see cref="TimedImporter"/>); otherwise itself.</summary>
        internal static IKsp1PartModuleImporter Wrap(IKsp1PartModuleImporter inner) =>
            _wrapImporters && !_stopped && inner != null ? new TimedImporter(inner) : inner;

        /// <summary>Back to the state before any arming (tests).</summary>
        internal static void Reset()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            PerfClock.ResetForTests();
            _stopped = false;
            _stopOnce = 0;
            _pendingStop = null;
            _armed = _wrapImporters = false;
            _flowStarted = null;
            _entryPartName = null;
            _importOpen = _inPart = false;
            _parts = 0;
            _cfgFiles = 0;
            _partPrefab = _partTextures = 0;
            _slowest = new SlowestParts(10);
            _rebuildSlowest = new SlowestParts(10);
            _rebuildTextures = _rebuildTextureLoads = _rebuildPartPrefab = _rebuildPartTextures = 0;
            _lastMoment = "game start";
            _loadingStart = -1f;
            _mainMenuSeen = false;
            LastImport = null;
            LastOurs = null;
            MainMenuAfterSeconds = null;
            Rebuilds.Clear();
            FlowSteps.Clear();
            CampaignLoads.Clear();
        }

        /// <summary>Tests: the parts kept as the slowest of the open import.</summary>
        internal static SlowPart[] SlowestOfImport => _slowest.ToArray();

        // ------------------------------------------------------------------ patches: generic

        private static void Begin(out long __state)
        {
            __state = _stopped ? 0L : Stopwatch.GetTimestamp();
        }

        private static void EndCacheKey(long __state) => Worker(PerfPhase.CacheKey, __state);
        private static void EndConfigGet(long __state) => Worker(PerfPhase.ConfigGet, __state);
        private static void EndMmFinalize(long __state) => Worker(PerfPhase.MmFinalize, __state);
        private static void EndTankCatalog(long __state) => Worker(PerfPhase.TankCatalog, __state);
        private static void EndPlumeCatalog(long __state) => Worker(PerfPhase.PlumeCatalog, __state);
        private static void EndLocalizationCatalog(long __state) => Worker(PerfPhase.LocalizationCatalog, __state);

        private static void EndCfgRead(long __state, object __result)
        {
            if (__state == 0L || _stopped) return;
            try
            {
                Worker(PerfPhase.CfgRead, __state);
                if (!PerfClock.InRebuild && __result is ICollection files) Interlocked.Add(ref _cfgFiles, files.Count);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void EndReportLine(long __state) => Report(__state, false);
        private static void EndReportWarning(long __state) => Report(__state, true);

        private static void EndReduxImporters(long __state) => Main(PerfPhase.ReduxImporters, __state);
        private static void EndPrefab(long __state) => Main(PerfPhase.Prefab, __state);
        private static void EndMuRead(long __state) => Main(PerfPhase.MuRead, __state);
        /// <summary>In a rebuild, a load is counted only when Redux's loader ran (<c>__runOriginal</c>): a hit of our shared
        /// texture cache (<c>Ksp1Textures</c> prefix) is timed but is not a load.</summary>
        private static void EndTextures(long __state, bool __runOriginal) => Main(PerfPhase.Textures, __state, __runOriginal);
        private static void EndLocalizationRegister(long __state) => Main(PerfPhase.LocalizationRegister, __state);
        private static void EndIcons(long __state) => Main(PerfPhase.Icons, __state);
        private static void EndRelease(long __state) => Main(PerfPhase.Release, __state);

        /// <summary>A worker (or either-thread) phase: added whenever it runs, except inside a rebuild.</summary>
        private static void Worker(PerfPhase phase, long state)
        {
            if (state == 0L || _stopped) return;
            try
            {
                PerfClock.NoteTicks(phase, Stopwatch.GetTimestamp() - state);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void Report(long state, bool warning)
        {
            if (state == 0L || _stopped) return;
            try
            {
                var ticks = Stopwatch.GetTimestamp() - state;
                PerfClock.NoteTicks(PerfPhase.ReportLines, ticks);
                if (warning) PerfClock.NoteTicks(PerfPhase.ReportWarnings, ticks);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        /// <summary>A main-thread phase: inside a rebuild it feeds the rebuild figures; otherwise it counts only while an import
        /// is open, and the prefab and texture shares also go to the part being translated.</summary>
        private static void Main(PerfPhase phase, long state, bool ranOriginal = true)
        {
            if (state == 0L || _stopped) return;
            try
            {
                var ticks = Stopwatch.GetTimestamp() - state;
                if (PerfClock.InRebuild)
                {
                    if (phase == PerfPhase.Textures)
                    {
                        _rebuildTextures += ticks;
                        if (ranOriginal) _rebuildTextureLoads++;
                        _rebuildPartTextures += ticks;
                    }
                    else if (phase == PerfPhase.Prefab) _rebuildPartPrefab += ticks;
                    return;
                }
                if (!_importOpen) return;
                PerfClock.NoteTicks(phase, ticks);
                if (!_inPart) return;
                if (phase == PerfPhase.Prefab) _partPrefab += ticks;
                else if (phase == PerfPhase.Textures) _partTextures += ticks;
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        // ------------------------------------------------------------------ patches: the import

        private static void BeforeBeginImport()
        {
            FlushStop();
            if (_stopped) return;
            try
            {
                _importOpen = true;
                _wallStart = Stopwatch.GetTimestamp();
                _frameStart = Time.frameCount;
                _parts = 0;
                _inPart = false;
                _slowest.Clear();
                PerfClock.InRebuild = false;
                // An import that never completed must not leak into this one; the worker phases (the cache key runs before
                // BeginImport) are kept.
                PerfClock.Clock.Reset(MainImportPhases);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        /// <summary>Redux's main-thread import phases, zeroed at each <c>BeginImport</c>.</summary>
        private static readonly PerfPhase[] MainImportPhases =
        {
            PerfPhase.Translate, PerfPhase.ReduxImporters, PerfPhase.Prefab, PerfPhase.MuRead, PerfPhase.Textures,
            PerfPhase.LocalizationRegister, PerfPhase.Icons, PerfPhase.Release,
        };

        /// <summary>Tests: the part flag of <c>TryTranslatePart</c>.</summary>
        internal static bool InPart => _inPart;

        /// <summary>Runs even when <c>TryTranslatePart</c> throws (postfixes do not): the part flag never stays set.</summary>
        private static void TranslateDone() => _inPart = false;

        /// <summary>Runs even when <c>RebuildPrefab</c> throws: the rebuild flag never stays set.</summary>
        private static void RebuildDone() => PerfClock.InRebuild = false;

        private static void BeforeTranslate(out long __state)
        {
            __state = 0L;
            if (_stopped) return;
            _inPart = true;
            _partPrefab = _partTextures = 0;
            __state = Stopwatch.GetTimestamp();
        }

        private static void AfterTranslate(long __state, bool __result, Ksp1ConfigNode __1)
        {
            _inPart = false;
            if (__state == 0L || _stopped) return;
            try
            {
                var ticks = Stopwatch.GetTimestamp() - __state;
                if (!_importOpen || PerfClock.InRebuild) return;
                PerfClock.NoteTicks(PerfPhase.Translate, ticks);
                if (__result) _parts++;
                if (_slowest.WouldEnter(ticks)) _slowest.Offer(__1?.GetValue("name") ?? "?", ticks, _partPrefab, _partTextures);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void AfterMarkImportComplete()
        {
            FlushStop();
            if (_stopped || !_importOpen) return;
            try
            {
                _importOpen = false;
                var wall = Stopwatch.GetTimestamp() - _wallStart;
                var frames = Math.Max(0, Time.frameCount - _frameStart);
                var summary = ImportSummary.From(PerfClock.Clock.Snapshot(), Stopwatch.Frequency, _parts, wall, frames,
                    (int)Math.Min(int.MaxValue, Interlocked.Read(ref _cfgFiles)));
                summary.Slowest = _slowest.ToArray();
                Info(PerfLines.ImportWorker(summary));
                Info(PerfLines.Import(summary));
                Info(PerfLines.ReportLines(summary));
                Info(PerfLines.SlowestImportParts(summary.Slowest, Stopwatch.Frequency));
                LastImport = summary;
                EndOfImport();
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static void AfterRestoreCachedImport(long __state)
        {
            FlushStop();
            if (__state == 0L || _stopped) return;
            try
            {
                var ms = PhaseClock.Ms(Stopwatch.GetTimestamp() - __state, Stopwatch.Frequency);
                var parts = Ksp1ImportedPartRegistry.Count;
                Info(PerfLines.Reused(parts, ms));
                LastImport = new ImportSummary { Reused = true, Parts = parts, Frequency = Stopwatch.Frequency };
                EndOfImport();
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        /// <summary>The import's phases start again from zero (our steps are read later, by the ours line).</summary>
        private static void EndOfImport()
        {
            PerfClock.Clock.Reset(PhaseClock.ImportPhases);
            Interlocked.Exchange(ref _cfgFiles, 0);
            _slowest.Clear();
            _parts = 0;
            _lastMoment = "end of import";
        }

        // ------------------------------------------------------------------ patches: rebuilds

        private static void BeforeRebuild(out long __state)
        {
            __state = 0L;
            if (_stopped) return;
            PerfClock.InRebuild = true;
            _rebuildPartPrefab = _rebuildPartTextures = 0;
            __state = Stopwatch.GetTimestamp();
        }

        private static void AfterRebuild(long __state, object __0)
        {
            PerfClock.InRebuild = false;
            if (__state == 0L || _stopped) return;
            try
            {
                var ticks = Stopwatch.GetTimestamp() - __state;
                PerfClock.NoteTicks(PerfPhase.Rebuild, ticks);
                if (_rebuildSlowest.WouldEnter(ticks))
                    _rebuildSlowest.Offer(PartNameOf(__0), ticks, _rebuildPartPrefab, _rebuildPartTextures);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        private static string PartNameOf(object entry)
        {
            if (entry == null || _entryPartName == null) return "?";
            try
            {
                return _entryPartName.Invoke(entry, null) as string ?? "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>The <c>perf: rebuilds since</c> line for the rebuilds since the previous moment, then their reset. Called
        /// once per VAB or flight memory moment (entry and entry + 5 s), from <c>Ksp1Textures</c>.</summary>
        internal static void WriteRebuilds(string moment)
        {
            if (!IsArmed) return;
            try
            {
                var totals = PerfClock.Clock.Get(PerfPhase.Rebuild);
                var record = new RebuildRecord
                {
                    Since = _lastMoment,
                    Until = moment,
                    Count = totals.Calls,
                    Ms = PhaseClock.Ms(totals.Ticks, Stopwatch.Frequency),
                    TexturesMs = PhaseClock.Ms(_rebuildTextures, Stopwatch.Frequency),
                    TextureLoads = _rebuildTextureLoads,
                    Slowest = _rebuildSlowest.ToArray(),
                    Frequency = Stopwatch.Frequency,
                };
                Info(PerfLines.Rebuilds(record));
                if (Rebuilds.Count >= MaxRebuildRecords) Rebuilds.RemoveAt(0);
                Rebuilds.Add(record);
                PerfClock.Clock.Reset(new[] { PerfPhase.Rebuild });
                _rebuildSlowest.Clear();
                _rebuildTextures = _rebuildTextureLoads = 0;
                _lastMoment = moment;
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        // ------------------------------------------------------------------ flow steps and game states

        private static void AfterFlowResolve(SequentialFlow __instance, FlowAction __0)
        {
            if (_stopped || __instance == null || _flowStarted == null) return;
            try
            {
                var started = _flowStarted(__instance);
                if (started < 0f || FlowSteps.Count >= MaxFlowSteps) return;
                var seconds = Time.realtimeSinceStartup - started;
                // The name is built only for a kept step (slow) or for one that may name KSP1: FlowAction.Name is a field read.
                var raw = seconds >= 0.1f ? null : __0?.Name;
                if (seconds < 0.1f && (raw == null || raw.IndexOf("KSP1", StringComparison.Ordinal) < 0)) return;
                FlowSteps.Add(new FlowStep { Name = __0?.ToString() ?? "?", Seconds = seconds });
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        /// <summary>Campaign load (<c>Loading</c> → the next playable state) and the first main menu. The rebuild line belongs to
        /// the VAB and flight memory moments of <c>Ksp1Textures</c> (<see cref="WriteRebuilds"/>). Same pattern as
        /// <c>Ksp1Textures.SubscribeToGameStates</c>.</summary>
        public static void SubscribeToGameStates(GameInstance game)
        {
            if (!IsArmed) return;
            try
            {
                if (game?.Messages == null)
                {
                    Warn(PerfLines.Prefix + "load: no message center, no campaign load, main menu or rebuild line");
                    return;
                }
                game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameState);
            }
            catch (Exception e)
            {
                Warn(PerfLines.Prefix + "load: no campaign load, main menu or rebuild line: " + e);
            }
        }

        private static void OnGameState(MessageCenterMessage message)
        {
            FlushStop();
            if (_stopped) return;
            try
            {
                if (!(message is GameStateChangedMessage m) || m.CurrentState == m.PreviousState) return;
                OnState(m.CurrentState, m.PreviousState, Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Stop(e);
            }
        }

        /// <summary>The game-state rules, with the time passed in (tests).</summary>
        internal static void OnState(GameState current, GameState previous, float now)
        {
            if (current == GameState.MainMenu)
            {
                _loadingStart = -1f;
                if (!_mainMenuSeen)
                {
                    _mainMenuSeen = true;
                    MainMenuAfterSeconds = now;
                    Info(PerfLines.GameStart(now));
                }
                return;
            }
            if (current == GameState.Loading)
            {
                if (_loadingStart < 0f) _loadingStart = now;
                return;
            }
            if (_loadingStart >= 0f && (current == GameState.KerbalSpaceCenter || current == GameState.VehicleAssemblyBuilder ||
                                        current == GameState.FlightView))
            {
                var seconds = now - _loadingStart;
                _loadingStart = -1f;
                Info(PerfLines.CampaignLoad(seconds, current.ToString()));
                CampaignLoads.Add(PerfLines.S(seconds) + " s to " + current);
                WriteOurs();
            }
        }

        /// <summary>The <c>perf: ours:</c> line, once the campaign load is over (all our steps have run: the tech-tree placement
        /// comes after the animation step), then the reset of our phases.</summary>
        internal static void WriteOurs()
        {
            if (!IsArmed) return;
            var ours = OurSteps.From(PerfClock.Clock.Snapshot(), Stopwatch.Frequency);
            Info(PerfLines.Ours(ours));
            LastOurs = ours;
            PerfClock.Clock.Reset(PhaseClock.OurPhases);
            try
            {
                ReportWriter?.Invoke("end of the campaign load");
            }
            catch (Exception)
            {
                // Write never throws; a replaced writer must not stop the timeline either.
            }
        }

        // ------------------------------------------------------------------ stop and log

        /// <summary>A timing patch threw: the whole timeline stops for the session. From the worker, the line waits for the
        /// next main-thread patch.</summary>
        private static void Stop(Exception e)
        {
            _stopped = true;
            PerfClock.Enabled = false;
            if (Interlocked.Exchange(ref _stopOnce, 1) == 1) return;
            var text = e?.ToString() ?? "?";
            if (Thread.CurrentThread.ManagedThreadId == _mainThread) Error(PerfLines.LoadStopped(text));
            else Interlocked.Exchange(ref _pendingStop, text);
        }

        private static void FlushStop()
        {
            var text = Interlocked.Exchange(ref _pendingStop, null);
            if (text != null) Error(PerfLines.LoadStopped(text));
        }

        private static void Info(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        private static void Warn(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogWarning(line);
        }

        private static void Error(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogError(line);
        }
    }
}
