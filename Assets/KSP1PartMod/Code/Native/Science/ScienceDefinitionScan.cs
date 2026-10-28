using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.Native.Science
{
    /// <summary>The read of the KSP1 cfg files for the science definitions (science plan, Task 3). Each file is offered with its
    /// text; only the files whose text names <c>EXPERIMENT_DEFINITION</c> or <c>experimentID</c> are parsed. It keeps the root
    /// <c>EXPERIMENT_DEFINITION</c> nodes, the <c>MODULE</c> lists of the parts that carry an experiment module (their
    /// <c>xmitDataScalar</c> and DMagic masks), counts the ModuleManager edits of definitions (not applied), and notes the files that
    /// may hold a <c>Localization</c> node (for <c>#autoLOC</c> titles). Pure: the parser is given.</summary>
    public sealed class ScienceDefinitionScan
    {
        public const string DefinitionNode = "EXPERIMENT_DEFINITION";

        /// <summary>Every cfg file offered.</summary>
        public int CfgFiles { get; private set; }
        /// <summary>Files with at least one root <c>EXPERIMENT_DEFINITION</c>.</summary>
        public int DefinitionFiles { get; private set; }
        /// <summary>Root nodes that edit a definition through ModuleManager (<c>@EXPERIMENT_DEFINITION[…]</c>, <c>+</c>, <c>%</c>, <c>!</c>,
        /// <c>-</c>, <c>$</c>): not applied.</summary>
        public int MmEdits { get; private set; }

        public IReadOnlyList<IKsp1Node> DefinitionNodes => _definitions;
        /// <summary>The <c>MODULE</c> list of each part that has an experiment module, in file order.</summary>
        public IReadOnlyList<IReadOnlyList<IKsp1Node>> Parts => _parts;
        /// <summary>The files whose text names <c>Localization</c>, in the order offered.</summary>
        public IReadOnlyList<string> LocalizationFiles => _localization;
        /// <summary><c>path: error</c> of the files that did not parse (skipped).</summary>
        public IReadOnlyList<string> Failures => _failures;

        private readonly List<IKsp1Node> _definitions = new List<IKsp1Node>();
        private readonly List<IReadOnlyList<IKsp1Node>> _parts = new List<IReadOnlyList<IKsp1Node>>();
        private readonly List<string> _localization = new List<string>();
        private readonly List<string> _failures = new List<string>();

        public static bool IsMmEdit(string nodeName)
        {
            var n = nodeName?.Trim() ?? "";
            return n.Length > 0 && "@+%!-$".IndexOf(n[0]) >= 0 && n.IndexOf(DefinitionNode, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Whether a file's text is worth parsing for science.</summary>
        public static bool Relevant(string text) =>
            text != null && (text.IndexOf(DefinitionNode, StringComparison.OrdinalIgnoreCase) >= 0
                             || text.IndexOf("experimentID", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <param name="parse">The cfg parser: a root node holding the file's top-level nodes.</param>
        public void Add(string path, string text, Func<string, IKsp1Node> parse)
        {
            CfgFiles++;
            if (text == null) return;
            if (text.IndexOf("Localization", StringComparison.Ordinal) >= 0) _localization.Add(path);
            if (!Relevant(text)) return;
            IKsp1Node root;
            try { root = parse(text); }
            catch (Exception e)
            {
                _failures.Add($"{path}: {e.Message}");
                return;
            }
            if (root == null) return;
            var any = false;
            foreach (var node in root.AllNodes)
            {
                var name = node?.Name?.Trim() ?? "";
                if (name == DefinitionNode)
                {
                    _definitions.Add(node);
                    any = true;
                }
                else if (IsMmEdit(name)) MmEdits++;
                else if (name == "PART")
                {
                    var modules = node.GetNodes("MODULE").ToList();
                    if (modules.Any(ScienceMapping.IsExperimentModule)) _parts.Add(modules);
                }
            }
            if (any) DefinitionFiles++;
        }

        /// <summary>The definitions, by key (<see cref="ScienceDefinitionBuilder.BuildAll"/>): built or not, base and DMagic variants.</summary>
        public Dictionary<string, ScienceDefinitionModel> Build(AnimationSettings settings, Func<string, string> titleResolver, List<string> duplicates = null) =>
            ScienceDefinitionBuilder.BuildAll(_definitions, _parts, settings, titleResolver, duplicates);

        /// <summary>The skip counts of the definitions that are not built, plus the duplicate ids, by reason (sorted).</summary>
        public static IReadOnlyList<(string Reason, int Count)> SkipCounts(IEnumerable<ScienceDefinitionModel> definitions, int duplicates)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in definitions ?? Enumerable.Empty<ScienceDefinitionModel>())
                if (!d.IsBuilt) counts[d.SkipReason ?? "?"] = counts.TryGetValue(d.SkipReason ?? "?", out var n) ? n + 1 : 1;
            if (duplicates > 0) counts["duplicate id"] = duplicates;
            return counts.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }
}
