using System;
using System.Collections.Generic;
using System.Linq;
using PlacementResult = KSP1PartMod.Placement.Placement;

namespace KSP1PartMod.Placement
{
    /// <summary>What to write into the tech tree: parts per final node id, the new nodes that receive parts, and
    /// the log lines describing it.</summary>
    public sealed class PlacementPlan
    {
        private const string Log = "[KSP1PartMod] ";

        /// <summary>Final KSP2 node id -> part names, in input order. "new:&lt;key&gt;" is already turned into
        /// "tNode_ksp1_&lt;key&gt;".</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> PartsByNode { get; }
        /// <summary>Only the new nodes that receive parts; a node comes after any new node it requires. A
        /// requirement on a new node that is not created is replaced by the node's anchor.</summary>
        public IReadOnlyList<NewNodeSpec> NodesToCreate { get; }
        public IReadOnlyList<string> LogLines { get; }

        private PlacementPlan(IReadOnlyDictionary<string, IReadOnlyList<string>> partsByNode,
            IReadOnlyList<NewNodeSpec> nodesToCreate, IReadOnlyList<string> logLines)
        {
            PartsByNode = partsByNode;
            NodesToCreate = nodesToCreate;
            LogLines = logLines;
        }

        private sealed class ModCounts
        {
            public int Table, Override, Fallback, CatchAll, Hidden, Kept;
            public int Placed => Table + Override + Fallback + CatchAll;
        }

        /// <param name="existingPartLookup">Part name -> node id, as in the game; such parts are kept where they are.</param>
        public static PlacementPlan Build(IEnumerable<Ksp1PartInfo> parts, PlacementResolver resolver, TechMapping map,
            IReadOnlyDictionary<string, string> existingPartLookup)
        {
            var partsByNode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var newKeys = new HashSet<string>(StringComparer.Ordinal);
            var counts = new SortedDictionary<string, ModCounts>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var log = new List<string>();

            foreach (var part in parts)
            {
                if (part?.PartName == null || !seen.Add(part.PartName)) continue;
                var modName = part.ModName ?? "(unknown)";
                if (!counts.TryGetValue(modName, out var c)) counts[modName] = c = new ModCounts();

                PlacementResult placement = resolver.Resolve(part);
                if (placement.Source == PlacementSource.Hidden) { c.Hidden++; continue; }

                if (existingPartLookup != null && existingPartLookup.TryGetValue(part.PartName, out var holder))
                {
                    c.Kept++;
                    log.Add($"{Log}kept: {part.PartName} already unlocked by {holder}");
                    continue;
                }

                var node = placement.Target;
                if (TechMapping.IsNewTarget(node, out var key))
                {
                    if (!map.NewNodes.TryGetValue(key, out var spec))
                    {
                        log.Add($"{Log}not placed: {part.PartName} ({modName}): {node} is not a declared new node");
                        continue;
                    }
                    newKeys.Add(key);
                    node = spec.NodeId;
                }
                if (string.IsNullOrEmpty(node))
                {
                    log.Add($"{Log}not placed: {part.PartName} ({modName}): no target");
                    continue;
                }

                switch (placement.Source)
                {
                    case PlacementSource.Table: c.Table++; break;
                    case PlacementSource.Override: c.Override++; break;
                    case PlacementSource.CategoryFallback: c.Fallback++; break;
                    case PlacementSource.CatchAll: c.CatchAll++; break;
                }
                if (placement.Source == PlacementSource.CategoryFallback || placement.Source == PlacementSource.CatchAll)
                    log.Add($"{Log}fallback: {part.PartName} ({modName}, TechRequired={part.TechRequired ?? "none"}, " +
                            $"{part.Category ?? "none"}/{part.Size ?? "none"}) -> {node}");

                if (!partsByNode.TryGetValue(node, out var list)) partsByNode[node] = list = new List<string>();
                list.Add(part.PartName);
            }

            foreach (var pair in counts)
            {
                var c = pair.Value;
                log.Add($"{Log}mod {pair.Key}: {c.Placed} placed ({c.Table} table, {c.Override} override, " +
                        $"{c.Fallback} fallback, {c.CatchAll} catch-all), {c.Hidden} hidden, {c.Kept} kept");
            }

            return new PlacementPlan(
                partsByNode.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal),
                OrderNodes(map, newKeys), log);
        }

        /// <summary>Depth-first over requirements, in the table's order, so a node follows the new nodes it requires.</summary>
        private static IReadOnlyList<NewNodeSpec> OrderNodes(TechMapping map, HashSet<string> keys)
        {
            var created = new HashSet<string>(map.NewNodes.Values.Where(n => keys.Contains(n.Key)).Select(n => n.NodeId));
            var byId = map.NewNodes.Values.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
            var ordered = new List<NewNodeSpec>();
            var visited = new HashSet<string>(StringComparer.Ordinal);

            void Visit(NewNodeSpec spec)
            {
                if (!visited.Add(spec.NodeId)) return; // also breaks cycles
                var requires = new List<string>();
                foreach (var req in spec.Requires)
                {
                    if (req != null && req.StartsWith(NewNodeSpec.IdPrefix, StringComparison.Ordinal))
                    {
                        if (created.Contains(req)) Visit(byId[req]);
                        else { requires.Add(spec.Anchor); continue; }
                    }
                    requires.Add(req);
                }
                ordered.Add(new NewNodeSpec(spec.Key, spec.Anchor, spec.OffsetX, spec.OffsetY,
                    requires.Distinct(StringComparer.Ordinal).ToArray()));
            }

            foreach (var spec in map.NewNodes.Values)
                if (created.Contains(spec.NodeId)) Visit(spec);
            return ordered;
        }
    }
}
