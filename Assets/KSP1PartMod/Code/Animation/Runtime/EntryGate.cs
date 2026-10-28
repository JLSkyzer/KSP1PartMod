using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The B9 gates of one entry on a part instance (spec 2b.1): each gate follows one watched variant set, or
    /// none (<c>-1</c>: Redux made no set of the switch, or the part has no such set), in which case it keeps its default
    /// subtype for good. An entry is driven only while <see cref="Active"/>; an inactive one holds its rest pose, hides
    /// its PAM button and ignores its action group (glue). No allocation after construction.</summary>
    public sealed class EntryGate
    {
        private readonly B9Gate[] _gates;
        private readonly int[] _setOf;
        private readonly GateState _state;

        /// <param name="gates">The entry's <see cref="AnimEntryDef.Gates"/>.</param>
        /// <param name="setOfGate">Per gate, the index of its watched set, or -1. Missing items count as -1.</param>
        public EntryGate(B9Gate[] gates, int[] setOfGate)
        {
            _gates = gates ?? new B9Gate[0];
            _setOf = new int[_gates.Length];
            for (var i = 0; i < _gates.Length; i++)
                _setOf[i] = setOfGate != null && i < setOfGate.Length ? setOfGate[i] : -1;
            _state = new GateState(_gates);
        }

        /// <summary>Every gate lets the entry run.</summary>
        public bool Active => _state.Active;

        public int Count => _gates.Length;

        public B9Gate Gate(int i) => _gates[i];

        /// <summary>The watched set of gate <paramref name="i"/>, -1 for none.</summary>
        public int SetOf(int i) => _setOf[i];

        /// <summary>True when a gate follows watched set <paramref name="set"/>.</summary>
        public bool Follows(int set)
        {
            if (set < 0) return false;
            for (var i = 0; i < _setOf.Length; i++)
                if (_setOf[i] == set) return true;
            return false;
        }

        /// <summary>Watched set <paramref name="set"/> now shows <paramref name="variantIdOrNull"/> (null: no selection,
        /// the default subtype). True when <see cref="Active"/> changed. A gate without set never moves.</summary>
        public bool OnVariant(int set, string variantIdOrNull)
        {
            if (set < 0) return false;
            var was = _state.Active;
            for (var i = 0; i < _setOf.Length; i++)
                if (_setOf[i] == set) _state.Set(i, variantIdOrNull);
            return was != _state.Active;
        }
    }
}
