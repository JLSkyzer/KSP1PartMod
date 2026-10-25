using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>ModuleColorChanger (research notes <c>stock/NOTES.md</c> §7): a 0..1 level that ramps toward
    /// <see cref="On"/> at <see cref="AnimEntryDef.Rate"/> per second, or snaps when
    /// <see cref="AnimEntryDef.UseRate"/> is false; the colour is the four curves (<see cref="AnimEntryDef.Curves"/>
    /// r, g, b, a) at that level. A channel with no keys is 0, as KSP1's empty FloatCurve. On load the level starts
    /// at the saved end. Saved: <c>.on</c>.</summary>
    public sealed class ColorRamp : IEntryMachine
    {
        private readonly string _kOn;
        private readonly float _rate;
        private readonly bool _useRate;
        private readonly FloatCurve _r, _g, _b, _a;

        public ColorRamp(AnimEntryDef def)
        {
            _kOn = def.Key + ".on";
            _rate = def.Rate;
            _useRate = def.UseRate;
            _r = Channel(def.Curves, 0);
            _g = Channel(def.Curves, 1);
            _b = Channel(def.Curves, 2);
            _a = Channel(def.Curves, 3);
            SetLevel(0f);
        }

        /// <summary>KSP1 <c>animState</c>.</summary>
        public bool On { get; private set; }

        /// <summary>KSP1 <c>currentRateState</c>.</summary>
        public float Level { get; private set; }

        public Rgba Color { get; private set; }

        public void Toggle()
        {
            On = !On;
            if (!_useRate) SetLevel(On ? 1f : 0f);
        }

        public void Tick(float dt)
        {
            if (!_useRate || !(dt > 0f)) return;
            var target = On ? 1f : 0f;
            if (Level == target) return;
            var step = _rate * dt;
            SetLevel(On ? System.Math.Min(1f, Level + step) : System.Math.Max(0f, Level - step));
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!Machine.TryRead(saved, _kOn, out var on)) return;
            On = Machine.Bool(on);
            SetLevel(On ? 1f : 0f);
        }

        public void Save(IDictionary<string, float> into) => into[_kOn] = Machine.Of(On);

        private void SetLevel(float level)
        {
            Level = level;
            Color = new Rgba(Eval(_r, level), Eval(_g, level), Eval(_b, level), Eval(_a, level));
        }

        private static FloatCurve Channel(float[][] curves, int i) =>
            curves != null && i < curves.Length && curves[i] != null && curves[i].Length >= 4 ? new FloatCurve(curves[i]) : null;

        private static float Eval(FloatCurve c, float t) => c == null ? 0f : c.Evaluate(t);
    }
}
