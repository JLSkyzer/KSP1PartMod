namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>KSP1's FloatCurve (a Unity <c>AnimationCurve</c>) without Unity: cubic Hermite between keys (a step when
    /// a tangent is infinite), the end values outside them. Built from <see cref="Cfg.AnimEntryDef.Curves"/> rows (time, value, inTangent,
    /// outTangent, flattened).</summary>
    public sealed class FloatCurve
    {
        private readonly float[] _time, _value, _in, _out;

        /// <summary>Null or empty: KSP1's default <c>animStateCurve</c>, keys (0, 0) and (1, 1), i.e. linear 0..1
        /// clamped. Keys are sorted by time; a key whose time is already present is dropped (Unity's
        /// <c>AddKey</c> refuses it); a key with a non-finite time or value is dropped.
        /// <para>A NaN tangent is a key KSP1 added with <c>AddKey(time, value)</c>. It gets an automatic smooth
        /// tangent, as Unity's <c>AddKey(time, value)</c> gives it (checked against a real AnimationCurve in
        /// <c>FloatCurveUnityTests</c>): for an inner key i, the mean of its two segment slopes,
        /// ((v[i] − v[i−1]) / (t[i] − t[i−1]) + (v[i+1] − v[i]) / (t[i+1] − t[i])) / 2; for the first and last key,
        /// the slope of its only segment; 0 for a single key.</para></summary>
        public FloatCurve(float[] keys)
        {
            if (keys == null || keys.Length < 4) keys = new[] { 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f };
            var n = keys.Length / 4;
            var t = new float[n];
            var v = new float[n];
            var ti = new float[n];
            var to = new float[n];
            var count = 0;
            for (var k = 0; k < n; k++)
            {
                float kt = keys[4 * k], kv = keys[4 * k + 1];
                if (!Finite(kt) || !Finite(kv)) continue;
                var at = count;
                var duplicate = false;
                for (var j = 0; j < count; j++)
                {
                    if (t[j] == kt) { duplicate = true; break; }
                    if (t[j] > kt) { at = j; break; }
                }
                if (duplicate) continue;
                for (var j = count; j > at; j--)
                {
                    t[j] = t[j - 1]; v[j] = v[j - 1]; ti[j] = ti[j - 1]; to[j] = to[j - 1];
                }
                t[at] = kt; v[at] = kv; ti[at] = keys[4 * k + 2]; to[at] = keys[4 * k + 3];
                count++;
            }

            _time = new float[count];
            _value = new float[count];
            _in = new float[count];
            _out = new float[count];
            for (var i = 0; i < count; i++)
            {
                _time[i] = t[i];
                _value[i] = v[i];
                var auto = AutoSlope(t, v, count, i);
                _in[i] = float.IsNaN(ti[i]) ? auto : ti[i];
                _out[i] = float.IsNaN(to[i]) ? auto : to[i];
            }
        }

        public int KeyCount => _time.Length;

        /// <summary>Allocation-free. A curve whose keys were all dropped evaluates to 0, as an empty Unity curve.
        /// NaN evaluates to the first key's value.</summary>
        public float Evaluate(float t)
        {
            var n = _time.Length;
            if (n == 0) return 0f;
            if (!(t > _time[0])) return _value[0];
            if (t >= _time[n - 1]) return _value[n - 1];
            var i = 1;
            while (_time[i] < t) i++;
            float t0 = _time[i - 1], t1 = _time[i];
            var dt = t1 - t0;
            if (float.IsInfinity(_out[i - 1]) || float.IsInfinity(_in[i])) return _value[i - 1];
            var s = (t - t0) / dt;
            var s2 = s * s;
            var s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * _value[i - 1]
                 + (s3 - 2f * s2 + s) * _out[i - 1] * dt
                 + (-2f * s3 + 3f * s2) * _value[i]
                 + (s3 - s2) * _in[i] * dt;
        }

        /// <summary>Evaluates a looping clip: t is wrapped into [0, length) first. A length that is not positive and
        /// finite evaluates like <see cref="Evaluate"/>. Allocation-free.</summary>
        public float EvaluateLooped(float t, float length)
        {
            if (!(length > 0f) || float.IsInfinity(length) || float.IsNaN(t) || float.IsInfinity(t)) return Evaluate(t);
            return Evaluate(t - (float)System.Math.Floor(t / length) * length);
        }

        private static float AutoSlope(float[] t, float[] v, int count, int i)
        {
            if (count < 2) return 0f;
            if (i == 0) return (v[1] - v[0]) / (t[1] - t[0]);
            if (i == count - 1) return (v[i] - v[i - 1]) / (t[i] - t[i - 1]);
            return ((v[i] - v[i - 1]) / (t[i] - t[i - 1]) + (v[i + 1] - v[i]) / (t[i + 1] - t[i])) * 0.5f;
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
