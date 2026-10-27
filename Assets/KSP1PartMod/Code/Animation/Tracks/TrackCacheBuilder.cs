using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Mu;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>What the worker needs of one part, copied on the main thread: the worker never touches game
    /// objects.</summary>
    public sealed class PartSnapshot
    {
        public string Part, CfgPath, GameDataRoot;
        /// <summary>The model references in Redux's order; <see cref="RefIsMesh"/> says which is a <c>mesh</c> value.</summary>
        public string[] ModelRefs;
        public bool[] RefIsMesh;
        /// <summary>The clip names whose tracks are wanted.</summary>
        public string[] Clips;

        private static readonly string[] NoRefs = new string[0];

        /// <summary>Model references as Redux reads them (<c>Ksp1ModelImporter.BuildPrefab</c>): every
        /// <c>MODEL { model }</c>; without MODEL node, the <c>model</c> value; else the <c>mesh</c> value, by default
        /// <c>model.mu</c>. Clips: those of our entries, those Redux keeps, and the parachute clips.</summary>
        public static PartSnapshot From(string part, string cfgPath, string gameDataRoot, IKsp1Node partNode, PartAnimationPlan plan)
        {
            var refs = NoRefs;
            var isMesh = new bool[0];
            if (partNode != null)
            {
                var models = partNode.GetNodes("MODEL").ToList();
                if (models.Count > 0)
                {
                    refs = models.Select(m => m.GetValue("model")).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
                    isMesh = new bool[refs.Length];
                }
                else
                {
                    var model = partNode.GetValue("model");
                    var mesh = string.IsNullOrWhiteSpace(model);
                    refs = new[] { mesh ? partNode.GetValue("mesh") ?? "model.mu" : model };
                    isMesh = new[] { mesh };
                }
            }

            var clips = new List<string>();
            if (plan != null)
            {
                foreach (var e in plan.Entries) { clips.Add(e.Clip); clips.Add(e.SecondClip); clips.Add(e.ThirdClip); }
                clips.AddRange(plan.ReduxClips);
            }
            if (partNode != null)
                foreach (var m in partNode.GetNodes("MODULE"))
                    if ("ModuleParachute".Equals(m.GetValue("name"), StringComparison.Ordinal))
                    {
                        clips.Add(m.GetValue("semiDeployedAnimation") ?? "semiDeploy");
                        clips.Add(m.GetValue("fullyDeployedAnimation") ?? "fullyDeploy");
                    }

            return new PartSnapshot
            {
                Part = part, CfgPath = cfgPath, GameDataRoot = gameDataRoot, ModelRefs = refs, RefIsMesh = isMesh,
                Clips = clips.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }
    }

    /// <summary>Reads a part's models and keeps the tracks of its named clips.</summary>
    public static class TrackCacheBuilder
    {
        /// <summary>Never throws for a model that is missing or unreadable: the part is marked failed (unreadable)
        /// or the reference noted (missing). <paramref name="readOnce"/> reads a model by full path.</summary>
        public static PartTracks Build(PartSnapshot s, ModelPathResolver r, Func<string, MuFile> readOnce)
        {
            var result = new PartTracks();
            var sets = new List<TrackSet>();
            var missing = new List<string>();
            var notes = new List<string>();
            var details = new List<string>();
            var drops = default(TrackDrops);
            var wanted = new HashSet<string>(s.Clips ?? new string[0], StringComparer.Ordinal);
            var refs = s.ModelRefs ?? new string[0];

            for (var i = 0; i < refs.Length; i++)
            {
                var isMesh = s.RefIsMesh != null && i < s.RefIsMesh.Length && s.RefIsMesh[i];
                var path = r.Resolve(s.CfgPath, refs[i], isMesh, out var tried);
                if (path == null)
                {
                    missing.Add($"model '{refs[i]}' not found ({tried})");
                    continue;
                }
                if (wanted.Count == 0) continue;

                MuFile file;
                try { file = readOnce(path); }
                catch (Exception e)
                {
                    result.Failed = true;
                    notes.Add($"{e.GetType().Name}: {e.Message} in '{refs[i]}'");
                    if (!(e is MuFormatException)) details.Add(e.ToString());
                    continue;
                }
                Collect(file.Root, i, wanted, sets, ref drops);
            }

            notes.AddRange(missing);
            result.Sets = sets;
            result.Missing = missing;
            result.Drops = drops;
            result.Note = notes.Count == 0 ? null : string.Join("; ", notes);
            result.Detail = details.Count == 0 ? null : string.Join("\n", details);
            return result;
        }

        private static void Collect(MuNode root, int modelIndex, HashSet<string> wanted, List<TrackSet> sets, ref TrackDrops drops)
        {
            var stack = new Stack<MuNode>();
            stack.Push(root);
            var found = new List<TrackSet>();
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.Animation != null)
                    foreach (var clip in node.Animation.Clips)
                    {
                        if (clip.Name == null || !wanted.Contains(clip.Name)) continue;
                        var set = TrackBuilder.FromClip(clip, node.Path(), modelIndex, out var d);
                        drops.Transform += d.Transform; drops.DrawHalo += d.DrawHalo; drops.Audio += d.Audio;
                        drops.Particle += d.Particle; drops.Unknown += d.Unknown;
                        if (set != null) found.Add(set);
                    }
                for (var c = node.Children.Count - 1; c >= 0; c--) stack.Push(node.Children[c]);
            }
            sets.AddRange(found);
        }

        /// <summary>Memoizes <paramref name="read"/> per full path (ignoring case, as Windows does), failures
        /// included: a file shared by many parts is read once. Not thread-safe: one per worker.</summary>
        public static Func<string, MuFile> ReadOnce(Func<string, MuFile> read)
        {
            var memo = new Dictionary<string, (MuFile file, ExceptionDispatchInfo error)>(StringComparer.OrdinalIgnoreCase);
            return path =>
            {
                var key = Path.GetFullPath(path);
                if (!memo.TryGetValue(key, out var hit))
                {
                    try { hit = (read(path), null); }
                    catch (Exception e) { hit = (null, ExceptionDispatchInfo.Capture(e)); }
                    memo[key] = hit;
                }
                hit.error?.Throw();
                return hit.file;
            };
        }
    }

    /// <summary>Builds every part's tracks on a thread-pool thread, so a load never waits for the <c>.mu</c> reads.</summary>
    public static class TrackCacheWorker
    {
        private const string Prefix = "[KSP1PartMod] anim: tracks: ";

        /// <summary>A line telling of a failure, to be logged as a warning. The summary line ends with
        /// "<c>; n failed</c>" and is not one.</summary>
        internal static bool IsFailureLine(string line) => line != null && line.Contains(" failed: ");

        /// <summary>Test seam, called before each part is built; never set in game.</summary>
        internal static Action<PartSnapshot> BeforeBuild;

        /// <summary>Queues the work and returns at once. <paramref name="log"/> is called on the worker thread. The
        /// cache is marked ready for <paramref name="generation"/> whatever happens.</summary>
        public static void Start(int generation, IReadOnlyList<PartSnapshot> parts, Action<string> log)
        {
            var copy = parts?.Where(p => p != null).ToList() ?? new List<PartSnapshot>();
            log = log ?? (_ => { });
            var queued = false;
            try
            {
                queued = ThreadPool.QueueUserWorkItem(_ => Run(generation, copy, log));
            }
            finally
            {
                if (!queued) TrackCache.MarkReady(generation);
            }
        }

        private static void Run(int generation, List<PartSnapshot> parts, Action<string> log)
        {
            try
            {
                var clock = Stopwatch.StartNew();
                var reads = 0;
                var readOnce = TrackCacheBuilder.ReadOnce(p => { reads++; return MuReader.Read(p); });
                var resolvers = new Dictionary<string, ModelPathResolver>(StringComparer.OrdinalIgnoreCase);
                var before = BeforeBuild;
                int clips = 0, tracks = 0, failed = 0;
                foreach (var s in parts)
                {
                    if (generation != TrackCache.Generation) return; // a newer load started: stop here
                    PartTracks t;
                    try
                    {
                        before?.Invoke(s);
                        var root = s.GameDataRoot ?? "";
                        if (!resolvers.TryGetValue(root, out var r))
                            resolvers[root] = r = new ModelPathResolver(root, () => DirectoryIndex.Build(root));
                        t = TrackCacheBuilder.Build(s, r, readOnce);
                    }
                    catch (Exception e)
                    {
                        t = new PartTracks
                        {
                            Failed = true, Note = $"{e.GetType().Name}: {e.Message}",
                            Detail = e is MuFormatException ? null : e.ToString(),
                        };
                    }
                    TrackCache.Put(generation, s.Part, t);

                    foreach (var m in t.Missing) log(Prefix + s.Part + " " + m);
                    if (t.Failed)
                    {
                        failed++;
                        log(Prefix + s.Part + " failed: " + t.Note + (t.Detail == null ? "" : "\n" + t.Detail));
                    }
                    var d = t.Drops;
                    if (d.DrawHalo + d.Audio + d.Particle > 0)
                        log($"{Prefix}{s.Part} dropped {d.DrawHalo} m_DrawHalo, {d.Audio} audio, {d.Particle} particle curve(s)");
                    clips += t.Sets.Count;
                    foreach (var set in t.Sets) tracks += set.Tracks.Count;
                }
                log($"{Prefix}read {reads} model(s) for {parts.Count} part(s) in {clock.ElapsedMilliseconds} ms on a worker; " +
                    $"{clips} clip(s) with {tracks} track(s); {failed} failed");
            }
            catch (Exception e)
            {
                try { log(Prefix + "worker failed: " + e); } catch { /* the log itself failed: nothing left to tell */ }
            }
            finally
            {
                TrackCache.MarkReady(generation);
            }
        }
    }
}
