using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The nozzle clip of DeployableEngines' <c>ModuleDeployableEngine</c> and Bluedog_DB's
    /// <c>ModuleBdbAnimatedEngine</c> (research notes <c>mods/NOTES.md</c> §3.5, §3.7), visual only. In flight the
    /// clip runs forward at 1 clip second per second while any engine is ignited, and backward when none is and the
    /// entry is <see cref="AnimEntryDef.Retractable"/>; a non-retractable entry stays out once extended. At flight
    /// start the pose is the target, with no motion. In the editor a toggle previews the extension at
    /// <see cref="EditorSpeed.Multiplier"/> (KSP1 value 1). Saved: <c>.deployed</c>, only with
    /// <see cref="AnimEntryDef.PersistDeployed"/>.</summary>
    public sealed class EngineDeployMachine : IEntryMachine
    {
        private readonly string _kDeployed;
        private readonly bool _retractable, _persist;
        private readonly float _length;
        private float _t, _speed;
        private bool _manual, _editorOn, _lastSaved;

        public EngineDeployMachine(AnimEntryDef def, float clipLength)
        {
            _kDeployed = def.Key + ".deployed";
            _retractable = def.Retractable;
            _persist = def.PersistDeployed;
            _length = Machine.Length(clipLength);
        }

        public bool Changed => _persist && Deployed != _lastSaved;

        /// <summary>BDB's <c>deployed</c>: out, or moving.</summary>
        public bool Deployed => _t > 0f || _speed != 0f;

        public ClipPose Pose => new ClipPose(_t, _speed, 1f, true);

        /// <summary>The editor preview's target (the PAM button's state).</summary>
        public bool PreviewOn => _editorOn;

        /// <summary>Flight: the pose is the target from the engine state, nothing moves. A saved deployed state
        /// keeps a non-ignited engine's nozzle out.</summary>
        public void Start(bool ignited)
        {
            if (ignited) _t = 1f;
            _speed = 0f;
        }

        /// <summary>The editor preview, and in flight a manual extension when <see cref="AnimEntryDef.Retractable"/>.
        /// False when flight does not allow it.</summary>
        public bool Toggle(bool inEditor)
        {
            if (inEditor) { _editorOn = !_editorOn; return true; }
            if (!_retractable) return false;
            _manual = !_manual;
            return true;
        }

        public void Tick(float dt, bool inEditor, bool anyIgnited, float editorSpeedSetting)
        {
            if (!(dt > 0f)) return;
            var target = inEditor ? _editorOn : anyIgnited || _manual;
            var rate = inEditor ? EditorSpeed.Multiplier(editorSpeedSetting, 1f) : 1f;
            if (target && _t < 1f) _speed = rate;
            else if (!target && _t > 0f && (_retractable || inEditor)) _speed = -rate;
            else _speed = 0f;
            if (_speed == 0f) return;
            _t += _speed * dt / _length;
            if (_t >= 1f) { _t = 1f; _speed = 0f; }
            else if (_t <= 0f) { _t = 0f; _speed = 0f; }
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!_persist || !Machine.TryRead(saved, _kDeployed, out var d)) return;
            _t = Machine.Bool(d) ? 1f : 0f;
            _speed = 0f;
            _lastSaved = Deployed;
        }

        public void Save(IDictionary<string, float> into)
        {
            if (!_persist) return;
            _lastSaved = Deployed;
            into[_kDeployed] = Machine.Of(_lastSaved);
        }
    }
}
