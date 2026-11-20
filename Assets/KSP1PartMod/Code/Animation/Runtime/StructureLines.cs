using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The exact log lines (and PAM labels) of the structural node feature, 0.17.0.</summary>
    public static class StructureLines
    {
        /// <summary>The VAB toggle's label: "on" is the structure hidden, so the label names the action that applies next.</summary>
        public static string Button(string menuName, bool hidden)
        {
            var name = string.IsNullOrWhiteSpace(menuName) ? "structure" : menuName.Trim();
            return (hidden ? "Show " : "Hide ") + name;
        }

        public static string Resolved(string part, string root, int nodesFound, int nodes, bool rootFound) =>
            $"[KSP1PartMod] anim: {part} structure '{root}': {(rootFound ? "found" : "not found in the model")}, {nodesFound} of {nodes} attach node(s) found";

        public static string Shown(string part, string root) => $"[KSP1PartMod] anim: {part} structure '{root}' shown";

        public static string Hidden(string part, string root) => $"[KSP1PartMod] anim: {part} structure '{root}' hidden";

        public static string MeshToggle(string part, bool hidden) =>
            $"[KSP1PartMod] anim: {part} structure mesh toggle: {(hidden ? "hidden" : "shown")}";

        /// <summary>The import-time summary of the feature (once per session).</summary>
        public static string Armed(int parts, int entries) =>
            $"[KSP1PartMod] anim: structural nodes: {entries} entry(ies) on {parts} part(s); Interstage Nodes toggle not reproduced";

        public static string Joined(IEnumerable<string> names) => string.Join(",", names);
    }
}
