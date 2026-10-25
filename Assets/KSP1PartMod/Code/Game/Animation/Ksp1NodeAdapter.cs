using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Redux's parsed KSP1 node seen as the planner's <see cref="IKsp1Node"/>.</summary>
    internal sealed class Ksp1NodeAdapter : IKsp1Node
    {
        private readonly Ksp1ConfigNode _node;

        public Ksp1NodeAdapter(Ksp1ConfigNode node) => _node = node;

        public string GetValue(string key) => _node.GetValue(key);

        public IEnumerable<string> GetValues(string key) => _node.GetValues(key) ?? Enumerable.Empty<string>();

        public IEnumerable<IKsp1Node> GetNodes(string name) =>
            (_node.GetNodes(name) ?? Enumerable.Empty<Ksp1ConfigNode>()).Where(n => n != null).Select(n => (IKsp1Node)new Ksp1NodeAdapter(n));

        /// <summary>The part's MODULE nodes in cfg order.</summary>
        public static IReadOnlyList<IKsp1Node> Modules(Ksp1ConfigNode part) =>
            part == null ? new IKsp1Node[0] : new Ksp1NodeAdapter(part).GetNodes("MODULE").ToList();
    }
}
