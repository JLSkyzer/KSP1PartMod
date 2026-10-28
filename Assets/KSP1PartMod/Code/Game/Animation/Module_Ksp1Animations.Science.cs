using System;
using System.Collections.Generic;
using KSP.Modules;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native.Science;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>KSP1 experiment clips and KSP2's <c>Module_ScienceExperiment</c> of the same part (science plan, Task 5, (i)).
    /// <see cref="ScienceRuntime"/> hands over the part (flight only) after the native module's <c>OnInitialize</c>, before or
    /// after ours. The bind reads the bound entries once (<see cref="ScienceAnimRules.TargetsFor"/>): a stock experiment opens our
    /// Toggle or Deploy entry of its <c>FxModules</c>, or the part's <c>Data_Deployable.toggleExtend</c> for a Redux follower (S, the
    /// 0.2.2 path); a DMagic Generic one its <c>ScienceToggle</c>. A configuration that waits (<c>experimentWaitForAnimation</c>)
    /// and has a ScienceToggle makes the part animated for KSP2, and <see cref="ScienceBridge"/> writes the deploy gate. No entry
    /// is planned, added or reordered: no key moves. The per-frame step does not allocate.</summary>
    public partial class Module_Ksp1Animations
    {
        private ScienceRuntime.Handle _science;
        private bool _scienceBound;

        /// <summary>From <see cref="ScienceRuntime"/>, flight only.</summary>
        internal void AttachScienceBridge(ScienceRuntime.Handle handle)
        {
            if (handle == null) return;
            _science = handle;
            _scienceBound = false;
            if (_initDone) FinishScienceInit();
        }

        internal void FinishScienceInit()
        {
            try
            {
                BindScience();
            }
            catch (Exception ex)
            {
                // The experiment must not wait for a clip that will never come: not animated, it completes on its own time.
                Error($"{SafePartName()} science bridge failed, no clip: {ex}");
                if (_science != null)
                {
                    _science.Bridge = null;
                    if (_science.Data != null) _science.Data.IsPartAnimated = false;
                }
            }
        }

        private void BindScience()
        {
            var h = _science;
            if (h == null || _scienceBound) return;
            _scienceBound = true;
            if (ScienceRuntime.Broken)
            {
                ScienceRuntime.Stop(h); // stopped for the session before this part bound: no bridge, not animated
                return;
            }
            var part = h.Part ?? _name ?? SafePartName();
            var animationsOn = ScienceRuntime.AnimationsOn;
            var waitOn = ScienceRuntime.WaitOn;

            // The bound entries only: an entry the bind left out (no clip, skip_modules) is no target.
            var byDef = new Dictionary<AnimEntryDef, Entry>();
            var defs = new List<AnimEntryDef>();
            if (_ready)
                foreach (var e in _entries)
                    if (!e.Disabled && !byDef.ContainsKey(e.Def))
                    {
                        byDef.Add(e.Def, e);
                        defs.Add(e.Def);
                    }

            var configs = new List<ScienceBridge.Config>();
            foreach (var c in h.Record.Configurations)
            {
                var t = ScienceAnimRules.TargetsFor(c, defs, animationsOn, d => byDef.TryGetValue(d, out var e) && Inactive(e));
                var targets = new List<ScienceBridge.Target>();
                foreach (var d in t.Entries)
                {
                    var target = byDef.TryGetValue(d, out var e) ? ScienceTarget(e) : null;
                    if (target != null) targets.Add(target);
                }
                var key = "clip|" + part + "|" + c.ExperimentDefinitionId;
                if (targets.Count == 0)
                {
                    var reason = t.Reason == ScienceNoClip.None ? ScienceNoClip.NoEntry : t.Reason;
                    ScienceRuntime.LogOnce(key, ScienceLines.NoClip(part, c.ExperimentDefinitionId, reason));
                    configs.Add(new ScienceBridge.Config(c.ExperimentDefinitionId, targets.ToArray(), null));
                    continue;
                }
                foreach (var note in t.Notes) Log($"{part} science: {note}");
                ScienceRuntime.LogOnce(key, ScienceLines.Opens(part, c.ExperimentDefinitionId, targets[0].Key));
                ScienceBridge.Target gate = null;
                if (c.Waits && waitOn)
                    foreach (var target in targets)
                        if (target.Closed != null)
                        {
                            gate = target;
                            break;
                        }
                if (gate != null)
                    ScienceRuntime.LogOnce("wait|" + key, ScienceLines.Waits(part, c.ExperimentDefinitionId, gate.Key, c.TimeToComplete));
                configs.Add(new ScienceBridge.Config(c.ExperimentDefinitionId, targets.ToArray(), gate));
            }
            h.Bridge = new ScienceBridge(part, h.Data, configs);
            // KSP2's countdown waits for PartIsDeployed only when a configuration has a clip to wait for.
            h.Data.IsPartAnimated = h.Bridge.Waits;
            if (_ready) StepScience();
        }

        /// <summary>How the bridge drives an entry: our own open path (Toggle when closed, Deploy extended), or Redux's
        /// <c>toggleExtend</c> for a follower. Null for a kind it cannot open.</summary>
        private ScienceBridge.Target ScienceTarget(Entry e)
        {
            var key = e.Def.Key;
            Func<bool> live = () => !e.Disabled && !Inactive(e);
            switch (e.Def.Kind)
            {
                case EntryKind.Toggle:
                case EntryKind.ScienceToggle:
                {
                    var toggle = e.Toggle;
                    if (toggle == null) return null;
                    return new ScienceBridge.Target(key, live, () => toggle.Closed && SetOn(e, true, false),
                        () => toggle.Closed, () => toggle.IsMoving);
                }
                case EntryKind.Deploy:
                    return e.Deploy == null ? null : new ScienceBridge.Target(key, live, () => !IsOn(e) && SetOn(e, true, false));
                case EntryKind.ReduxFollower:
                {
                    var data = ReduxFollowerBinder.DeployableOf(this, e.Def.Clip);
                    if (data == null)
                    {
                        Log($"{_name} science: Redux follower '{key}' has no Data_Deployable, no clip from it");
                        return null;
                    }
                    var toggle = data.toggleExtend;
                    return new ScienceBridge.Target(key, live, () =>
                    {
                        if (toggle.GetValue()) return false;
                        toggle.SetValue(true); // Redux's OnToggleExtendChanged extends it; the follower draws it
                        return true;
                    });
                }
                default:
                    return null;
            }
        }

        /// <summary>Per flight frame, after the entries: the science bridge (<see cref="ScienceRuntime.Step"/>).</summary>
        private void StepScience() => ScienceRuntime.Step(_science);
    }
}
