using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>ModuleAnimateGeneric: a clip toggled between 0 (closed) and 1 (open) (research notes
    /// <c>stock/NOTES.md</c> §1). The state is held in pose mode; a move runs at speed ±1 in flight, or
    /// ±<see cref="AnimEntryDef.EditorSpeedMult"/> × clip length in the editor (KSP1 10: 0.1 s), and settles at
    /// the end it was heading for. A one-shot entry (<c>isOneShot</c>, <see cref="AnimEntryDef.Retractable"/>
    /// false) is fixed once a move has ended open. Saved: <c>.t</c>, <c>.closed</c>, <c>.moving</c>.</summary>
    public sealed class ToggleMachine : IEntryMachine
    {
        private readonly string _kt, _kClosed, _kMoving;
        private readonly bool _oneShot;
        private readonly float _editorSpeedMult, _editorSpeed;
        private float _t, _speed;

        /// <param name="clipLength">Seconds, from the bound <c>AnimationState.length</c>.</param>
        /// <param name="editorSpeed"><see cref="EditorSpeed"/>: positive, the flight speed times it; 0, KSP1's rule.</param>
        public ToggleMachine(AnimEntryDef def, float clipLength, float editorSpeed = EditorSpeed.Ksp1)
        {
            _kt = def.Key + ".t";
            _kClosed = def.Key + ".closed";
            _kMoving = def.Key + ".moving";
            _oneShot = !def.Retractable;
            _editorSpeedMult = def.EditorSpeedMult;
            _editorSpeed = editorSpeed;
            ClipLength = Machine.Length(clipLength);
            Closed = true;
        }

        public float ClipLength { get; }

        /// <summary>KSP1 <c>animSwitch</c>: true means at or going to 0.</summary>
        public bool Closed { get; private set; }

        public bool IsMoving { get; private set; }

        public bool Changed { get; private set; }

        /// <summary>A one-shot that has opened: KSP1's FIXED, no toggle any more.</summary>
        public bool IsFixed => _oneShot && !Closed && !IsMoving;

        public ClipPose Pose => new ClipPose(_t, IsMoving ? _speed : 0f, 1f, true);

        /// <summary>Flips <see cref="Closed"/> and starts the move. Refused (false) while moving, since KSP1 hides
        /// the event and the action then, and once fixed.</summary>
        public bool Toggle(bool inEditor)
        {
            if (IsMoving || IsFixed) return false;
            Closed = !Closed;
            var speed = !inEditor ? 1f
                : !EditorSpeed.UsesKsp1(_editorSpeed) ? _editorSpeed
                : _editorSpeedMult > 0f ? _editorSpeedMult * ClipLength : 1f;
            Start(Closed ? -speed : speed);
            return true;
        }

        public void Tick(float dt)
        {
            if (!IsMoving || !(dt > 0f)) return;
            _t += _speed * dt / ClipLength;
            Changed = true;
            if (_t >= 1f || _t <= 0f)
            {
                // KSP1 snaps animTime to the end the switch points at once the clip has stopped.
                _t = Closed ? 0f : 1f;
                IsMoving = false;
            }
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (Machine.TryRead(saved, _kt, out var t)) _t = Machine.Clamp01(t);
            if (Machine.TryRead(saved, _kClosed, out var closed)) Closed = Machine.Bool(closed);
            // KSP1 resumes a MOVING state at its saved speed; we do not save the speed, so it resumes at flight speed.
            if (Machine.TryRead(saved, _kMoving, out var moving) && Machine.Bool(moving)) Start(Closed ? -1f : 1f);
            else IsMoving = false;
            Changed = false;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kt] = _t;
            into[_kClosed] = Machine.Of(Closed);
            into[_kMoving] = Machine.Of(IsMoving);
            Changed = false;
        }

        private void Start(float speed)
        {
            _speed = speed;
            IsMoving = true;
            Changed = true;
        }
    }
}
