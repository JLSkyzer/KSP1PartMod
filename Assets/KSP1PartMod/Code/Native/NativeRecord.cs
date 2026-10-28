using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>What the native converters did during the session's import, by part name: the native modules each part
    /// got (read by the audit), the notes already logged (each once per part), and per-part flags for the summaries.
    /// Rebuilding a part never counts it twice. Pure.</summary>
    public sealed class NativeRecord
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, List<string>> _converted = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _notes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _flags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary><paramref name="part"/> got <paramref name="nativeModule"/>; true the first time for that pair.</summary>
        public bool AddConversion(string part, string nativeModule)
        {
            part = part ?? "?";
            lock (_lock)
            {
                if (!_converted.TryGetValue(part, out var modules)) _converted[part] = modules = new List<string>();
                if (modules.Contains(nativeModule)) return false;
                modules.Add(nativeModule);
                return true;
            }
        }

        /// <summary>True the first time <paramref name="note"/> is seen for <paramref name="part"/> (log it then).</summary>
        public bool AddNote(string part, string note)
        {
            lock (_lock) return _notes.Add((part ?? "?") + "\n" + note);
        }

        /// <summary>Marks <paramref name="part"/> with <paramref name="flag"/> (a set: a rebuild does not count twice).</summary>
        public void Flag(string part, string flag)
        {
            lock (_lock)
            {
                if (!_flags.TryGetValue(flag, out var parts)) _flags[flag] = parts = new HashSet<string>(StringComparer.Ordinal);
                parts.Add(part ?? "?");
            }
        }

        /// <summary>True when <paramref name="part"/> is marked with <paramref name="flag"/>.</summary>
        public bool HasFlag(string part, string flag)
        {
            lock (_lock) return _flags.TryGetValue(flag, out var parts) && parts.Contains(part ?? "?");
        }

        /// <summary>The number of parts marked with <paramref name="flag"/>.</summary>
        public int CountFlag(string flag)
        {
            lock (_lock) return _flags.TryGetValue(flag, out var parts) ? parts.Count : 0;
        }

        /// <summary>Every flag starting with <paramref name="prefix"/> (the prefix removed) and its number of parts, in
        /// ordinal order of the flag.</summary>
        public IReadOnlyList<(string Flag, int Parts)> FlagsWithPrefix(string prefix)
        {
            lock (_lock)
                return _flags.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => (kv.Key.Substring(prefix.Length), kv.Value.Count)).ToList();
        }

        /// <summary>The number of parts that got <paramref name="nativeModule"/>.</summary>
        public int PartsWith(string nativeModule)
        {
            lock (_lock) return _converted.Values.Count(m => m.Contains(nativeModule));
        }

        /// <summary>The native modules <paramref name="part"/> got, in order; empty when none.</summary>
        public IReadOnlyList<string> ConvertedModules(string part)
        {
            lock (_lock)
                return part != null && _converted.TryGetValue(part, out var modules) ? modules.ToArray() : Array.Empty<string>();
        }

        /// <summary>A copy of every part's converted modules.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot()
        {
            lock (_lock)
                return _converted.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToArray(), StringComparer.Ordinal);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _converted.Clear();
                _notes.Clear();
                _flags.Clear();
            }
        }
    }
}
