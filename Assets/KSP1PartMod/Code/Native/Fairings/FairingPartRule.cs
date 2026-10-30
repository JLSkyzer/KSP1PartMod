using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Fairings
{
    /// <summary>Why a KSP1 fairing module got no <c>Module_Fairing</c>.</summary>
    public enum FairingRefusal
    {
        /// <summary>A second fairing module on one part (KSP2 has one fairing per part), or a <c>Data_Fairing</c> already there.</summary>
        Second,
        /// <summary>The mapping found no usable size.</summary>
        Mapping,
        /// <summary>The conversion threw; what it added was removed.</summary>
        Failed,
    }

    /// <summary>The runtime guards on <c>Module_Fairing</c> act only on the KSP1 parts this plan converted, never on KSP2
    /// parts nor on the 0.7.0 shroud parts. Pure.</summary>
    public static class FairingPartRule
    {
        public static bool TakeOver(bool ksp1, bool converted) => ksp1 && converted;

        public static string Label(FairingRefusal r)
        {
            switch (r)
            {
                case FairingRefusal.Second: return "second";
                case FairingRefusal.Mapping: return "mapping";
                default: return "failed";
            }
        }
    }

    /// <summary>The fairing bases converted this session, by part name (read by the runtime guards and the SAF prefab pass),
    /// with the refusals and the "not reproduced" fields of the summary lines. Thread-safe.</summary>
    public sealed class FairingRecord
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, FairingKind> _parts = new Dictionary<string, FairingKind>(StringComparer.Ordinal);
        private readonly Dictionary<(FairingKind, FairingRefusal), int> _refused = new Dictionary<(FairingKind, FairingRefusal), int>();
        private readonly Dictionary<FairingKind, SortedDictionary<string, int>> _notes = new Dictionary<FairingKind, SortedDictionary<string, int>>();

        /// <summary>Records <paramref name="part"/>; false when it was already recorded (a rebuild or a second import).</summary>
        public bool Add(string part, FairingKind kind, IEnumerable<string> notReproduced = null)
        {
            if (part == null) return false;
            lock (_lock)
            {
                if (_parts.ContainsKey(part)) return false;
                _parts[part] = kind;
                if (!_notes.TryGetValue(kind, out var notes)) _notes[kind] = notes = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var f in notReproduced ?? Enumerable.Empty<string>()) notes[f] = notes.TryGetValue(f, out var n) ? n + 1 : 1;
                return true;
            }
        }

        public bool Contains(string part)
        {
            if (part == null) return false;
            lock (_lock) return _parts.ContainsKey(part);
        }

        public FairingKind? KindOf(string part)
        {
            if (part == null) return null;
            lock (_lock) return _parts.TryGetValue(part, out var k) ? k : (FairingKind?)null;
        }

        public int Count(FairingKind kind)
        {
            lock (_lock) return _parts.Values.Count(k => k == kind);
        }

        public void Refuse(FairingKind kind, FairingRefusal reason)
        {
            lock (_lock) _refused[(kind, reason)] = _refused.TryGetValue((kind, reason), out var n) ? n + 1 : 1;
        }

        /// <summary>The non-zero refusals of <paramref name="kind"/>, in enum order.</summary>
        public List<(string Reason, int Count)> Refusals(FairingKind kind)
        {
            lock (_lock)
                return Enum.GetValues(typeof(FairingRefusal)).Cast<FairingRefusal>()
                    .Where(r => _refused.TryGetValue((kind, r), out var n) && n > 0)
                    .Select(r => (FairingPartRule.Label(r), _refused[(kind, r)])).ToList();
        }

        /// <summary>The "not reproduced" fields of <paramref name="kind"/>, most frequent first.</summary>
        public List<(string Field, int Count)> NotReproduced(FairingKind kind)
        {
            lock (_lock)
                return _notes.TryGetValue(kind, out var d)
                    ? d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList()
                    : new List<(string, int)>();
        }

        public void Clear()
        {
            lock (_lock)
            {
                _parts.Clear();
                _refused.Clear();
                _notes.Clear();
            }
        }
    }
}
