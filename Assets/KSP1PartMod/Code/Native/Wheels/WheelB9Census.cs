using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>The B9 subtype edits of KSP1 wheel modules seen during the import (wheels plan (d), (g)): one edit per part,
    /// subtype and module, and the edited keys that KSP2 holds as <b>state</b> (<see cref="WheelMapping.IsStateField"/>), which
    /// the 0.9.0 module-data route does not carry (it diffs <c>[KSPDefinition]</c> fields only). A rebuilt part counts once.
    /// Thread-safe. Pure.</summary>
    public sealed class WheelB9Census
    {
        private readonly object _lock = new object();
        private readonly HashSet<string> _edits = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<(string Part, string Field)> _state = new List<(string, string)>();

        /// <summary>One subtype edit of <paramref name="ksp1Module"/> on <paramref name="part"/>; ignored when the module is not
        /// a wheel module.</summary>
        public void Add(string part, string subtype, string ksp1Module, IEnumerable<string> dataKeys)
        {
            if (!WheelMapping.TryKind(ksp1Module, out var kind)) return;
            part = part ?? "?";
            lock (_lock)
            {
                if (!_edits.Add(part + "\n" + (subtype ?? "") + "\n" + kind)) return;
                foreach (var key in dataKeys ?? Enumerable.Empty<string>())
                {
                    var field = key?.Trim();
                    if (!WheelMapping.IsStateField(kind, field)) continue;
                    if (!_state.Contains((part, field))) _state.Add((part, field));
                }
            }
        }

        public int Edits
        {
            get { lock (_lock) return _edits.Count; }
        }

        /// <summary>The state fields edited, by part, in the order first seen.</summary>
        public IReadOnlyCollection<(string Part, string Field)> StateEdits
        {
            get { lock (_lock) return _state.ToArray(); }
        }

        public string Line() => WheelLines.B9(Edits, StateEdits);

        public void Clear()
        {
            lock (_lock)
            {
                _edits.Clear();
                _state.Clear();
            }
        }
    }
}
