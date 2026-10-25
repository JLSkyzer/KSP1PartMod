using System.Collections.Generic;
using KSP1PartMod.Placement;
using Redux.Ksp1Import.Assets;

namespace KSP1PartMod.Game
{
    /// <summary>The KSP1 parts Redux imported, read from its registry.</summary>
    internal static class ImportedPartsSource
    {
        /// <summary>One entry per imported part. Call only once Ksp1ImportedPartRegistry.IsComplete holds.</summary>
        public static List<Ksp1PartInfo> Read()
        {
            var parts = new List<Ksp1PartInfo>();
            foreach (var entry in Ksp1ImportedPartRegistry.ImportedParts)
            {
                if (entry == null) continue;
                var node = entry.PartNode;
                parts.Add(Ksp1PartInfoReader.Read(entry.PartName, entry.ModName, key => node?.GetValue(key, null)));
            }
            return parts;
        }
    }
}
