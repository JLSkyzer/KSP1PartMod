using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Runtime;

namespace KSP1PartMod.Audit
{
    /// <summary>Totals over the audited parts (the controls are counted apart), the exact log lines and the Markdown report.</summary>
    public sealed class AuditSummary
    {
        public const int PodNamesShown = 10;
        public const int SlowestShown = 10;
        private const int MessageChars = 200;

        public sealed class ModCount
        {
            public string Mod;
            public int Problems;
            public int Total;
        }

        public sealed class NameCount
        {
            public string Name;
            public int Count;
        }

        public int Total { get; private set; }
        /// <summary>No kind at all.</summary>
        public int Ok { get; private set; }
        /// <summary>Only <see cref="AuditKind.ImportWarning"/>.</summary>
        public int OkWithWarnings { get; private set; }
        public int WithProblem { get; private set; }
        public double ElapsedSeconds { get; private set; }
        /// <summary>Non-zero kinds, in declaration order.</summary>
        public IReadOnlyList<KeyValuePair<AuditKind, int>> KindCounts { get; private set; }
        public IReadOnlyList<ModCount> Mods { get; private set; }
        /// <summary>Failing or uninitialized module types, by number of parts.</summary>
        public IReadOnlyList<NameCount> Modules { get; private set; }
        public IReadOnlyList<AuditRecord> Slowest { get; private set; }
        public IReadOnlyList<AuditRecord> Problems { get; private set; }
        public IReadOnlyList<AuditRecord> Controls { get; private set; }
        public IReadOnlyList<AuditRecord> FailedControls { get; private set; }
        /// <summary>Crew &gt; 0 and a command module.</summary>
        public int PodsWithCommand { get; private set; }
        public IReadOnlyList<string> PodsWithEngine { get; private set; }
        public int PodsCommandInitialized { get; private set; }
        /// <summary>Parts whose <c>mod_fixes</c> say <c>shroud_kept</c>, <c>shroud_missing</c>, <c>shroud_no_node</c>, and parts
        /// with <see cref="AuditRecord.ReduxFairing"/> (engine shrouds, 0.7.0).</summary>
        public int ShroudsKept { get; private set; }
        public int ShroudsMissing { get; private set; }
        public int ShroudsNoNode { get; private set; }
        public int ReduxFairings { get; private set; }
        /// <summary>Native modules (0.8.0): parts whose converted module runs, and parts where one did not.</summary>
        public int NativeHeatshields { get; private set; }
        public int NativeCrossfeeds { get; private set; }
        public int NativeFailedParts { get; private set; }
        /// <summary>B9 overrides (0.9.0): parts with an override set, and parts where the default variant did not apply.</summary>
        public int B9Parts { get; private set; }
        public int B9FailedParts { get; private set; }

