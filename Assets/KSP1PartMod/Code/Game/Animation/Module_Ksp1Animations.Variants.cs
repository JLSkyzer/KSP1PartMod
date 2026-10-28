using System;
using System.Collections.Generic;
using System.Text;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>B9 gates at runtime and the rebind on a variant change (spec 2b.1, "Variant application and plugin inputs
    /// (settled 2026-10-28)"). The V-SwiFT selection is polled every frame (<see cref="VariantWatch"/>); each gated entry
    /// follows its sets (<see cref="EntryGate"/>), and an inactive entry holds its rest pose, hides its PAM button and
    /// ignores its action group, whatever its kind. A change of any set rebinds by <see cref="RebindRule.KnownEffects"/>.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>The tracks of an entry held at rest: time 0 at weight 0, so its channels leave the slot to the other
        /// clips (<see cref="ChannelTable"/>: a channel at weight 0 is not active).</summary>
        private static readonly ClipPose RestTrackPose = new ClipPose(0f, 0f, 0f, true);

        private VariantWatch _variants;

        // ---------------------------------------------------------------- bind

        /// <summary>At Setup, once every entry is built: the watch, the bind line, and each entry's gates. Never throws: a
        /// failure leaves every gate on its default subtype.</summary>
        private void BindVariants()
        {
            var gated = false;
            foreach (var e in _entries)
                if (e.Def.Gates != null && e.Def.Gates.Length > 0) gated = true;
            try
            {
                _variants = VariantWatch.Create(this, out var note, out var initialized);
                if (_variants != null)
                {
                    Log($"{_name} variants at bind: {_variants.Describe()}, Module_PartSwitch initialized {(initialized ? "true" : "false")}");
                    if (gated)
                        for (var set = 0; set < _variants.SetCount; set++)
                            LogTransforms(set, _variants.ActiveOf(set));
                }
                else if (gated) Log($"{_name} B9 gates on their default subtype: {note}");
            }
            catch (Exception ex)
            {
                _variants = null;
                Error($"{_name} variant watch failed, B9 gates on their default subtype: {ex}");
            }
            foreach (var e in _entries)
            {
                try
                {
                    InitGate(e);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        /// <summary>The entry's gates on the watched sets (a gate whose set the part does not have keeps its default
        /// subtype), set to the selection seen at bind.</summary>
        private void InitGate(Entry e)
        {
            var gates = e.Def.Gates;
            if (gates == null || gates.Length == 0) return;
            var sets = new int[gates.Length];
            for (var i = 0; i < gates.Length; i++) sets[i] = _variants?.IndexOf(gates[i]?.SetId) ?? -1;
            e.Gate = new EntryGate(gates, sets);
            for (var i = 0; i < gates.Length; i++)
            {
                if (sets[i] >= 0) e.Gate.OnVariant(sets[i], _variants.ActiveOf(sets[i]));
                var g = gates[i];
                if (g == null) continue;
                var source = sets[i] >= 0 ? $"Data_PartSwitch set {sets[i]}" : $"default '{g.DefaultSubtype}'";
                Log($"{_name} {e.Def.Key} gated by B9 '{SetLabel(g.SetId)}' ({source}), inactive in [{string.Join(", ", g.InactiveIn ?? new string[0])}]");
            }
            for (var i = 0; i < gates.Length; i++)
            {
                var g = gates[i];
                if (g == null) continue;
                var id = sets[i] >= 0 ? _variants.ActiveOf(sets[i]) ?? g.DefaultSubtype : g.DefaultSubtype;
                Log($"{_name} B9 '{SetLabel(g.SetId)}' -> '{id}': {e.Def.Key} {(e.Gate.Active ? "active" : "inactive")}");
            }
        }

        private static string SetLabel(string setId) => setId ?? "no set";

        // ---------------------------------------------------------------- per frame

        /// <summary>One read of the selection; on a change, the gates, the transform check and the rebind. Allocation-free
        /// while nothing changes.</summary>
        private void PollVariants(bool inEditor)
        {
            var w = _variants;
            if (w == null) return;
            try
            {
                if (w.Poll()) OnVariantsChanged(w, inEditor);
            }
            catch (Exception ex)
            {
                _variants = null;
                Error($"{_name} variant watch stopped for this session, B9 gates frozen: {ex}");
            }
        }

        private void OnVariantsChanged(VariantWatch w, bool inEditor)
        {
            var reason = new StringBuilder("variant ");
            var first = true;
            for (var set = 0; set < w.SetCount; set++)
            {
                if (!w.Changed(set)) continue;
                var id = w.ActiveOf(set);
                var label = SetLabel(w.SetId(set));
                if (!first) reason.Append(", ");
                first = false;
                reason.Append('\'').Append(label).Append("' -> '").Append(id).Append('\'');
                LogTransforms(set, id);
                foreach (var e in _entries)
                {
                    if (e.Gate == null || !e.Gate.Follows(set) || !e.Gate.OnVariant(set, id)) continue;
                    e.Rested = false;
                    Log($"{_name} B9 '{label}' -> '{id}': {e.Def.Key} {(e.Gate.Active ? "active" : "inactive")}");
                }
            }
            Rebind(RebindRule.For(RebindRule.KnownEffects), reason.ToString(), inEditor);
        }

        /// <summary><c>TransformActivator</c> looks only at the part root's direct children, so the shown transforms may
        /// stay those of the import-time default: logged, nothing more (the gate follows the selection).</summary>
        private void LogTransforms(int set, string id)
        {
            if (_variants == null || id == null) return;
            if (_variants.TransformsActive(gameObject.transform, set, id, out var active, out var total))
                Log($"{_name} B9 '{SetLabel(_variants.SetId(set))}' -> '{id}': transforms of '{id}' active {active}/{total}");
        }

        /// <summary>First the states sampled at rest last frame are disabled again (end of the hold-pose idiom); then the
        /// entries whose gates just closed (or whose samplers were bound again) are put at rest, before any entry is stepped
        /// this frame: an active entry sharing their clip then writes its pose after them. Allocation-free when nothing
        /// changes.</summary>
        private void RestInactive()
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                var held = _entries[i].RestHeld;
                if (held == null || held.Count == 0) continue;
                for (var k = 0; k < held.Count; k++)
                    if (held[k] != null) held[k].enabled = false;
                held.Clear();
            }
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Disabled || e.Rested || !Inactive(e)) continue;
                try
                {
                    Rest(e);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        private static bool Inactive(Entry e) => e.Gate != null && !e.Gate.Active;

        /// <summary>The rest pose of an inactive entry, shown once (spec 2b.1): each of its legacy states that no active
        /// entry of the part holds (<see cref="RestPlan.Unshared"/>) is sampled at time 0, weight 1, speed 0 for this frame
        /// and disabled on the next (<see cref="RestInactive"/>); a state an active entry holds is left to it. Its tracks
        /// at rest (weight 0); a colour entry withdraws its channels, so another colour entry of the renderer wins. Its
        /// machine is left as it is and resumes when the gate opens.</summary>
        private void Rest(Entry e)
        {
            e.Rested = true;
            var active = new List<AnimationState[]>();
            foreach (var o in _entries)
            {
                if (ReferenceEquals(o, e) || o.Disabled || Inactive(o)) continue;
                active.Add(o.States);
                active.Add(o.ActiveStates);
            }
            HoldAtRest(e, e.States, RestPlan.Unshared(e.States, active), e.Def.Layer);
            HoldAtRest(e, e.ActiveStates, RestPlan.Unshared(e.ActiveStates, active), e.Group != null ? ActiveLayer : e.Def.Layer);
            e.StatesOn = e.ActiveOn = false;
            TrackSampler.SampleAll(e.Tracks, in RestTrackPose);
            TrackSampler.SampleAll(e.ActiveTracks, in RestTrackPose);
            e.Writer?.Withdraw();
        }

        private static void HoldAtRest(Entry e, AnimationState[] states, bool[] sample, int layer)
        {
            for (var i = 0; i < states.Length; i++)
            {
                if (!sample[i]) continue;
                var s = states[i];
                s.enabled = true;
                s.layer = layer;
                s.weight = 1f;
                s.speed = 0f;
                s.normalizedTime = 0f;
                (e.RestHeld ??= new List<AnimationState>()).Add(s);
            }
        }

        // ---------------------------------------------------------------- rebind

        /// <summary><see cref="RebindScope.Renderers"/>: the material decisions and copies of every bound renderer
        /// (<see cref="RendererChannels.RebindAll"/>). <see cref="RebindScope.Full"/>: states, samplers, table targets bound
        /// again; machines and their saved values kept. Never throws.</summary>
        private void Rebind(RebindScope scope, string reason, bool inEditor)
        {
            if (scope == RebindScope.None) return;
            try
            {
                if (scope == RebindScope.Renderers) _sink?.RebindAll();
                else
                {
                    foreach (var e in _entries)
                        if (!e.Disabled) RebindStates(e);
                    if (_tracksWanted && !_tracksPending) BindTracks(PartTracksOrNull(), _tracksGeneration, inEditor, late: false);
                    else
                    {
                        NewChannels();
                        FinishChannels();
                    }
                }
                Log($"{_name} rebind {scope} after {reason}");
            }
            catch (Exception ex)
            {
                Error($"{_name} rebind {scope} after {reason} failed: {ex}");
            }
        }

        /// <summary>Full rebind of one entry's legacy states (machines untouched). Not reached with Redux 26w42c's KSP1
        /// variants (<see cref="RebindRule.KnownEffects"/>).</summary>
        private void RebindStates(Entry e)
        {
            var def = e.Def;
            e.StatesOn = e.ActiveOn = false;
            e.Rested = false;
            switch (def.Kind)
            {
                case EntryKind.ReduxFollower:
                    if (ReduxFollowerBinder.TryDeployable(this, def, out var state, out _, out _, out var holder, out _))
                    {
                        e.States = new[] { state };
                        e.Holders = new List<GameObject> { holder };
                    }
                    break;
                case EntryKind.ChuteFollower:
                    if (ReduxFollowerBinder.TryChute(this, def, out var semi, out var full, out _, out var chuteHolder, out _))
                    {
                        e.States = new[] { semi };
                        e.ActiveStates = new[] { full };
                        e.Holders = new List<GameObject> { chuteHolder };
                        e.ActiveHolders = new List<GameObject> { chuteHolder };
                    }
                    break;
                default:
                    if (e.States.Length > 0) e.States = StatesOf(def, def.Clip, out e.Holders);
                    if (e.ActiveStates.Length > 0) e.ActiveStates = StatesOf(def, def.SecondClip, out e.ActiveHolders);
                    break;
            }
            if (def.WrapMode != 0)
            {
                foreach (var s in e.States) s.wrapMode = (WrapMode)def.WrapMode;
                foreach (var s in e.ActiveStates) s.wrapMode = (WrapMode)def.WrapMode;
            }
        }

        private AnimationState[] StatesOf(AnimEntryDef def, string clip, out List<GameObject> holders)
        {
            holders = new List<GameObject>();
            return string.IsNullOrWhiteSpace(clip) ? NoStates : Bind(def, clip, out _, holders);
        }
    }
}
