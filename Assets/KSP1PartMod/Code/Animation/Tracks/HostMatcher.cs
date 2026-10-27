using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Finds the object of an instance that carries a TrackSet's Animation, by its <c>.mu</c> path.</summary>
    public static class HostMatcher
    {
        private static readonly IReadOnlyList<int> None = new int[0];

        /// <summary>Indices of <paramref name="instancePaths"/> (transform paths relative to the part root, '/'-separated)
        /// sharing the longest common suffix of whole path segments with <paramref name="muHostPath"/>, at least one
        /// segment; in index order. Names compare as Unity's <c>Transform.Find</c> does: case-sensitive. Several
        /// results mean several copies of the model (several MODEL nodes naming one <c>.mu</c>). Runs at bind time.</summary>
        public static IReadOnlyList<int> Match(IReadOnlyList<string> instancePaths, string muHostPath)
        {
            if (instancePaths == null || string.IsNullOrEmpty(muHostPath)) return None;
            var host = muHostPath.Split('/');
            var best = 0;
            var hits = new List<int>();
            for (var i = 0; i < instancePaths.Count; i++)
            {
                var path = instancePaths[i];
                if (string.IsNullOrEmpty(path)) continue;
                var n = CommonSuffix(path.Split('/'), host);
                if (n == 0 || n < best) continue;
                if (n > best) { best = n; hits.Clear(); }
                hits.Add(i);
            }
            return hits.Count == 0 ? None : hits;
        }

        private static int CommonSuffix(string[] a, string[] b)
        {
            var n = 0;
            while (n < a.Length && n < b.Length && string.Equals(a[a.Length - 1 - n], b[b.Length - 1 - n], StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
