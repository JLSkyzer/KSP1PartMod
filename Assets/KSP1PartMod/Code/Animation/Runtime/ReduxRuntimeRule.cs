using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>When and to what our module restricts Redux's deployable runtime (which otherwise plays every
    /// deployable source clip together). Missing data never stops Redux: a part whose data was synthesised, has no
    /// entry of ours or no <c>ReduxClips</c> list keeps Redux's runtime as it was.</summary>
    public static class ReduxRuntimeRule
    {
        /// <summary>Null when the runtime is to be restricted, else why it is left untouched.</summary>
        /// <param name="synthesised">The module had no data and runs on an empty one it made itself.</param>
        public static string SkipReason(bool synthesised, int entryCount, bool hasReduxClips)
        {
            if (synthesised) return "no data, an empty one was made";
            if (entryCount == 0) return "no entry of ours";
            if (!hasReduxClips) return "no Redux clip list";
            return null;
        }

        /// <summary>The clips Redux's runtime keeps: Redux's own (<paramref name="reduxClips"/>), then the clips of
        /// our entries that failed to build or bind, when the runtime played them (<paramref name="runtimeClips"/>).
        /// A failed entry's clip then goes back to Redux. Blank names are dropped, duplicates kept once (ordinal).</summary>
        public static string[] KeptClips(IEnumerable<string> reduxClips, IEnumerable<string> failedClips, IEnumerable<string> runtimeClips)
        {
            var kept = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (reduxClips != null)
                foreach (var c in reduxClips)
                    if (!string.IsNullOrWhiteSpace(c) && seen.Add(c)) kept.Add(c);
            if (failedClips != null && runtimeClips != null)
            {
                var played = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in runtimeClips)
                    if (!string.IsNullOrWhiteSpace(c)) played.Add(c);
                foreach (var c in failedClips)
                    if (!string.IsNullOrWhiteSpace(c) && played.Contains(c) && seen.Add(c)) kept.Add(c);
            }
            return kept.ToArray();
        }
    }
}
