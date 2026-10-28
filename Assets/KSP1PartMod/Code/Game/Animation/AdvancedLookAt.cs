using KSP1PartMod.Animation.Behaviours;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>DeployableEngines' <c>ModuleAdvancedLookAtConstraint</c> (spec 2b.4, notes §3.2b): each frame, after the
    /// clips have moved the part, every rotator looks at its target with the target's up
    /// (<c>rotation = LookRotation(target.position - rotator.position, target.up)</c>). With an axis constrained, its local
    /// Euler angles are blended per axis with <see cref="AxisBlend"/>, the "original" being the rotator's local rotation
    /// at the start of this frame, as KSP1 (the blend compounds frame after frame). Visual only. A plain
    /// <c>MonoBehaviour</c> added by <c>Module_Ksp1Animations</c> at bind and destroyed in its <c>OnShutdown</c>: no ECS
    /// type; one per look-at entry. No allocation per frame.</summary>
    internal sealed class AdvancedLookAt : MonoBehaviour
    {
        private Transform[] _rotators = new Transform[0];
        private Transform[] _targets = new Transform[0];
        private bool[] _constrained;
        private float[] _scales;
        private bool _anyConstrained;

        /// <param name="rotators">Same length as <paramref name="targets"/>; a null pair is skipped.</param>
        /// <param name="constrained"><c>axisConstrained</c> (x, y, z), null when none.</param>
        /// <param name="scales"><c>axisScales</c> (x, y, z), null when none.</param>
        public void Init(Transform[] rotators, Transform[] targets, bool[] constrained, float[] scales)
        {
            _rotators = rotators ?? new Transform[0];
            _targets = targets ?? new Transform[0];
            _constrained = constrained;
            _scales = scales;
            _anyConstrained = false;
            if (constrained != null)
                for (var i = 0; i < constrained.Length && i < 3; i++) _anyConstrained |= constrained[i];
        }

        private void LateUpdate() => Apply();

        /// <summary>One frame of the constraint (internal for tests: <c>LateUpdate</c> does not run in EditMode).</summary>
        internal void Apply()
        {
            var n = _rotators.Length < _targets.Length ? _rotators.Length : _targets.Length;
            for (var i = 0; i < n; i++)
            {
                var rotator = _rotators[i];
                var target = _targets[i];
                if (rotator == null || target == null) continue;
                var direction = target.position - rotator.position;
                if (direction.sqrMagnitude < 1e-12f) continue; // LookRotation of a zero vector: Unity logs and does nothing
                if (!_anyConstrained)
                {
                    rotator.rotation = Quaternion.LookRotation(direction, target.up);
                    continue;
                }
                var original = rotator.localEulerAngles;
                rotator.rotation = Quaternion.LookRotation(direction, target.up);
                var look = rotator.localEulerAngles;
                float x = original.x, y = original.y, z = original.z;
                AxisBlend.Blend(ref x, ref y, ref z, look.x, look.y, look.z, _constrained, _scales);
                rotator.localEulerAngles = new Vector3(x, y, z);
            }
        }
    }
}
