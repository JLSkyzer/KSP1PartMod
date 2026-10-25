using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>ModuleAnimateHeat (research notes <c>stock/NOTES.md</c> §6), with KSP1's default <c>lerpMin</c> 0,
    /// <c>lerpOffset</c> −798 (Draper point) and <c>lerpMax</c> = max temperature: x = T − 798; 1 when
    /// x ≥ maxTemp, 0 when x ≤ 0, else x / (maxTemp − 798). The upper clamp compares against maxTemp, not
    /// maxTemp − 798, so values up to maxTemp / (maxTemp − 798) pass (KSP1 quirk, kept). The scalar then goes
    /// through <c>animStateCurve</c> (<see cref="AnimEntryDef.Curves"/>[0]; null: linear 0..1). A NaN temperature
    /// reads as 0. Nothing is saved.</summary>
    public sealed class HeatScalar : IEntryMachine
    {
        public const float DraperPoint = 798f;

        private readonly FloatCurve _curve;
        private float _time;

        public HeatScalar(AnimEntryDef def)
        {
            _curve = new FloatCurve(def.Curves != null && def.Curves.Length > 0 ? def.Curves[0] : null);
            _time = _curve.Evaluate(0f);
        }

        /// <summary>The value before the curve (KSP1 <c>inputState</c>).</summary>
        public float Scalar { get; private set; }

        public ClipPose Pose => new ClipPose(_time, 0f, 1f, true);

        /// <param name="dt">Unused: KSP1 reads the temperature every frame without smoothing.</param>
        public void Tick(float dt, float temperatureK, float maxTempK)
        {
            var t = float.IsNaN(temperatureK) ? 0f : temperatureK;
            var x = t - DraperPoint;
            Scalar = x >= maxTempK ? 1f : x <= 0f ? 0f : x / (maxTempK - DraperPoint);
            _time = _curve.Evaluate(Scalar);
        }

        public void Load(IReadOnlyDictionary<string, float> saved) { }

        public void Save(IDictionary<string, float> into) { }
    }
}
