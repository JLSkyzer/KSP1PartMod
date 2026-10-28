using System;
using System.Diagnostics;
using KSP.Game;
using KSP.Messages;
using KSP1PartMod.Game.Perf;
using KSP1PartMod.Perf;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Frame cost per feature (performance plan Task 5), behind <c>perf_counter</c>. <see cref="ChannelFlush"/> lives
    /// across scenes and owns the one <see cref="FrameCostWindow"/>: the timed sites add to it (<see cref="Lap"/>,
    /// <see cref="AddSince"/>), its <c>LateUpdate</c> ends the frame (<see cref="EndFrame"/>), and the last game state names
    /// the scene; a change of scene closes the window early. With <c>perf_counter</c> off, each site reads
    /// <see cref="PerfOn"/> and nothing else: no timestamp, and the window is never created. Nothing here allocates per frame
    /// (strings only when a window closes). Never throws into the game: a failure turns the frame cost off for the session,
    /// with one line.</summary>
    internal sealed partial class ChannelFlush
    {
        /// <summary>The back-to-back timestamps of the overhead estimate (~0.3 ms, once).</summary>
        private const int OverheadTimestamps = 10001;

        private static FrameCostWindow _frame;
        private static bool _frameBroken, _forcePerfForTests, _subscribedToStates;
        private static double _windowSecondsForTests;
        private static string _scene = SceneOther;

        private const string SceneOther = "other";

        /// <summary><c>perf_counter</c>: the one check of every timed site.</summary>
        internal static bool PerfOn => _forcePerfForTests || KSP1PartModPlugin.Animations?.PerfCounter == true;

        /// <summary>The window (null before the first timed site, and always null with <c>perf_counter</c> off).</summary>
        internal static FrameCostWindow FrameWindow => _frame;

        /// <summary>The scene the open window is counted in.</summary>
        internal static string FrameScene => _scene;

        /// <summary>The window, created at the first call with <c>perf_counter</c> on; null when off or broken.</summary>
        internal static FrameCostWindow FrameWindowOrCreate()
        {
            if (_frame != null) return _frame;
            if (_frameBroken || !PerfOn) return null;
            try
            {
                var seconds = _windowSecondsForTests > 0 ? _windowSecondsForTests : KSP1PartModPlugin.Animations.PerfWindowSeconds;
                var w = new FrameCostWindow(Stopwatch.Frequency, seconds)
                {
                    TimerOverheadTicks = TimerOverhead.Mean(Stopwatch.GetTimestamp, OverheadTimestamps),
                };
                w.Start(Stopwatch.GetTimestamp());
                _frame = w;
                return w;
            }
            catch (Exception e)
            {
                BreakFrameCost(e);
                return null;
            }
        }

        /// <summary>Adds the time since <paramref name="since"/> to <paramref name="feature"/> and returns now, the start of the
        /// next block. <paramref name="window"/> may be null (the time is then dropped).</summary>
        internal static long Lap(FrameCostWindow window, PerfFeature feature, long since)
        {
            var now = Stopwatch.GetTimestamp();
            window?.Add(feature, now - since);
            return now;
        }

        /// <summary>A timed site outside <c>Drive</c> (a patch, a <c>LateUpdate</c>, a render callback): the time since
        /// <paramref name="since"/>, counted in <paramref name="feature"/>, with its two timestamps for the overhead. The first
        /// such site also creates the behaviour if nothing has (no KSP1 table registered and no game-state subscription), so
        /// that its frames end.</summary>
        internal static void AddSince(PerfFeature feature, long since)
        {
            var now = Stopwatch.GetTimestamp();
            if (_instance == null && !_instanceTried) TryEnsureInstance();
            FrameWindowOrCreate()?.Add(feature, now - since, 2);
        }

        private static bool _instanceTried;

        private static void TryEnsureInstance()
        {
            _instanceTried = true;
            try
            {
                EnsureInstance();
            }
            catch (Exception e)
            {
                BreakFrameCost(e);
            }
        }

        /// <summary>From <c>LateUpdate</c>, after the flush: the frame ends (<paramref name="unscaledDeltaTime"/> gives the fps and
        /// the frame p95), and a window that has lasted <c>perf_window_seconds</c> writes its lines.</summary>
        internal static void EndFrame(float unscaledDeltaTime)
        {
            var w = FrameWindowOrCreate();
            if (w == null) return;
            try
            {
                w.EndFrame((long)(unscaledDeltaTime * (double)Stopwatch.Frequency));
                if (w.TryReport(Stopwatch.GetTimestamp(), _scene, out var lines, out var count)) WriteWindow(w, lines, count);
            }
            catch (Exception e)
            {
                BreakFrameCost(e);
            }
        }

        /// <summary>The scene of a game state, as printed on the frame lines.</summary>
        internal static string SceneOf(GameState state)
        {
            switch (state)
            {
                case GameState.VehicleAssemblyBuilder: return "VAB";
                case GameState.FlightView: return "flight";
                case GameState.Map3DView: return "map";
                case GameState.KerbalSpaceCenter: return "KSC";
                default: return SceneOther;
            }
        }

        /// <summary>A game state was entered: a change of scene closes the open window (its lines, counted in the scene left)
        /// and the next one counts in <paramref name="state"/>'s scene. A VAB or flight entry also creates the behaviour, so
        /// that the patches of a craft without KSP1 animation module still end their frames.</summary>
        internal static void NoteGameState(GameState state)
        {
            if (!PerfOn) return;
            try
            {
                var scene = SceneOf(state);
                if (state == GameState.VehicleAssemblyBuilder || state == GameState.FlightView) EnsureInstance();
                if (scene == _scene) return;
                var w = FrameWindowOrCreate();
                if (w != null && w.TryReport(Stopwatch.GetTimestamp(), _scene, out var lines, out var count, force: true))
                    WriteWindow(w, lines, count);
                _scene = scene;
            }
            catch (Exception e)
            {
                BreakFrameCost(e);
            }
        }

        /// <summary>From <c>OnInitialized</c>, with <c>perf_counter</c> on only: the scene of the frame lines.</summary>
        internal static void SubscribeToGameStates(GameInstance game)
        {
            if (!PerfOn || _subscribedToStates) return;
            try
            {
                if (game?.Messages == null)
                {
                    KSP1PartModPlugin.Log?.LogWarning(PerfLines.Prefix + "frame: no message center, every frame line says 'other'");
                    return;
                }
                game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameState);
                _subscribedToStates = true;
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogWarning(PerfLines.Prefix + "frame: no game-state subscription, every frame line says 'other': " + e);
            }
        }

        private static void OnGameState(MessageCenterMessage message)
        {
            if (message is GameStateChangedMessage m && m.CurrentState != m.PreviousState) NoteGameState(m.CurrentState);
        }

        private static void WriteWindow(FrameCostWindow w, string[] lines, int count)
        {
            var log = KSP1PartModPlugin.Log;
            for (var i = 0; i < count; i++) log?.LogInfo(lines[i]);
            PerfReportWriter.NoteFrameWindow(w.Last);
        }

        private static void BreakFrameCost(Exception e)
        {
            if (_frameBroken) return;
            _frameBroken = true;
            _frame = null;
            KSP1PartModPlugin.Log?.LogError(PerfLines.Prefix + "frame: cost per feature stopped for this session: " + e);
        }

        /// <summary>Tests only: <c>perf_counter</c> forced on or back to the settings, no window, scene <c>other</c>.</summary>
        internal static void ResetFrameCostForTests(bool forcePerf, double windowSeconds = 0)
        {
            _forcePerfForTests = forcePerf;
            _windowSecondsForTests = windowSeconds;
            _frame = null;
            _frameBroken = false;
            _instanceTried = false;
            _scene = SceneOther;
        }
    }
}
