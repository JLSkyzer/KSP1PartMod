using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace KSP1PartMod.Placement
{
    /// <summary>The player's overrides.json: part name or KSP1 node id -> KSP2 node id. Wins over the table.</summary>
    public sealed class Overrides
    {
        /// <summary>Part name (case-sensitive) -> KSP2 node id.</summary>
        public IReadOnlyDictionary<string, string> Parts { get; }
        /// <summary>KSP1 node id (case-insensitive) -> KSP2 node id.</summary>
        public IReadOnlyDictionary<string, string> Ksp1Nodes { get; }

        public static readonly Overrides Empty = new Overrides(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private Overrides(IReadOnlyDictionary<string, string> parts, IReadOnlyDictionary<string, string> ksp1Nodes)
        {
            Parts = parts;
            Ksp1Nodes = ksp1Nodes;
        }

        /// <summary>Never throws. Bad JSON gives Empty and an error; a non-string entry is skipped and reported.
        /// error is null when everything was read.</summary>
        public static Overrides Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) return Empty;
            try
            {
                var root = JObject.Parse(json);
                var problems = new List<string>();
                var parts = Read(root, "parts", StringComparer.Ordinal, problems);
                var nodes = Read(root, "ksp1_nodes", StringComparer.OrdinalIgnoreCase, problems);
                if (problems.Count > 0) error = string.Join("; ", problems);
                return new Overrides(parts, nodes);
            }
            catch (Exception e)
            {
                error = $"overrides ignored, invalid JSON: {e.Message}";
                return Empty;
            }
        }

        private static Dictionary<string, string> Read(JObject root, string section, StringComparer comparer, List<string> problems)
        {
            var map = new Dictionary<string, string>(comparer);
            var token = root[section];
            if (token == null) return map;
            if (!(token is JObject obj))
            {
                problems.Add($"\"{section}\" is not an object, ignored");
                return map;
            }
            foreach (var p in obj.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                if (p.Value.Type == JTokenType.String && ((string)p.Value).Trim().Length > 0)
                    map[p.Name] = ((string)p.Value).Trim();
                else
                    problems.Add($"{section}.{p.Name} is not a node id, ignored");
            }
            return map;
        }
    }
}
