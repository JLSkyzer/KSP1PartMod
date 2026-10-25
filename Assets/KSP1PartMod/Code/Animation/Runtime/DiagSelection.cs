using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>Which parts the one-shot material diagnostic dumps: the first time each named part is seen, plus the
    /// first <see cref="Others"/> other parts. Each part name is dumped at most once per session.</summary>
    public sealed class DiagSelection
    {
        public static readonly string[] DefaultNames = { "mk1pod_v2", "Mk2Pod", "liquidEngine" };
        public const int DefaultOthers = 5;

        private readonly HashSet<string> _named;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private int _othersLeft;

        public DiagSelection(IEnumerable<string> names = null, int others = DefaultOthers)
        {
            _named = new HashSet<string>(names ?? DefaultNames, StringComparer.Ordinal);
            _othersLeft = Math.Max(0, others);
        }

        public int Others => _othersLeft;

        /// <summary>True when every named part has been dumped and no other slot is left.</summary>
        public bool Done => _othersLeft == 0 && _named.IsSubsetOf(_seen);

        /// <summary>True the first time a part should be dumped; records it.</summary>
        public bool Take(string partName)
        {
            if (string.IsNullOrEmpty(partName) || _seen.Contains(partName)) return false;
            if (_named.Contains(partName)) { _seen.Add(partName); return true; }
            if (_othersLeft == 0) return false;
            _othersLeft--;
            _seen.Add(partName);
            return true;
        }
    }
}
