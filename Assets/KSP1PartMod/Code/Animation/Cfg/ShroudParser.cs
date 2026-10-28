using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Reads a KSP1 <c>ModuleJettison</c> into a <see cref="EntryKind.Shroud"/> entry (0.7.0 engine shrouds):
    /// the shroud meshes it names, the attach node that shows them and the KSP1 flags that decide when they go. Field
    /// names and defaults are KSP1 1.12's (research <c>stock/NOTES.md</c> section 13). Nothing is read from the game.</summary>
    public static class ShroudParser
    {
        /// <summary>The KSP1 module name, compared ignoring case as Redux does.</summary>
        public const string ModuleName = "ModuleJettison";

        /// <summary>The default <c>bottomNodeName</c>.</summary>
        public const string DefaultNode = "bottom";

        /// <summary>True when <paramref name="module"/> is a <c>ModuleJettison</c> (name compared trimmed, ignoring case).</summary>
        public static bool Handles(IKsp1Node module) =>
            module != null && ModuleName.Equals(module.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The <c>jettisonName</c> list: split on commas, trimmed, empty names dropped (Redux's
        /// <c>SplitTransformList</c>). Empty for a module without the key.</summary>
        public static string[] Names(IKsp1Node module)
        {
            var raw = module?.GetValue("jettisonName");
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];
            return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        }

        /// <summary>The shroud entry of a <c>ModuleJettison</c>, or null for another module and for one without a
        /// <c>jettisonName</c>. The key is built by <c>EntryParser.SetKey</c> from the first name and
        /// <paramref name="ordinal"/>.</summary>
        public static AnimEntryDef Parse(IKsp1Node module, int moduleIndex, int ordinal)
        {
            if (!Handles(module)) return null;
            var names = Names(module);
            if (names.Length == 0) return null;
            var e = new AnimEntryDef
            {
                Kind = EntryKind.Shroud,
                ModuleIndex = moduleIndex,
                TransformNames = names,
                NodeName = Node(module),
                IsFairing = Bool(module, "isFairing", true),
                CheckBottomNode = Bool(module, "checkBottomNode", false),
                DecoupleEnabled = Bool(module, "decoupleEnabled", true),
                ManualJettison = Bool(module, "manualJettison", false),
                IgnoreNodes = Bool(module, "ignoreNodes", false),
                AllowShroudToggle = Bool(module, "allowShroudToggle", true),
            };
            EntryParser.SetKey(e, ordinal);
            return e;
        }

        /// <summary>Redux's <c>IsConvertibleJettisonShroud</c> (26w42c): the module names at least one transform,
        /// <c>ignoreNodes</c> does not parse as true, and the node name (default <c>bottom</c>) is not blank. Only such a
        /// module's meshes are taken out of Redux's variants and hidden in the prefab; with <c>ignoreNodes</c> the meshes
        /// stay as any other.</summary>
        public static bool Convertible(IKsp1Node module)
        {
            if (!Handles(module) || Names(module).Length == 0) return false;
            if (bool.TryParse(module.GetValue("ignoreNodes")?.Trim(), out var ignoreNodes) && ignoreNodes) return false;
            return !string.IsNullOrWhiteSpace(Node(module));
        }

        /// <summary>The shroud names of every convertible module of the part, compared ignoring case (Redux's ignored
        /// transform set).</summary>
        public static HashSet<string> ConvertibleNames(IEnumerable<IKsp1Node> modules)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (modules == null) return set;
            foreach (var m in modules)
                if (Convertible(m))
                    foreach (var n in Names(m)) set.Add(n);
            return set;
        }

        private static string Node(IKsp1Node module)
        {
            var raw = module.GetValue("bottomNodeName");
            return string.IsNullOrWhiteSpace(raw) ? DefaultNode : raw.Trim();
        }

        private static bool Bool(IKsp1Node m, string key, bool fallback) =>
            bool.TryParse(m.GetValue(key)?.Trim(), out var v) ? v : fallback;
    }
}
