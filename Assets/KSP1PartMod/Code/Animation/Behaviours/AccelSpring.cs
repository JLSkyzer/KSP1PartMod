using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB's <c>ModuleBdbAccelAnimation</c> (notes §3.9): no playback, a damped spring scrubs the
    /// clip. The clip position <c>p</c> is pulled toward a target between <see cref="AnimEntryDef.AnchorRetracted"/>
    /// (no acceleration) and <see cref="AnimEntryDef.AnchorDeployed"/> (one g along the part's up axis, clamped):
    /// <c>p'' = K (target - p) - C p'</c>, integrated in sub-steps of at most 10 ms. The notes give the inputs and
    /// not the formula, so this is our model of it. NaN acceleration (no field found) holds the deployed anchor;
    /// the machine also holds it in the editor, which KSP1 gives no toggle for. Nothing is saved.</summary>
    public sealed class AccelSpring : IEntryMachine
    {
        public const float OneG = 9.80665f;
        private const float MaxStep = 0.01f;
        private const int MaxSteps = 100;

        private readonly float _k, _c, _retracted, _deployed;
        private float _p, _v;

        public AccelSpring(AnimEntryDef def, float clipLength)
        {
            _k = def.SpringK;
            _c = def.SpringDamping;
            _retracted = def.AnchorRetracted;
            _deployed = def.AnchorDeployed;
            _p = _deployed;
        }

        public bool Changed => false;

        public ClipPose Pose => new ClipPose(Machine.Clamp01(_p), 0f, 1f, true);

        public void Tick(float dt, float accelerationAlongUp)
        {
            if (!(dt > 0f)) return;
            if (float.IsNaN(accelerationAlongUp) || float.IsInfinity(accelerationAlongUp))
            {
                _p = _deployed;
                _v = 0f;
                return;
            }
            var target = _retracted + (_deployed - _retracted) * Machine.Clamp01(accelerationAlongUp / OneG);
            if (!(_k > 0f)) { _p = target; _v = 0f; return; }
            var steps = Math.Min(MaxSteps, (int)Math.Ceiling(dt / MaxStep));
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                _v += (_k * (target - _p) - _c * _v) * h;
                _p += _v * h;
            }
        }

        public void Load(IReadOnlyDictionary<string, float> saved) { }

        public void Save(IDictionary<string, float> into) { }
    }
}
