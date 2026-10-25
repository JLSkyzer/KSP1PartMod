using System;

namespace KSP1PartMod.Placement
{
    public static class Ksp1PartInfoReader
    {
        // Largest first: the first profile found wins.
        private static readonly string[][] SizeProfiles =
        {
            new[] { "size4", "4v" }, new[] { "size3", "3v" }, new[] { "size2", "2v" },
            new[] { "size1p5", "1.5v" }, new[] { "size1", "1v" }, new[] { "size0", "0v" },
            new[] { "mk3", "mk3" }, new[] { "mk2", "mk2" }, new[] { "srf", "srf" },
        };

        /// <param name="getValue">Returns the raw config value of a key, or null.</param>
        public static Ksp1PartInfo Read(string partName, string modName, Func<string, string> getValue)
        {
            string tech = Clean(getValue("TechRequired"));
            string category = Clean(getValue("category"));
            bool techHidden = bool.TryParse(Clean(getValue("TechHidden")), out bool hidden) && hidden;
            // KSP1's default category is none (never shown): a part without category (EVA kerbals, flag) is hidden too.
            // Unresearcheable is KSP1's placeholder for comets and asteroids, not a tree node.
            bool hiddenByConfig = techHidden || category == null
                || string.Equals(category, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tech, "Unresearcheable", StringComparison.OrdinalIgnoreCase);
            return new Ksp1PartInfo(partName, modName, tech, category, ReadSize(getValue("bulkheadProfiles")), hiddenByConfig);
        }

        private static string Clean(string raw)
        {
            if (raw == null) return null;
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) raw = raw.Substring(0, comment);
            raw = raw.Trim();
            return raw.Length == 0 ? null : raw;
        }

        private static string ReadSize(string profiles)
        {
            if (profiles == null) return null;
            var tokens = profiles.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in SizeProfiles)
                foreach (var token in tokens)
                    if (string.Equals(token, entry[0], StringComparison.OrdinalIgnoreCase)) return entry[1];
            return null;
        }
    }
}
