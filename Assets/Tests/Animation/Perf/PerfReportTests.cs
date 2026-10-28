using System.Globalization;
using System.Threading;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Textures;
using KSP1PartMod.Perf;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class PerfReportTests
    {
        private const long Hz = 1000;

        private static string Full()
        {
            var header = new PerfSessionHeader
            {
                Started = "2026-10-28 20:00:00", ModVersion = "0.13.0", GameVersion = "0.2.9", UnityVersion = "6000.6.0f1",
                Card = "NVIDIA GeForce RTX 3070", CardMb = 8192, SystemRamMb = 32768, MainMenuAfterSeconds = 62.5,
            };
            header.CampaignLoads.Add("431.5 s to KerbalSpaceCenter");
            var clock = new PhaseClock();
            clock.Add(PerfPhase.Translate, 2500);
            clock.Add(PerfPhase.AnimationStep, 2780);
            var import = ImportSummary.From(clock.Snapshot(), Hz, 2001, 315000, 9000, 1800);
            var slow = new SlowestParts(1);
            slow.Offer("bluedog|Saturn", 1500, 1200, 900);
            import.Slowest = slow.ToArray();
            var census = new TextureCensus("VAB entry", 3) { CensusMs = 12.5 };
            census.Add("Saturn_S1C", 4096, 4096, "DXT5", 8, true, 13, false);
            var window = new FrameWindowSummary { Scene = "VAB", Seconds = 10, Frames = 600, Fps = 60, OursAvgMs = 0.25 };
            window.Features.Add(new FeatureCost { Feature = PerfFeature.Clips, AvgMs = 0.25, Parts = 20, Entries = 40 });
            return PerfReport.Build(header,
                new[] { new FlowStep { Name = "Redux: Importing KSP1 parts", Seconds = 315.04 } },
                import,
                OurSteps.From(clock.Snapshot(), Hz),
                new[] { new RebuildRecord { Since = "end of import", Count = 3, Ms = 120.5, Frequency = Hz } },
                new[] { new MemorySnapshot { Moment = "VAB entry", Allocated = 3L << 30, BudgetMb = 1536.4, Ksp1Live = 512L << 20 } },
                new[] { census },
                new[] { window },
                AnimationSettings.Parse("{\"perf_counter\": true}", out _));
        }

        [Test] public void AuditLink_OnlyWhenTheAuditRan()
        {
            StringAssert.DoesNotContain("- Audit:", Full(), "no audit this session");
            var header = new PerfSessionHeader
            {
                AuditFile = "audit-20261028-210000.md",
                AuditPerfLine = "audit: perf: prefab_ms p50 10, p95 19, max 20; finalize_ms p50 100, p95 190, max 200 over 20 part(s)",
            };
            var md = PerfReport.Build(header, null, null, null, null, null, null, null, null);
            StringAssert.Contains("- Audit: [audit-20261028-210000.md](../audit/audit-20261028-210000.md) (`audit: perf: prefab_ms p50 10, " +
                                  "p95 19, max 20; finalize_ms p50 100, p95 190, max 200 over 20 part(s)`)\n", md);
        }

        [Test] public void FullReport_EverySection()
        {
            var md = Full();
            foreach (var h in new[] { "# KSP1PartMod performance report", "## Session", "## Load", "## Memory", "## Textures", "## Frame cost", "## Que m'envoyer" })
                StringAssert.Contains(h + "\n", md);
            StringAssert.DoesNotContain("not measured", md);
            StringAssert.Contains("- KSP1PartMod 0.13.0, game 0.2.9, Unity 6000.6.0f1", md);
            StringAssert.Contains("- Graphics card: NVIDIA GeForce RTX 3070 (8192 MB)", md);
            StringAssert.Contains("- Main menu after: 62.5 s", md);
            StringAssert.Contains("- Campaign loads: 431.5 s to KerbalSpaceCenter", md);
            StringAssert.Contains("| perf_counter | true |", md);
            StringAssert.Contains("| perf_top_n | 10 |", md);
            StringAssert.Contains("| share_ksp1_textures | true |", md);
            StringAssert.Contains("| ksp1_textures_follow_quality | true |", md);
            StringAssert.Contains("| raise_streaming_budget | false |", md);
            StringAssert.Contains("[KSP1PartMod] perf: import: 2001 part(s) in 315.0 s wall", md);
            StringAssert.Contains("| bluedog\\|Saturn | 1500 | 1200 | 900 |", md, "a pipe in a name is escaped");
            StringAssert.Contains("animation step 2780 ms", md);
            StringAssert.Contains("| Redux: Importing KSP1 parts | 315.04 |", md);
            StringAssert.Contains("[KSP1PartMod] perf: rebuilds since end of import: 3 prefab rebuild(s) in 120.5 ms", md);
            StringAssert.Contains("| VAB entry | 3072 | n/a |", md);
            StringAssert.Contains("| 1536 | 512 |", md);
            StringAssert.Contains("[KSP1PartMod] textures: census at VAB entry: 1 live KSP1 texture(s) ~21 MB", md);
            StringAssert.Contains("[KSP1PartMod] perf: frame: VAB: clips 0.25 ms avg", md);
            StringAssert.Contains("Gestionnaire des tâches", md);
        }

        [Test] public void EmptyReport_EachSectionSaysWhy()
        {
            var md = PerfReport.Build(null, null, null, null, null, null, null, null, null);
            StringAssert.Contains("## Que m'envoyer", md);
            StringAssert.Contains("not measured (no KSP1 import this session yet)", md);
            StringAssert.Contains("not measured (the animation step has not run yet)", md);
            StringAssert.Contains("not measured (no memory moment yet)", md);
            StringAssert.Contains("not measured (no census yet)", md);
            StringAssert.Contains("not measured (perf_counter is false)", md);
            StringAssert.Contains("- System RAM: n/a", md);

            var off = PerfReport.Build(null, null, null, null, null, null, null, null,
                AnimationSettings.Parse("{\"perf_load_timeline\": false, \"perf_memory_lines\": false}", out _));
            StringAssert.Contains("not measured (perf_load_timeline is false)", off);
            StringAssert.Contains("not measured (perf_memory_lines is false)", off);
        }

        [Test] public void ReusedImport_OneLine()
        {
            var md = PerfReport.Build(null, null, new ImportSummary { Reused = true, Parts = 1994, WallS = 0.0305 }, null, null, null, null, null, null);
            StringAssert.Contains("[KSP1PartMod] perf: import: reused 1994 cached part(s) in 30.5 ms", md);
        }

        [Test] public void Numbers_InvariantCulture()
        {
            var invariant = Full();
            var was = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                Assert.AreEqual(invariant, Full());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
            StringAssert.Contains("315.04", invariant);
        }
    }
}
