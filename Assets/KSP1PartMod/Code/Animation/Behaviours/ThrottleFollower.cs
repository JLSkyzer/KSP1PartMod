using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>FXModuleAnimateThrottle with <c>affectTime</c> (research notes <c>stock/NOTES.md</c> §4): the clip
    /// time follows a target, smoothed as <c>animState += (target − animState) × min(1, responseSpeed × 25 × dt)</c>,
    /// snapped to 1 above 0.99995 when rising and to 0 below 5e-5 when falling.
    /// <para>Target: in flight, the throttle clamped to 0..1 when the engine is operational, else 0; in the editor,
    /// 1 with <see cref="AnimEntryDef.PlayInEditor"/>, else 0. KSP1 never stores the editor value back into
    /// <c>animState</c>, so the editor pose is one smoothing step away from the saved value and does not
    /// accumulate. With no engine link the caller passes the vessel throttle and operational = true.</para>
    /// <para>With <see cref="AnimEntryDef.WeightOnOperational"/>, the weight moves from 1 toward operational ? 1 : 0
    /// (editor with PlayInEditor: 1) by <c>min(1, responseSpeed × 75 × dt)</c>, snapped above 0.999 and below 0.001.</para>
    /// Saved: <c>.s</c> (animState).</summary>
    public sealed class ThrottleFollower : IEntryMachine
    {
        private readonly string _ks;
        private readonly float _response;
        private readonly bool _playInEditor, _weightOnOperational;
        private float _time, _weight = 1f;

        public ThrottleFollower(AnimEntryDef def)
        {
            _ks = def.Key + ".s";
            _response = def.ResponseSpeed;
            _playInEditor = def.PlayInEditor;
            _weightOnOperational = def.WeightOnOperational;
        }

        /// <summary>KSP1 <c>animState</c>: the smoothed value kept between frames.</summary>
        public float Value { get; private set; }

        public ClipPose Pose => new ClipPose(_time, 0f, _weight, true);

        public void Tick(float dt, bool inEditor, float throttle, bool operational)
        {
            if (!(dt > 0f)) return;
            if (_weightOnOperational)
            {
                var wTarget = inEditor && _playInEditor || operational ? 1f : 0f;
                var w = Lerp(_weight, wTarget, _response * 75f * dt);
                if (w > 0.999f && _weight < wTarget) w = 1f;
                if (w < 0.001f && wTarget < _weight) w = 0f;
                _weight = w;
            }

            float target;
            if (inEditor) target = _playInEditor ? 1f : 0f;
            else target = operational && !float.IsNaN(throttle) ? Machine.Clamp01(throttle) : 0f;
            var v = Lerp(Value, target, _response * 25f * dt);
            if (v > 0.99995f && Value < target) v = 1f;
            if (v < 5e-5f && target < Value) v = 0f;
            _time = v;
            if (!inEditor) Value = v;
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!Machine.TryRead(saved, _ks, out var s)) return;
            Value = _time = Machine.Clamp01(s);
        }

        public void Save(IDictionary<string, float> into) => into[_ks] = Value;

        /// <summary>Unity's Mathf.Lerp: the factor is clamped to 0..1.</summary>
        private static float Lerp(float a, float b, float k) => a + (b - a) * Machine.Clamp01(k);
    }
}
