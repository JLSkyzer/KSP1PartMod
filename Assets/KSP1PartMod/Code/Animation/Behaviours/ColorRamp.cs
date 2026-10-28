using System;
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
        private readonly bool _useRate, _scalarLinked;
        private readonly FloatCurve _r, _g, _b, _a;

        public ColorRamp(AnimEntryDef def)
        {
            _kOn = def.Key + ".on";
            _rate = Math.Max(1e-4f, def.Rate); // a rate of 0 or less would never reach the target
            _useRate = def.UseRate;
            _scalarLinked = def.ScalarLinked;
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

        public bool Changed { get; private set; }

        public void Toggle()
        {
            On = !On;
            Changed = true;
            if (!_useRate) SetLevel(On ? 1f : 0f);
        }

        public void Tick(float dt)
        {
            if (_scalarLinked || !_useRate || !(dt > 0f)) return;
            var target = On ? 1f : 0f;
            if (Level == target) return;
            var step = _rate * dt;
            SetLevel(On ? Math.Min(1f, Level + step) : Math.Max(0f, Level - step));
        }

        /// <summary>KSP1 <c>SetScalar(t)</c>, called by an FX setter (<see cref="ScalarLink"/>): t clamped to 0..1 (NaN
        /// gives 0), <see cref="On"/> = t ≥ 0.5, the colour of the curves at t. A linked entry never ramps in
        /// <see cref="Tick"/>; the value set here is the one shown.</summary>
        public void SetScalar(float t)
        {
            t = float.IsNaN(t) ? 0f : Machine.Clamp01(t);
            On = t >= 0.5f;
            if (t != Level) SetLevel(t);
        }

        /// <summary>A <see cref="AnimEntryDef.ScalarLinked"/> ramp reads nothing (spec 2a.7: its state follows the FX
        /// setter, which restarts at 0).</summary>
        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (_scalarLinked || !Machine.TryRead(saved, _kOn, out var on)) return;
            On = Machine.Bool(on);
            SetLevel(On ? 1f : 0f);
            Changed = false;
        }

        /// <summary>A <see cref="AnimEntryDef.ScalarLinked"/> ramp writes nothing.</summary>
        public void Save(IDictionary<string, float> into)
        {
            if (!_scalarLinked) into[_kOn] = Machine.Of(On);
            Changed = false;
        }

        private void SetLevel(float level)
        {
            Level = level;
            Color = ColorAt(level);
        }

        /// <summary>The colour of the curves at <paramref name="level"/>, without changing the state (an entry held at rest
        /// by a B9 gate shows level 0).</summary>
        public Rgba ColorAt(float level) => new Rgba(Eval(_r, level), Eval(_g, level), Eval(_b, level), Eval(_a, level));

        private static FloatCurve Channel(float[][] curves, int i) =>
            curves != null && i < curves.Length && curves[i] != null && curves[i].Length >= 4 ? new FloatCurve(curves[i]) : null;

        private static float Eval(FloatCurve c, float t) => c == null ? 0f : c.Evaluate(t);
    }
}
