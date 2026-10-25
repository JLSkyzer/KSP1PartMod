using System;
using System.Linq;
using KSP.Modules;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Binds a Redux follower on a part instance (Redux 26w42c, decompiled). Redux's Mecanim path:
    /// <c>Ksp1GenericDeployableAnimatorBuilder.TryConfigure</c> (and <c>Ksp1ParachuteAnimatorBuilder</c>) copies the
    /// legacy clip with <c>legacy = false</c> into an <c>AnimatorOverrideController</c> on the holder's GameObject, disables
    /// the holder's <c>Animation</c>, and adds a <c>Ksp1DeployableAnimationPoseKeeper</c> (deployables only). The clip was
    /// built at runtime by <c>Ksp1ModelImporter.ToUnityAnimationClip</c> with <c>SetCurve</c>, which Unity documents as
    /// runtime-only for legacy clips, so the copy moves nothing (in game, 2026-10-25: the KRE leg says "Extended" and does
    /// not move). The binder re-enables the legacy <c>Animation</c> and disables the pose keeper. Redux's Animator is
    /// kept: its state machine is what ends a move (<c>Module_Deployable.OnAnimStateEnter</c> →
    /// <c>UpdateDeployStateToFullStop</c> sets Extended / Retracted), and it writes no transform. A part whose clip is
    /// only on enabled legacy holders went through Redux's legacy runtime (<c>Ksp1DeployableAnimationRuntime</c>), which
    /// moves: nothing is bound. Runs at init only; never throws for a missing piece (false with a note).</summary>
    internal static class ReduxFollowerBinder
    {
        public const string DeployPlaceholder = "KSP1_GenericDeployable_Deploy";
        public const string ChuteOverrideMarker = "KSP1ParachuteOverride";

        public static bool TryDeployable(Module_Ksp1Animations m, AnimEntryDef def, out AnimationState state,
            out Func<ReduxDeployState> read, out string module, out string note)
        {
            state = null;
            read = null;
            module = null;
            var deployable = PickDeployable(m, def.Clip, out var data);
            if (deployable == null || data == null)
            {
                note = "no Module_Deployable with a Data_Deployable on the part (Redux's legacy runtime, Ksp1Module_Deployable, plays it)";
                return false;
            }
            module = deployable.GetType().Name;
            if (!TryHolder(m, def.Clip, null, def.AnimatorName, out var animation, out note)) return false;

            var keeper = animation.GetComponent<Ksp1DeployableAnimationPoseKeeper>();
            if (keeper != null) keeper.enabled = false;
            animation.enabled = true;
            state = animation[def.Clip];
            if (state == null)
            {
                note = $"clip '{def.Clip}' vanished from '{animation.gameObject.name}'";
                return false;
            }
            var d = data;
            read = () => (ReduxDeployState)(byte)d.CurrentDeployState.GetValue();
            note = $"on '{animation.gameObject.name}', pose keeper {(keeper != null ? "disabled" : "absent")}, Redux Animator kept; " +
                   AnimatorNote(animation.GetComponent<Animator>(), DeployPlaceholder);
            return true;
        }

        public static bool TryChute(Module_Ksp1Animations m, AnimEntryDef def, out AnimationState semi, out AnimationState full,
            out Func<ReduxChuteState> read, out string note)
        {
            semi = full = null;
            read = null;
            var chute = m.GetComponent<Module_Parachute>();
            if (chute == null || !chute.DataModules.TryGetByType<Data_Parachute>(out var data) || data == null)
            {
                note = "no Module_Parachute with a Data_Parachute on the part";
                return false;
            }
            if (!TryHolder(m, def.Clip, def.SecondClip, null, out var animation, out note)) return false;
            var animator = animation.GetComponent<Animator>();
            var controller = animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none";
            if (controller.IndexOf(ChuteOverrideMarker, StringComparison.Ordinal) < 0)
            {
                note = $"the Animator on '{animation.gameObject.name}' is not Redux's parachute copy (controller '{controller}')";
                return false;
            }
            animation.enabled = true;
            semi = animation[def.Clip];
            full = animation[def.SecondClip];
            if (semi == null || full == null)
            {
                note = $"clips '{def.Clip}'/'{def.SecondClip}' not both on '{animation.gameObject.name}'";
                return false;
            }
            var d = data;
            read = () => (ReduxChuteState)(byte)d.deployState.GetValue();
            note = $"on '{animation.gameObject.name}', Redux Animator kept; " +
                   AnimatorNote(animator, "KSP1_GenericParachute_SemiDeploy");
            return true;
        }

        /// <summary>The Module_Deployable (Module_SolarPanel included) whose data names the clip, else the plain
        /// Module_Deployable, else the first one.</summary>
        private static Module_Deployable PickDeployable(Module_Ksp1Animations m, string clip, out Data_Deployable data)
        {
            data = null;
            Module_Deployable plain = null, first = null;
            Data_Deployable plainData = null, firstData = null;
            foreach (var d in m.GetComponents<Module_Deployable>())
            {
                if (d == null || !d.DataModules.TryGetByType<Data_Deployable>(out var dd) || dd == null) continue;
                if (string.Equals(dd.animationName, clip, StringComparison.OrdinalIgnoreCase))
                {
                    data = dd;
                    return d;
                }
                if (first == null) { first = d; firstData = dd; }
                if (plain == null && d.GetType() == typeof(Module_Deployable)) { plain = d; plainData = dd; }
            }
            data = plain != null ? plainData : firstData;
            return plain ?? first;
        }

        /// <summary>The Redux-bound holder of <paramref name="clip"/> (and <paramref name="second"/>, when given).</summary>
        private static bool TryHolder(Module_Ksp1Animations m, string clip, string second, string objectName,
            out UnityEngine.Animation animation, out string note)
        {
            animation = null;
            var animations = m.GetComponentsInChildren<UnityEngine.Animation>(true);
            var candidates = animations.Select(a => new AnimationCandidate(a.gameObject.name,
                a[clip] != null && (second == null || a[second] != null),
                !a.enabled && a.GetComponent<Animator>() != null)).ToArray();
            var i = AnimationLookup.SelectReduxBound(candidates, objectName);
            if (i < 0)
            {
                var holders = candidates.Count(c => c.HasClip);
                note = holders == 0
                    ? $"no Animation holds '{clip}'{(second != null ? $" and '{second}'" : "")}"
                    : $"'{clip}' is on {holders} legacy Animation(s), none bound to a Redux Animator (Redux's legacy runtime plays it)";
                return false;
            }
            animation = animations[i];
            note = null;
            return true;
        }

        /// <summary>What Redux's copy holds: the evidence for the cause, read in game.</summary>
        private static string AnimatorNote(Animator animator, string placeholder)
        {
            try
            {
                if (animator == null) return "no Animator";
                var aoc = animator.runtimeAnimatorController as AnimatorOverrideController;
                var copy = aoc == null ? null : aoc[placeholder];
                if (copy == null) return $"Animator controller '{(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}', no override for {placeholder}";
                return $"Mecanim copy '{copy.name}' empty={copy.empty} legacy={copy.legacy} length={copy.length:0.###} s, Animator enabled={animator.enabled}";
            }
            catch (Exception e)
            {
                return "Animator not read: " + e.GetBaseException().Message;
            }
        }
    }
}
