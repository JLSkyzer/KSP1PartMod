using System.Collections.Generic;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>What one machine wants a Unity legacy <c>AnimationState</c> to show this frame. The glue writes
    /// <see cref="NormalizedTime"/> with the state's own speed at 0: the machines advance time themselves.
    /// <see cref="Speed"/> is the KSP1 playback speed in clip seconds per second (±1 real time, sign = direction,
    /// 0 when holding), for logs and for code that needs the direction.</summary>
    public readonly struct ClipPose
    {
        public readonly float NormalizedTime;
        public readonly float Speed;
        public readonly float Weight;
        public readonly bool Enabled;

        public ClipPose(float normalizedTime, float speed, float weight, bool enabled)
        {
            NormalizedTime = normalizedTime;
            Speed = speed;
            Weight = weight;
            Enabled = enabled;
        }

        /// <summary>The state is left alone (no clip, or a clip KSP1 has stopped).</summary>
        public static readonly ClipPose Off = new ClipPose(0f, 0f, 0f, false);
    }

    /// <summary>A shader colour, linear components as the curves give them.</summary>
    public readonly struct Rgba
    {
        public readonly float R, G, B, A;

        public Rgba(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>The part every machine shares: its saved values, under <c>&lt;entry key&gt;.&lt;name&gt;</c>.</summary>
    public interface IEntryMachine
    {
        /// <summary>Missing or unreadable values keep the defaults.</summary>
        void Load(IReadOnlyDictionary<string, float> saved);

        /// <summary>Writes every saved value and clears <see cref="Changed"/>.</summary>
        void Save(IDictionary<string, float> into);

        /// <summary>A saved value differs from the last <see cref="Load"/> or <see cref="Save"/>: the caller re-encodes
        /// the state only then.</summary>
        bool Changed { get; }
    }

    internal static class Machine
    {
        /// <summary>Clips shorter than this (or with an unusable length) end in one tick.</summary>
        private const float MinLength = 1e-6f;

        public static float Length(float clipLength) =>
            clipLength > MinLength && !float.IsInfinity(clipLength) ? clipLength : MinLength;

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public static bool TryRead(IReadOnlyDictionary<string, float> saved, string key, out float value)
        {
            value = 0f;
            return saved != null && saved.TryGetValue(key, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool Bool(float value) => value > 0.5f;

        public static float Of(bool value) => value ? 1f : 0f;
    }
}
