using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>How a KSP1 module finds the legacy <c>Animation</c> components that play its clip (research notes
    /// <c>stock/NOTES.md</c> §0). The candidates are every <c>Animation</c> under the part, in hierarchy order, which
    /// is also how the ModuleDeployablePart family searches; KSP1's other modules start from the <c>model</c> child,
    /// which on a Redux instance holds the same components.</summary>
    public enum LookupStrategy
    {
        /// <summary>ModuleAnimateGeneric, ModuleAnimationGroup, ModuleDeployablePart: the first holder.</summary>
        FirstMatch,
        /// <summary>ModuleAnimateHeat, FXModuleAnimateThrottle: every holder, all driven together. FXModuleAnimateRCS:
        /// every holder, each with its thruster.</summary>
        AllMatches,
        /// <summary>ModuleWheelDeployment: the last holder whose GameObject has the given name.</summary>
        LastByObjectName,
    }

    /// <summary>One <c>Animation</c> component of the part, as the lookup sees it.</summary>
    public readonly struct AnimationCandidate
    {
        public readonly string ObjectName;
        public readonly bool HasClip;
        /// <summary>Disabled, with an <c>Animator</c> on the same GameObject: Redux bound it to its own controller.</summary>
        public readonly bool ReduxBound;

        public AnimationCandidate(string objectName, bool hasClip, bool reduxBound)
        {
            ObjectName = objectName;
            HasClip = hasClip;
            ReduxBound = reduxBound;
        }
    }

    /// <summary>What a lookup picked, and what it saw on the way.</summary>
    public sealed class LookupResult
    {
        /// <summary>Indices into the candidates, in hierarchy order.</summary>
        public readonly List<int> Picked = new List<int>();
        /// <summary>Candidates holding the clip, Redux-bound ones included.</summary>
        public int Holding;
        /// <summary>Holders left out because Redux drives them.</summary>
        public int SkippedReduxBound;
        /// <summary>LastByObjectName had no object name and used the first holder.</summary>
        public bool ObjectNameFallback;
        public int CandidateCount;
    }

    public static class AnimationLookup
    {
        public static LookupStrategy For(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Heat:
                case EntryKind.Throttle:
                case EntryKind.RcsGlow: return LookupStrategy.AllMatches; // FXModuleAnimateRCS: one holder per thruster
                case EntryKind.Gear: return LookupStrategy.LastByObjectName;
                default: return LookupStrategy.FirstMatch;
            }
        }

        /// <summary>Redux-bound holders are never picked: Mecanim would overwrite our pose.</summary>
        public static LookupResult Select(IReadOnlyList<AnimationCandidate> candidates, LookupStrategy strategy, string objectName)
        {
            var r = new LookupResult();
            if (candidates == null) return r;
            r.CandidateCount = candidates.Count;
            var name = objectName?.Trim();
            if (strategy == LookupStrategy.LastByObjectName && string.IsNullOrEmpty(name))
            {
                strategy = LookupStrategy.FirstMatch;
                r.ObjectNameFallback = true;
            }
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.HasClip) continue;
                if (strategy == LookupStrategy.LastByObjectName && !string.Equals(c.ObjectName, name, StringComparison.Ordinal)) continue;
                r.Holding++;
                if (strategy == LookupStrategy.FirstMatch && r.Picked.Count > 0) continue; // counted, not picked
                if (c.ReduxBound)
                {
                    r.SkippedReduxBound++;
                    continue;
                }
                if (strategy == LookupStrategy.LastByObjectName) r.Picked.Clear();
                r.Picked.Add(i);
            }
            return r;
        }

        /// <summary>For a Redux follower: the holder Redux bound to its Mecanim copy (disabled, with an Animator), the one
        /// on <paramref name="objectName"/> first, else the first such holder; -1 when the clip is only on legacy holders
        /// (Redux's fallback runtime plays it).</summary>
        public static int SelectReduxBound(IReadOnlyList<AnimationCandidate> candidates, string objectName)
        {
            if (candidates == null) return -1;
            var name = objectName?.Trim();
            var first = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.HasClip || !c.ReduxBound) continue;
                if (!string.IsNullOrEmpty(name) && string.Equals(c.ObjectName, name, StringComparison.Ordinal)) return i;
                if (first < 0) first = i;
            }
            return first;
        }

        /// <summary>Why nothing was picked, or what is worth knowing about what was; null when there is nothing to
        /// say.</summary>
        public static string Note(LookupResult r, string clip, string objectName)
        {
            var parts = new List<string>();
            if (r.Picked.Count == 0)
            {
                if (r.CandidateCount == 0) parts.Add($"no Animation on the part (phase 2 if material-only)");
                else if (r.Holding > 0 && r.SkippedReduxBound == r.Holding)
                    parts.Add($"clip '{clip}' is only on {r.SkippedReduxBound} Redux-bound Animation(s) (disabled, with an Animator)");
                else if (!string.IsNullOrWhiteSpace(objectName) && !r.ObjectNameFallback)
                    parts.Add($"no GameObject '{objectName.Trim()}' holds clip '{clip}'");
                else parts.Add($"no Animation holds clip '{clip}' (phase 2 if material-only)");
            }
            else if (r.SkippedReduxBound > 0) parts.Add($"skipped {r.SkippedReduxBound} Redux-bound Animation(s)");
            if (r.ObjectNameFallback) parts.Add("no animator object name, first holder used");
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }
}
