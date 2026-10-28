using System;

namespace KSP1PartMod.Perf
{
    /// <summary>One slow part: its total time and the prefab and texture shares of it, in timestamp ticks.</summary>
    public struct SlowPart
    {
        public string Name;
        public long TotalTicks;
        public long PrefabTicks;
        public long TextureTicks;
    }

    /// <summary>The N slowest parts, largest first. <see cref="WouldEnter"/> lets the caller skip reading a part's name when
    /// the part is fast; neither it nor a refused <see cref="Offer"/> allocates. Ties keep the part seen first. Main thread
    /// only. Pure.</summary>
    public sealed class SlowestParts
    {
        private readonly SlowPart[] _parts;

        public SlowestParts(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _parts = new SlowPart[capacity];
        }

        public int Capacity => _parts.Length;
        public int Count { get; private set; }

        public SlowPart this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _parts[index];
            }
        }

        /// <summary>True when a part of <paramref name="totalTicks"/> would be kept: the list is not full, or it is strictly
        /// slower than the last kept (an equal time loses to the part seen first).</summary>
        public bool WouldEnter(long totalTicks) => Count < _parts.Length || totalTicks > _parts[Count - 1].TotalTicks;

        /// <summary>Keeps the part when it <see cref="WouldEnter"/>; the fastest kept part then drops out of a full list.</summary>
        public bool Offer(string name, long totalTicks, long prefabTicks, long textureTicks)
        {
            if (!WouldEnter(totalTicks)) return false;
            var at = 0;
            while (at < Count && _parts[at].TotalTicks >= totalTicks) at++;
            var last = Count < _parts.Length ? Count : _parts.Length - 1;
            for (var i = last; i > at; i--) _parts[i] = _parts[i - 1];
            _parts[at] = new SlowPart { Name = name ?? "?", TotalTicks = totalTicks, PrefabTicks = prefabTicks, TextureTicks = textureTicks };
            if (Count < _parts.Length) Count++;
            return true;
        }

        public void Clear()
        {
            Array.Clear(_parts, 0, _parts.Length);
            Count = 0;
        }

        /// <summary>A copy of the kept parts, largest first (for the report).</summary>
        public SlowPart[] ToArray()
        {
            var copy = new SlowPart[Count];
            Array.Copy(_parts, copy, Count);
            return copy;
        }
    }
}
