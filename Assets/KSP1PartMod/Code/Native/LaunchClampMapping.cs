using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>KSP1 <c>LaunchClamp</c> (the stock clamp and the Bluedog_DB towers, 7 installed cfg files) read as KSP2's native
    /// <c>Module_Decouple</c> (launch clamps, 0.19.0). Redux has no importer for it, only a staging icon: the clamp stayed welded to the
    /// rocket and its stage did nothing. KSP1's clamp is released at its stage and falls away; the equivalent is an omni decoupler on
    /// the part's surface attach node, with no ejection push. Pure: no Unity, game or Redux type.
    /// <list type="bullet">
    /// <item>the node is the part's <c>srfAttach</c> (KSP1 <c>node_attach</c>), else the first attach node, else <c>srfAttach</c>;</item>
    /// <item>not reproduced: the stretching tower (<c>trf_towerPivot_name</c>, <c>trf_towerStretch_name</c>, <c>trf_anchor_name</c>),
    /// the release animation (<c>trf_animationRoot_name</c>, <c>anim_decouple_name</c>) and KSP1's ground anchoring: noted.</item>
    /// </list></summary>
    public sealed class LaunchClampMapping
    {
        public const string Ksp1Module = "LaunchClamp";
        public const string NativeModule = "Module_Decouple";
        public const string PreferredNode = "srfAttach";

        /// <summary>The note of a part that already has a decoupler (KSP2's <c>Module_Decouple</c> is one per part for staging).</summary>
        public const string ExtraModuleNote = Ksp1Module + ": the part already has a decoupler, the clamp is not imported";

        /// <summary>The attach node the clamp is released at.</summary>
        public string NodeId { get; private set; } = PreferredNode;

        /// <summary>What is not reproduced, one line each, in the module's order.</summary>
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<string> _notes = new List<string>();

        public static string NodeNotFoundNote(string wanted, string used) =>
            $"{Ksp1Module}: attach node '{wanted}' not found, '{used}' used";

        public static LaunchClampMapping From(IKsp1Node module, IReadOnlyList<string> nodeIds)
        {
            var m = new LaunchClampMapping();
            var ids = (nodeIds ?? new string[0]).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var match = ids.FirstOrDefault(n => n.Equals(PreferredNode, StringComparison.OrdinalIgnoreCase));
            if (match != null) m.NodeId = match;
            else if (ids.Count > 0)
            {
                m.NodeId = ids[0];
                m._notes.Add(NodeNotFoundNote(PreferredNode, m.NodeId));
            }
            if (module == null) return m;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in module.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                if (key == "name" || !seen.Add(key)) continue;
                m._notes.Add($"{Ksp1Module}: {key} not reproduced");
            }
            foreach (var child in module.AllNodes)
                m._notes.Add($"{Ksp1Module}: node {child.Name} not reproduced");
            return m;
        }
    }
}
