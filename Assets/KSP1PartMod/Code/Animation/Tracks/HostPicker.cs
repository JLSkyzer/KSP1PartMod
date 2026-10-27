using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Finds, on a part instance, the objects that carry a <see cref="TrackSet"/>'s Animation host.
    /// <list type="bullet">
    /// <item>Redux 26w42c (<c>Ksp1ModelImporter.BuildPrefab</c> / <c>TryLoadModel</c> / <c>BuildObject</c>, decompiled)
    /// makes one object per model it loads, named like the <c>.mu</c> root object (a blank name gives
    /// <c>muObject</c>, at every level), and parents it under the part object in reference order. A reference it
    /// cannot resolve or read adds nothing. Nothing renames these objects later; Unity's <c>Instantiate</c> renames
    /// only the instantiated root (the part object, outside the paths here).</item>
    /// <item>So a set's model is found by name and rank: the k-th loaded model whose root is named R is the k-th
    /// object named R among the siblings that hold the host (two copies of one model have equal path strings; the
    /// hierarchy order tells them apart).</item>
    /// </list></summary>
    public static class HostPicker
    {
        /// <summary>The name Redux gives an object whose <c>.mu</c> name is blank.</summary>
        public const string BlankName = "muObject";

        private static readonly IReadOnlyList<int> None = new int[0];

        /// <summary>A <c>.mu</c> path ('/'-separated) as Redux names the objects: blank segments become
        /// <see cref="BlankName"/>. Null gives "".</summary>
        public static string InstancePath(string muPath)
        {
            if (muPath == null) return "";
            var segments = muPath.Split('/');
            for (var i = 0; i < segments.Length; i++) segments[i] = ObjectName(segments[i]);
            return string.Join("/", segments);
        }

        /// <summary>The rank of model <paramref name="modelIndex"/> among the loaded models (non-null
        /// <paramref name="modelRoots"/>) of the same root name; -1 when that model was not loaded or is unknown.</summary>
        public static int ModelOrdinal(IReadOnlyList<string> modelRoots, int modelIndex)
        {
            if (modelRoots == null || modelIndex < 0 || modelIndex >= modelRoots.Count || modelRoots[modelIndex] == null) return -1;
            var name = ObjectName(modelRoots[modelIndex]);
            var rank = 0;
            for (var i = 0; i < modelIndex; i++)
                if (modelRoots[i] != null && ObjectName(modelRoots[i]) == name) rank++;
            return rank;
        }

        /// <summary>Indices of <paramref name="paths"/> (the instance's transforms under the part object, in hierarchy
        /// pre-order, '/'-separated, relative to the part object) that are the host <paramref name="muHostPath"/> (its
        /// <c>.mu</c> path, root object included) of model <paramref name="modelIndex"/>. Whole-path matches only, found
        /// with <see cref="HostMatcher"/>. Without model roots every whole match is returned; when the instance does not
        /// hold as many models of that name as were loaded, nothing (the note says why). Runs at bind time.</summary>
        public static IReadOnlyList<int> Pick(IReadOnlyList<string> paths, string muHostPath, IReadOnlyList<string> modelRoots,
            int modelIndex, out string note)
        {
            note = null;
            var host = InstancePath(muHostPath);
            if (paths == null || host.Length == 0)
            {
                note = "no host path";
                return None;
            }
            var hostSegments = host.Split('/').Length;
            var hits = new List<int>();
            foreach (var i in HostMatcher.Match(paths, host))
                if (paths[i].Split('/').Length >= hostSegments && (paths[i] == host || paths[i].EndsWith("/" + host, StringComparison.Ordinal)))
                    hits.Add(i);
            if (hits.Count == 0)
            {
                note = $"no object '{host}'";
                return None;
            }

            var ordinal = ModelOrdinal(modelRoots, modelIndex);
            if (ordinal < 0)
            {
                note = $"model unknown, {hits.Count} object(s) '{host}' taken";
                return hits;
            }
            var rootName = host.Split('/')[0];
            if (ObjectName(modelRoots[modelIndex]) != rootName)
            {
                note = $"host '{host}' is not under model root '{ObjectName(modelRoots[modelIndex])}'";
                return None;
            }
            var expected = 0;
            foreach (var r in modelRoots)
                if (r != null && ObjectName(r) == rootName) expected++;

            // The objects of that root name that hold one of the hits: its path minus the host, plus the root name.
            var rootPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var h in hits) rootPaths.Add(paths[h].Substring(0, paths[h].Length - host.Length) + rootName);
            var roots = new List<int>();
            for (var i = 0; i < paths.Count; i++)
                if (paths[i] != null && rootPaths.Contains(paths[i])) roots.Add(i);
            if (roots.Count != expected)
            {
                note = $"{roots.Count} object(s) '{rootName}' for {expected} model(s) of that root, tracks not placed";
                return None;
            }

            // Pre-order: a hit belongs to the last root of its root path at or before it.
            var root = roots[ordinal];
            var picked = new List<int>();
            foreach (var h in hits)
                if (OwnerRoot(paths, roots, h, host, rootName) == root) picked.Add(h);
            if (picked.Count == 0) note = $"no object '{host}' under model {ordinal + 1} of {expected} '{rootName}'";
            return picked;
        }

        private static int OwnerRoot(IReadOnlyList<string> paths, List<int> roots, int hit, string host, string rootName)
        {
            var rootPath = paths[hit].Substring(0, paths[hit].Length - host.Length) + rootName;
            var owner = -1;
            foreach (var r in roots)
            {
                if (r > hit) break;
                if (paths[r] == rootPath) owner = r;
            }
            return owner;
        }

        private static string ObjectName(string muName) => string.IsNullOrWhiteSpace(muName) ? BlankName : muName;
    }
}
