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

        /// <summary>Component-wise ==: a NaN component always counts as a change.</summary>
        public bool SameAs(in Value4 o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
    }

    /// <summary>What a channel holds: a material property (<see cref="Color"/>, <see cref="Vector"/>,
    /// <see cref="Float"/>) or a field of a Unity <c>Light</c>. Not named <c>ValueType</c>, which clashes with
    /// <see cref="System.ValueType"/>.</summary>
    public enum ChannelValueType { Color, Vector, Float, LightIntensity, LightColor, LightRange, LightSpotAngle, LightEnabled }

    /// <summary>Receives the flush of a <see cref="ChannelTable"/>: one <see cref="Begin"/> / <see cref="End"/> pair per
    /// written target, with the properties to write in between. The glue implements it with one
    /// <c>GetPropertyBlock</c> / <c>SetPropertyBlock</c> pair per renderer, and plain field writes for a light.</summary>
    public interface IChannelSink
    {
        void Begin(int target, TargetKind kind);
        void Write(int property, ChannelValueType type, in Value4 v);
        void End(int target, TargetKind kind);
    }

    /// <summary>Precedence class of a writer: a colour entry beats a clip track.</summary>
    public enum WriterClass { Track = 0, Color = 1 }

    public enum TargetKind { Renderer, Light }

    /// <summary>The values every writer of one part instance submits, per channel, and the one write per target and
    /// frame that results (spec 2a.5).
    /// <list type="bullet">
    /// <item>A slot is one (target, property). Its shown value is that of its highest (class, module index) writer
    /// that has submitted at least once, the later registration on a tie. The choice is latched: a colour entry
    /// that went idle still beats a track, as KSP1's property block beats the animated material.</item>
    /// <item>A channel keeps its last submitted value until the next <see cref="Submit"/>: an unchanged pose submits
    /// nothing, and the shown value stays. Nothing is ever reset to the rest value by the table.</item>
    /// <item><see cref="Flush"/> writes a target when one of its slots shows a new value (only those slots), or every
    /// flush, all its shown slots, when the target was added with <c>rewriteEveryFrame</c> (a renderer whose block
    /// another writer replaces). An excluded target never reaches the sink. A slot nobody submitted is never
    /// written.</item>
    /// </list>
    /// Arrays grow at registration only; <see cref="Submit"/> and <see cref="Flush"/> allocate nothing.</summary>
    public sealed class ChannelTable
    {
        private struct Target
        {
            public TargetKind Kind;
            public bool RewriteEveryFrame, Excluded, Dirty;
            /// <summary>Rewrite all shown slots at the next flush (rewriteEveryFrame was just turned off).</summary>
            public bool RewriteOnce;
            /// <summary>First slot of the target, then <see cref="Slot.NextInTarget"/>; -1 ends the list.</summary>
            public int FirstSlot, LastSlot;
        }

        private struct Slot
        {
            public int Target, Property, NextInTarget;
            public ChannelValueType Type;
            /// <summary>The channel shown, -1 while nobody has submitted.</summary>
            public int Winner;
            /// <summary>The value last given to the sink (or pending for it when <see cref="Dirty"/>).</summary>
            public Value4 Shown;
            public bool Written, Dirty;
        }

        private struct Channel
        {
            public int Slot, ModuleIndex;
            public WriterClass Class;
            public Value4 Rest, Value;
            public bool Submitted;
        }

        private Target[] _targets = new Target[4];
        private Slot[] _slots = new Slot[8];
        private Channel[] _channels = new Channel[8];
        private int _targetCount, _slotCount, _count;

        /// <summary>Calls of <see cref="Submit"/> since the last <see cref="ResetCounts"/>.</summary>
        public int SubmitCount { get; private set; }

        /// <summary><see cref="IChannelSink.Write"/> calls of the last <see cref="Flush"/>.</summary>
        public int WritesLastFlush { get; private set; }

        public int AddTarget(TargetKind kind, bool rewriteEveryFrame, bool excluded)
        {
            if (_targetCount == _targets.Length) System.Array.Resize(ref _targets, _targetCount * 2);
            _targets[_targetCount] = new Target
            {
                Kind = kind, RewriteEveryFrame = rewriteEveryFrame, Excluded = excluded, FirstSlot = -1, LastSlot = -1,
            };
            return _targetCount++;
        }

        /// <summary>A channel of <paramref name="target"/>'s <paramref name="property"/> (a shader property id, or a
        /// light field); its value is <paramref name="rest"/> until submitted. Channels of the same (target, property)
        /// share one slot and must give its type.</summary>
        /// <exception cref="System.ArgumentException">The slot exists with another <paramref name="type"/>.</exception>
        public int Register(int target, int property, ChannelValueType type, WriterClass cls, int moduleIndex, in Value4 rest)
        {
            if (target < 0 || target >= _targetCount) throw new System.ArgumentOutOfRangeException(nameof(target));
            var slot = FindSlot(target, property);
            if (slot < 0) slot = AddSlot(target, property, type);
            else if (_slots[slot].Type != type)
                throw new System.ArgumentException(
                    $"property {property} of target {target} is registered as {_slots[slot].Type}, not {type}", nameof(type));
            if (_count == _channels.Length) System.Array.Resize(ref _channels, _count * 2);
            _channels[_count] = new Channel { Slot = slot, Class = cls, ModuleIndex = moduleIndex, Rest = rest, Value = rest };
            return _count++;
        }

        /// <summary>The value read at bind time.</summary>
        public Value4 Rest(int channel) => _channels[channel].Rest;

        public void Submit(int channel, in Value4 v)
        {
            SubmitCount++;
            ref var c = ref _channels[channel];
            c.Value = v;
            c.Submitted = true;
            ref var s = ref _slots[c.Slot];
            if (s.Winner != channel)
            {
                if (s.Winner >= 0 && Outranks(s.Winner, channel)) return;
                s.Winner = channel;
            }
            if (s.Written && !s.Dirty && s.Shown.SameAs(v)) return;
            s.Shown = v;
            s.Dirty = true;
            _targets[s.Target].Dirty = true;
        }

        public void Flush(IChannelSink sink) => Flush(sink, false);

        /// <summary><paramref name="rewriteAll"/>: every target not excluded writes all its shown slots, dirty or not
        /// (the render-time fallback, when another writer replaced our block after our flush).</summary>
        public void Flush(IChannelSink sink, bool rewriteAll)
        {
            var writes = 0;
            for (var t = 0; t < _targetCount; t++)
            {
                ref var target = ref _targets[t];
                var every = rewriteAll || target.RewriteEveryFrame || target.RewriteOnce;
                target.RewriteOnce = false;
                if (target.Excluded || (!target.Dirty && !every))
                {
                    target.Dirty = false;
                    continue;
                }
                target.Dirty = false;
                var begun = false;
                for (var i = target.FirstSlot; i >= 0; i = _slots[i].NextInTarget)
                {
                    ref var s = ref _slots[i];
                    if (s.Winner < 0 || !(s.Dirty || every)) continue;
                    if (!begun)
                    {
                        sink.Begin(t, target.Kind);
                        begun = true;
                    }
                    sink.Write(s.Property, s.Type, s.Shown);
                    s.Dirty = false;
                    s.Written = true;
                    writes++;
                }
                if (begun) sink.End(t, target.Kind);
            }
            WritesLastFlush = writes;
        }

        /// <summary>Turns <c>rewriteEveryFrame</c> on or off after <see cref="AddTarget"/> (a renderer whose block another
        /// writer replaces only while that writer runs, such as a <c>ReentryMesh</c> source while its hull is active).
        /// Turning it off makes the next <see cref="Flush"/> rewrite the target once more: the other writer's last block
        /// stays on the renderer.</summary>
        public void SetRewriteEveryFrame(int target, bool on)
        {
            if (target < 0 || target >= _targetCount) throw new System.ArgumentOutOfRangeException(nameof(target));
            ref var t = ref _targets[target];
            if (t.RewriteEveryFrame == on) return;
            t.RewriteEveryFrame = on;
            if (!on) t.RewriteOnce = true;
        }

        public void ResetCounts() => SubmitCount = 0;

        /// <summary>The last value submitted on <paramref name="channel"/>, its rest value if none.</summary>
        internal Value4 Submitted(int channel) => _channels[channel].Value;

        /// <summary>Whether <paramref name="a"/> keeps the slot against <paramref name="b"/>: higher class, then
        /// higher module index, then the later registration.</summary>
        private bool Outranks(int a, int b)
        {
            ref var ca = ref _channels[a];
            ref var cb = ref _channels[b];
            if (ca.Class != cb.Class) return ca.Class > cb.Class;
            if (ca.ModuleIndex != cb.ModuleIndex) return ca.ModuleIndex > cb.ModuleIndex;
            return a > b;
        }

        private int FindSlot(int target, int property)
        {
            for (var i = _targets[target].FirstSlot; i >= 0; i = _slots[i].NextInTarget)
                if (_slots[i].Property == property) return i;
            return -1;
        }

        private int AddSlot(int target, int property, ChannelValueType type)
        {
            if (_slotCount == _slots.Length) System.Array.Resize(ref _slots, _slotCount * 2);
            var slot = _slotCount++;
            _slots[slot] = new Slot { Target = target, Property = property, Type = type, NextInTarget = -1, Winner = -1 };
            ref var t = ref _targets[target];
            if (t.LastSlot < 0) t.FirstSlot = slot;
            else _slots[t.LastSlot].NextInTarget = slot;
            t.LastSlot = slot;
            return slot;
        }
    }
}
