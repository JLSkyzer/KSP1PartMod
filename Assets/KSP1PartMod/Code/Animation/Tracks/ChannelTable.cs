using System.Collections.Generic;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Up to four floats: a colour (r, g, b, a), a vector, or a scalar in <see cref="X"/>.</summary>
    public readonly struct Value4
    {
        public readonly float X, Y, Z, W;

        public Value4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    public enum ValueType { Color, Vector, Float, LightIntensity, LightColor, LightRange, LightSpotAngle, LightEnabled }

    /// <summary>Precedence class of a writer: a colour entry beats a clip track.</summary>
    public enum WriterClass { Track = 0, Color = 1 }

    public enum TargetKind { Renderer, Light }

    /// <summary>The values every writer of one part instance submits, per channel. This first part (Task 4) holds the
    /// registration, the rest values and the submitted values; precedence and the flush come with Task 5.
    /// <see cref="Submit"/> allocates nothing.</summary>
    public sealed class ChannelTable
    {
        private struct Target
        {
            public TargetKind Kind;
            public bool RewriteEveryFrame, Excluded;
        }

        private struct Channel
        {
            public int Target, Property, ModuleIndex;
            public ValueType Type;
            public WriterClass Class;
            public Value4 Rest, Value;
            public bool Submitted;
        }

        private readonly List<Target> _targets = new List<Target>();
        private Channel[] _channels = new Channel[8];
        private int _count;

        /// <summary>Calls of <see cref="Submit"/> since the last <see cref="ResetCounts"/>.</summary>
        public int SubmitCount { get; private set; }

        public int AddTarget(TargetKind kind, bool rewriteEveryFrame, bool excluded)
        {
            _targets.Add(new Target { Kind = kind, RewriteEveryFrame = rewriteEveryFrame, Excluded = excluded });
            return _targets.Count - 1;
        }

        /// <summary>A channel of <paramref name="target"/>; its value is <paramref name="rest"/> until submitted.</summary>
        public int Register(int target, int property, ValueType type, WriterClass cls, int moduleIndex, in Value4 rest)
        {
            if (target < 0 || target >= _targets.Count) throw new System.ArgumentOutOfRangeException(nameof(target));
            if (_count == _channels.Length) System.Array.Resize(ref _channels, _count * 2);
            _channels[_count] = new Channel
            {
                Target = target, Property = property, Type = type, Class = cls, ModuleIndex = moduleIndex, Rest = rest, Value = rest,
            };
            return _count++;
        }

        /// <summary>The value read at bind time.</summary>
        public Value4 Rest(int channel) => _channels[channel].Rest;

        public void Submit(int channel, in Value4 v)
        {
            ref var c = ref _channels[channel];
            c.Value = v;
            c.Submitted = true;
            SubmitCount++;
        }

        public void ResetCounts() => SubmitCount = 0;

        /// <summary>The last value submitted on <paramref name="channel"/>, its rest value if none.</summary>
        internal Value4 Submitted(int channel) => _channels[channel].Value;
    }
}
