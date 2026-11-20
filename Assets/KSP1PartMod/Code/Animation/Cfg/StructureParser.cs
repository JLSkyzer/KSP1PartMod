using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Reads a KSP1 <c>ModuleStructuralNode</c> into a <see cref="EntryKind.Structure"/> entry (0.17.0): the root
    /// object (a cap, a truss) that KSP1 shows while a part is attached to one of the listed attach nodes. Field names and
    /// defaults are KSP1 1.12's (<c>ModuleStructuralNode</c> decompiled, research <c>stock/decompiled/full</c>). The options
    /// <c>reverseVisibility</c>, <c>spawnManually</c> and <c>visibilityState</c> are used by no installed cfg and are not read.
    /// Nothing is read from the game.</summary>
    public static class StructureParser
    {
        /// <summary>The KSP1 module name, compared ignoring case as Redux does.</summary>
        public const string ModuleName = "ModuleStructuralNode";

        /// <summary>The companion module that adds the editor toggles (mesh and interstage nodes).</summary>
        public const string ToggleModuleName = "ModuleStructuralNodeToggle";

        /// <summary>KSP1's default <c>rootObject</c>.</summary>
        public const string DefaultRoot = "Fairing";

        /// <summary>KSP1's default <c>attachNodeNames</c>.</summary>
        public const string DefaultNode = "bottom";

        /// <summary>The PAM label of the mesh toggle when the cfg gives no <c>MeshMenuName</c>.</summary>
        public const string DefaultMenuName = "Structure";

        public static bool Handles(IKsp1Node module) =>
            module != null && ModuleName.Equals(module.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The structure entry of a <c>ModuleStructuralNode</c>, or null for another module. The key is built by
        /// <c>EntryParser.SetKey</c> from the root object and <paramref name="ordinal"/>.</summary>
        public static AnimEntryDef Parse(IKsp1Node module, int moduleIndex, int ordinal)
        {
            if (!Handles(module)) return null;
            var root = module.GetValue("rootObject")?.Trim();
            if (string.IsNullOrEmpty(root)) root = DefaultRoot;
            var nodes = Split(module.GetValue("attachNodeNames"));
            if (nodes.Length == 0) nodes = new[] { DefaultNode };
            var e = new AnimEntryDef
            {
                Kind = EntryKind.Structure,
                ModuleIndex = moduleIndex,
                TransformNames = new[] { root },
                TargetNames = nodes,
            };
            EntryParser.SetKey(e, ordinal);
            return e;
        }

        /// <summary>The comma list split, trimmed, empty names dropped.</summary>
        public static string[] Split(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];
            return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        }

        /// <summary>The mesh toggle's PAM label when the part has a <c>ModuleStructuralNodeToggle</c> (its <c>MeshMenuName</c>, else
        /// <see cref="DefaultMenuName"/>); null when it has none or no <c>ModuleStructuralNode</c> to hide.</summary>
        public static string MeshToggleName(IReadOnlyList<IKsp1Node> modules)
        {
            if (modules == null || !modules.Any(Handles)) return null;
            var toggle = modules.FirstOrDefault(m => m != null && ToggleModuleName.Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (toggle == null) return null;
            var name = toggle.GetValue("MeshMenuName")?.Trim();
            return string.IsNullOrEmpty(name) ? DefaultMenuName : name;
        }
    }
}
