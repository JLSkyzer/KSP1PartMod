using System;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The runtime state of an entry's B9 gates (spec 2b.1): the entry is active while no gate's current
    /// variant is one of its <see cref="B9Gate.InactiveIn"/>. Each gate starts at its default subtype. Ordinal
    /// comparisons, no allocation after construction.</summary>
    public sealed class GateState
    {
        private readonly B9Gate[] _gates;
        private readonly bool[] _active;
        private readonly string[] _current;
        private int _inactiveCount;

        public GateState(B9Gate[] gates)
        {
            _gates = gates ?? new B9Gate[0];
            _active = new bool[_gates.Length];
            _current = new string[_gates.Length];
            for (var i = 0; i < _gates.Length; i++)
            {
                _current[i] = _gates[i]?.DefaultSubtype;
                _active[i] = IsActive(_gates[i], _current[i]);
                if (!_active[i]) _inactiveCount++;
            }
        }

        /// <summary>True when every gate lets the entry run.</summary>
        public bool Active => _inactiveCount == 0;

        /// <summary>Number of gates.</summary>
        public int Count => _gates.Length;

        /// <summary>Gate <paramref name="gate"/> now sees <paramref name="variantIdOrNull"/> (null: its default subtype).
        /// Returns true when <see cref="Active"/> changed. An index out of range is ignored.</summary>
        public bool Set(int gate, string variantIdOrNull)
        {
            if (gate < 0 || gate >= _gates.Length) return false;
            var id = variantIdOrNull ?? _gates[gate]?.DefaultSubtype;
            if (string.Equals(_current[gate], id, StringComparison.Ordinal)) return false;
            _current[gate] = id;
            var now = IsActive(_gates[gate], id);
            if (now == _active[gate]) return false;
            var was = Active;
            _active[gate] = now;
            _inactiveCount += now ? -1 : 1;
            return was != Active;
        }

        private static bool IsActive(B9Gate gate, string id)
        {
            var off = gate?.InactiveIn;
            if (off == null || id == null) return true;
            for (var i = 0; i < off.Length; i++)
                if (string.Equals(off[i], id, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
