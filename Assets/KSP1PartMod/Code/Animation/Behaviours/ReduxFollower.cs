using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary><c>Data_Deployable.DeployState</c> of Redux 26w42c, same byte values.</summary>
    public enum ReduxDeployState : byte { Retracted, Extended, Retracting, Extending, Broken }

    /// <summary>Draws the legacy clip of a deployable whose state Redux owns. Redux 26w42c binds a single deployable
    /// source to a Mecanim copy of the clip (<c>Ksp1GenericDeployableAnimatorBuilder.TryConfigure</c>: Instantiate,
    /// <c>legacy = false</c>), but the clip was built at runtime with <c>SetCurve</c>, which Unity documents as working
    /// at runtime for legacy clips only: the copy moves nothing. Redux's state machine still runs (its Animator reaches
    /// OPENED and <c>Module_Deployable.OnAnimStateEnter</c> sets Extended), so we read the state and move the legacy
    /// clip toward it at KSP1 speed: flight 1 (gear: <see cref="AnimEntryDef.SpeedFactorIn"/> / Out), editor the
    /// <see cref="EditorSpeed"/> setting, else <see cref="AnimEntryDef.EditorSpeedMult"/> (≥ 1000 snaps, ≤ 0 means
    /// flight speed). The follower owns the visual time: Redux's empty Mecanim copy reaches Extended (or Retracted) in
    /// one frame, and only the direction is taken from it. The first state seen is shown at once (load). Broken holds.
    /// Nothing is saved: the state is Redux's.</summary>
    public sealed class ReduxFollower : IEntryMachine
    {
        private const float Snap = 1000f;
        private const float MinFactor = 1e-4f;

        private readonly float _extendedTime, _speedIn, _speedOut, _editorMult;
        private bool? _target;
        private float _t, _speed;

        /// <param name="editorSpeed"><see cref="EditorSpeed"/>: positive, it replaces the cfg's editor multiplier.</param>
        public ReduxFollower(AnimEntryDef def, float clipLength, float editorSpeed = EditorSpeed.Ksp1)
        {
            _extendedTime = def.DeployForward ? 1f : 0f;
            _speedIn = Math.Max(MinFactor, def.SpeedFactorIn);
            _speedOut = Math.Max(MinFactor, def.SpeedFactorOut);
            _editorMult = EditorSpeed.Multiplier(editorSpeed, def.EditorSpeedMult > 0f ? def.EditorSpeedMult : 1f);
            ClipLength = Machine.Length(clipLength);
        }

        public float ClipLength { get; }

        /// <summary>Extended or extending: true; retracted or retracting: false; broken: null (hold).</summary>
        public static bool? Target(ReduxDeployState state)
        {
            switch (state)
            {
                case ReduxDeployState.Extended:
                case ReduxDeployState.Extending: return true;
                case ReduxDeployState.Retracted:
                case ReduxDeployState.Retracting: return false;
                default: return null;
            }
        }

        /// <summary>The target last followed; null before the first known state.</summary>
        public bool? Following => _target;

        public ClipPose Pose => _target == null ? ClipPose.Off : new ClipPose(_t, _speed, 1f, true);

        /// <summary>True when the target changed (the first state included).</summary>
        public bool Tick(float dt, ReduxDeployState state, bool inEditor)
        {
            var target = Target(state);
            if (target == null)
            {
                _speed = 0f;
                return false;
            }
            var changed = target != _target;
            var end = target.Value ? _extendedTime : 1f - _extendedTime;
            if (_target == null)
            {
                _target = target;
                _t = end;
                _speed = 0f;
                return true;
            }
            _target = target;
            var factor = target.Value ? _speedIn : _speedOut;
            if (inEditor && _editorMult >= Snap)
            {
                _t = end;
                _speed = 0f;
                return changed;
            }
            var speed = inEditor ? factor * _editorMult : factor;
            if (_t == end || !(dt > 0f))
            {
                _speed = _t == end ? 0f : _speed;
                return changed;
            }
            var step = speed * dt / ClipLength;
            if (end > _t)
            {
                _t = Math.Min(end, _t + step);
                _speed = speed;
            }
            else
            {
                _t = Math.Max(end, _t - step);
                _speed = -speed;
            }
            if (_t == end) _speed = 0f;
            return changed;
        }

        public bool Changed => false;

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
        }

        public void Save(IDictionary<string, float> into)
        {
        }
    }
}