        public static AuditSummary From(IEnumerable<AuditRecord> records, double elapsedSeconds)
        {
            var all = (records ?? new AuditRecord[0]).Where(r => r != null).ToList();
            var parts = all.Where(r => !r.Control).ToList();
            var s = new AuditSummary { ElapsedSeconds = elapsedSeconds, Total = parts.Count };
            s.Ok = parts.Count(r => r.Kinds == AuditKind.None);
            s.OkWithWarnings = parts.Count(r => r.Kinds == AuditKind.ImportWarning);
            s.WithProblem = parts.Count(r => AuditClassifier.IsProblem(r.Kinds));
            s.Problems = parts.Where(r => AuditClassifier.IsProblem(r.Kinds)).ToList();

            s.KindCounts = AuditKinds.All
                .Select(k => new KeyValuePair<AuditKind, int>(k, parts.Count(r => (r.Kinds & k) != 0)))
                .Where(p => p.Value > 0).ToList();

            s.Mods = parts.GroupBy(r => r.Mod ?? "", StringComparer.Ordinal)
                .Select(g => new ModCount { Mod = g.Key, Total = g.Count(), Problems = g.Count(r => AuditClassifier.IsProblem(r.Kinds)) })
                .OrderByDescending(m => m.Problems).ThenBy(m => m.Mod, StringComparer.Ordinal).ToList();

            var modules = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in parts)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(r.FailingModule)) names.Add(r.FailingModule);
                if (r.UninitializedModules != null) foreach (var n in r.UninitializedModules) if (!string.IsNullOrEmpty(n)) names.Add(n);
                foreach (var n in names) modules[n] = modules.TryGetValue(n, out var c) ? c + 1 : 1;
            }
            s.Modules = modules.Select(p => new NameCount { Name = p.Key, Count = p.Value })
                .OrderByDescending(m => m.Count).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();

            s.Slowest = parts.OrderByDescending(r => r.TotalMs).ThenBy(r => r.Part, StringComparer.Ordinal).Take(SlowestShown).ToList();

            var pods = parts.Where(r => r.Crew > 0 && r.HasCommand).ToList();
            s.PodsWithCommand = pods.Count;
            s.PodsWithEngine = pods.Where(r => r.Engines > 0).Select(r => r.Part).OrderBy(n => n, StringComparer.Ordinal).ToList();
            s.PodsCommandInitialized = pods.Count(r => r.Engines > 0 && r.CommandInitialized);

            s.ShroudsKept = parts.Count(r => HasFix(r, "shroud_kept"));
            s.ShroudsMissing = parts.Count(r => HasFix(r, "shroud_missing"));
            s.ShroudsNoNode = parts.Count(r => HasFix(r, "shroud_no_node"));
            s.ReduxFairings = parts.Count(r => r.ReduxFairing);
            s.NativeHeatshields = parts.Count(r => HasNative(r, AuditNative.Heatshield));
            s.NativeCrossfeeds = parts.Count(r => HasNative(r, AuditNative.Crossfeed));
            s.NativeFailedParts = parts.Count(r => r.NativeModules != null && r.NativeModules.Any(AuditNative.IsFailed));
            s.B9Parts = parts.Count(r => AuditB9.HasOverrides(r.B9Overrides));
            s.B9FailedParts = parts.Count(r => AuditB9.HasOverrides(r.B9Overrides) && AuditB9.IsFailed(r.B9Overrides));

            s.Controls = all.Where(r => r.Control).ToList();
            s.FailedControls = s.Controls.Where(r => AuditClassifier.IsProblem(r.Kinds)).ToList();
            return s;
        }

        // ---- log lines (Task 2's exact formats) ----

        public string DoneLine(string reportPath, string skipPath = null) =>
            $"audit: done in {Seconds(ElapsedSeconds)} s: {Total} part(s), {Ok} ok, {OkWithWarnings} ok with import warnings only, {WithProblem} with a problem; report {reportPath}" +
            (string.IsNullOrEmpty(skipPath) ? "" : "; " + SkipNote(skipPath));

        /// <summary>How to clear the skip list.</summary>
        public static string SkipNote(string skipPath) => $"skipped parts are listed in {skipPath}: delete it to retry these parts";

        public string ByKindLine() =>
            "audit: by kind: " + (KindCounts.Count == 0 ? "none" : string.Join(", ", KindCounts.Select(p => $"{AuditKinds.Name(p.Key)} {p.Value}")));

        public string ByModLine() =>
            "audit: by mod: " + (Mods.Count == 0 ? "none" : string.Join(", ", Mods.Select(m => $"{m.Mod} {m.Problems}/{m.Total}")));

        public string ByModuleLine() =>
            "audit: by module: " + (Modules.Count == 0 ? "none" : string.Join(", ", Modules.Select(m => $"{m.Name} {m.Count}")));

        public string SlowestLine() =>
            "audit: slowest: " + (Slowest.Count == 0 ? "none" : string.Join(", ", Slowest.Select(r => $"{r.Part} {Ms(r.TotalMs)} ms")));

        public string PodsLine() =>
            $"audit: pods: {PodsWithCommand} command part(s) with crew, {PodsWithEngine.Count} with an engine" +
            (PodsWithEngine.Count > 0 ? $" ({string.Join(", ", PodsWithEngine.Take(PodNamesShown))})" : "") +
            $", {PodsCommandInitialized} of those with Module_Command initialized";

        public string ShroudsLine() => ShroudLines.AuditSummary(ShroudsKept, ShroudsMissing, ShroudsNoNode, ReduxFairings);

        public string NativeLine() =>
            $"audit: native modules: {NativeHeatshields} heat shield(s), {NativeCrossfeeds} crossfeed toggle(s), {NativeFailedParts} part(s) where one failed to initialize";

        public string B9Line() => B9.B9Lines.AuditSummary(B9Parts, B9FailedParts);

        /// <summary>One line per part with a kind other than <see cref="AuditKind.ImportWarning"/>.</summary>
        public static string PartLine(AuditRecord r)
        {
            var line = $"audit: {r.Part} ({r.Mod}): {AuditKinds.Join(r.Kinds)}";
            if (!string.IsNullOrEmpty(r.ExceptionType))
            {
                line += $": {r.ExceptionType} in {FailingStep(r)}";
                var msg = FirstLine(r.ExceptionMessage, MessageChars);
                if (msg.Length > 0) line += ": " + msg;
            }
            return line;
        }

        /// <summary>Where the exception came from: the failing module, else the step that ran (the prefab build or the
        /// template step for <c>NoPrefab</c>/<c>TemplateFailed</c>, else <c>FinalizeLoad</c>).</summary>
        internal static string FailingStep(AuditRecord r)
        {
            if ((r.Kinds & AuditKind.HarnessFault) != 0) return "the audit harness";
            if (!string.IsNullOrEmpty(r.FailingModule)) return r.FailingModule;
            if ((r.Kinds & AuditKind.NoPrefab) != 0) return "the prefab build";
            if ((r.Kinds & AuditKind.TemplateFailed) != 0) return "the template step";
            return "FinalizeLoad";
        }

        public static string ProgressLine(int done, int total, int withProblem, double elapsedSeconds) =>
            $"audit: {done}/{total} part(s), {withProblem} with a problem, {Seconds(elapsedSeconds)} s elapsed";

        public static string ControlFailedLine(AuditRecord r) =>
            $"audit: control part {r.Part} failed ({AuditKinds.Join(r.Kinds)}): the audit harness is suspect for this kind";

        // ---- Markdown ----

        /// <param name="b9Lines">The session's <c>b9:</c> end-of-import lines (B9 overrides), copied into the header.</param>
        public string Markdown(string stamp, string skipPath = null, IEnumerable<string> moduleManagerLines = null,
            IEnumerable<string> b9Lines = null)
        {
            var sb = new StringBuilder();
            sb.Append("# KSP1PartMod audit ").Append(stamp).Append("\n\n");
            sb.Append($"{Total} part(s) in {Seconds(ElapsedSeconds)} s: {Ok} ok, {OkWithWarnings} ok with import warnings only, {WithProblem} with a problem.\n\n");
            if (!string.IsNullOrEmpty(skipPath)) sb.Append(SkipNote(skipPath)).Append(".\n\n");

            var mm = (moduleManagerLines ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (mm.Count > 0)
            {
                sb.Append("## ModuleManager (this session)\n" + "\n");
                foreach (var l in mm) sb.Append("- ").Append(l.Replace("\r", " ").Replace("\n", " ")).Append('\n');
                sb.Append('\n');
            }

            var b9 = (b9Lines ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (b9.Count > 0)
            {
                sb.Append("## B9PartSwitch (this session)\n\n");
                foreach (var l in b9) sb.Append("- ").Append(l.Replace("\r", " ").Replace("\n", " ")).Append('\n');
                sb.Append('\n');
            }

            sb.Append("## By kind\n\n| Kind | Parts |\n|---|---|\n");
            foreach (var p in KindCounts) sb.Append($"| {AuditKinds.Name(p.Key)} | {p.Value} |\n");

            sb.Append("\n## By mod\n\n| Mod | Problems | Total |\n|---|---|---|\n");
            foreach (var m in Mods) sb.Append($"| {Cell(m.Mod)} | {m.Problems} | {m.Total} |\n");

            sb.Append("\n## By module\n\n| Module | Parts |\n|---|---|\n");
            foreach (var m in Modules) sb.Append($"| {Cell(m.Name)} | {m.Count} |\n");

            sb.Append("\n## Slowest parts\n\n| Part | ms |\n|---|---|\n");
            foreach (var r in Slowest) sb.Append($"| {Cell(r.Part)} | {Ms(r.TotalMs)} |\n");

            sb.Append("\n## Pods\n\n");
            sb.Append($"- command parts with crew: {PodsWithCommand}\n");
            sb.Append($"- of which with an engine: {PodsWithEngine.Count}");
            if (PodsWithEngine.Count > 0) sb.Append(" (").Append(string.Join(", ", PodsWithEngine.Take(PodNamesShown))).Append(')');
            sb.Append($"\n- of those with Module_Command initialized: {PodsCommandInitialized}\n");

            sb.Append("\n## Shrouds\n\n").Append(ShroudsLine()).Append('\n');

            sb.Append("\n## Native modules\n\n").Append(NativeLine()).Append('\n');

            sb.Append("\n## B9 overrides\n\n").Append(B9Line()).Append('\n');

            sb.Append("\n## Controls\n\n");
            if (Controls.Count == 0) sb.Append("No control part was audited.\n");
            else if (FailedControls.Count == 0) sb.Append($"All {Controls.Count} control part(s) are clean.\n");
            else foreach (var r in FailedControls) sb.Append("- ").Append(ControlFailedLine(r)).Append('\n');

            sb.Append("\n## Parts with a problem\n\n| Part | Mod | Kinds | Module | Message |\n|---|---|---|---|---|\n");
            foreach (var r in Problems)
                sb.Append($"| {Cell(r.Part)} | {Cell(r.Mod)} | {AuditKinds.Join(r.Kinds)} | {Cell(r.FailingModule)} | {Cell(FirstLine(r.ExceptionMessage, MessageChars))} |\n");
            return sb.ToString();
        }

        // ---- helpers ----

        private static bool HasNative(AuditRecord r, string module) => r.NativeModules != null && r.NativeModules.Contains(module);

        private static bool HasFix(AuditRecord r, string fix) => r.ModFixes != null && r.ModFixes.Contains(fix);

        /// <summary>Whole seconds, with one decimal under 10 s (a short run would otherwise read "0 s").</summary>
        internal static string Seconds(double s) => s.ToString(s < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
        private static string Ms(double ms) => ms.ToString("0.#", CultureInfo.InvariantCulture);

        private static string Cell(string s) => (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        /// <summary>The first line of a text, at most <paramref name="max"/> characters.</summary>
        internal static string FirstLine(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int nl = text.IndexOfAny(new[] { '\r', '\n' });
            var line = nl >= 0 ? text.Substring(0, nl) : text;
            return line.Length > max ? line.Substring(0, max) : line;
        }
    }
}
