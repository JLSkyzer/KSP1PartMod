using System.Collections.Generic;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>A KSP1 config node as the planner needs it: Redux's <c>Ksp1ConfigNode</c> in game, a test parser
    /// in EditMode. <see cref="GetValue"/> returns the first value of the key, or null.</summary>
    public interface IKsp1Node
    {
        string GetValue(string key);
        IEnumerable<string> GetValues(string key);
        IEnumerable<IKsp1Node> GetNodes(string name);
        /// <summary>The node's name (<c>MODULE</c>, <c>TEXTURE:emissive</c>…).</summary>
        string Name { get; }
        /// <summary>Every <c>key = value</c> of the node, in order.</summary>
        IEnumerable<KeyValuePair<string, string>> AllValues { get; }
        /// <summary>Every child node, in order.</summary>
        IEnumerable<IKsp1Node> AllNodes { get; }
    }
}
