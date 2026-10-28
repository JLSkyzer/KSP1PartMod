using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Textures;
using KSP1PartMod.Perf;
using UnityEngine;

namespace KSP1PartMod.Game.Perf
{
    /// <summary>The session's report file, <c>&lt;folder&gt;/perf-&lt;yyyyMMdd-HHmmss&gt;.md</c> (performance plan Task 4). Each
    /// write goes to a temporary file that then replaces the report. After the first write, only the 10 newest
    /// <c>perf-*.md</c> are kept. Any I/O error logs one line and turns the file off for the session.</summary>
    internal sealed class PerfReportFile
    {
        public const int Keep = 10;
        private const string Pattern = "perf-*.md";

        private readonly string _folder;
        private readonly Action<string> _info, _debug, _error;
        private bool _written;

        public PerfReportFile(string folder, DateTime sessionStart, Action<string> info, Action<string> debug, Action<string> error)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            FullPath = Path.Combine(folder, FileName(sessionStart));
            _info = info ?? (_ => { });
            _debug = debug ?? (_ => { });
            _error = error ?? (_ => { });
        }

        public string FullPath { get; }

        /// <summary>An I/O error happened: nothing more is written this session.</summary>
        public bool Off { get; private set; }

        public static string FileName(DateTime sessionStart) =>
            "perf-" + sessionStart.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".md";

        /// <summary>Writes the report (temporary file, then a replace). False when off or on an I/O error.</summary>
        public bool Write(string content, string when)
        {
            if (Off) return false;
            var temporary = FullPath + ".tmp";
            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllText(temporary, content ?? "", new UTF8Encoding(false));
                if (File.Exists(FullPath)) File.Replace(temporary, FullPath, null);
                else File.Move(temporary, FullPath);
                if (!_written) Prune();
            }
            catch (Exception e)
            {
                Off = true;
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception)
                {
                    // The line below is the one error line of the session.
                }
                _error(PerfLines.ReportOff(e.GetType().Name + ": " + e.Message));
                return false;
            }
            var line = PerfLines.ReportWritten(FullPath, when);
            if (_written) _debug(line);
            else _info(line);
            _written = true;
            return true;
        }

        /// <summary>Deletes all but the <see cref="Keep"/> newest reports (by the session stamp in the name); this session's is
        /// always kept.</summary>
        private void Prune()
        {
            var own = Path.GetFileName(FullPath);
            var others = Directory.GetFiles(_folder, Pattern)
                .Select(Path.GetFileName)
                .Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !string.Equals(n, own, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .Skip(Keep - 1);
            foreach (var name in others) File.Delete(Path.Combine(_folder, name));
        }
    }

    /// <summary>Builds the session's <see cref="PerfReport"/> from the load timeline, the memory moments and the last frame
    /// window per scene, and writes it at each memory moment (<c>perf_report_file</c>). Synchronous, main thread: a few kB, a
    /// few times per session.</summary>
    internal static class PerfReportWriter
    {
        public const string Folder = "perf";
        /// <summary>The moments kept for the report (the oldest go first): the session has about ten.</summary>
        public const int MaxMoments = 64;

        private static DateTime _sessionStart = DateTime.Now;
        private static PerfReportFile _file;
        private static bool _off;
        private static readonly List<MemorySnapshot> SnapshotList = new List<MemorySnapshot>();
        private static readonly List<TextureCensus> CensusList = new List<TextureCensus>();
        private static readonly List<FrameWindowSummary> FrameWindowList = new List<FrameWindowSummary>();

        internal static IReadOnlyList<MemorySnapshot> Snapshots => SnapshotList;
        internal static IReadOnlyList<TextureCensus> Censuses => CensusList;
        internal static IReadOnlyList<FrameWindowSummary> FrameWindows => FrameWindowList;

        /// <summary>From <c>OnPreInitialized</c>: the session stamp of the file name.</summary>
        internal static void Begin(DateTime sessionStart) => _sessionStart = sessionStart;

        /// <summary>A memory moment (either may be null).</summary>
        internal static void NoteMoment(MemorySnapshot snapshot, TextureCensus census)
        {
            if (snapshot != null) Add(SnapshotList, snapshot);
            if (census != null) Add(CensusList, census);
        }

        /// <summary>A closed frame window (Task 5): the last one of each scene is kept.</summary>
        internal static void NoteFrameWindow(FrameWindowSummary window)
        {
            if (window == null) return;
            var at = FrameWindowList.FindIndex(w => w.Scene == window.Scene);
            if (at >= 0) FrameWindowList[at] = window;
            else FrameWindowList.Add(window);
        }

        private static void Add<T>(List<T> list, T item)
        {
            if (list.Count >= MaxMoments) list.RemoveAt(0);
            list.Add(item);
        }

        /// <summary>Rewrites the report after the lines of a moment. Never throws.</summary>
        internal static void Write(string moment)
        {
            var settings = KSP1PartModPlugin.Animations ?? AnimationSettings.Defaults;
            if (!settings.PerfReportFile || _off) return;
            try
            {
                _file ??= new PerfReportFile(Path.Combine(KSP1PartModPlugin.ModFolder, Folder), _sessionStart,
                    l => KSP1PartModPlugin.Log?.LogInfo(l), l => KSP1PartModPlugin.Log?.LogDebug(l), l => KSP1PartModPlugin.Log?.LogError(l));
                if (_file.Off) return;
                _file.Write(Build(settings), moment);
            }
            catch (Exception e)
            {
                // Building the report failed (the file side catches its own errors): off as well, with one line.
                _off = true;
                KSP1PartModPlugin.Log?.LogError(PerfLines.ReportOff(e.ToString()));
            }
        }

        internal static string Build(AnimationSettings settings) =>
            PerfReport.Build(Header(), LoadTimeline.FlowSteps, LoadTimeline.LastImport, LoadTimeline.LastOurs, LoadTimeline.Rebuilds,
                SnapshotList, CensusList, FrameWindowList, settings);

        private static PerfSessionHeader Header()
        {
            var h = new PerfSessionHeader
            {
                Started = _sessionStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ModVersion = KSP1PartModPlugin.Version,
                MainMenuAfterSeconds = LoadTimeline.MainMenuAfterSeconds,
            };
            try
            {
                h.GameVersion = Application.version;
                h.UnityVersion = Application.unityVersion;
                h.Card = SystemInfo.graphicsDeviceName;
                h.CardMb = SystemInfo.graphicsMemorySize;
                h.SystemRamMb = SystemInfo.systemMemorySize;
            }
            catch (Exception)
            {
                // Unknown facts print "?" or "n/a".
            }
            h.CampaignLoads.AddRange(LoadTimeline.CampaignLoads);
            return h;
        }

        /// <summary>Back to a fresh session (tests).</summary>
        internal static void Reset()
        {
            _file = null;
            _off = false;
            _sessionStart = DateTime.Now;
            SnapshotList.Clear();
            CensusList.Clear();
            FrameWindowList.Clear();
        }
    }
}
