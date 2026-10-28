using KSP1PartMod.Animation.Textures;
using KSP1PartMod.Perf;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Perf
{
    /// <summary>Every exact line of the performance plan's Tasks 3 to 5. Frequency 1000: one tick is one millisecond.</summary>
    public class PerfLinesTests
    {
        private const long Hz = 1000;

        private static PhaseTotals[] Phases()
        {
            var c = new PhaseClock();
            void Add(PerfPhase p, long ms, int calls = 1)
            {
                for (var i = 0; i < calls; i++) c.Add(p, ms / calls + (i == 0 ? ms % calls : 0));
            }

            Add(PerfPhase.CacheKey, 120, 2);
            Add(PerfPhase.CfgRead, 4500);
            Add(PerfPhase.MmFinalize, 2300);
            Add(PerfPhase.TankCatalog, 40);
            Add(PerfPhase.PlumeCatalog, 15);
            Add(PerfPhase.LocalizationCatalog, 8);
            Add(PerfPhase.Translate, 200000);
            Add(PerfPhase.ReduxImporters, 30000);
            Add(PerfPhase.OurImporters, 1500, 300);
            Add(PerfPhase.Prefab, 150000);
            Add(PerfPhase.MuRead, 40000, 2000);
            Add(PerfPhase.Textures, 90000, 6000);
            Add(PerfPhase.LocalizationRegister, 250);
            Add(PerfPhase.Icons, 60000);
            Add(PerfPhase.Release, 30);
            Add(PerfPhase.ReportLines, 3000, 14810);
            Add(PerfPhase.ReportWarnings, 1000, 5239);
            return c.Snapshot();
        }

        private static ImportSummary Summary()
        {
            var s = ImportSummary.From(Phases(), Hz, 2001, 315000, 9000, 1800);
            var slow = new SlowestParts(2);
            slow.Offer("bluedog_Saturn_S1C", 1500, 1200, 900);
            slow.Offer("bluedog_Apollo_CM", 800, 600, 300);
            s.Slowest = slow.ToArray();
            return s;
        }

        [Test] public void Armed()
        {
            Assert.AreEqual("[KSP1PartMod] perf: armed: load timeline on (16 of 18 Redux phase(s) timed; not found: A.B, C.D), memory lines on, " +
                            "report file off, frame cost off (window 10 s); timer overhead ~25 ns per timing",
                PerfLines.Armed(true, 16, 18, new[] { "A.B", "C.D" }, true, false, false, 10, 25.4));
            Assert.AreEqual("[KSP1PartMod] perf: armed: load timeline off (0 of 18 Redux phase(s) timed), memory lines off, " +
                            "report file on, frame cost on (window 2.5 s); timer overhead ~30 ns per timing",
                PerfLines.Armed(false, 0, 18, null, false, true, true, 2.5, 30));
        }

        [Test] public void MemberNotFound() =>
            Assert.AreEqual("[KSP1PartMod] perf: load: Ksp1MuReader.Read not found, phase 'MuRead' not timed",
                PerfLines.MemberNotFound("Ksp1MuReader.Read", PerfPhase.MuRead));

        [Test] public void ImportWorker() =>
            Assert.AreEqual("[KSP1PartMod] perf: import: worker: cache key 120 ms over 2 call(s), cfg read 4500 ms (1800 file(s)), " +
                            "MM finalize 2300 ms, tank catalog 40 ms, plume catalog 15 ms, localization catalog 8 ms",
                PerfLines.ImportWorker(Summary()));

        [Test] public void Import()
        {
            // other = 200 - 30 - 150 = 20 s; outside = 315 - 200 - 60 - 0.25 - 0.03 = 54.72 s.
            Assert.AreEqual("[KSP1PartMod] perf: import: 2001 part(s) in 315.0 s wall over 9000 frame(s): translate 200.0 s " +
                            "(Redux importers 30.0 s, of which ours 1.5 s over 300 call(s); prefab 150.0 s, of which .mu read 40.0 s over " +
                            "2000 model(s) and textures 90.0 s over 6000 load(s); other 20.0 s), localization 250 ms, icons 60.0 s, " +
                            "release 30 ms; outside our timings (frames, worker wait, loading screen) 54.72 s",
                PerfLines.Import(Summary()));
        }

        [Test] public void ReportLines() =>
            Assert.AreEqual("[KSP1PartMod] perf: import: report lines: 14810 in 3000 ms (5239 warning(s))", PerfLines.ReportLines(Summary()));

        [Test] public void SlowestParts()
        {
            Assert.AreEqual("[KSP1PartMod] perf: import: slowest parts: bluedog_Saturn_S1C 1500 ms (prefab 1200, textures 900), " +
                            "bluedog_Apollo_CM 800 ms (prefab 600, textures 300)",
                PerfLines.SlowestImportParts(Summary().Slowest, Hz));
            Assert.AreEqual("[KSP1PartMod] perf: import: slowest parts: none", PerfLines.SlowestImportParts(new SlowPart[0], Hz));
        }

        [Test] public void Reused() =>
            Assert.AreEqual("[KSP1PartMod] perf: import: reused 1994 cached part(s) in 30.5 ms; no import phase to time",
                PerfLines.Reused(1994, 30.5));

        [Test] public void Ours()
        {
            var o = OurSteps.From(Phases(), Hz);
            Assert.AreEqual("[KSP1PartMod] perf: ours: mu fix —, shroud import —, B9 —, native importers 1500 ms, science scan —, " +
                            "tracks read — (worker), animation step —, placement —", PerfLines.Ours(o));
            o = new OurSteps
            {
                MuFixMs = 1.24, ShroudImportMs = 12, B9Ms = 40, NativeImportersMs = 900, ScienceScanMs = 310, TracksReadMs = 2200,
                AnimationStepMs = 2780, PlacementMs = 45,
            };
            Assert.AreEqual("[KSP1PartMod] perf: ours: mu fix 1.2 ms, shroud import 12 ms, B9 40 ms, native importers 900 ms, " +
                            "science scan 310 ms, tracks read 2200 ms (worker), animation step 2780 ms, placement 45 ms", PerfLines.Ours(o));
        }

        [Test] public void Rebuilds()
        {
            var r = new RebuildRecord
            {
                Since = "VAB entry", Count = 20, Ms = 1834.56, TexturesMs = 1200, TextureLoads = 61, Frequency = Hz,
                Slowest = new[] { new SlowPart { Name = "bluedog_LR87", TotalTicks = 210 }, new SlowPart { Name = "?", TotalTicks = 90 } },
            };
            Assert.AreEqual("[KSP1PartMod] perf: rebuilds since VAB entry: 20 prefab rebuild(s) in 1834.6 ms (textures 1200 ms over 61 load(s)); " +
                            "slowest: bluedog_LR87 210 ms, ? 90 ms", PerfLines.Rebuilds(r));
            Assert.AreEqual("[KSP1PartMod] perf: rebuilds since main menu: 0 prefab rebuild(s) in 0 ms (textures 0 ms over 0 load(s)); slowest: none",
                PerfLines.Rebuilds(new RebuildRecord { Since = "main menu", Frequency = Hz }));
        }

        [Test] public void CampaignAndGameStartAndStopped()
        {
            Assert.AreEqual("[KSP1PartMod] perf: campaign load: 431.25 s from Loading to KerbalSpaceCenter",
                PerfLines.CampaignLoad(431.25, "KerbalSpaceCenter"));
            Assert.AreEqual("[KSP1PartMod] perf: game start: main menu after 62.4 s", PerfLines.GameStart(62.4));
            Assert.AreEqual("[KSP1PartMod] perf: load: stopped for this session: System.Exception: x", PerfLines.LoadStopped("System.Exception: x"));
        }

        [Test] public void Memory()
        {
            var m = new MemorySnapshot
            {
                Moment = "VAB entry", Allocated = 3L << 30, Reserved = 4L << 30, MonoUsed = 900L << 20, MonoHeap = 1200L << 20,
                WorkingSet = 9L << 30, PrivateBytes = 10L << 30, SystemRamMb = 32768,
            };
            Assert.AreEqual("[KSP1PartMod] perf: memory at VAB entry: allocated 3072 MB, reserved 4096 MB, mono 900/1200 MB, process working set " +
                            "9216 MB, private 10240 MB; system RAM 32768 MB", PerfLines.Memory(m));
            Assert.AreEqual("[KSP1PartMod] perf: memory at main menu: allocated n/a, reserved n/a, mono n/a/n/a, process working set n/a, " +
                            "private n/a; system RAM n/a", PerfLines.Memory(new MemorySnapshot { Moment = "main menu" }));
        }

        [Test] public void CensusAndLargest()
        {
            var c = new TextureCensus("flight entry", 2) { CensusMs = 41.26 };
            c.Add("Saturn_S1C", 4096, 4096, "DXT5", 8, true, 1, false);
            c.Add("decal", 300, 200, "RGBA32", 32, false, 1, true);
            Assert.AreEqual("[KSP1PartMod] textures: census at flight entry: 2 live KSP1 texture(s) ~16 MB; by format DXT5 1 (16), RGBA32 1 (0.2); " +
                            "by size ≤256 0 (0), 512 1 (0.2), 1024 0 (0), 2048 0 (0), >2048 1 (16); without mipmaps 2 (16.2); " +
                            "uncompressed 1 (0.2); not a power of two 1 (0.2); readable 1 (0.2 MB of RAM); census 41.3 ms", PerfLines.Census(c));
            Assert.AreEqual("[KSP1PartMod] textures: largest at flight entry: Saturn_S1C 4096x4096 DXT5 16 MB, decal 300x200 RGBA32 0.2 MB",
                PerfLines.Largest(c));
            var empty = new TextureCensus("main menu", 3);
            StringAssert.StartsWith("[KSP1PartMod] textures: census at main menu: 0 live KSP1 texture(s) ~0 MB; by format none; ", PerfLines.Census(empty));
            Assert.AreEqual("[KSP1PartMod] textures: largest at main menu: none", PerfLines.Largest(empty));
        }

        [Test] public void ReportFile()
        {
            Assert.AreEqual("[KSP1PartMod] perf: report written to C:/game/mods/KSP1PartMod/perf/perf-20261028-200000.md (main menu)",
                PerfLines.ReportWritten("C:/game/mods/KSP1PartMod/perf/perf-20261028-200000.md", "main menu"));
            Assert.AreEqual("[KSP1PartMod] perf: report not written, off for this session: access denied", PerfLines.ReportOff("access denied"));
        }

        [Test] public void Frame()
        {
            var w = new FrameWindowSummary
            {
                Scene = "flight", Seconds = 10.02, Frames = 600, Fps = 59.9, FrameP95Ms = 18.234, OursAvgMs = 0.4126, OursP95Ms = 0.61,
                OursMaxMs = 2.5, OverheadMs = 0.0213,
            };
            Assert.AreEqual("[KSP1PartMod] perf: frame: flight over 10.02 s, 600 frame(s), 59.9 fps (frame p95 18.234 ms); ours 0.413 ms avg, " +
                            "0.61 ms p95, 2.5 ms max per frame; timing overhead ~0.021 ms per frame", PerfLines.FrameWindow(w));
            var f = new FeatureCost { Feature = PerfFeature.Plugins, AvgMs = 0.2, P95Ms = 0.3, MaxMs = 1.25, Parts = 120, Entries = 340 };
            Assert.AreEqual("[KSP1PartMod] perf: frame: flight: 2b plugins 0.2 ms avg, 0.3 ms p95, 1.25 ms max per frame; 120 part(s), 340 entr(ies)",
                PerfLines.FrameFeature("flight", f));
        }

        [Test] public void Numbers_InvariantCulture()
        {
            var was = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                StringAssert.Contains("in 30.5 ms", PerfLines.Reused(1, 30.5));
                StringAssert.Contains("after 1.5 s", PerfLines.GameStart(1.5));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = was;
            }
        }
    }
}
