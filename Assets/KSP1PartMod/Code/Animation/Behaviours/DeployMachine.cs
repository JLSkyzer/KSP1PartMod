using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    public enum DeployState { Retracted, Extending, Extended, Retracting }

    /// <summary>One clip between retracted and extended (research notes <c>stock/NOTES.md</c> §2 and §10).
    /// No saved state means Retracted for both kinds (gear keeps Redux's start state, by the spec's ruling, where KSP1
    /// would start it deployed).
    /// <para><b>Deploy</b> (the ModuleDeployablePart family): Extend only from
    /// Retracted, from time 0 at speed +1 (editor: +<see cref="AnimEntryDef.EditorSpeedMult"/>); Retract only from
    /// Extended, and only when <see cref="AnimEntryDef.Retractable"/> or in the editor, from time 1 at −1. A move ends
    /// when the time reaches its end.</para>
    /// <para><b>Gear</b> (ModuleWheelDeployment, ModuleLandingLeg): the clip runs
    /// forward to extend when <see cref="AnimEntryDef.DeployForward"/>, backward otherwise; extending runs at
    /// <see cref="AnimEntryDef.SpeedFactorIn"/>, retracting at <see cref="AnimEntryDef.SpeedFactorOut"/>, from the
    /// current time; a move can be reversed halfway; it ends within 0.01 of its end, and at once in the editor
    /// (<see cref="AnimEntryDef.EditorSpeedMult"/> ≥ 1000). KSP1 starts a gear retraction from the time that matches
    /// the physics-compressed piston; we start from the current time.</para>
    /// Saved: <c>.t</c>, <c>.state</c> (the <see cref="DeployState"/> number).</summary>
    public sealed class DeployMachine : IEntryMachine
    {
        private const float Snap = 1000f;
        private const float GearTolerance = 0.01f;
        private const float MinFactor = 1e-4f;

        private readonly string _kt, _kState;
        private readonly bool _gear, _retractable;
        private readonly float _extendedTime, _speedIn, _speedOut, _editorSpeedMult;
        private float _t, _speed;

        /// <param name="clipLength">Seconds, from the bound <c>AnimationState.length</c>.</param>
        /// <param name="editorSpeed"><see cref="EditorSpeed"/>: positive, it replaces the cfg's editor multiplier.</param>
        public DeployMachine(AnimEntryDef def, float clipLength, float editorSpeed = EditorSpeed.Ksp1)
        {
            _kt = def.Key + ".t";
            _kState = def.Key + ".state";
            _gear = def.Kind == EntryKind.Gear;
            _retractable = def.Retractable;
            _extendedTime = _gear && !def.DeployForward ? 0f : 1f;
            // A factor of 0 or less would never end (or run backward): keep it positive.
            _speedIn = _gear ? Math.Max(MinFactor, def.SpeedFactorIn) : 1f;
            _speedOut = _gear ? Math.Max(MinFactor, def.SpeedFactorOut) : 1f;
            _editorSpeedMult = EditorSpeed.Multiplier(editorSpeed, def.EditorSpeedMult);
            ClipLength = Machine.Length(clipLength);
            Settle(DeployState.Retracted);
            Changed = false;
        }

        public float ClipLength { get; }

        public DeployState State { get; private set; }

        public bool IsMoving => State == DeployState.Extending || State == DeployState.Retracting;

        public bool Changed { get; private set; }

        public ClipPose Pose => new ClipPose(_t, IsMoving ? _speed : 0f, 1f, true);

        private float RetractedTime => 1f - _extendedTime;

        public bool Extend(bool inEditor)
        {
            if (!(State == DeployState.Retracted || _gear && State == DeployState.Retracting)) return false;
            if (!_gear) _t = RetractedTime;
            return Move(DeployState.Extending, _speedIn, inEditor);
        }

        /// <summary>False when the state does not allow it, or when the entry is not retractable outside the
        /// editor.</summary>
        public bool Retract(bool inEditor)
        {
            if (!_retractable && !inEditor) return false;
            if (!(State == DeployState.Extended || _gear && State == DeployState.Extending)) return false;
            if (!_gear) _t = _extendedTime;
            return Move(DeployState.Retracting, _speedOut, inEditor);
        }

        public void Tick(float dt)
        {
            if (!IsMoving || !(dt > 0f)) return;
            _t += _speed * dt / ClipLength;
            Changed = true;
            var end = State == DeployState.Extending ? _extendedTime : RetractedTime;
            var passed = _speed > 0f ? _t >= end : _t <= end;
            var near = _gear && Math.Abs(Machine.Clamp01(_t) - end) < GearTolerance;
            if (passed || near) Settle(State == DeployState.Extending ? DeployState.Extended : DeployState.Retracted);
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!Machine.TryRead(saved, _kState, out var s)) return;
            var state = (int)s;
            if (state != s || state < (int)DeployState.Retracted || state > (int)DeployState.Retracting) return;
            switch ((DeployState)state)
            {
                case DeployState.Extended: Settle(DeployState.Extended); break;
                case DeployState.Retracted: Settle(DeployState.Retracted); break;
                default:
                    // KSP1 resumes at the stored time and speed; we resume at flight speed in the saved direction.
                    _t = Machine.TryRead(saved, _kt, out var t) ? Machine.Clamp01(t) : _t;
                    var target = (DeployState)state == DeployState.Extending ? _extendedTime : RetractedTime;
                    var factor = (DeployState)state == DeployState.Extending ? _speedIn : _speedOut;
                    State = (DeployState)state;
                    _speed = target >= _t ? factor : -factor;
                    break;
            }
            Changed = false;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kt] = _t;
            into[_kState] = (int)State;
            Changed = false;
        }

        private bool Move(DeployState moving, float factor, bool inEditor)
        {
            var target = moving == DeployState.Extending ? _extendedTime : RetractedTime;
            if (inEditor && _editorSpeedMult >= Snap)
            {
                Settle(moving == DeployState.Extending ? DeployState.Extended : DeployState.Retracted);
                return true;
            }
            var speed = inEditor ? factor * _editorSpeedMult : factor;
            State = moving;
            Changed = true;
            _speed = target >= _t ? speed : -speed;
            return true;
        }

        private void Settle(DeployState state)
        {
            Changed = true;
            State = state;
            _t = state == DeployState.Extended ? _extendedTime : RetractedTime;
            _speed = 0f;
        }
    }
}
