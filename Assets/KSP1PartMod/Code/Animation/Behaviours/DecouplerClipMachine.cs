using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB ModuleBdbDecouplerAnimation (notes 3.8), visual only. On the first flight tick the pose
    /// comes from the decoupler (decoupled: end pose, else 0, as KSP1 resets the position at flight start); then
    /// the clip plays forward once, at flight speed, when the decoupler turns decoupled. The editor has a preview
    /// toggle at <see cref="EditorSpeed.Multiplier"/> (KSP1 value 1) kept apart from the saved play, so a vessel
    /// launched with the preview out shows the decoupler attached. Saved: <c>.t</c>.</summary>
    public sealed class DecouplerClipMachine : IEntryMachine
    {
        private readonly string _kt;
        private readonly float _length;
        private float _t, _speed, _lastSaved, _pv, _pvSpeed;
        private bool _done, _seenFlight, _previewOn, _inEditor;

        public DecouplerClipMachine(AnimEntryDef def, float clipLength)
        {
            _kt = def.Key + ".t";
            _length = Machine.Length(clipLength);
        }

        public bool Changed => _t != _lastSaved;

        public ClipPose Pose => _inEditor ? new ClipPose(_pv, _pvSpeed, 1f, true) : new ClipPose(_t, _speed, 1f, true);

        /// <summary>The editor preview's target (the PAM button's state).</summary>
        public bool PreviewOn => _previewOn;

        /// <summary>The editor preview: forward, then back. Always accepted.</summary>
        public bool TogglePreview()
        {
            _previewOn = !_previewOn;
            return true;
        }

        public void Tick(float dt, bool inEditor, bool decoupled, float editorSpeedSetting)
        {
            if (inEditor)
            {
                _inEditor = true;
                Step(dt, _previewOn, EditorSpeed.Multiplier(editorSpeedSetting, 1f), ref _pv, ref _pvSpeed);
                return;
            }
            _inEditor = false;
            if (!_seenFlight)
            {
                _seenFlight = true;
                _done = decoupled;
                _t = decoupled ? 1f : 0f;
                _speed = 0f;
                return;
            }
            if (decoupled) _done = true;
            if (_done) Step(dt, true, 1f, ref _t, ref _speed);
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (Machine.TryRead(saved, _kt, out var t)) _lastSaved = Machine.Clamp01(t);
        }

        public void Save(IDictionary<string, float> into)
        {
            _lastSaved = _t;
            into[_kt] = _t;
        }

        private void Step(float dt, bool forward, float rate, ref float t, ref float speed)
        {
            if (!(dt > 0f)) return;
            if (forward && t < 1f) speed = rate;
            else if (!forward && t > 0f) speed = -rate;
            else { speed = 0f; return; }
            t += speed * dt / _length;
            if (t >= 1f) { t = 1f; speed = 0f; }
            else if (t <= 0f) { t = 0f; speed = 0f; }
        }
    }
}
