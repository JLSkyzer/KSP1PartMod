using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Tests.Animation
{
    /// <summary>A small KSP1 cfg parser for tests: <c>name { … }</c> nodes (brace on the same line or the next),
    /// <c>key = value</c> lines and <c>//</c> comments. The returned root holds the file's top-level nodes. It
    /// does not apply ModuleManager patches: a node named <c>@PART[x]</c> is just a node with that name.</summary>
    internal static class CfgText
    {
        public static IKsp1Node Parse(string text)
        {
            var root = new Node("root");
            var stack = new Stack<Node>();
            stack.Push(root);
            string pendingName = null;
            foreach (var rawLine in (text ?? "").TrimStart('﻿').Split('\n'))
            {
                var line = rawLine;
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line.Substring(0, comment);
                var buffer = new StringBuilder();
                foreach (var c in line)
                {
                    if (c == '{')
                    {
                        var name = buffer.ToString().Trim();
                        if (name.Length == 0) name = pendingName ?? "";
                        pendingName = null;
                        buffer.Clear();
                        var node = new Node(name);
                        stack.Peek().Children.Add(node);
                        stack.Push(node);
                    }
                    else if (c == '}')
                    {
                        Flush(buffer, stack.Peek(), ref pendingName);
                        pendingName = null;
                        if (stack.Count > 1) stack.Pop();
                    }
                    else buffer.Append(c);
                }
                Flush(buffer, stack.Peek(), ref pendingName);
            }
            return root;
        }

        private static void Flush(StringBuilder buffer, Node into, ref string pendingName)
        {
            var s = buffer.ToString().Trim();
            buffer.Clear();
            if (s.Length == 0) return;
            var eq = s.IndexOf('=');
            if (eq < 0) { pendingName = s; return; }
            into.Values.Add(new KeyValuePair<string, string>(s.Substring(0, eq).Trim(), s.Substring(eq + 1).Trim()));
            pendingName = null;
        }

        /// <summary>One MODULE node from <c>key = value</c> lines (and sub-nodes), for planner tests.</summary>
        public static IKsp1Node Module(string body) => Parse("MODULE\n{\n" + body + "\n}").GetNodes("MODULE").Single();

        private sealed class Node : IKsp1Node
        {
            public string Name { get; }
            public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
            public readonly List<Node> Children = new List<Node>();

            public Node(string name) { Name = name; }

            public string GetValue(string key) => GetValues(key).FirstOrDefault();

            public IEnumerable<string> GetValues(string key) =>
                Values.Where(v => v.Key == key).Select(v => v.Value);

            public IEnumerable<IKsp1Node> GetNodes(string name) => Children.Where(c => c.Name == name);

            public IEnumerable<KeyValuePair<string, string>> AllValues => Values;

            public IEnumerable<IKsp1Node> AllNodes => Children;

            public override string ToString() => Name;
        }
    }
}
