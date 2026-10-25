using System.Collections.Generic;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Finds the legacy <c>AnimationState</c>s of an entry's clip on a part instance, with the lookup of its
    /// KSP1 module (<see cref="AnimationLookup"/>). Redux disables the <c>Animation</c> it binds to an
    /// <c>Animator</c>: such a component is skipped and the note says so. One exception: an Animation bound by
    /// Redux's generic deployable controller (it carries Redux's pose keeper) on a part that has no Redux deployable
    /// module any more, because <c>own_first_deployable</c> removed it. That clip is ours: the <c>Animation</c> is
    /// enabled again, and the <c>Animator</c> and pose keeper are kept disabled. A clip Redux keeps (in
    /// <c>ReduxClips</c>, for example a solar panel's) is never taken over. Runs at init only.</summary>
    internal static class AnimationBinder
    {
        private static readonly AnimationState[] None = new AnimationState[0];

        /// <summary>Binds <paramref name="clip"/> (Group: also the active clip) with the entry's lookup.</summary>
        /// <param name="reduxClips">The clips Redux's runtime keeps (<c>Data_Ksp1Animations.ReduxClips</c>); may be null.</param>
        public static IReadOnlyList<AnimationState> Bind(GameObject partRoot, AnimEntryDef e, string clip,
            ICollection<string> reduxClips, out string note)
        {
            note = null;
            if (partRoot == null || string.IsNullOrWhiteSpace(clip))
            {
                note = "no clip name";
                return None;
            }
            var animations = partRoot.GetComponentsInChildren<UnityEngine.Animation>(true);
            var takeOver = !HasReduxDeployableModule(partRoot) && (reduxClips == null || !reduxClips.Contains(clip));
            var candidates = new AnimationCandidate[animations.Length];
            for (var i = 0; i < animations.Length; i++)
            {
                var a = animations[i];
                var bound = !a.enabled && a.GetComponent<Animator>() != null;
                var ours = takeOver && a.GetComponent<Ksp1DeployableAnimationPoseKeeper>() != null;
                candidates[i] = new AnimationCandidate(a.gameObject.name, a[clip] != null, bound && !ours);
            }
            var strategy = AnimationLookup.For(e.Kind);
            var result = AnimationLookup.Select(candidates, strategy, e.Kind == EntryKind.Gear ? e.AnimatorName : null);
            note = AnimationLookup.Note(result, clip, e.Kind == EntryKind.Gear ? e.AnimatorName : null);

            var states = new List<AnimationState>(result.Picked.Count);
            foreach (var i in result.Picked)
            {
                var a = animations[i];
                if (!a.enabled)
                {
                    var animator = a.GetComponent<Animator>();
                    if (animator != null) animator.enabled = false;
                    var keeper = a.GetComponent<Ksp1DeployableAnimationPoseKeeper>();
                    if (keeper != null) keeper.enabled = false;
                    a.enabled = true;
                    note = Append(note, animator != null
                        ? $"took over the Animator-bound Animation on '{a.gameObject.name}' (no Redux deployable module on the part)"
                        : $"enabled the disabled Animation on '{a.gameObject.name}'");
                }
                var state = a[clip];
                if (state != null) states.Add(state);
            }
            return states;
        }

        /// <summary>Redux's own deployable module: the stock <c>Module_Deployable</c> itself (not a subclass such as
        /// <c>Module_SolarPanel</c>) or Redux's <c>Ksp1Module_Deployable</c>.</summary>
        internal static PartBehaviourModule ReduxDeployableModule(GameObject partRoot)
        {
            if (partRoot == null) return null;
            var ksp1 = partRoot.GetComponent<Ksp1Module_Deployable>();
            if (ksp1 != null) return ksp1;
            foreach (var m in partRoot.GetComponents<Module_Deployable>())
                if (m != null && m.GetType() == typeof(Module_Deployable)) return m;
            return null;
        }

        private static bool HasReduxDeployableModule(GameObject partRoot) => ReduxDeployableModule(partRoot) != null;

        private static string Append(string note, string more) => note == null ? more : note + "; " + more;
    }
}
