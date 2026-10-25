using System.Collections.Generic;

namespace KSP1PartMod.Placement
{
    public enum PlacementSource { Override, Table, CategoryFallback, CatchAll, Hidden }

    /// <summary>Where one KSP1 part goes.</summary>
    public readonly struct Placement
    {
        /// <summary>KSP2 node id or "new:&lt;key&gt;"; null if Hidden.</summary>
        public string Target { get; }
        public PlacementSource Source { get; }
        /// <summary>What was skipped on the way, for the log; null if nothing was.</summary>
        public string Note { get; }

        public Placement(string target, PlacementSource source, string note)
        {
            Target = target;
            Source = source;
            Note = note;
        }
    }

    /// <summary>Order: hidden, part override, KSP1-node override, table, category + size, category default,
    /// catch-all. A target missing from the loaded tree is skipped and named in the note.</summary>
    public sealed class PlacementResolver
    {
        private readonly TechMapping _map;
        private readonly Overrides _overrides;
        private readonly ISet<string> _ksp2Nodes;

        public PlacementResolver(TechMapping map, Overrides overrides, ISet<string> ksp2Nodes)
        {
            _map = map;
            _overrides = overrides ?? Overrides.Empty;
            _ksp2Nodes = ksp2Nodes;
        }

        public Placement Resolve(Ksp1PartInfo part)
        {
            if (part.HiddenByConfig) return new Placement(null, PlacementSource.Hidden, null);

            var skipped = new List<string>();
            Placement? Try(string target, PlacementSource source, string what)
            {
                if (IsUsable(target)) return new Placement(target, source, Note(skipped));
                skipped.Add($"{what} {target} is not in the tree");
                return null;
            }

            Placement? found = null;
            if (part.PartName != null && _overrides.Parts.TryGetValue(part.PartName, out var byPart))
                found = Try(byPart, PlacementSource.Override, $"part override for {part.PartName}:");
            if (found != null) return found.Value;

            var tech = part.TechRequired;
            if (tech == null)
                skipped.Add("no TechRequired");
            else
            {
                if (_overrides.Ksp1Nodes.TryGetValue(tech, out var byNodeOverride))
                    found = Try(byNodeOverride, PlacementSource.Override, $"node override for {tech}:");
                if (found != null) return found.Value;

                if (_map.Ksp1Nodes.TryGetValue(tech, out var byTable))
                    found = Try(byTable, PlacementSource.Table, $"table entry for {tech}:");
                else
                    skipped.Add($"KSP1 node {tech} is not in the table");
                if (found != null) return found.Value;
            }

            if (part.Category != null && _map.CategoryFallback.TryGetValue(part.Category, out var sizes))
            {
                if (part.Size != null && sizes.TryGetValue(part.Size, out var bySize))
                    found = Try(bySize, PlacementSource.CategoryFallback, $"fallback {part.Category}/{part.Size}:");
                if (found != null) return found.Value;

                if (sizes.TryGetValue(TechMapping.DefaultSize, out var byDefault))
                    found = Try(byDefault, PlacementSource.CategoryFallback, $"fallback {part.Category}/{TechMapping.DefaultSize}:");
                if (found != null) return found.Value;
            }
            else
                skipped.Add($"no fallback for category {part.Category ?? "(none)"}");

            // The catch-all is used even if unusable, so the part is never lost; the note says so.
            if (!IsUsable(_map.CatchAll)) skipped.Add($"catch-all {_map.CatchAll} is not in the tree");
            return new Placement(_map.CatchAll, PlacementSource.CatchAll, Note(skipped));
        }

        private bool IsUsable(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (!TechMapping.IsNewTarget(target, out var key)) return _ksp2Nodes.Contains(target);
            return _map.NewNodes.TryGetValue(key, out var spec) && spec.Anchor != null && _ksp2Nodes.Contains(spec.Anchor);
        }

        private static string Note(List<string> skipped) => skipped.Count == 0 ? null : string.Join("; ", skipped);
    }
}
