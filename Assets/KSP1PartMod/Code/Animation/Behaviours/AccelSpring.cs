using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB ModuleBdbAccelAnimation (notes 3.9): no playback, a damped spring scrubs the clip.
    /// Per step: <c>v += (-K (p - anchor) + a - C v) dt; p += v dt</c>, where the anchor is AnchorDeployed while
    /// <see cref="Deployed"/> (default true, flipped by <see cref="Toggle"/>) and AnchorRetracted otherwise, and
    /// <c>a</c> is the acceleration along the part's up axis in m/s2, raw (the caller passes 9.80665 outside
    /// flight, so the spring also runs in the editor). p is clamped to 0..1; at a wall v is reversed when
    /// <see cref="Bounce"/> (default true) and zeroed otherwise. At rest p sits at anchor + a/K. NaN acceleration
    /// (no field found) holds the current anchor with no motion. Steps are at most 10 ms; dt is clamped to 0.25 s.
    /// Saved: <c>.deployed</c>, <c>.bounce</c>.</summary>
    public sealed class AccelSpring : IEntryMachine
    {
        public const float OneG = 9.80665f;
        private const float MaxStep = 0.01f;
        private const float MaxDt = 0.25f;

        private readonly string _kDeployed, _kBounce;
        private readonly float _k, _c, _retracted, _deployed;
        private float _p, _v;
        private bool _lastDeployed = true, _lastBounce = true;

        public AccelSpring(AnimEntryDef def, float clipLength)
        {
            _kDeployed = def.Key + ".deployed";
            _kBounce = def.Key + ".bounce";
            _k = def.SpringK;
            _c = def.SpringDamping;
            _retracted = def.AnchorRetracted;
            _deployed = def.AnchorDeployed;
            Deployed = true;
            Bounce = true;
            _p = _deployed;
        }

        public bool Deployed { get; private set; }

        public bool Bounce { get; private set; }

        public bool Changed => Deployed != _lastDeployed || Bounce != _lastBounce;

        public ClipPose Pose => new ClipPose(Machine.Clamp01(_p), 0f, 1f, true);

        private float Anchor => Deployed ? _deployed : _retracted;

        /// <summary>KSP1's Deploy event: moves the anchor.</summary>
        public void Toggle() => Deployed = !Deployed;

        public void Tick(float dt, float accelerationAlongUp)
        {
            if (!(dt > 0f)) return;
            if (float.IsNaN(accelerationAlongUp) || float.IsInfinity(accelerationAlongUp))
            {
                _p = Anchor;
                _v = 0f;
                return;
            }
            if (dt > MaxDt) dt = MaxDt;
            var steps = (int)Math.Ceiling(dt / MaxStep);
            var h = dt / steps;
            var anchor = Anchor;
            for (var i = 0; i < steps; i++)
            {
                _v += (-_k * (_p - anchor) + accelerationAlongUp - _c * _v) * h;
                _p += _v * h;
                if (_p < 0f) { _p = 0f; _v = Bounce ? -_v : 0f; }
                else if (_p > 1f) { _p = 1f; _v = Bounce ? -_v : 0f; }
            }
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (Machine.TryRead(saved, _kDeployed, out var d)) Deployed = Machine.Bool(d);
            if (Machine.TryRead(saved, _kBounce, out var b)) Bounce = Machine.Bool(b);
            _lastDeployed = Deployed;
            _lastBounce = Bounce;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kDeployed] = Machine.Of(Deployed);
            into[_kBounce] = Machine.Of(Bounce);
            _lastDeployed = Deployed;
            _lastBounce = Bounce;
        }
    }
}
