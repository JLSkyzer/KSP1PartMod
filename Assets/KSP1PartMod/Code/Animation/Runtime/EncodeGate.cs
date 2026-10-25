namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>When to re-encode the saved state string. A player action encodes at once; changes made by the
    /// machines themselves (a clip moving, a throttle following) are folded into at most one encoding per interval,
    /// so a running engine does not build a string every frame. Machines keep <c>Changed</c> until saved, so the
    /// last change is never lost, only delayed by up to the interval.</summary>
    public sealed class EncodeGate
    {
        private readonly float _interval;
        private float _last = float.NegativeInfinity;

        public EncodeGate(float intervalSeconds) => _interval = intervalSeconds;

        /// <param name="changed">Some machine reports <c>Changed</c>.</param>
        /// <param name="urgent">A player action changed something this frame.</param>
        /// <param name="now">Seconds, any monotonic clock; a clock that goes back (new scene) encodes.</param>
        public bool ShouldEncode(bool changed, bool urgent, float now)
        {
            if (!urgent && !changed) return false;
            if (!urgent && now >= _last && now - _last < _interval) return false;
            _last = now;
            return true;
        }
    }
}
