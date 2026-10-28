using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>KSP1's <c>FXModuleAnimateRCS</c> pairs the i-th object holding its clip with the i-th thruster, both found
    /// in hierarchy order (spec 2b.6). On a Redux instance the clip's hosts are the TrackSet hosts and the thrusters are
    /// <c>Module_RCS.ThrusterTransforms</c>, which Redux may fill in another order (registry names first): both lists are
    /// put back in hierarchy order before they are paired. Runs at bind time.</summary>
    public static class RcsThrusterMatch
    {
        /// <summary>For each sampler, the index into <c>ThrusterTransforms</c> of the thruster it follows; null when the
        /// number of distinct hosts differs from the number of thrusters (or either is zero): the entry is then not
        /// driven.</summary>
        /// <param name="hostOfSampler">Hierarchy index of each sampler's host (several samplers may share a host).</param>
        /// <param name="thrusterHierarchy">Hierarchy index of each thruster transform, negative when it is not under the
        /// part (kept last, in its own order).</param>
        public static int[] Pair(IReadOnlyList<int> hostOfSampler, IReadOnlyList<int> thrusterHierarchy)
        {
            if (hostOfSampler == null || thrusterHierarchy == null) return null;
            var hosts = new List<int>();
            foreach (var h in hostOfSampler)
                if (!hosts.Contains(h)) hosts.Add(h);
            hosts.Sort();
            if (hosts.Count == 0 || hosts.Count != thrusterHierarchy.Count) return null;

            var thrusters = new List<int>();
            for (var j = 0; j < thrusterHierarchy.Count; j++) thrusters.Add(j);
            thrusters.Sort((a, b) =>
            {
                var ka = thrusterHierarchy[a] < 0 ? int.MaxValue : thrusterHierarchy[a];
                var kb = thrusterHierarchy[b] < 0 ? int.MaxValue : thrusterHierarchy[b];
                return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
            });

            var result = new int[hostOfSampler.Count];
            for (var k = 0; k < result.Length; k++) result[k] = thrusters[hosts.IndexOf(hostOfSampler[k])];
            return result;
        }
    }
}
