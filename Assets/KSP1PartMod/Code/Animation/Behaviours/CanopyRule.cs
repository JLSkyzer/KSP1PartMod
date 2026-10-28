using UnityEngine;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Why a KSP1 radial canopy spread out and turned (0.13.0, in game). <c>Module_Parachute.OnLateUpdate</c>
    /// (Redux 26w42c) orients the canopy bone, while semi-deployed or deployed, with
    /// <c>bone.rotation = bone.rotation · parent.rotation⁻¹ · C</c> when its Animator is not on the bone itself. That
    /// compose expects the Animator to put the bone back each frame; a KSP1 radial clip only scales the canopy and
    /// Redux's Mecanim copy of it is empty, so nothing does, and the rotation compounds frame after frame (and the
    /// fixed-step <c>Rotate(spreadAngle × symmetryCount)</c> with it). Putting the bone back to its parent's rotation
    /// (local identity) before the compose gives exactly <c>C</c>, which is what Redux's own branch for a KSP1 Animator
    /// on the bone writes. Pure.</summary>
    public static class CanopyRule
    {
        /// <summary>The states in which Redux composes the canopy rotation.</summary>
        public static bool Applies(ReduxChuteState state) =>
            state == ReduxChuteState.SemiDeployed || state == ReduxChuteState.Deployed;

        /// <summary>Redux's relative compose, as written in <c>Module_Parachute.OnLateUpdate</c>.</summary>
        public static Quaternion ReduxCompose(Quaternion bone, Quaternion parent, Quaternion canopy) =>
            bone * Quaternion.Inverse(parent) * canopy;

        public static string ResetLine(string part, string bone, string holder) =>
            $"[KSP1PartMod] chute: {part}: canopy reset on '{bone}' (animator on '{holder}')";

        public static string SkipLine(string part, string reason) =>
            $"[KSP1PartMod] chute: {part}: canopy left to Redux ({reason})";
    }
}
