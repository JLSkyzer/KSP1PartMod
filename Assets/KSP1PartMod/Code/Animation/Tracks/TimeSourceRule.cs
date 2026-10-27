using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Mu;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Who plays a clip (the phase 1 ownership table). Tracks never own a clip: they take its owner's time.</summary>
    public enum ClipOwner
    {
        /// <summary>One of our entries (Toggle, Deploy, Gear, Throttle, Heat, Group, Redux follower with a state, chute
        /// follower).</summary>
        OurEntry,
        /// <summary>Redux's legacy runtime (several deployable sources), clip with a state.</summary>
        ReduxRuntimeWithState,
        /// <summary>Redux's legacy runtime, clip without a state (material-only).</summary>
        ReduxRuntimeNoState,
        /// <summary>Redux's single deployable source, clip without a state (material-only).</summary>
        ReduxSingleSourceNoState,
    }

    /// <summary>Where the tracks of a clip read their time.</summary>
    public enum TimeSource
    {
        /// <summary>The entry's <see cref="Behaviours.ClipPose"/> of this frame.</summary>
        EntryPose,
        /// <summary>The legacy state's <c>normalizedTime</c>, read only.</summary>
        ReduxStateTime,
        /// <summary><c>Ksp1DeployableAnimationRuntime.NormalizedTime</c>.</summary>
        ReduxRuntimeNormalizedTime,
        /// <summary>A Redux follower in tracks-only mode: its own clock from <c>Data_Deployable.CurrentDeployState</c>,
        /// on the TrackSet's length.</summary>
        FollowerClock,
        None,
    }

    /// <summary>Spec 2a.4: one time source per owner.</summary>
    public static class TimeSourceRule
    {
        public static TimeSource For(ClipOwner owner)
        {
            switch (owner)
            {
                case ClipOwner.OurEntry: return TimeSource.EntryPose;
                case ClipOwner.ReduxRuntimeWithState: return TimeSource.ReduxStateTime;
                case ClipOwner.ReduxRuntimeNoState: return TimeSource.ReduxRuntimeNormalizedTime;
                case ClipOwner.ReduxSingleSourceNoState: return TimeSource.FollowerClock;
                default: return TimeSource.None;
            }
        }
    }

    /// <summary>How an entry's clip is bound on an instance.</summary>
    public enum BindMode
    {
        /// <summary>The legacy state only, as in phase 1.</summary>
        State,
        /// <summary>The state keeps its owner; the tracks follow its time.</summary>
        StateAndTracks,
        /// <summary>No state: nobody can play the clip today, so the entry's machine runs on the TrackSet length.</summary>
        TracksOnly,
        /// <summary>Neither: not bound, as in phase 1.</summary>
        NotBound,
    }

    /// <summary>Tracks-only binding only where the clip has no state, so that a clip that plays keeps its owner.</summary>
    public static class TrackBindingRule
    {
        /// <summary>The <c>.mu</c> Transform properties Redux's importer turns into legacy clip curves (read in the
        /// 26w42c IL of <c>Ksp1ModelImporter</c>, <c>redux/NOTES.md</c> §1). Ordinal.</summary>
        private static readonly HashSet<string> KeptProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
            "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z",
        };

        /// <summary>Whether the clip has a legacy <c>AnimationState</c> on the instance: Redux builds a clip only when
        /// it has a name and keeps at least one curve, and it keeps only the curves of <see cref="KeptProperties"/>
        /// that have keys. A clip it does not build is not added to the <c>Animation</c>.</summary>
        public static bool HasState(MuAnimClip clip)
        {
            if (clip == null || string.IsNullOrWhiteSpace(clip.Name) || clip.Curves == null) return false;
            foreach (var curve in clip.Curves)
                if (curve != null && curve.Property != null && curve.Keys != null && curve.Keys.Count > 0 && KeptProperties.Contains(curve.Property))
                    return true;
            return false;
        }

        public static BindMode Mode(bool hasState, bool hasTracks) =>
            hasState ? (hasTracks ? BindMode.StateAndTracks : BindMode.State)
                     : (hasTracks ? BindMode.TracksOnly : BindMode.NotBound);
    }
}
