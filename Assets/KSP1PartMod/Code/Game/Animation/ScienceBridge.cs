using System;
using System.Collections.Generic;
using KSP.Modules;
using KSP1PartMod.Native.Science;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The per-frame glue between a converted KSP1 experiment part and its KSP1 clips (science plan, Task 5). Built once by
    /// <see cref="Module_Ksp1Animations"/> when it binds the part's science, stepped every flight frame:
    /// <list type="bullet">
    /// <item>it reads each <c>ExperimentStanding.CurrentExperimentState</c> and keeps the last one; at
    /// <see cref="ScienceAnimRules.ShouldOpen"/> it opens every target of the configuration (the clip stays open afterwards, the
    /// player closes it with the existing button);</item>
    /// <item>for a configuration that waits (<see cref="Config.Gate"/>), it writes <c>PartIsDeployed</c> on change only: true once
    /// the clip has reached its open end during the run (<see cref="ScienceAnimRules.ReachedOpen"/>, latched until the run ends),
    /// false when no experiment runs. A clip refused while it moves (a one-way clip still playing back) is asked again while it is
    /// closed and still. A gate whose entry is gone (disabled, gated by B9) never blocks: true.</item>
    /// </list>
    /// Only <c>PartIsDeployed</c> is written on the game's data. No allocation per step while the standings keep their count.</summary>
    internal sealed class ScienceBridge
    {
        /// <summary>An existing entry of the part, as the bridge drives it (delegates bound once).</summary>
        internal sealed class Target
        {
            public readonly string Key;
            /// <summary>Not disabled, not switched off by B9 now.</summary>
            public readonly Func<bool> Live;
            /// <summary>Opens the clip; true when it started a move (or asked Redux to).</summary>
            public readonly Func<bool> Open;
            /// <summary>The toggle state, for a gate; null: this target is no gate source.</summary>
            public readonly Func<bool> Closed, Moving;
            internal bool LastClosed = true, LastMoving;

            public Target(string key, Func<bool> live, Func<bool> open, Func<bool> closed = null, Func<bool> moving = null)
            {
                Key = key;
                Live = live;
                Open = open;
                Closed = closed;
                Moving = moving;
            }
        }

        /// <summary>One <c>ExperimentConfiguration</c> of the part: its KSP2 id, the entries it opens, the one it waits for.</summary>
        internal sealed class Config
        {
            public readonly string Id;
            public readonly Target[] Targets;
            /// <summary>The target whose clip the countdown waits for; null: the configuration does not wait.</summary>
            public readonly Target Gate;

            public Config(string id, Target[] targets, Target gate)
            {
                Id = id;
                Targets = targets ?? new Target[0];
                Gate = gate;
            }
        }

        private static readonly Config[] NoConfigs = new Config[0];

        private readonly Data_ScienceExperiment _data;
        private readonly Config[] _configs;
        private Config[] _ofStanding = NoConfigs;
        private ExperimentState[] _last = new ExperimentState[0];
        private bool[] _seen = new bool[0];
        private int _mapped = -1;
        private int _runningFor = -1;
        private bool _latched;
        private bool? _written;

        public string Part { get; }
        /// <summary>A configuration waits for its clip: <c>IsPartAnimated</c> is true.</summary>
        public bool Waits { get; }
        /// <summary>Tests: the opens asked so far.</summary>
        internal int Opens { get; private set; }

        public ScienceBridge(string part, Data_ScienceExperiment data, IReadOnlyList<Config> configs)
        {
            Part = part;
            _data = data;
            var list = new List<Config>();
            if (configs != null)
                foreach (var c in configs)
                    if (c != null)
                    {
                        list.Add(c);
                        Waits |= c.Gate != null;
                    }
            _configs = list.ToArray();
        }

        public void Step()
        {
            var standings = _data?.ExperimentStandings;
            if (standings == null) return;
            var n = standings.Count;
            if (n != _mapped) Remap(standings);
            var running = -1;
            for (var i = 0; i < n; i++)
            {
                var s = standings[i];
                if (s == null) continue;
                var state = s.CurrentExperimentState;
                var previous = _seen[i] ? (ScienceRunState?)(ScienceRunState)(byte)_last[i] : null;
                if (ScienceAnimRules.ShouldOpen(previous, (ScienceRunState)(byte)state)) Open(_ofStanding[i]);
                _last[i] = state;
                _seen[i] = true;
                if (state == ExperimentState.RUNNING && running < 0) running = i;
            }
            if (!Waits) return;
            Write(Gate(running));
        }

        private bool Gate(int running)
        {
            if (running < 0)
            {
                _runningFor = -1;
                _latched = false;
                return false;
            }
            var g = _ofStanding[running]?.Gate;
            if (g == null || !g.Live()) return true; // no wait, or the entry is gone: never block
            var closed = g.Closed();
            var moving = g.Moving();
            if (_runningFor != running)
            {
                // A new run: transitions count from here, not from a frame of an earlier run.
                _runningFor = running;
                _latched = false;
                g.LastClosed = closed;
                g.LastMoving = moving;
            }
            if (!_latched && ScienceAnimRules.ReachedOpen(g.LastClosed, g.LastMoving, closed, moving)) _latched = true;
            else if (!_latched && closed && !moving && g.Open())
            {
                // Asked at the start of the run but refused while it moved: asked again.
                Opens++;
                closed = g.Closed();
                moving = g.Moving();
            }
            g.LastClosed = closed;
            g.LastMoving = moving;
            return _latched;
        }

        private void Write(bool deployed)
        {
            if (_written == deployed) return;
            _written = deployed;
            _data.PartIsDeployed = deployed;
        }

        private void Open(Config c)
        {
            if (c == null) return;
            var targets = c.Targets;
            for (var i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Live() && t.Open()) Opens++;
            }
        }

        /// <summary>Standing i to its configuration, by KSP2 id (the game drops unknown ids at its start, so indices may differ from
        /// the record). Allocates only when the count changes.</summary>
        private void Remap(List<ExperimentStanding> standings)
        {
            var n = standings.Count;
            _mapped = n;
            var map = new Config[n];
            var last = new ExperimentState[n];
            var seen = new bool[n];
            for (var i = 0; i < n; i++)
            {
                var id = standings[i]?.ExperimentID;
                foreach (var c in _configs)
                    if (string.Equals(c.Id, id, StringComparison.Ordinal))
                    {
                        map[i] = c;
                        break;
                    }
                // Keep what was seen of a standing with the same id at the same index.
                if (i < _ofStanding.Length && ReferenceEquals(_ofStanding[i], map[i]))
                {
                    last[i] = _last[i];
                    seen[i] = _seen[i];
                }
            }
            _ofStanding = map;
            _last = last;
            _seen = seen;
            _runningFor = -1;
        }
    }
}
