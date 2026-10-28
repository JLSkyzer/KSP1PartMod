using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The entry kinds the part module can drive. The planner may plan kinds whose runtime comes later (phase 2b
    /// plans the plugin modules before their binders exist): such an entry is left unbound, its clip at rest, and logged
    /// once per kind per load (<see cref="OncePerLoad"/>).</summary>
    public static class RuntimeKinds
    {
        /// <summary>True for the kinds of 0.4.0 (up to <see cref="EntryKind.ChuteFollower"/>); each phase 2b binder adds
        /// its kind here.</summary>
        public static bool HasBinder(EntryKind kind) => kind >= EntryKind.Toggle && kind <= EntryKind.ChuteFollower;
    }

    /// <summary>True the first time a kind is seen in a load generation (<c>TrackCache.Generation</c>): a new load
    /// forgets the kinds of the previous one. Main thread only.</summary>
    public sealed class OncePerLoad
    {
        private readonly HashSet<EntryKind> _seen = new HashSet<EntryKind>();
        private int _generation = int.MinValue;

        public bool First(EntryKind kind, int generation)
        {
            if (generation != _generation)
            {
                _seen.Clear();
                _generation = generation;
            }
            return _seen.Add(kind);
        }
    }
}
