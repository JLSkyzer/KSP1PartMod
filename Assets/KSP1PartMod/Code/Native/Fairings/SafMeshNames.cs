using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Fairings
{
    /// <summary>The KSP1 SAF meshes of a part, hidden in its prefab once KSP2's fairing replaces them (fairings plan, Task 1):
    /// <list type="bullet">
    /// <item>the <c>transformName</c> of <c>WALL_BASE</c>, <c>WALL</c> and <c>CONE</c> in the SAF module;</item>
    /// <item>every <c>transform =</c> of every <c>SUBTYPE</c> of a <c>ModuleB9PartSwitch</c> one of whose subtypes edits the SAF
    /// module (<c>MODULE { IDENTIFIER { name = ModuleSimpleAdjustableFairing } }</c>);</item>
    /// <item>the <c>transformName</c> of those edits' <c>DATA</c> pieces.</item>
    /// </list>
    /// Distinct, ignoring case, in cfg order. Pure.</summary>
    public static class SafMeshNames
    {
        public const string B9Module = "ModuleB9PartSwitch";

        public static List<string> Collect(IKsp1Node part)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modules = Children(part, "MODULE").ToList();
            foreach (var saf in modules.Where(m => IsruCfg.IsModule(m, FairingMapping.SafModule)))
                AddPieces(saf, names, seen);

            foreach (var b9 in modules.Where(m => IsruCfg.IsModule(m, B9Module)))
            {
                var subtypes = Children(b9, "SUBTYPE").ToList();
                var edits = subtypes.SelectMany(s => Children(s, "MODULE")).Where(EditsSaf).ToList();
                if (edits.Count == 0) continue;
                foreach (var subtype in subtypes)
                foreach (var t in subtype.GetValues("transform") ?? Enumerable.Empty<string>())
                    Add(IsruCfg.Text(t), names, seen);
                foreach (var data in edits.SelectMany(e => Children(e, "DATA")))
                    AddPieces(data, names, seen);
            }
            return names;
        }

        /// <summary>A B9 <c>MODULE</c> edit whose <c>IDENTIFIER</c> names the SAF module.</summary>
        internal static bool EditsSaf(IKsp1Node edit) =>
            Children(edit, "IDENTIFIER").Any(i => IsruCfg.IsModule(i, FairingMapping.SafModule));

        private static void AddPieces(IKsp1Node node, List<string> names, HashSet<string> seen)
        {
            foreach (var piece in FairingMapping.SafPieces)
            foreach (var n in Children(node, piece))
                Add(IsruCfg.Value(n, "transformName"), names, seen);
        }

        private static void Add(string name, List<string> names, HashSet<string> seen)
        {
            if (!string.IsNullOrEmpty(name) && seen.Add(name)) names.Add(name);
        }

        private static IEnumerable<IKsp1Node> Children(IKsp1Node node, string name) =>
            (node?.GetNodes(name) ?? Enumerable.Empty<IKsp1Node>()).Where(n => n != null);
    }
}
