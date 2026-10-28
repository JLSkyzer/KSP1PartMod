using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Textures;

namespace KSP1PartMod.Perf
{
    /// <summary>The session's perf report, <c>&lt;mod&gt;/perf/perf-&lt;session start&gt;.md</c> (performance plan Task 2):
    /// Session, Load, Memory, Textures, Frame cost, then what to send (in French, for the player). A section without data says
    /// "not measured (&lt;reason&gt;)". Numbers use the invariant culture. Pure.</summary>
    public static class PerfReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Build(PerfSessionHeader header, IReadOnlyList<FlowStep> flowSteps, ImportSummary import,
            OurSteps ours, IReadOnlyList<RebuildRecord> rebuilds, IReadOnlyList<MemorySnapshot> snapshots,
            IReadOnlyList<TextureCensus> censuses, IReadOnlyList<FrameWindowSummary> frameWindows, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            header = header ?? new PerfSessionHeader();
            var b = new StringBuilder();
            b.Append("# KSP1PartMod performance report\n\n");
            Session(b, header, settings);
            Load(b, flowSteps, import, ours, rebuilds, settings);
            Memory(b, snapshots, settings);
            Textures(b, censuses, settings);
            Frames(b, frameWindows, settings);
            Send(b);
            return b.ToString();
        }

        public static string NotMeasured(string reason) => $"not measured ({reason})";

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "?" : s;
        private static string Mb(long? bytes) => bytes.HasValue ? PerfLines.Mb(bytes.Value) : PerfLines.NotAvailable;
        private static string Cell(string s) => (s ?? "?").Replace("|", "\\|");

