using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB's <c>ModuleBdbDecouplerAnimation</c> (notes §3.8), visual only: the clip plays forward
    /// once, at flight speed, when the part's decoupler turns decoupled. A part already decoupled when first seen
    /// (a load), or with a saved play, shows the end pose. The editor has a preview toggle at
    /// <see cref="EditorSpeed.Multiplier"/> (KSP1 value 1). Saved: <c>.t</c>.</summary>
    public sealed class DecouplerClipMachine : IEntryMachine
    {
        private readonly string _kt;
        private readonly float _length;
        private float _t, _speed, _lastSaved;
        private bool _done, _seenFlight, _previewOn;

        public DecouplerClipMachine(AnimEntryDef def, float clipLength)
        {
            _kt = def.Key + ".t";
            _length = Machine.Length(clipLength);
        }

        public bool Changed => _t != _lastSaved;

        public ClipPose Pose => new ClipPose(_t, _speed, 1f, true);

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
                Step(dt, _previewOn, EditorSpeed.Multiplier(editorSpeedSetting, 1f));
                return;
            }
            if (!_seenFlight)
            {
                _seenFlight = true;
                if (decoupled && !_done)
                {
                    _t = 1f;
                    _done = true;
                    _speed = 0f;
                    return;
                }
            }
            if (decoupled) _done = true;
            if (_done) Step(dt, true, 1f);
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!Machine.TryRead(saved, _kt, out var t)) return;
            if (t > 0f) { _t = 1f; _done = true; }
            _speed = 0f;
            _lastSaved = _t;
        }

        public void Save(IDictionary<string, float> into)
        {
            _lastSaved = _t;
            into[_kt] = _t;
        }

        private void Step(float dt, bool forward, float rate)
        {
            if (!(dt > 0f)) return;
            if (forward && _t < 1f) _speed = rate;
            else if (!forward && _t > 0f) _speed = -rate;
            else { _speed = 0f; return; }
            _t += _speed * dt / _length;
            if (_t >= 1f) { _t = 1f; _speed = 0f; }
            else if (_t <= 0f) { _t = 0f; _speed = 0f; }
        }
    }
}
