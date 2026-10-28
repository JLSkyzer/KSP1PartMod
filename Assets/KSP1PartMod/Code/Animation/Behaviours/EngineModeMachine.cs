using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>DeployableEngines' <c>ModuleMultiStateEngine</c> (notes §3.6): the clip plays forward while the
    /// engine's active mode is not <see cref="AnimEntryDef.ModeAtTimeZero"/> and backward otherwise, at
    /// <see cref="AnimEntryDef.SpeedMultiplier"/> clip seconds per second (times <see cref="EditorSpeed.Multiplier"/>,
    /// KSP1 value 2, in the editor). Nothing is saved: the pose follows the engine mode.</summary>
    public sealed class EngineModeMachine : IEntryMachine
    {
        private readonly string _modeAtZero;
        private readonly float _length, _speedMult;
        private float _t, _speed;

        public EngineModeMachine(AnimEntryDef def, float clipLength)
        {
            _modeAtZero = def.ModeAtTimeZero;
            _speedMult = def.SpeedMultiplier > 0f ? def.SpeedMultiplier : 1f;
            _length = Machine.Length(clipLength);
        }

        public bool Changed => false;

        public ClipPose Pose => new ClipPose(_t, _speed, 1f, true);

        /// <summary>Flight start: the pose is the target of the mode, with no motion.</summary>
        public void Start(string activeMode)
        {
            _t = Forward(activeMode) ? 1f : 0f;
            _speed = 0f;
        }

        public void Tick(float dt, bool inEditor, string activeMode, float editorSpeedSetting)
        {
            if (!(dt > 0f)) return;
            var forward = Forward(activeMode);
            var rate = _speedMult * (inEditor ? EditorSpeed.Multiplier(editorSpeedSetting, 2f) : 1f);
            if (forward && _t < 1f) _speed = rate;
            else if (!forward && _t > 0f) _speed = -rate;
            else _speed = 0f;
            if (_speed == 0f) return;
            _t += _speed * dt / _length;
            if (_t >= 1f) { _t = 1f; _speed = 0f; }
            else if (_t <= 0f) { _t = 0f; _speed = 0f; }
        }

        public void Load(IReadOnlyDictionary<string, float> saved) { }

        public void Save(IDictionary<string, float> into) { }

        private bool Forward(string mode) =>
            mode != null && !string.Equals(mode, _modeAtZero, System.StringComparison.Ordinal);
    }
}
