using System.Collections.Concurrent;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>What the worker found for one part.</summary>
    public sealed class PartTracks
    {
        private static readonly IReadOnlyList<TrackSet> NoSets = new TrackSet[0];
        private static readonly IReadOnlyList<string> NoMissing = new string[0];

        /// <summary>A model could not be read, or building threw: the part keeps its phase 1 behaviour.</summary>
        public bool Failed;
        /// <summary>Why it failed and which references were not found, joined by "; "; null when all went well.</summary>
        public string Note;
        /// <summary>The full text, stack included, of an exception the reader does not throw on purpose (anything but
        /// a <see cref="Mu.MuFormatException"/>); null otherwise. Logged, never shown in <see cref="Note"/>.</summary>
        public string Detail;
        public IReadOnlyList<TrackSet> Sets = NoSets;
        /// <summary>One <c>model '&lt;ref&gt;' not found (&lt;tried&gt;)</c> per reference no rule resolved.</summary>
        public IReadOnlyList<string> Missing = NoMissing;
        /// <summary>Curves of the part's named clips that no track takes.</summary>
        public TrackDrops Drops;
    }

    /// <summary>The tracks of every imported part for the current load. Written by the worker, read on the main
    /// thread. Each load starts a new generation; writes of an older one are ignored, so a worker still running
    /// from the previous load can never mix its parts into this one.</summary>
    public static class TrackCache
    {
        private static readonly object Gate = new object();
        private static readonly ConcurrentDictionary<string, PartTracks> Parts =
            new ConcurrentDictionary<string, PartTracks>(System.StringComparer.Ordinal);
        private static volatile int _generation;

        public static int Generation => _generation;

        /// <summary>True once the worker of the current generation has finished, whatever it found. Read every
        /// frame by pending parts: a single volatile read.</summary>
        public static volatile bool Ready;

        /// <summary>Clears the cache and starts a new generation, returned.</summary>
        public static int Begin()
        {
            lock (Gate)
            {
                Ready = false;
                Parts.Clear();
                return ++_generation;
            }
        }

        /// <summary>Ignored when <paramref name="generation"/> is not the current one.</summary>
        public static void Put(int generation, string part, PartTracks t)
        {
            if (part == null || t == null) return;
            lock (Gate)
                if (generation == _generation) Parts[part] = t;
        }

        /// <summary>Ignored when <paramref name="generation"/> is not the current one.</summary>
        public static void MarkReady(int generation)
        {
            lock (Gate)
                if (generation == _generation) Ready = true;
        }

        public static bool TryGet(string part, out PartTracks t)
        {
            t = null;
            return part != null && Parts.TryGetValue(part, out t);
        }
    }
}
