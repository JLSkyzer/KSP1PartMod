using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KSP.Game;
using KSP.Messages;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Audit;
using KSP1PartMod.Game.Perf;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game.Audit
{
    /// <summary>The opt-in audit (<c>audit_on_vab_entry</c>): at the VAB entry, every KSP1 part is spawned once on a
    /// detached copy (<see cref="AuditSpawner"/>), within a frame budget, and a report is written to <c>&lt;mod&gt;/audit/</c>.
    /// Once per game session; leaving the VAB cancels, and the next VAB entry resumes. A breadcrumb (<c>current.txt</c>)
    /// names the part being spawned, so a part that crashed the game is skipped at the next run (<c>skip.txt</c>).
    /// Read-only towards the game: the player's craft, workspace, undo history, staging and the tracker's lists are never
    /// touched. Every path catches and logs; an unexpected error stops the audit, never the game.</summary>
    internal static class AuditRunner
    {
        private const string P = "[KSP1PartMod] audit: ";
        public const string Folder = "audit";
        public const string BreadcrumbFile = "current.txt";
        public const string SkipFile = "skip.txt";
        public const int ControlCount = 5;
        private const float SettleSeconds = 5f;
        private const float LoadTimeoutSeconds = 30f;

        private static AuditTargets _targets;
        private static AnimationSettings _settings;
        private static bool _armed, _running, _cancel, _done, _inSpawn;
        // Bumped at each start and at each cancel: a coroutine from an older VAB visit stops at its next step.
        private static int _generation;

        // The session's report: one CSV for the session, resumed after a cancel.
        private static string _stamp, _csvPath;
        private static readonly List<AuditRecord> Records = new List<AuditRecord>();
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Stopwatch SessionClock = new Stopwatch();

        private static string Dir => Path.Combine(KSP1PartModPlugin.ModFolder, Folder);

        /// <summary>From <see cref="KSP1PartModPlugin.OnInitialized"/>. With the switch off, one line and nothing else.</summary>
        public static void Arm(GameInstance game, AnimationSettings settings)
        {
            var log = KSP1PartModPlugin.Log;
            if (!settings.AuditOnVabEntry)
            {
                log.LogInfo(P + "off (audit_on_vab_entry)");
                return;
            }
            _settings = settings;
            _targets = AuditTargets.Resolve(out var missing);
            if (_targets == null)
            {
                log.LogWarning(P + missing + " not found, audit off");
                return;
            }
            if (game?.Messages == null)
            {
                log.LogWarning(P + "GameInstance.Messages not found, audit off");
                return;
            }
            game.Messages.PersistentSubscribe<OABLoadFinalizedMessage>(OnOabLoadFinalized);
            game.Messages.PersistentSubscribe<OABUnloadedMessage>(OnOabUnloaded);
            game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameState);
            _armed = true;
            log.LogInfo(P + $"armed, runs at the next VAB entry (budget {Num(settings.AuditFrameBudgetMs)} ms per frame, " +
                        $"unload every {settings.AuditUnloadEvery} part(s))");
        }

        // ---- messages ----

        private static void OnOabLoadFinalized(MessageCenterMessage message)
        {
            try
            {
                if (!_armed || _done || _running) return;
                _running = true;
                _cancel = false;
                var generation = ++_generation;
                if (TechTreeLoadStep.Runner().StartCoroutine(Guarded(Run(generation), generation)) == null) _running = false;
            }
            catch (Exception e)
            {
                _running = false;
                KSP1PartModPlugin.Log?.LogError(P + "could not start: " + e);
            }
        }

        private static void OnOabUnloaded(MessageCenterMessage message) => Leave();

        private static void OnGameState(MessageCenterMessage message)
        {
            if (message is GameStateChangedMessage m && m.CurrentState != GameState.VehicleAssemblyBuilder) Leave();
        }

        /// <summary>Leaving the VAB cancels at once (report closed, flag cleared), so a quick re-entry resumes; the old
        /// coroutine sees a newer generation and stops without touching anything. During a spawn (a message published
        /// synchronously from the game's code) the coroutine cancels itself right after the part.</summary>
        private static void Leave()
        {
            try
            {
                if (!_running) return;
                if (_inSpawn)
                {
                    _cancel = true;
                    return;
                }
                _generation++;
                _running = false;
                if (_run != null) Cancel(_run);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(P + "cancel failed: " + e);
            }
        }

        private static bool Stale(int generation) => generation != _generation;

        /// <summary>An exception out of the audit stops it with one line; the files are closed.</summary>
        private static IEnumerator Guarded(IEnumerator inner, int generation)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    KSP1PartModPlugin.Log?.LogError(P + "stopped by an error (the next VAB entry starts again): " + e);
                    if (!Stale(generation))
                    {
                        FinishAfterError();
                        _run?.Close();
                        _run = null;
                        _running = false;
                    }
                    yield break;
                }
                yield return current;
            }
            if (!Stale(generation)) _running = false;
        }

        /// <summary>After an error: the session clock stops and the partial summary is written, as a cancel does.</summary>
        private static void FinishAfterError()
        {
            try
            {
                SessionClock.Stop();
                WriteMarkdown(AuditSummary.From(Records, SessionClock.Elapsed.TotalSeconds));
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(P + "partial summary not written: " + e.Message);
            }
        }

        // ---- the run ----

        private static RunState _run;

        private sealed class RunState
        {
            public ObjectAssemblyBuilder Builder;
            public AuditSpawner Spawner;
            public AuditPacer Pacer;
            public List<AuditTarget> Pending;
            public Dictionary<string, PartCore> Cores;
            public Dictionary<string, Ksp1ImportedPartRegistry.Entry> Entries;
            public StreamWriter Csv;
            public List<string> TextureKeysAtStart;
            public EventCounts Events;
            public MessageCounts Messages;
            public int Total;
            public int Processed, Problems;

            public void Close()
            {
                try
                {
                    Csv?.Dispose();
                }
                catch (Exception)
                {
                    // Already closed.
                }
                Csv = null;
                TryDelete(Path.Combine(Dir, BreadcrumbFile));
            }
        }

        private static IEnumerator Run(int gen)
        {
            var log = KSP1PartModPlugin.Log;
            var settle = Stopwatch.StartNew();
            while (settle.Elapsed.TotalSeconds < SettleSeconds)
            {
                if (Stale(gen)) yield break;
                yield return null;
            }
            if (!Ready(out var builder, out var why))
            {
                log.LogInfo(P + "not started at this VAB entry: " + why);
                yield break;
            }
            var run = Setup(builder);
            if (run == null) yield break;
            _run = run;
            SessionClock.Start();
            var frame = Stopwatch.StartNew();
            var inFrame = 0;

            // Controls: loaded first (awaited), then spawned together in one frame. If the address lookup itself
            // throws, one line is logged and the audit runs without them (never stopped at each VAB entry).
            var controls = run.Pending.Where(t => t.IsControl).ToList();
            var loaded = new Dictionary<string, LoadResult>(StringComparer.Ordinal);
            try
            {
                foreach (var c in controls)
                    if (!run.Spawner.HasTemplate(run.Cores[c.Name])) loaded[c.Name] = Load(run.Spawner.AsyncAddress(run.Cores[c.Name], true));
            }
            catch (Exception e)
            {
                log.LogError(P + "control parts skipped, the address lookup failed: " + e.Message);
                loaded.Clear();
                run.Pending = run.Pending.Where(t => !t.IsControl).ToList();
                run.Total = run.Pending.Count;
            }
            var total = run.Total;
            var wait = Stopwatch.StartNew();
            while (loaded.Values.Any(l => !l.Done) && wait.Elapsed.TotalSeconds < LoadTimeoutSeconds)
            {
                if (Stale(gen)) yield break;
                yield return null;
            }
            frame.Restart();

            foreach (var target in run.Pending)
            {
                if (Stopping(gen, run)) yield break;
                var core = run.Cores[target.Name];
                GameObject prefab = null;
                string loadError = null;
                if (target.IsControl)
                {
                    if (loaded.TryGetValue(target.Name, out var l)) l.Take(out prefab, out loadError);
                }
                else
                {
                    if (!run.Pacer.ShouldContinue(frame.Elapsed.TotalMilliseconds, inFrame))
                    {
                        yield return null;
                        frame.Restart();
                        inFrame = 0;
                        if (Stopping(gen, run)) yield break;
                    }
                    string address = null;
                    try
                    {
                        if (!run.Spawner.HasTemplate(core)) address = run.Spawner.AsyncAddress(core, false);
                    }
                    catch (Exception e)
                    {
                        loadError = "prefab address: " + e.Message;
                    }
                    if (address != null)
                    {
                        // A Patch Manager remap: loaded as the VAB does (handle never released, as in the VAB).
                        var load = Load(address);
                        wait.Restart();
                        while (!load.Done && wait.Elapsed.TotalSeconds < LoadTimeoutSeconds)
                        {
                            if (Stale(gen) || _cancel) break;
                            yield return null;
                        }
                        if (Stopping(gen, run)) yield break;
                        load.Take(out prefab, out loadError);
                        frame.Restart();
                        inFrame = 0;
                    }
                }

                ProcessOne(run, target, core, prefab, loadError);
                inFrame++;

                if (run.Pacer.ShouldLogProgress(run.Processed))
                {
                    log.LogInfo(AuditSummary.ProgressLine(run.Processed, total, run.Problems, SessionClock.Elapsed.TotalSeconds));
                    Ksp1Textures.ReportAt($"audit {run.Processed}/{total}");
                }
                if (run.Pacer.ShouldUnload(run.Processed) && run.Processed < total)
                {
                    var unload = Stopwatch.StartNew();
                    var evicted = Evict(run);
                    var op = Resources.UnloadUnusedAssets();
                    while (op != null && !op.isDone)
                    {
                        if (Stale(gen)) yield break;
                        yield return null;
                    }
                    log.LogInfo(P + $"unloaded unused assets after {run.Processed} part(s): {Entries(evicted)} evicted, " +
                                $"{unload.ElapsedMilliseconds} ms");
                    frame.Restart();
                    inFrame = 0;
                }
            }

            // End: the audit's cache entries, then what nothing uses any more.
            {
                var unload = Stopwatch.StartNew();
                var evicted = Evict(run);
                var op = Resources.UnloadUnusedAssets();
                while (op != null && !op.isDone)
                {
                    if (Stale(gen)) yield break;
                    yield return null;
                }
                log.LogInfo(P + $"unloaded unused assets after {run.Processed} part(s): {Entries(evicted)} evicted, {unload.ElapsedMilliseconds} ms");
            }
            if (Stale(gen)) yield break;
            SessionClock.Stop();
            Ksp1Textures.ReportAt("audit end");
            var summary = AuditSummary.From(Records, SessionClock.Elapsed.TotalSeconds);
            log.LogInfo(summary.DoneLine(_csvPath, SkipNotePath()));
            log.LogInfo(summary.ByKindLine());
            log.LogInfo(summary.ByModLine());
            log.LogInfo(summary.ByModuleLine());
            log.LogInfo(summary.SlowestLine());
            log.LogInfo(summary.PodsLine());
            log.LogInfo(summary.ShroudsLine());
            log.LogInfo(summary.NativeLine());
            log.LogInfo(summary.B9Line());
            log.LogInfo(summary.PerfLine());
            WriteMarkdown(summary);
            run.Close();
            _run = null;
            _done = true;
        }

        private static bool Ready(out ObjectAssemblyBuilder builder, out string why)
        {
            builder = null;
            why = null;
            try
            {
                // The mod's campaign feature: without it neither the placement nor our module injection runs, so the
                // report would not describe the player's setup.
                var plugin = KSP1PartModPlugin.Instance;
                if (plugin != null && !plugin.Enabled)
                {
                    why = "KSP1PartMod feature off in this campaign";
                    return false;
                }
                builder = GameManager.Instance?.Game?.OAB?.Current;
                if (builder != null && builder.ActiveBuilderVariant != OABVariant.VAB)
                {
                    why = $"not the VAB ({builder.ActiveBuilderVariant})";
                    return false;
                }
                var complete = Ksp1ImportedPartRegistry.IsComplete;
                var tracker = builder != null && builder.ActivePartTracker != null;
                if (complete && tracker) return true;
                why = $"KSP1 import complete={(complete ? "true" : "false")}, VAB part tracker ready={(tracker ? "true" : "false")}";
            }
            catch (Exception e)
            {
                why = e.Message;
            }
            return false;
        }

        /// <summary>The breadcrumb, the skip list, the targets and the CSV. Null when the audit cannot start (logged).</summary>
        private static RunState Setup(ObjectAssemblyBuilder builder)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                Directory.CreateDirectory(Dir);
                var breadcrumb = Path.Combine(Dir, BreadcrumbFile);
                var skipPath = Path.Combine(Dir, SkipFile);
                var skip = ReadLines(skipPath);
                var crashedPart = File.Exists(breadcrumb) ? AuditResume.FromBreadcrumb(File.ReadAllText(breadcrumb)) : null;
                if (crashedPart != null)
                {
                    if (!skip.Contains(crashedPart))
                    {
                        File.AppendAllText(skipPath, crashedPart + Environment.NewLine);
                        skip.Add(crashedPart);
                    }
                    log.LogWarning(P + $"the previous run stopped during {crashedPart} (game closed or crashed): added to {SkipFile}");
                }
                TryDelete(breadcrumb);

                var cores = new Dictionary<string, PartCore>(StringComparer.Ordinal);
                var all = _targets.AllPossibleParts.GetValue(builder) as IEnumerable<PartCore>;
                if (all != null)
                    foreach (var core in all)
                        if (core?.data?.partName != null && !cores.ContainsKey(core.data.partName)) cores[core.data.partName] = core;
                var entries = new Dictionary<string, Ksp1ImportedPartRegistry.Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in Ksp1ImportedPartRegistry.ImportedParts)
                    if (e?.PartName != null) entries[e.PartName] = e;

                var targets = new List<AuditTarget>();
                foreach (var name in cores.Keys)
                    if (Ksp1ImportedPartRegistry.IsImportedPart(name)) targets.Add(new AuditTarget(name, false));
                foreach (var control in PickControls(cores.Values)) targets.Add(new AuditTarget(control, true));

                var firstStart = _csvPath == null;
                if (firstStart)
                {
                    _stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    _csvPath = Path.Combine(Dir, $"audit-{_stamp}.csv");
                }
                var csv = new StreamWriter(_csvPath, append: true, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
                if (firstStart) csv.WriteLine(AuditCsv.Header());

                var alreadyReported = targets.Count(t => Reported.Contains(t.Name));
                var skipped = 0;
                foreach (var t in targets)
                {
                    if (!skip.Contains(t.Name) || Reported.Contains(t.Name)) continue;
                    var rec = AuditSpawner.Crashed(t.Name, t.IsControl ? "KSP2" : ModOf(entries, t.Name), t.IsControl);
                    csv.WriteLine(AuditCsv.Line(rec));
                    Records.Add(rec);
                    Reported.Add(t.Name);
                    log.LogInfo(AuditSummary.PartLine(rec));
                    skipped++;
                }

                var pending = AuditResume.Pending(targets, Reported, skip).ToList();
                var run = new RunState
                {
                    Builder = builder,
                    Spawner = new AuditSpawner(_targets, builder),
                    Pacer = new AuditPacer(_settings.AuditFrameBudgetMs, Math.Max(1, _settings.AuditUnloadEvery), pending.Count),
                    Pending = pending,
                    Cores = cores,
                    Entries = entries,
                    Csv = csv,
                    TextureKeysAtStart = Ksp1Textures.CacheKeys(),
                    Events = new EventCounts(builder.eventsBuilder),
                    Messages = MessageCounts.TryCreate(_targets, GameManager.Instance?.Game?.Messages),
                    Total = pending.Count,
                };
                log.LogInfo(P + $"starting: {pending.Count(t => !t.IsControl)} KSP1 part(s) and {pending.Count(t => t.IsControl)} control(s), " +
                            $"{skipped} skipped, {alreadyReported} already reported; report {_csvPath}" +
                            (skip.Count > 0 ? "; " + AuditSummary.SkipNote(skipPath) : ""));
                Ksp1Textures.ReportAt("audit start");
                return run;
            }
            catch (Exception e)
            {
                log.LogError(P + "could not start, audit off for this VAB entry: " + e);
                return null;
            }
        }

        private static void ProcessOne(RunState run, AuditTarget target, PartCore core, GameObject prefab, string loadError)
        {
            var log = KSP1PartModPlugin.Log;
            var breadcrumb = Path.Combine(Dir, BreadcrumbFile);
            run.Entries.TryGetValue(target.Name, out var entry);
            try
            {
                File.WriteAllText(breadcrumb, target.Name);
            }
            catch (Exception e)
            {
                log.LogWarning(P + $"breadcrumb not written for {target.Name}: {e.Message}");
            }
            AuditRecord rec;
            List<EventCounts.Growth> grewEvents = null;
            List<MessageCounts.Kept> keptMessages = null;
            // Snapshots right before the spawn, compared right after it, in the same frame: nothing the player does
            // between frames is blamed on the audited part.
            var snapshotFault = (Exception)null;
            try
            {
                run.Events.Snapshot();
                run.Messages?.Snapshot();
            }
            catch (Exception e)
            {
                // A collection changed under the snapshot: no leak check for this part, and the audit goes on.
                snapshotFault = e;
                log.LogWarning(P + $"leak snapshot failed before {target.Name}: {e.Message}");
            }
            _inSpawn = true;
            try
            {
                rec = run.Spawner.Spawn(core, entry, target.IsControl, prefab, loadError);
            }
            catch (Exception e)
            {
                // The spawner catches its own steps; this is a fault of the audit's own code, not of the part.
                log.LogError(P + $"{target.Name}: the audit harness failed: {e}");
                rec = HarnessFault(target, entry, e);
            }
            finally
            {
                _inSpawn = false;
            }
            try
            {
                if (snapshotFault == null)
                {
                    grewEvents = run.Events.Compare();
                    keptMessages = run.Messages?.Compare();
                }
            }
            catch (Exception e)
            {
                log.LogError(P + $"leak check failed after {target.Name}: {e.Message}");
            }
            try
            {
                File.WriteAllText(breadcrumb, "");
            }
            catch (Exception)
            {
                // The next run would skip a part that did not crash: harmless, and it is reported as such.
            }
            try
            {
                run.Csv.WriteLine(AuditCsv.Line(rec));
            }
            catch (Exception e)
            {
                log.LogError(P + $"CSV row not written for {target.Name}: {e.Message}");
            }
            Records.Add(rec);
            Reported.Add(target.Name);
            run.Processed++;
            if (target.IsControl)
            {
                if (AuditClassifier.IsProblem(rec.Kinds)) log.LogWarning(AuditSummary.ControlFailedLine(rec));
            }
            else if (AuditClassifier.IsProblem(rec.Kinds))
            {
                run.Problems++;
                log.LogInfo(AuditSummary.PartLine(rec));
            }
            if (grewEvents != null)
                foreach (var grew in grewEvents)
                    log.LogWarning(P + $"OAB event {grew.Field} grew {grew.Before} -> {grew.After} while auditing {target.Name}");
            if (keptMessages != null)
                foreach (var kept in keptMessages)
                    log.LogWarning(P + $"message {kept.Type} kept {kept.Count} subscription(s) after auditing {target.Name} (destroyed)");
        }

        internal static AuditRecord HarnessFault(AuditTarget target, Ksp1ImportedPartRegistry.Entry entry, Exception e) => new AuditRecord
        {
            Part = target.Name,
            Mod = target.IsControl ? "KSP2" : entry?.ModName ?? "",
            Control = target.IsControl,
            ExceptionType = e.GetType().Name,
            ExceptionMessage = e.Message ?? "",
            Kinds = AuditClassifier.Kinds(new AuditObservations { HarnessFault = true }),
        };

        /// <summary>True when the run must stop: a newer generation (already cancelled by <see cref="Leave"/>), or a
        /// cancel deferred during a spawn, which is done here.</summary>
        private static bool Stopping(int gen, RunState run)
        {
            if (Stale(gen)) return true;
            if (!_cancel) return false;
            _generation++;
            _running = false;
            Cancel(run);
            return true;
        }

        private static void Cancel(RunState run)
        {
            var total = run.Total;
            var log = KSP1PartModPlugin.Log;
            SessionClock.Stop();
            Evict(run); // entries only: the scene change frees what nothing uses
            log.LogInfo(P + $"cancelled at {run.Processed}/{total} (left the VAB); partial report {_csvPath}; the next VAB entry resumes");
            try
            {
                WriteMarkdown(AuditSummary.From(Records, SessionClock.Elapsed.TotalSeconds));
            }
            catch (Exception e)
            {
                log.LogError(P + "partial summary not written: " + e.Message);
            }
            run.Close();
            _run = null;
        }

        // ---- helpers ----

        /// <summary>The 5 controls: a KSP2 engine, a pod and a tank when present, then the first KSP2 parts, all in
        /// ordinal name order.</summary>
        internal static List<string> PickControls(IEnumerable<PartCore> cores)
        {
            var ksp2 = cores.Where(c => c?.data?.partName != null && !Ksp1ImportedPartRegistry.IsImportedPart(c.data.partName))
                .OrderBy(c => c.data.partName, StringComparer.Ordinal).ToList();
            bool Engine(PartCore c) => c.modules != null && c.modules.Any(m => m is Data_Engine);
            bool Command(PartCore c) => c.modules != null && c.modules.Any(m => m is Data_Command);
            var picks = new List<PartCore>
            {
                ksp2.FirstOrDefault(Engine),
                ksp2.FirstOrDefault(c => c.data.crewCapacity > 0 && Command(c)),
                ksp2.FirstOrDefault(c => c.data.resourceContainers != null && c.data.resourceContainers.Count > 0 && !Engine(c) && !Command(c)),
            };
            var names = new List<string>();
            foreach (var c in picks.Concat(ksp2))
                if (c != null && names.Count < ControlCount && !names.Contains(c.data.partName)) names.Add(c.data.partName);
            return names;
        }

        private static string ModOf(Dictionary<string, Ksp1ImportedPartRegistry.Entry> entries, string name) =>
            entries.TryGetValue(name, out var e) ? e.ModName ?? "" : "";

        private static int Evict(RunState run)
        {
            try
            {
                // Only the paths our own spawns loaded fresh (recorded by Ksp1Textures), minus any present at the start.
                var keys = AuditTextureEviction.KeysToEvict(run.TextureKeysAtStart, run.Spawner.TextureKeys);
                run.Spawner.TextureKeys.Clear();
                return Ksp1Textures.Evict(keys);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(P + "texture cache eviction failed: " + e.Message);
                return 0;
            }
        }

        private static string Entries(int n) => n == 1 ? "1 cache entry" : n + " cache entries";

        private static void WriteMarkdown(AuditSummary summary)
        {
            if (_stamp == null) return;
            try
            {
                var path = Path.Combine(Dir, $"audit-{_stamp}.md");
                File.WriteAllText(path, summary.Markdown(_stamp, SkipNotePath(), MmPatches.LastSummary, B9Overrides.LastSummary), new UTF8Encoding(false));
                // The perf report links this run (perf_report_file), rewritten now so that the link is there even if the
                // session ends before the next memory moment.
                PerfReportWriter.NoteAudit(Path.GetFileName(path), summary.PerfLine());
                PerfReportWriter.Write("audit end");
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError(P + "summary not written: " + e.Message);
            }
        }

        /// <summary>The skip list path when it holds a part, else null (no note to show).</summary>
        private static string SkipNotePath()
        {
            try
            {
                var path = Path.Combine(Dir, SkipFile);
                return File.Exists(path) && ReadLines(path).Count > 0 ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static HashSet<string> ReadLines(string path)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return set;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // Left in place: an empty breadcrumb names no part.
            }
        }

        private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ---- async loads ----

        private sealed class LoadResult
        {
            public bool Done;
            public GameObject Prefab;
            public string Error;

            public void Take(out GameObject prefab, out string error)
            {
                prefab = Prefab;
                error = Prefab != null ? null : Error ?? (Done ? "the asset load gave nothing" : "the asset load timed out");
            }
        }

        private static LoadResult Load(string address)
        {
            var result = new LoadResult();
            if (address == null)
            {
                result.Done = true;
                result.Error = "no prefab address";
                return result;
            }
            try
            {
                Action<GameObject> done = go =>
                {
                    result.Prefab = go;
                    result.Done = true;
                };
                _targets.LoadAsset(GameManager.Instance.Assets, address, done);
            }
            catch (Exception e)
            {
                result.Done = true;
                result.Error = "asset load of " + address + " failed: " + e.Message;
            }
            return result;
        }

        // ---- OAB event leak check ----

        /// <summary>Invocation-list lengths of the delegate fields of the real <c>eventsBuilder</c>; growth is reported
        /// once per field.</summary>
        internal sealed class EventCounts
        {
            public readonly struct Growth
            {
                public Growth(string field, int before, int after)
                {
                    Field = field;
                    Before = before;
                    After = after;
                }

                public string Field { get; }
                public int Before { get; }
                public int After { get; }
            }

            private readonly object _events;
            private readonly FieldInfo[] _fields;
            private readonly int[] _counts;
            private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

            public EventCounts(object events)
            {
                _events = events;
                _fields = events == null
                    ? new FieldInfo[0]
                    : events.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                        .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)).ToArray();
                _counts = new int[_fields.Length];
                for (int i = 0; i < _fields.Length; i++) _counts[i] = Count(i);
            }

            private int Count(int i) => (_fields[i].GetValue(_events) as Delegate)?.GetInvocationList().Length ?? 0;

            public void Snapshot()
            {
                for (int i = 0; i < _fields.Length; i++) _counts[i] = Count(i);
            }

            public List<Growth> Compare()
            {
                var grew = new List<Growth>();
                for (int i = 0; i < _fields.Length; i++)
                {
                    var now = Count(i);
                    if (now > _counts[i] && _reported.Add(_fields[i].Name)) grew.Add(new Growth(_fields[i].Name, _counts[i], now));
                    _counts[i] = now;
                }
                return grew;
            }
        }

        /// <summary>The game's message subscriptions left behind by a destroyed part. Subscribing appends a handle to the
        /// type's list in <c>MessageCenter._subscriptions</c>; unsubscribing resets it to the default handle in place, and
        /// the defaults are removed in <c>LateUpdate</c>. So, within the frame of one spawn, a live handle among the
        /// entries appended during that spawn is a subscription the part kept after <c>DestroyImmediate</c>
        /// (<c>PartBehaviourModule.Init</c>, <c>ObjectAssemblyPart.FinalizeLoad</c>). Only the appended tail is read.
        /// Reported once per message type.</summary>
        internal sealed class MessageCounts
        {
            public readonly struct Kept
            {
                public Kept(string type, int count)
                {
                    Type = type;
                    Count = count;
                }

                public string Type { get; }
                public int Count { get; }
            }

            private readonly IDictionary _subscriptions;
            private readonly Dictionary<object, int> _before = new Dictionary<object, int>();
            private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

            internal MessageCounts(IDictionary subscriptions) => _subscriptions = subscriptions;

            /// <summary>Null (check off) when the field cannot be read.</summary>
            public static MessageCounts TryCreate(AuditTargets targets, object messageCenter)
            {
                if (messageCenter == null || targets?.MessageSubscriptions == null) return null;
                return targets.MessageSubscriptions.GetValue(messageCenter) is IDictionary d ? new MessageCounts(d) : null;
            }

            public void Snapshot()
            {
                _before.Clear();
                foreach (DictionaryEntry e in _subscriptions)
                    if (e.Value is ICollection list) _before[e.Key] = list.Count;
            }

            public List<Kept> Compare()
            {
                var kept = new List<Kept>();
                foreach (DictionaryEntry e in _subscriptions)
                {
                    if (!(e.Value is IList list)) continue;
                    var start = _before.TryGetValue(e.Key, out var b) ? b : 0;
                    if (list.Count <= start) continue;
                    var live = 0;
                    object empty = null;
                    for (int i = start; i < list.Count; i++)
                    {
                        var handle = list[i];
                        if (handle == null) continue;
                        if (empty == null) empty = Activator.CreateInstance(handle.GetType());
                        if (!handle.Equals(empty)) live++;
                    }
                    var name = (e.Key as Type)?.Name ?? e.Key.ToString();
                    if (live > 0 && _reported.Add(name)) kept.Add(new Kept(name, live));
                }
                return kept;
            }
        }
    }
}
