using KSP1PartMod.Animation.Behaviours;
using Curve = KSP1PartMod.Animation.Behaviours.FloatCurve;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Turns the pose of a clip's owner into the values of the clip's tracks and submits them to a
    /// <see cref="ChannelTable"/>. Samples only when the pose changed; allocation-free after construction.</summary>
    public sealed class TrackSampler
    {
        /// <summary>Unity's <c>WrapMode.Loop</c>.</summary>
        private const int WrapLoop = 2;

        private readonly ChannelTable _table;
        private readonly int[] _channelOfTrack;
        private readonly Curve[][] _curves;
        private readonly float _length;

        /// <summary>Time past the clip's end repeats rather than clamps. Starts true for a clip whose own wrap mode is
        /// Loop; the glue sets it for a state forced to loop (a Group's active clip).</summary>
        public bool Loop;

        private bool _sampled;
        private float _lastTime, _lastWeight;
        private bool _lastEnabled, _lastLoop;

        /// <param name="channelOfTrack">Per track of <paramref name="set"/>, its channel in <paramref name="table"/>, or
        /// -1 when the track found no target (skipped).</param>
        public TrackSampler(TrackSet set, ChannelTable table, int[] channelOfTrack)
        {
            _table = table;
            var tracks = set.Tracks;
            var n = tracks?.Count ?? 0;
            _curves = new Curve[n][];
            _channelOfTrack = new int[n];
            for (var i = 0; i < n; i++)
            {
                _curves[i] = tracks[i].Channels ?? new Curve[0];
                _channelOfTrack[i] = channelOfTrack != null && i < channelOfTrack.Length ? channelOfTrack[i] : -1;
            }
            _length = set.Length > 0f && !float.IsInfinity(set.Length) ? set.Length : 0f;
            Loop = set.WrapMode == WrapLoop;
        }

        /// <summary>Submits one value per bound track: per channel <c>lerp(rest, curve(t), Weight)</c> with
        /// <c>t = NormalizedTime × length</c>, clamped to the clip or repeated (<see cref="Loop"/>); a channel without
        /// a curve keeps its rest value. False, and nothing submitted, when the pose (time, weight, enabled) and
        /// <see cref="Loop"/> are those of the last call, or the pose is disabled (a stopped state leaves its last
        /// values).</summary>
        public bool Sample(in ClipPose pose)
        {
            if (_sampled && pose.Enabled == _lastEnabled && pose.NormalizedTime == _lastTime
                && pose.Weight == _lastWeight && Loop == _lastLoop)
                return false;
            _sampled = true;
            _lastEnabled = pose.Enabled;
            _lastTime = pose.NormalizedTime;
            _lastWeight = pose.Weight;
            _lastLoop = Loop;
            if (!pose.Enabled) return false;

            var t = pose.NormalizedTime * _length;
            var loop = Loop && _length > 0f;
            if (!loop) t = t > _length ? _length : t > 0f ? t : 0f; // NaN clamps to 0
            var w = pose.Weight;
            for (var i = 0; i < _curves.Length; i++)
            {
                var channel = _channelOfTrack[i];
                if (channel < 0) continue;
                var rest = _table.Rest(channel);
                var c = _curves[i];
                var v = new Value4(
                    Blend(rest.X, c, 0, t, w, loop),
                    Blend(rest.Y, c, 1, t, w, loop),
                    Blend(rest.Z, c, 2, t, w, loop),
                    Blend(rest.W, c, 3, t, w, loop));
                _table.Submit(channel, in v);
            }
            return true;
        }

        /// <summary>Samples each of <paramref name="samplers"/> (null items and a null array skipped) from one pose.</summary>
        public static void SampleAll(TrackSampler[] samplers, in ClipPose pose)
        {
            if (samplers == null) return;
            for (var i = 0; i < samplers.Length; i++) samplers[i]?.Sample(in pose);
        }

        private float Blend(float rest, Curve[] curves, int channel, float t, float weight, bool loop)
        {
            if (channel >= curves.Length) return rest;
            var curve = curves[channel];
            if (curve == null) return rest;
            var value = loop ? curve.EvaluateLooped(t, _length) : curve.Evaluate(t);
            return rest + (value - rest) * weight;
        }
    }
}
