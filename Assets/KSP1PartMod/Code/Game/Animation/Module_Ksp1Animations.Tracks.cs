using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Modules;
using KSP.VFX.Reentry;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The material and light tracks of the part (spec 2a.3 / 2a.4): who owns each clip's time, binding on
    /// the instance, and the per-frame sampling of the clips Redux plays.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>(part, clip) pairs already logged as having no owner: once per part type and session.</summary>
        private static readonly HashSet<string> LoggedNoOwner = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>(part, scene) pairs whose shader set was logged; parts whose reentry watch was logged.</summary>
        private static readonly HashSet<string> LoggedShaders = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedReentry = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Main-throttle fields already logged (<c>throttle from …</c>), and the <c>output read as throttle</c> line.</summary>
        private static readonly HashSet<string> LoggedThrottleFields = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loggedOutputAsThrottle;

        // Tracks (spec 2a.3 / 2a.4). Bound when the TrackCache generation of the current load is ready: at Setup, or on
        // the first Drive after it (one volatile read per frame while pending); rebound when the generation changes.
        private bool _tracksWanted, _tracksPending;
        private int _tracksGeneration = -1;
        private IReadOnlyDictionary<string, float> _saved;
        /// <summary>Entries not bound at Setup while the cache was pending: retried once with the tracks.</summary>
        private readonly List<(AnimEntryDef def, string note)> _retry = new List<(AnimEntryDef, string)>();
        private ChannelTable _table;
        private RendererChannels _sink;
        private PartTransforms _transforms;
        /// <summary>Tracks of clips Redux's legacy runtime plays: their time is read from Redux.</summary>
        private readonly List<ReduxTracks> _reduxTracks = new List<ReduxTracks>();

        /// <summary>This part's entry of the current cache, null when the worker has none for it.</summary>
        private PartTracks PartTracksOrNull() => TrackCache.TryGet(_name, out var t) ? t : null;

        /// <summary>At shutdown: nothing bound, nothing pending.</summary>
        private void ResetTracks()
        {
            _reduxTracks.Clear();
            _retry.Clear();
            _tracksWanted = false;
            _tracksPending = false;
            _tracksGeneration = -1;
            ReleaseChannels();
            _transforms = null;
        }

        // ---------------------------------------------------------------- tracks

        /// <summary>Binds the material and light tracks of this part (spec 2a.3 / 2a.4) on a fresh table: each clip's
        /// tracks follow the clip's owner. <paramref name="late"/>: the cache became ready after Setup; the entries not
        /// bound then are retried, tracks-only. Never throws; a part whose tracks failed keeps its phase 1
        /// behaviour.</summary>
        private void BindTracks(PartTracks tracks, int generation, bool inEditor, bool late)
        {
            _tracksPending = false;
            _tracksGeneration = generation;
            foreach (var e in _entries) e.Tracks = e.ActiveTracks = NoSamplers;
            _reduxTracks.Clear();
            NewChannels();
            if (tracks != null && tracks.Failed) tracks = null; // logged by the worker; phase 1 for this part

            if (late) RetryUnbound(tracks, inEditor);
            if (tracks != null && tracks.Sets.Count > 0) BindSetsOfPart(tracks);
            FinishChannels();
        }

        private void BindSetsOfPart(PartTracks tracks)
        {
            try
            {
                if (_transforms == null) _transforms = new PartTransforms(gameObject.transform);
                var reduxLights = ReduxLights();
                var runtime = GetComponentInChildren<Ksp1DeployableAnimationRuntime>(true);
                var clips = new List<string>();
                foreach (var set in tracks.Sets)
                    if (set?.Clip != null && !clips.Contains(set.Clip)) clips.Add(set.Clip);
                foreach (var clip in clips)
                {
                    try
                    {
                        BindClip(tracks, clip, runtime, reduxLights);
                    }
                    catch (Exception ex)
                    {
                        Error($"{_name} tracks '{clip}' not bound: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"{_name} binding tracks failed: {ex}");
            }
        }

        /// <summary>The entries Setup left unbound while the cache was pending: tracks-only now, or their phase 1 line.</summary>
        private void RetryUnbound(PartTracks tracks, bool inEditor)
        {
            var retry = _retry.ToArray();
            _retry.Clear();
            foreach (var (def, setupNote) in retry)
            {
                try
                {
                    var e = tracks == null ? null : Build(def, _saved, tracks, out _);
                    if (e == null)
                    {
                        Log($"{_name} {def.Kind} '{Subject(def)}' not bound: {setupNote}");
                        continue;
                    }
                    _entries.Add(e);
                    e.Writer?.Register(_table, _sink);
                    if (ControlLabels.HasAction(def.Kind))
                    {
                        int buttons = 0, actions = 0;
                        var dynamicLabels = false;
                        AddControl(e, inEditor, ref buttons, ref actions, ref dynamicLabels);
                    }
                    Step(e, 0f, inEditor, catchErrors: true);
                }
                catch (Exception ex)
                {
                    Error($"{_name} {def.Kind} '{Subject(def)}' disabled for this session: {ex}");
                }
            }
        }

        /// <summary>One clip: its owner is one of our entries (each entry naming it), else Redux's legacy runtime when
        /// it plays the clip, else nobody (logged once per part type, tracks skipped).</summary>
        private void BindClip(PartTracks tracks, string clip, Ksp1DeployableAnimationRuntime runtime, IReadOnlyCollection<Light> reduxLights)
        {
            var owned = false;
            foreach (var e in _entries)
            {
                if (e.Disabled) continue;
                var primary = OwnsClip(e, clip, out var activeClip);
                if (!primary && !activeClip) continue;
                owned = true;
                if (primary) e.Tracks = BindEntryClip(e, tracks, clip, e.States.Length > 0 ? e.Holders : null, false, reduxLights);
                if (activeClip) e.ActiveTracks = BindEntryClip(e, tracks, clip, e.ActiveStates.Length > 0 ? e.ActiveHolders : null,
                    e.Group != null, reduxLights);
            }
            if (owned) return;

            if (runtime != null && runtime.enabled && Contains(runtime.AnimationNames, clip))
            {
                BindReduxClip(tracks, clip, runtime, reduxLights);
                return;
            }
            if (LoggedNoOwner.Add(_name + "|" + clip))
                Log($"tracks: {_name} clip '{clip}' has no owner, tracks skipped");
        }

        /// <summary>Whether the entry plays <paramref name="clip"/>: as its clip, or as its second clip (Group active,
        /// parachute full). Color, AeroGate and GearBridge entries own no clip time.</summary>
        private static bool OwnsClip(Entry e, string clip, out bool second)
        {
            second = false;
            var kind = e.Def.Kind;
            if (kind == EntryKind.Color || kind == EntryKind.AeroGate || kind == EntryKind.GearBridge) return false;
            second = (kind == EntryKind.Group || kind == EntryKind.ChuteFollower) && string.Equals(e.Def.SecondClip, clip, StringComparison.Ordinal);
            return string.Equals(e.Def.Clip, clip, StringComparison.Ordinal);
        }

        /// <summary>The samplers of one clip of an entry. With states, the hosts are the objects whose
        /// <c>Animation</c> the entry took (its lookup already chose them). Tracks-only, the model's hosts, chosen with
        /// the entry's KSP1 lookup (<see cref="AnimationLookup"/>: first, all, or by object name).</summary>
        private TrackSampler[] BindEntryClip(Entry e, PartTracks tracks, string clip, List<GameObject> holders, bool loop,
            IReadOnlyCollection<Light> reduxLights)
        {
            var hosts = new List<(TrackSet set, int index)>();
            foreach (var set in tracks.Sets)
            {
                if (!string.Equals(set.Clip, clip, StringComparison.Ordinal)) continue;
                var picked = TrackBinder.Hosts(_transforms, set, tracks, out var note);
                foreach (var i in picked)
                    if (holders == null || holders.Contains(_transforms.Transforms[i].gameObject)) hosts.Add((set, i));
                if (note != null) Log($"{_name} tracks '{clip}': {note}");
            }
            if (holders == null && hosts.Count > 1)
            {
                hosts.Sort((a, b) => a.index.CompareTo(b.index));
                var candidates = hosts.Select(h => new AnimationCandidate(_transforms.Transforms[h.index].name, true, false)).ToArray();
                var lookup = AnimationLookup.Select(candidates, AnimationLookup.For(e.Def.Kind), e.Def.Kind == EntryKind.Gear ? e.Def.AnimatorName : null);
                hosts = lookup.Picked.Select(i => hosts[i]).ToList();
            }
            return BindSets(tracks, clip, hosts, e.Def, loop, reduxLights);
        }

        /// <summary>A clip Redux's legacy runtime plays (<c>Ksp1DeployableAnimationRuntime</c>, which drives every
        /// <c>Animation</c> holding it): each host follows its own state's time, weight and enabled flag (read only),
        /// or, for a clip without state, the runtime's <c>NormalizedTime</c>.</summary>
        private void BindReduxClip(PartTracks tracks, string clip, Ksp1DeployableAnimationRuntime runtime, IReadOnlyCollection<Light> reduxLights)
        {
            var hosts = new List<(TrackSet set, int index)>();
            foreach (var set in tracks.Sets)
            {
                if (!string.Equals(set.Clip, clip, StringComparison.Ordinal)) continue;
                var picked = TrackBinder.Hosts(_transforms, set, tracks, out var note);
                foreach (var i in picked)
                    if (!set.HasState || StateOn(_transforms.Transforms[i], clip) != null) hosts.Add((set, i));
                if (note != null) Log($"{_name} tracks '{clip}': {note}");
            }
            foreach (var (set, sampler, host) in BindEach(tracks, clip, hosts, null, false, reduxLights))
            {
                sampler.Loop = false; // Redux's runtime forces ClampForever on every state it plays
                _reduxTracks.Add(new ReduxTracks(sampler, set.HasState ? StateOn(host, clip) : null, runtime));
            }
        }

        private static AnimationState StateOn(Transform host, string clip)
        {
            var a = host.GetComponent<UnityEngine.Animation>();
            return a == null ? null : a[clip];
        }

        private TrackSampler[] BindSets(PartTracks tracks, string clip, List<(TrackSet set, int index)> hosts, AnimEntryDef def,
            bool loop, IReadOnlyCollection<Light> reduxLights) =>
            BindEach(tracks, clip, hosts, def, loop, reduxLights).Select(b => b.sampler).ToArray();

        /// <summary>Binds each set on its chosen hosts, logs one line per set, and returns every sampler with its set and
        /// host.</summary>
        private List<(TrackSet set, TrackSampler sampler, Transform host)> BindEach(PartTracks tracks, string clip,
            List<(TrackSet set, int index)> hosts, AnimEntryDef def, bool loop, IReadOnlyCollection<Light> reduxLights)
        {
            var result = new List<(TrackSet, TrackSampler, Transform)>();
            foreach (var set in tracks.Sets)
            {
                if (!string.Equals(set.Clip, clip, StringComparison.Ordinal)) continue;
                var mine = hosts.Where(h => ReferenceEquals(h.set, set)).Select(h => _transforms.Transforms[h.index]).ToList();
                if (mine.Count == 0) continue;
                var bound = TrackBinder.Bind(_transforms, mine, set, def, _table, _sink, KSP1PartModPlugin.Animations, reduxLights, out var note);
                Log($"{_name} {note}");
                foreach (var path in bound.ReduxLights) Log($"{_name} light '{path}' left to Module_Light");
                for (var i = 0; i < bound.Samplers.Length; i++)
                {
                    if (loop) bound.Samplers[i].Loop = true; // a Group's active clip loops (KSP1 plays it with WrapMode.Loop)
                    result.Add((set, bound.Samplers[i], bound.Hosts[i]));
                }
            }
            return result;
        }

        /// <summary>The lights of every <c>Module_Light</c> of the part (<c>kspLights[*].light</c>): Redux drives them.</summary>
        private IReadOnlyCollection<Light> ReduxLights()
        {
            var lights = new List<Light>();
            foreach (var m in GetComponentsInChildren<Module_Light>(true))
            {
                if (m == null || m.kspLights == null) continue;
                foreach (var k in m.kspLights)
                    if (k.light != null) lights.Add(k.light);
            }
            return lights;
        }

        private static bool Contains(string[] names, string clip)
        {
            if (names == null) return false;
            foreach (var n in names)
                if (string.Equals(n, clip, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---------------------------------------------------------------- channels

        /// <summary>A fresh table and sink for the part (Setup, or a rebind on a new TrackCache generation): the old ones
        /// leave the flush and release their material copies first, so nothing is flushed twice or leaks. The colour
        /// entries register their channels again.</summary>
        private void NewChannels()
        {
            ReleaseChannels();
            _table = new ChannelTable();
            _sink = new RendererChannels(_table, _name, gameObject.transform);
            foreach (var e in _entries)
            {
                if (e.Disabled || e.Writer == null) continue;
                try
                {
                    e.Writer.Register(_table, _sink);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
            ChannelFlush.Register(_table, _sink);
        }

        /// <summary>Once every writer is registered: the reentry hulls to follow, and the shader set of the part type.</summary>
        private void FinishChannels()
        {
            try
            {
                WatchReentry();
                var key = _name + "|" + PartBackingMode;
                if (_sink.Shaders.Count > 0 && LoggedShaders.Add(key))
                    Log($"{_name} shaders: [{string.Join(", ", _sink.Shaders)}]");
            }
            catch (Exception ex)
            {
                Error($"{_name} channel setup failed: {ex}");
            }
        }

        /// <summary>Out of the flush, material copies destroyed (shutdown, or before a new table).</summary>
        private void ReleaseChannels()
        {
            if (_table != null) ChannelFlush.Unregister(_table);
            try
            {
                _sink?.ReleaseAll();
            }
            catch (Exception ex)
            {
                Error($"{_name} releasing material copies failed: {ex}");
            }
            _table = null;
            _sink = null;
        }

        /// <summary><see cref="WriterPolicy.ReentryMesh"/>: the source renderers we write of each <c>ReentryMesh</c> hull of
        /// the part are rewritten every frame while that hull is active (flight reentry only).</summary>
        private void WatchReentry()
        {
            if (WriterPolicy.For(WriterPolicy.ReentryMesh) != WriterDecision.EveryFrame || _sink.RendererCount == 0) return;
            var watched = 0;
            foreach (var mesh in GetComponentsInChildren<ReentryMesh>(true))
            {
                var group = mesh == null ? null : mesh.ReentryGroupData;
                if (group == null) continue;
                if (group.Sources != null)
                    foreach (var source in group.Sources)
                        if (source != null && Watch(source.Renderer, mesh.gameObject)) watched++;
                if (group.SkinnedMeshSources != null)
                    foreach (var skinned in group.SkinnedMeshSources)
                        if (Watch(skinned, mesh.gameObject)) watched++;
            }
            if (watched > 0 && LoggedReentry.Add(_name))
                Log($"{_name} {watched} renderer(s) rewritten every frame while their reentry hull is active");
        }

        private bool Watch(Renderer r, GameObject hull)
        {
            if (r == null || !_sink.TryGetTarget(r, out var target)) return false;
            _sink.WatchActive(target, hull);
            return true;
        }

        // ---------------------------------------------------------------- FX throttle links (spec 2a.7)

        /// <summary>A Color entry named by an <c>FXModuleThrottleEffects</c>: its state follows the setter's input, read
        /// from the engine (<c>dependOnThrottle</c> / <c>dependOnOutput</c> / <c>dependOnEngineState</c>), else from the
        /// vessel's main throttle.</summary>
        private void LinkThrottle(Entry e)
        {
            var def = e.Def;
            e.Link = new ScalarLink(def);
            var input = new List<string>();
            string engineField = null;
            if (def.DependOnThrottle || def.DependOnOutput || def.DependOnEngineState)
                GameInputs.TryEngine(this, def.EngineName, out e.Engine, out engineField);
            if (def.DependOnOutput)
            {
                if (GameInputs.TryEngineOutput(this, def.EngineName, out e.Output, out var outputField)) input.Add("output from " + outputField);
                else
                {
                    input.Add("output read as throttle from " + engineField);
                    if (!_loggedOutputAsThrottle)
                    {
                        _loggedOutputAsThrottle = true;
                        Log("output read as throttle");
                    }
                }
            }
            else if (def.DependOnThrottle) input.Add("throttle from " + engineField);
            else
            {
                GameInputs.TryMainThrottle(this, out e.MainThrottle, out var mainField);
                input.Add("main throttle from " + mainField);
                if (LoggedThrottleFields.Add(mainField)) Log($"throttle from {mainField}");
            }
            if (def.DependOnEngineState) input.Add("engine state from " + engineField);
            Log($"{_name} color '{def.ModuleId ?? def.ShaderProperty}' follows FXModuleThrottleEffects ({string.Join("; ", input)}, " +
                $"response {def.ScalarResponse.ToString("0.###", CultureInfo.InvariantCulture)})");
        }

        /// <summary><see cref="ScalarLink.Target"/> from this frame's inputs. Allocation-free.</summary>
        private static float LinkTarget(Entry e)
        {
            var (throttle, operational) = e.Engine != null ? e.Engine() : (0f, true);
            var output = e.Output != null ? e.Output() : throttle;
            var main = e.MainThrottle != null ? e.MainThrottle() : 0f;
            return ScalarLink.Target(e.Def, throttle, output, main, operational);
        }

        /// <summary>Per frame: re-binds when the cache generation changed or became ready (one volatile read of the
        /// generation, plus one of <c>Ready</c> while pending); allocation-free otherwise.</summary>
        private void PollTracks(bool inEditor)
        {
            if (!_tracksWanted) return;
            var generation = TrackCache.Generation;
            if (!_tracksPending && generation == _tracksGeneration) return;
            if (!TrackCache.Ready)
            {
                _tracksPending = true;
                return;
            }
            try
            {
                BindTracks(PartTracksOrNull(), generation, inEditor, late: true);
            }
            catch (Exception ex)
            {
                _tracksPending = false;
                _tracksGeneration = generation;
                Error($"{_name} binding tracks failed: {ex}");
            }
        }

        /// <summary>Samples the tracks of the clips Redux's runtime plays, from Redux's time
        /// (<see cref="ReduxTrackPose"/>: an idle, disabled state gives way to the runtime's time).</summary>
        private void DriveReduxTracks()
        {
            for (var i = 0; i < _reduxTracks.Count; i++)
            {
                var r = _reduxTracks[i];
                if (r.Runtime == null) continue; // destroyed with the part
                if (r.State != null)
                    r.Sampler.Sample(ReduxTrackPose.For(r.State.enabled, r.State.normalizedTime, r.State.weight, r.Runtime.NormalizedTime));
                else
                    r.Sampler.Sample(ReduxTrackPose.For(r.Runtime.NormalizedTime));
            }
        }

        /// <summary>The tracks of one host of a clip Redux's legacy runtime plays, and where their time comes from.</summary>
        private sealed class ReduxTracks
        {
            public readonly TrackSampler Sampler;
            /// <summary>The host's legacy state of the clip; null for a clip without state (the runtime's time is used).</summary>
            public readonly AnimationState State;
            public readonly Ksp1DeployableAnimationRuntime Runtime;

            public ReduxTracks(TrackSampler sampler, AnimationState state, Ksp1DeployableAnimationRuntime runtime)
            {
                Sampler = sampler;
                State = state;
                Runtime = runtime;
            }
        }
    }
}
