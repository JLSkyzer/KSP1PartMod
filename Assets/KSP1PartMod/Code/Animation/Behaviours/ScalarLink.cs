using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>KSP1's <c>FXModuleThrottleEffects</c> state for one linked Color entry (spec 2a.7): flight only,
    /// <c>state = lerp(state, target, responseSpeed × 25 × dt)</c> (factor clamped to 0..1), snapped to 1 above
    /// 0.99995 when rising and to 0 below 5e-5 when falling. In the editor it holds 0, as KSP1 only runs the setter
    /// in flight. Nothing is saved: KSP1 persists the setter's <c>state</c>, which restarts at 0 here (one smoothing
    /// step of difference). The caller hands <see cref="State"/> to <see cref="ColorRamp.SetScalar"/>.</summary>
    public sealed class ScalarLink : IEntryMachine
    {
        private readonly float _response;

        public ScalarLink(AnimEntryDef e)
        {
            _response = e.ScalarResponse;
        }

        /// <summary>KSP1 <c>state</c>, 0..1.</summary>
        public float State { get; private set; }

        /// <summary>Always false: nothing is saved.</summary>
        public bool Changed => false;

        /// <summary>The setter's target: the engine's normalized output with <c>dependOnOutput</c>, else its throttle
        /// with <c>dependOnThrottle</c>, else the vessel's main throttle, clamped to 0..1 (NaN gives 0); 0 when
        /// <c>dependOnEngineState</c> and the engine is not operational.</summary>
        public static float Target(AnimEntryDef e, float throttle, float output, float mainThrottle, bool operational)
        {
            if (e.DependOnEngineState && !operational) return 0f;
            var v = e.DependOnOutput ? output : e.DependOnThrottle ? throttle : mainThrottle;
            return float.IsNaN(v) ? 0f : Machine.Clamp01(v);
        }

        public void Tick(float dt, bool inEditor, float target)
        {
            if (inEditor)
            {
                State = 0f;
                return;
            }
            if (!(dt > 0f) || float.IsNaN(target)) return;
            var k = Machine.Clamp01(_response * 25f * dt);
            var v = State + (target - State) * k;
            if (v > 0.99995f && State < target) v = 1f;
            if (v < 5e-5f && target < State) v = 0f;
            State = v;
        }

        public void Load(IReadOnlyDictionary<string, float> saved) { }

        public void Save(IDictionary<string, float> into) { }
    }
}
