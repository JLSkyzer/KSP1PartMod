using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.Game;
using KSP1PartMod.Animation.Textures;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Perf;
using KSP1PartMod.Perf;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Performance plan Task 4: the report file (temporary file and replace, pruning to the 10 newest, off after an
    /// I/O error with one line), the census glue against real textures it creates and destroys itself, and the moments.</summary>
    public class PerfMemoryTests
    {
        private string _folder;
        private readonly List<string> _info = new List<string>(), _debug = new List<string>(), _error = new List<string>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        [SetUp] public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ksp1partmod-perf-" + Guid.NewGuid().ToString("N"));
            _info.Clear();
            _debug.Clear();
            _error.Clear();
            PerfReportWriter.Reset();
        }

        [TearDown] public void TearDown()
        {
            foreach (var t in _textures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
            _textures.Clear();
            PerfReportWriter.Reset();
            if (!Directory.Exists(_folder)) return;
            foreach (var f in Directory.GetFiles(_folder)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_folder, true);
        }

        private static readonly DateTime Start = new DateTime(2026, 10, 28, 20, 0, 0);

        private PerfReportFile File_() => new PerfReportFile(_folder, Start, _info.Add, _debug.Add, _error.Add);

        // ------------------------------------------------------------------ report file

        [Test] public void Write_CreatesTheFolderAndTheFile_FirstLineInfoThenDebug()
        {
            var file = File_();
            Assert.AreEqual(Path.Combine(_folder, "perf-20261028-200000.md"), file.FullPath);
            Assert.IsTrue(file.Write("# one\n", "main menu entry"));
            Assert.AreEqual("# one\n", File.ReadAllText(file.FullPath));
            Assert.AreEqual(new[] { $"[KSP1PartMod] perf: report written to {file.FullPath} (main menu entry)" }, _info);
            Assert.IsTrue(file.Write("# two\n", "VAB entry"));
            Assert.AreEqual(1, _info.Count, "the info line is written once a session");
            Assert.AreEqual(new[] { $"[KSP1PartMod] perf: report written to {file.FullPath} (VAB entry)" }, _debug);
            CollectionAssert.IsEmpty(_error);
        }

        [Test] public void Write_ReplacesTheFile_NoTemporaryLeft()
        {
            var file = File_();
            file.Write("# first, a longer text than the second\n", "a");
            file.Write("# second\n", "b");
            Assert.AreEqual("# second\n", File.ReadAllText(file.FullPath));
            CollectionAssert.AreEquivalent(new[] { "perf-20261028-200000.md" }, Directory.GetFiles(_folder).Select(Path.GetFileName));
        }

        [Test] public void Write_PrunesToTheTenNewest_OwnFileAlwaysKept()
        {
            Directory.CreateDirectory(_folder);
            // Twelve older sessions, one newer (a clock change), and files that are not reports.
            for (var i = 0; i < 12; i++) File.WriteAllText(Path.Combine(_folder, $"perf-202610{10 + i:00}-120000.md"), "old");
            File.WriteAllText(Path.Combine(_folder, "perf-20261029-080000.md"), "newer");
            File.WriteAllText(Path.Combine(_folder, "notes.md"), "mine");
            File.WriteAllText(Path.Combine(_folder, "perf-20261001-000000.txt"), "not a report");

            var file = File_();
            file.Write("# now\n", "end of import");

            var reports = Directory.GetFiles(_folder, "perf-*.md").Select(Path.GetFileName).OrderByDescending(n => n, StringComparer.Ordinal).ToList();
            Assert.AreEqual(PerfReportFile.Keep, reports.Count);
            CollectionAssert.Contains(reports, "perf-20261028-200000.md");
            CollectionAssert.Contains(reports, "perf-20261029-080000.md");
            CollectionAssert.Contains(reports, "perf-20261021-120000.md");
            CollectionAssert.DoesNotContain(reports, "perf-20261013-120000.md");
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "notes.md")));
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "perf-20261001-000000.txt")));
            CollectionAssert.IsEmpty(_error);
        }

        [Test] public void Write_ReadOnlyReport_OneErrorLine_ThenOffForTheSession()
        {
            var file = File_();
            Assert.IsTrue(file.Write("# one\n", "a"));
            File.SetAttributes(file.FullPath, FileAttributes.ReadOnly);

            Assert.IsFalse(file.Write("# two\n", "b"));
            Assert.IsFalse(file.Write("# three\n", "c"));

            Assert.IsTrue(file.Off);
            Assert.AreEqual(1, _error.Count, string.Join("\n", _error));
            StringAssert.StartsWith("[KSP1PartMod] perf: report not written, off for this session: ", _error[0]);
            Assert.AreEqual("# one\n", File.ReadAllText(file.FullPath));
            Assert.IsFalse(File.Exists(file.FullPath + ".tmp"), "the temporary file is removed");
            CollectionAssert.IsEmpty(_debug);
        }

        // ------------------------------------------------------------------ report contents

        [Test] public void Writer_KeepsTheLastFrameWindowPerScene_AndCapsTheMoments()
        {
            PerfReportWriter.NoteFrameWindow(new FrameWindowSummary { Scene = "VAB", Frames = 1 });
            PerfReportWriter.NoteFrameWindow(new FrameWindowSummary { Scene = "flight", Frames = 2 });
            PerfReportWriter.NoteFrameWindow(new FrameWindowSummary { Scene = "VAB", Frames = 3 });
            CollectionAssert.AreEqual(new[] { "VAB", "flight" }, PerfReportWriter.FrameWindows.Select(w => w.Scene));
            Assert.AreEqual(3, PerfReportWriter.FrameWindows[0].Frames);

            for (var i = 0; i < PerfReportWriter.MaxMoments + 5; i++)
                PerfReportWriter.NoteMoment(new MemorySnapshot { Moment = "m" + i }, new TextureCensus("m" + i, 1));
            Assert.AreEqual(PerfReportWriter.MaxMoments, PerfReportWriter.Snapshots.Count);
            Assert.AreEqual("m5", PerfReportWriter.Snapshots[0].Moment, "the oldest go first");
            Assert.AreEqual(PerfReportWriter.MaxMoments, PerfReportWriter.Censuses.Count);

            var text = PerfReportWriter.Build(null);
            StringAssert.Contains("| m5 |", text);
            StringAssert.Contains("textures: census at m5:", text);
        }

        // ------------------------------------------------------------------ census glue

        private Texture2D Make(int w, int h, TextureFormat format, bool mips)
        {
            var t = new Texture2D(w, h, format, mips) { name = "perf-test" };
            _textures.Add(t);
            return t;
        }

        [Test] public void Census_ReadsOnlyTheSeenTextures_WithTheirFormatSizeMipsAndReadability()
        {
            var dxt1 = Make(4, 4, TextureFormat.DXT1, false);
            var rgba = Make(300, 200, TextureFormat.RGBA32, false);
            var dxt5 = Make(512, 512, TextureFormat.DXT5, true);
            var other = Make(64, 64, TextureFormat.RGBA32, false);
            var seen = new Dictionary<EntityId, Ksp1Textures.TextureOrigin>
            {
                [dxt1.GetEntityId()] = new Ksp1Textures.TextureOrigin("Squad/Parts/tiny", ".dds"),
                [rgba.GetEntityId()] = new Ksp1Textures.TextureOrigin("decal.png", ".png"),
                [dxt5.GetEntityId()] = new Ksp1Textures.TextureOrigin("BDB/s1c", null),
            };

            var c = Ksp1Textures.Census("VAB entry", 10, new[] { dxt1, null, rgba, dxt5, other }, seen);

            Assert.AreEqual(10, dxt5.mipmapCount);
            var dxt1Bytes = TextureMemory.Estimate(4, 4, 1, 4, true);
            var rgbaBytes = TextureMemory.Estimate(300, 200, 1, 32, false);
            var dxt5Bytes = TextureMemory.Estimate(512, 512, 10, 8, true);
            Assert.AreEqual(3, c.All.Count, "the texture we did not see loaded is not counted");
            Assert.AreEqual(dxt1Bytes + rgbaBytes + dxt5Bytes, c.All.Bytes);
            Assert.AreEqual(240000, rgbaBytes);

            var formats = c.ByFormat.ToDictionary(kv => kv.Key, kv => kv.Value);
            CollectionAssert.AreEquivalent(new[] { "DXT1", "RGBA32", "DXT5" }, formats.Keys);
            Assert.AreEqual(dxt5Bytes, formats["DXT5"].Bytes);

            Assert.AreEqual(1, c.WithoutMipmaps.Count, "RGBA32 without mips; the 4x4 DXT1 is too small to count");
            Assert.AreEqual(rgbaBytes, c.WithoutMipmaps.Bytes);
            Assert.AreEqual(1, c.Uncompressed.Count);
            Assert.AreEqual(1, c.NotPowerOfTwo.Count);
            Assert.AreEqual(new[] { dxt1, rgba, dxt5 }.Count(t => t.isReadable), c.Readable.Count);
            Assert.IsTrue(rgba.isReadable);
            Assert.AreEqual(1, c.BySize[0].Count, "4x4");
            Assert.AreEqual(2, c.BySize[1].Count, "300x200 and 512x512");

            CollectionAssert.AreEqual(new[] { "BDB/s1c", "decal.png", "Squad/Parts/tiny.dds" }, c.Largest.Select(t => t.Name),
                "largest first; the source extension is added to a name without it");
            var largest = c.Largest.First();
            Assert.AreEqual(512, largest.Width);
            Assert.AreEqual("DXT5", largest.Format);
        }

        [Test] public void Census_LeavesTheTexturesAsTheyWere()
        {
            var rgba = Make(16, 16, TextureFormat.RGBA32, true);
            var before = (rgba.isReadable, rgba.format, rgba.mipmapCount, rgba.width, rgba.height, rgba.imageContentsHash);
            var seen = new Dictionary<EntityId, Ksp1Textures.TextureOrigin> { [rgba.GetEntityId()] = new Ksp1Textures.TextureOrigin("t", null) };
            Ksp1Textures.Census("x", 1, new[] { rgba }, seen);
            Assert.AreEqual(before, (rgba.isReadable, rgba.format, rgba.mipmapCount, rgba.width, rgba.height, rgba.imageContentsHash));
        }

        [Test] public void Census_EmptyWhenNothingWasSeen()
        {
            var c = Ksp1Textures.Census("main menu entry", 3, new[] { Make(8, 8, TextureFormat.RGBA32, false) },
                new Dictionary<EntityId, Ksp1Textures.TextureOrigin>());
            Assert.AreEqual(0, c.All.Count);
            Assert.AreEqual("[KSP1PartMod] textures: largest at main menu entry: none", PerfLines.Largest(c));
        }

        // ------------------------------------------------------------------ moments

        [Test] public void Moments_VabFlightAndMainMenu()
        {
            Assert.AreEqual("VAB", Ksp1Textures.MomentOf(GameState.VehicleAssemblyBuilder, GameState.KerbalSpaceCenter, false));
            Assert.AreEqual("flight", Ksp1Textures.MomentOf(GameState.FlightView, GameState.VehicleAssemblyBuilder, false));
            Assert.IsNull(Ksp1Textures.MomentOf(GameState.FlightView, GameState.Map3DView, true), "back from the map is not a moment");
            Assert.AreEqual("main menu", Ksp1Textures.MomentOf(GameState.MainMenu, GameState.KerbalSpaceCenter, true),
                "after leaving a campaign too");
            Assert.IsNull(Ksp1Textures.MomentOf(GameState.MainMenu, GameState.WarmUpLoading, false), "only with perf_memory_lines");
            Assert.IsNull(Ksp1Textures.MomentOf(GameState.KerbalSpaceCenter, GameState.Loading, true));
        }
    }
}