        private static void Session(StringBuilder b, PerfSessionHeader h, AnimationSettings s)
        {
            b.Append("## Session\n\n");
            b.Append($"- Started: {Or(h.Started)}\n");
            b.Append($"- KSP1PartMod {Or(h.ModVersion)}, game {Or(h.GameVersion)}, Unity {Or(h.UnityVersion)}\n");
            b.Append($"- Graphics card: {Or(h.Card)} ({(h.CardMb.HasValue ? h.CardMb.Value.ToString(Inv) + " MB" : PerfLines.NotAvailable)})\n");
            b.Append($"- System RAM: {(h.SystemRamMb.HasValue ? h.SystemRamMb.Value.ToString(Inv) + " MB" : PerfLines.NotAvailable)}\n");
            b.Append($"- Main menu after: {(h.MainMenuAfterSeconds.HasValue ? PerfLines.S(h.MainMenuAfterSeconds.Value) + " s" : NotMeasured("no main menu yet"))}\n");
            if (h.CampaignLoads.Count > 0) b.Append($"- Campaign loads: {string.Join("; ", h.CampaignLoads)}\n");
            if (!string.IsNullOrEmpty(h.AuditFile))
                b.Append($"- Audit: [{h.AuditFile}](../audit/{h.AuditFile})" +
                         (string.IsNullOrEmpty(h.AuditPerfLine) ? "" : $" (`{h.AuditPerfLine}`)") + "\n");
            b.Append("\n| Switch | Value |\n|---|---|\n");
            Switch(b, "perf_load_timeline", s.PerfLoadTimeline);
            Switch(b, "perf_memory_lines", s.PerfMemoryLines);
            Switch(b, "perf_report_file", s.PerfReportFile);
            Switch(b, "perf_counter", s.PerfCounter);
            b.Append($"| perf_top_n | {s.PerfTopN.ToString(Inv)} |\n");
            b.Append($"| perf_window_seconds | {s.PerfWindowSeconds.ToString("0.#", Inv)} |\n");
            Switch(b, "share_ksp1_textures", s.ShareKsp1Textures);
            Switch(b, "ksp1_textures_follow_quality", s.Ksp1TexturesFollowQuality);
            Switch(b, "raise_streaming_budget", s.RaiseStreamingBudget);
            b.Append('\n');
        }

        private static void Switch(StringBuilder b, string key, bool value) => b.Append($"| {key} | {(value ? "true" : "false")} |\n");

        private static void Load(StringBuilder b, IReadOnlyList<FlowStep> flow, ImportSummary import, OurSteps ours,
            IReadOnlyList<RebuildRecord> rebuilds, AnimationSettings s)
        {
            b.Append("## Load\n\n### KSP1 import\n\n");
            var off = s.PerfLoadTimeline ? null : "perf_load_timeline is false";
            if (import == null) b.Append(NotMeasured(off ?? "no KSP1 import this session yet")).Append("\n\n");
            else if (import.Reused) b.Append(PerfLines.Reused(import.Parts, import.WallS * 1000)).Append("\n\n");
            else
            {
                b.Append("```\n").Append(PerfLines.ImportWorker(import)).Append('\n').Append(PerfLines.Import(import)).Append('\n')
                    .Append(PerfLines.ReportLines(import)).Append("\n```\n\n");
                if (import.Slowest.Length > 0)
                {
                    b.Append("| Slowest part | ms | prefab ms | textures ms |\n|---|---|---|---|\n");
                    foreach (var p in import.Slowest)
                        b.Append($"| {Cell(p.Name)} | {PerfLines.Ms(PhaseClock.Ms(p.TotalTicks, import.Frequency))} | " +
                                 $"{PerfLines.Ms(PhaseClock.Ms(p.PrefabTicks, import.Frequency))} | " +
                                 $"{PerfLines.Ms(PhaseClock.Ms(p.TextureTicks, import.Frequency))} |\n");
                    b.Append('\n');
                }
            }

            b.Append("### Our steps\n\n");
            b.Append(ours == null ? NotMeasured(off ?? "the animation step has not run yet") : "```\n" + PerfLines.Ours(ours) + "\n```").Append("\n\n");

            b.Append("### Game flow steps (0.1 s or more, and every KSP1 step)\n\n");
            if (flow == null || flow.Count == 0) b.Append(NotMeasured(off ?? "no flow step recorded yet")).Append("\n\n");
            else
            {
                b.Append("| Step | s |\n|---|---|\n");
                foreach (var f in flow) b.Append($"| {Cell(f.Name)} | {PerfLines.S(f.Seconds)} |\n");
                b.Append('\n');
            }

            b.Append("### Prefab rebuilds\n\n");
            if (rebuilds == null || rebuilds.Count == 0) b.Append(NotMeasured(off ?? "no rebuild recorded yet")).Append("\n\n");
            else
            {
                b.Append("```\n");
                foreach (var r in rebuilds) b.Append(PerfLines.Rebuilds(r)).Append('\n');
                b.Append("```\n\n");
            }
        }

        private static void Memory(StringBuilder b, IReadOnlyList<MemorySnapshot> snapshots, AnimationSettings s)
        {
            b.Append("## Memory\n\n");
            if (snapshots == null || snapshots.Count == 0)
            {
                b.Append(NotMeasured(s.PerfMemoryLines ? "no memory moment yet" : "perf_memory_lines is false")).Append("\n\n");
                return;
            }
            b.Append("MB. Texture figures are Unity's; KSP1 live is the census estimate. Dedicated GPU memory: read it in Task Manager.\n\n");
            b.Append("| Moment | allocated | reserved | mono used | mono heap | working set | private | tex current | tex desired | " +
                     "tex non-streaming | tex total | budget | KSP1 live |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (var m in snapshots)
                b.Append($"| {Cell(m.Moment)} | {Mb(m.Allocated)} | {Mb(m.Reserved)} | {Mb(m.MonoUsed)} | {Mb(m.MonoHeap)} | " +
                         $"{Mb(m.WorkingSet)} | {Mb(m.PrivateBytes)} | {Mb(m.TextureCurrent)} | {Mb(m.TextureDesired)} | " +
                         $"{Mb(m.TextureNonStreaming)} | {Mb(m.TextureTotal)} | " +
                         $"{(m.BudgetMb.HasValue ? m.BudgetMb.Value.ToString("0", Inv) : PerfLines.NotAvailable)} | {Mb(m.Ksp1Live)} |\n");
            b.Append('\n');
        }

        private static void Textures(StringBuilder b, IReadOnlyList<TextureCensus> censuses, AnimationSettings s)
        {
            b.Append("## Textures\n\n");
            if (censuses == null || censuses.Count == 0)
            {
                b.Append(NotMeasured(s.PerfMemoryLines ? "no census yet" : "perf_memory_lines is false")).Append("\n\n");
                return;
            }
            b.Append("```\n");
            foreach (var c in censuses) b.Append(PerfLines.Census(c)).Append('\n').Append(PerfLines.Largest(c)).Append('\n');
            b.Append("```\n\n");
        }

        private static void Frames(StringBuilder b, IReadOnlyList<FrameWindowSummary> windows, AnimationSettings s)
        {
            b.Append("## Frame cost\n\n");
            if (windows == null || windows.Count == 0)
            {
                b.Append(NotMeasured(s.PerfCounter ? "no window closed yet" : "perf_counter is false")).Append("\n\n");
                return;
            }
            b.Append("The last window of each scene. A p95 is the upper bound of its histogram bucket (within about 13 %).\n\n```\n");
            foreach (var w in windows)
            {
                b.Append(PerfLines.FrameWindow(w)).Append('\n');
                foreach (var f in w.Features) b.Append(PerfLines.FrameFeature(w.Scene, f)).Append('\n');
            }
            b.Append("```\n\n");
        }

        private static void Send(StringBuilder b)
        {
            b.Append("## Que m'envoyer\n\n");
            b.Append("- `Player.log`, complet (`%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2`) ;\n");
            b.Append("- ce fichier (le plus récent de `<jeu>/mods/KSP1PartMod/perf/`) ;\n");
            b.Append("- si l'audit a tourné, `<jeu>/mods/KSP1PartMod/audit/audit-<date>.md` ;\n");
            b.Append("- les chiffres « Mémoire GPU dédiée » du Gestionnaire des tâches, notés à la main avec leur moment.\n\n");
            b.Append("Lignes utiles du log : `[KSP1PartMod] perf:`, `[KSP1PartMod] textures:`, `[KSP1PartMod] anim: perf:` et les " +
                     "lignes `[System] … completed in …` du jeu.\n");
        }
    }
}
