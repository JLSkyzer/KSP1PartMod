using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Science
{
    /// <summary>KSP2's <c>ExperimentState</c>, same names and values (26w42c), for the pure rules.</summary>
    public enum ScienceRunState : byte
    {
        NONE, INVALIDLOCATION, READY, RUNNING, PAUSED, OUTOFRESOURCE, LOCATIONCHANGED, INSUFFICIENTCREW, NOCONTROL,
        INSUFFICIENTSTORAGE, ALREADYSTORED, BLOCKED,
    }

    /// <summary>Why an experiment plays no clip.</summary>
    public enum ScienceNoClip { None, NoFxModules, NoEntry, GatedByB9, AnimationsOff, NoExperimentAnimation }

    /// <summary>The existing entries an experiment opens, or why there is none.</summary>
    public sealed class ScienceTargets
    {
        public IReadOnlyList<AnimEntryDef> Entries { get; }
        public ScienceNoClip Reason { get; }
        public IReadOnlyList<string> Notes { get; }
        public AnimEntryDef First => Entries.Count > 0 ? Entries[0] : null;

        public ScienceTargets(IReadOnlyList<AnimEntryDef> entries, ScienceNoClip reason, IReadOnlyList<string> notes)
        {
            Entries = entries;
            Reason = reason;
            Notes = notes;
        }
    }

    /// <summary>How an experiment meets the part's KSP1 clips (science plan (i), Task 5). The rules only read existing entries: no
    /// entry is planned, so no key moves. Pure.
    /// <list type="bullet">
    /// <item>a stock experiment opens the Deploy or Redux follower entry of each of its <c>FxModules</c>; a DMagic Generic one
    /// the <c>ScienceToggle</c> entry of its own module;</item>
    /// <item>an entry switched off by B9 (inactive) is no target (noted);</item>
    /// <item>the clip opens at a transition to <c>RUNNING</c>, or straight to a stored report (a zero-time run);</item>
    /// <item>a waiting experiment's countdown runs while the clip is open and still (<c>PartIsDeployed</c>).</item>
    /// </list></summary>
    public static class ScienceAnimRules
    {
        private static readonly IReadOnlyList<AnimEntryDef> NoEntries = Array.Empty<AnimEntryDef>();
        private static readonly IReadOnlyList<string> NoNotes = Array.Empty<string>();

        /// <param name="animationsOn"><c>enabled</c> and <c>science_deploy_animations</c>.</param>
        /// <param name="inactive">The entries switched off by B9 now; null: none.</param>
        public static ScienceTargets TargetsFor(ScienceConfiguration config, IReadOnlyList<AnimEntryDef> entries, bool animationsOn,
            Func<AnimEntryDef, bool> inactive = null)
        {
            var clip = config?.Clip ?? ScienceClip.None;
            if (!clip.HasSource) return new ScienceTargets(NoEntries, ScienceNoClip.NoFxModules, NoNotes);
            if (!animationsOn) return new ScienceTargets(NoEntries, ScienceNoClip.AnimationsOff, NoNotes);
            if (clip.ToggleModuleIndex >= 0 && !clip.ExperimentAnimation)
                return new ScienceTargets(NoEntries, ScienceNoClip.NoExperimentAnimation, NoNotes);
            var found = new List<AnimEntryDef>();
            var notes = new List<string>();
            var gated = 0;
            foreach (var e in entries ?? NoEntries)
            {
                if (e == null || !Matches(clip, e)) continue;
                if (inactive != null && inactive(e))
                {
                    gated++;
                    notes.Add($"{config?.ExperimentDefinitionId}: {e.Key} is switched off by B9, no clip from it");
                    continue;
                }
                found.Add(e);
            }
            var reason = found.Count > 0 ? ScienceNoClip.None : gated > 0 ? ScienceNoClip.GatedByB9 : ScienceNoClip.NoEntry;
            return new ScienceTargets(found, reason, notes);
        }

        private static bool Matches(ScienceClip clip, AnimEntryDef e)
        {
            if (clip.ToggleModuleIndex >= 0) return e.Kind == EntryKind.ScienceToggle && e.ModuleIndex == clip.ToggleModuleIndex;
            if (e.Kind != EntryKind.Deploy && e.Kind != EntryKind.ReduxFollower) return false;
            for (var i = 0; i < clip.FxModules.Count; i++)
                if (clip.FxModules[i] == e.ModuleIndex) return true;
            return false;
        }

        /// <summary>True on a transition to <c>RUNNING</c> (a part loaded running included: <paramref name="previous"/> null), or from
        /// anything but <c>RUNNING</c> straight to <c>ALREADYSTORED</c> (a zero-time run stores its report in the same frame). A
        /// part loaded with a stored report opens nothing.</summary>
        public static bool ShouldOpen(ScienceRunState? previous, ScienceRunState state)
        {
            if (previous == state) return false;
            if (state == ScienceRunState.RUNNING) return true;
            return state == ScienceRunState.ALREADYSTORED && previous.HasValue && previous.Value != ScienceRunState.RUNNING;
        }

        /// <summary>The value written to <c>PartIsDeployed</c>: the clip is open and not moving.</summary>
        public static bool DeployGate(bool toggleClosed, bool moving) => !toggleClosed && !moving;
    }
}
