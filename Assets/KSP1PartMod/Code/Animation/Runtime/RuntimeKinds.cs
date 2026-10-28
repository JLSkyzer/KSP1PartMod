using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The entry kinds the part module can drive. The planner may plan kinds whose runtime comes later (phase 2b
    /// plans the plugin modules before their binders exist): such an entry is left unbound, its clip at rest, and logged
    /// once per kind per load (<see cref="OncePerLoad"/>).</summary>
    public static class RuntimeKinds
    {
        /// <summary>True for the kinds of 0.4.0 (up to <see cref="EntryKind.ChuteFollower"/>) and the phase 2b kinds whose
        /// binder exists; each phase 2b binder adds its kind here.</summary>
        public static bool HasBinder(EntryKind kind)
        {
            if (kind >= EntryKind.Toggle && kind <= EntryKind.ChuteFollower) return true;
            switch (kind)
            {
                case EntryKind.EngineDeploy:
                case EntryKind.EngineMode:
                case EntryKind.DecouplerClip:
                case EntryKind.ScienceToggle:
                case EntryKind.AccelSpring:
                case EntryKind.LandingBag:
                case EntryKind.RcsGlow:
                case EntryKind.RcsEmissive:
                case EntryKind.MeshHider:
                case EntryKind.LookAt:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>True the first time a kind (or a string key) is seen in a load generation (<c>TrackCache.Generation</c>):
    /// a new load forgets the kinds and keys of the previous one. Main thread only.</summary>
    public sealed class OncePerLoad
    {
        private readonly HashSet<EntryKind> _seen = new HashSet<EntryKind>();
        private readonly HashSet<string> _keys = new HashSet<string>(System.StringComparer.Ordinal);
        private int _generation = int.MinValue;

        public bool First(EntryKind kind, int generation)
        {
            NewLoad(generation);
            return _seen.Add(kind);
        }

        /// <summary>For lines logged once per part type: the key names the part, the entry and the scene.</summary>
        public bool First(string key, int generation)
        {
            NewLoad(generation);
            return _keys.Add(key ?? "");
        }

        private void NewLoad(int generation)
        {
            if (generation == _generation) return;
            _seen.Clear();
            _keys.Clear();
            _generation = generation;
        }
    }
}
