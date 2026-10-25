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
    }
}
