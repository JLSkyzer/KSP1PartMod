using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Tests.Animation
{
    /// <summary>A KSP1 node built by hand, whose values are kept exactly as given (a trailing <c>//</c> comment included), as a
    /// reader that does not strip comments would give them. <see cref="CfgText"/> strips comments.</summary>
    internal sealed class RawNode : IKsp1Node
    {
        private readonly List<KeyValuePair<string, string>> _values;
        private readonly Dictionary<string, List<IKsp1Node>> _children;
        public string Name { get; }

        public RawNode(string name, IEnumerable<KeyValuePair<string, string>> values, Dictionary<string, List<IKsp1Node>> children = null)
        {
            Name = name;
            _values = values.ToList();
            _children = children ?? new Dictionary<string, List<IKsp1Node>>();
        }

        public static KeyValuePair<string, string> KV(string key, string value) => new KeyValuePair<string, string>(key, value);

        public string GetValue(string key) => GetValues(key).FirstOrDefault();
        public IEnumerable<string> GetValues(string key) => _values.Where(v => v.Key == key).Select(v => v.Value);
        public IEnumerable<IKsp1Node> GetNodes(string name) => _children.TryGetValue(name, out var l) ? l : Enumerable.Empty<IKsp1Node>();
        public IEnumerable<KeyValuePair<string, string>> AllValues => _values;
        public IEnumerable<IKsp1Node> AllNodes => _children.Values.SelectMany(l => l);
    }
}
