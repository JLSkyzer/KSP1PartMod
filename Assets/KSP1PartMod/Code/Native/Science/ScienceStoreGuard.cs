using System;
using System.Collections.Generic;

namespace KSP1PartMod.Native.Science
{
    /// <summary>The experiments of a part whose generated definition (<c>KSP1_*</c>) is not in the live experiment store: the
    /// configurations and standings to drop before KSP2's <c>OnStart</c> reads them. KSP2 drops an unknown configuration of a new
    /// vessel itself, but a saved vessel's standing keeps a null definition, which its location check then dereferences. Only our
    /// own ids are touched; a configuration and its standing go together (the standing is matched by id, as KSP2 matches them).
    /// Pure.</summary>
    public static class ScienceStoreGuard
    {
        public sealed class Drop
        {
            /// <summary>Indices into the configurations, descending (remove in this order).</summary>
            public readonly List<int> Configurations = new List<int>();
            /// <summary>Indices into the standings, descending.</summary>
            public readonly List<int> Standings = new List<int>();
            /// <summary>The ids dropped, distinct, in ordinal order.</summary>
            public readonly List<string> Ids = new List<string>();
            public bool Any => Configurations.Count > 0 || Standings.Count > 0;
        }

        public static bool IsOurs(string id) => id != null && id.StartsWith(ScienceDefinitionBuilder.IdPrefix, StringComparison.Ordinal);

        /// <param name="known">Whether the live store holds the id.</param>
        public static Drop Missing(IReadOnlyList<string> configurationIds, IReadOnlyList<string> standingIds, Func<string, bool> known)
        {
            var d = new Drop();
            var missing = new HashSet<string>(StringComparer.Ordinal);
            bool Gone(string id)
            {
                if (!IsOurs(id)) return false;
                if (missing.Contains(id)) return true;
                if (known(id)) return false;
                missing.Add(id);
                d.Ids.Add(id);
                return true;
            }
            for (var i = (configurationIds?.Count ?? 0) - 1; i >= 0; i--)
                if (Gone(configurationIds[i])) d.Configurations.Add(i);
            for (var i = (standingIds?.Count ?? 0) - 1; i >= 0; i--)
                if (Gone(standingIds[i])) d.Standings.Add(i);
            d.Ids.Sort(StringComparer.Ordinal);
            return d;
        }
    }
}
