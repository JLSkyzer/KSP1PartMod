using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KSP1PartMod.Placement
{
    /// <summary>A tech node this mod creates, placed next to a KSP2 node (its anchor).</summary>
    public sealed class NewNodeSpec
    {
        public const string IdPrefix = "tNode_ksp1_";

        public string Key { get; }
        /// <summary>KSP2 node id; the new node takes its cost, icon, category, tier and version.</summary>
        public string Anchor { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        /// <summary>KSP2 ids or other new nodes' ids (tNode_ksp1_...).</summary>
        public string[] Requires { get; }
        public string NodeId => IdPrefix + Key;

        public NewNodeSpec(string key, string anchor, float offsetX, float offsetY, string[] requires)
        {
            Key = key;
            Anchor = anchor;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Requires = requires ?? Array.Empty<string>();
        }
    }

    /// <summary>The shipped table: KSP1 node id -> KSP2 node id or "new:&lt;key&gt;", plus the fallbacks.</summary>
    public sealed class TechMapping
    {
        public const string NewPrefix = "new:";
        public const string DefaultSize = "default";

        /// <summary>Case-insensitive KSP1 node id -> KSP2 id or "new:&lt;key&gt;".</summary>
        public IReadOnlyDictionary<string, string> Ksp1Nodes { get; }
        public IReadOnlyDictionary<string, NewNodeSpec> NewNodes { get; }
        /// <summary>Case-insensitive KSP1 category -> size (or "default") -> target.</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CategoryFallback { get; }
        /// <summary>"new:&lt;key&gt;".</summary>
        public string CatchAll { get; }

        private TechMapping(IReadOnlyDictionary<string, string> ksp1Nodes, IReadOnlyDictionary<string, NewNodeSpec> newNodes,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> categoryFallback, string catchAll)
        {
            Ksp1Nodes = ksp1Nodes;
            NewNodes = newNodes;
            CategoryFallback = categoryFallback;
            CatchAll = catchAll;
        }

        /// <summary>Keys starting with "_" are comments. Throws on invalid JSON or a malformed entry.</summary>
        public static TechMapping Parse(string json)
        {
            var root = JObject.Parse(json);

            var ksp1 = StringMap(root["ksp1_nodes"] as JObject, StringComparer.OrdinalIgnoreCase);

            var newNodes = new Dictionary<string, NewNodeSpec>(StringComparer.Ordinal);
            foreach (var (key, value) in Entries(root["new_nodes"] as JObject))
            {
                var node = (JObject)value;
                var offset = node["offset"] as JObject;
                newNodes[key] = new NewNodeSpec(key, (string)node["anchor"],
                    (float?)offset?["x"] ?? 0f, (float?)offset?["y"] ?? 0f,
                    (node["requires"] as JArray)?.Select(t => (string)t).ToArray());
            }

            var fallback = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, sizes) in Entries(root["category_fallback"] as JObject))
                fallback[category] = StringMap((JObject)sizes, StringComparer.OrdinalIgnoreCase);

            return new TechMapping(ksp1, newNodes, fallback, (string)root["catch_all"]);
        }

        /// <summary>One message per target that is neither a KSP2 node nor a declared new node, per anchor that is
        /// not a KSP2 node, and per requirement that is neither.</summary>
        public IReadOnlyList<string> Validate(ISet<string> ksp2Nodes)
        {
            var errors = new List<string>();
            void CheckTarget(string where, string target)
            {
                if (IsNewTarget(target, out var key) ? !NewNodes.ContainsKey(key) : !ksp2Nodes.Contains(target ?? ""))
                    errors.Add($"{where}: target {target ?? "(none)"} is neither in the KSP2 tree nor a new node");
            }

            foreach (var pair in Ksp1Nodes) CheckTarget($"ksp1_nodes.{pair.Key}", pair.Value);
            foreach (var category in CategoryFallback)
                foreach (var size in category.Value)
                    CheckTarget($"category_fallback.{category.Key}.{size.Key}", size.Value);
            CheckTarget("catch_all", CatchAll);

            var newIds = new HashSet<string>(NewNodes.Values.Select(n => n.NodeId));
            foreach (var node in NewNodes.Values)
            {
                if (!ksp2Nodes.Contains(node.Anchor ?? ""))
                    errors.Add($"new_nodes.{node.Key}: anchor {node.Anchor ?? "(none)"} is not in the KSP2 tree");
                foreach (var req in node.Requires)
                    if (!ksp2Nodes.Contains(req ?? "") && !newIds.Contains(req ?? ""))
                        errors.Add($"new_nodes.{node.Key}: requirement {req ?? "(none)"} is neither in the KSP2 tree nor a new node");
            }
            return errors;
        }

        /// <summary>True for "new:&lt;key&gt;"; key is then the part after the prefix.</summary>
        public static bool IsNewTarget(string target, out string key)
        {
            var isNew = target != null && target.StartsWith(NewPrefix, StringComparison.Ordinal);
            key = isNew ? target.Substring(NewPrefix.Length) : null;
            return isNew;
        }

        private static IEnumerable<(string key, JToken value)> Entries(JObject obj) =>
            obj == null
                ? Enumerable.Empty<(string, JToken)>()
                : obj.Properties().Where(p => !p.Name.StartsWith("_")).Select(p => (p.Name, p.Value));

        private static IReadOnlyDictionary<string, string> StringMap(JObject obj, StringComparer comparer)
        {
            var map = new Dictionary<string, string>(comparer);
            foreach (var (key, value) in Entries(obj)) map[key] = (string)value;
            return map;
        }
    }
}
