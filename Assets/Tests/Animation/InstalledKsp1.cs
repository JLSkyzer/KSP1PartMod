using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;

[assembly: InternalsVisibleTo("KSP1PartMod.Tests.ReduxMu")]

namespace KSP1PartMod.Tests.Animation
{
    /// <summary>The KSP1 files laid out under the game's <c>mods/KSP1</c>, for tests on real data. Override the
    /// location with the <c>KSP1_ROOT</c> environment variable.</summary>
    internal static class InstalledKsp1
    {
        public const string DefaultRoot = @"I:\SteamLibrary\steamapps\common\Kerbal Space Program 2\mods\KSP1";

        public static string Root
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("KSP1_ROOT");
                return string.IsNullOrWhiteSpace(env) ? DefaultRoot : env;
            }
        }

        /// <summary>Every <c>.mu</c> file under the root, sorted; ignores the test when the root is absent.</summary>
        public static string[] AllMu()
        {
            var root = Root;
            if (!Directory.Exists(root)) Assert.Ignore($"KSP1 files not found at {root} (set KSP1_ROOT)");
            return Directory.EnumerateFiles(root, "*.mu", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Every <c>.cfg</c> file under the root, sorted; ignores the test when the root is absent.</summary>
        public static string[] AllCfg()
        {
            var root = Root;
            if (!Directory.Exists(root)) Assert.Ignore($"KSP1 files not found at {root} (set KSP1_ROOT)");
            return Directory.EnumerateFiles(root, "*.cfg", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>The first folder under the root (the KSP1 mod folder, e.g. <c>Squad</c>).</summary>
        public static string TopFolder(string path) =>
            path.Substring(Root.TrimEnd('\\', '/').Length).TrimStart('\\', '/').Split('\\', '/')[0];

        /// <summary>True for files of the stock game: under <c>Squad/</c> or <c>SquadExpansion/</c>.</summary>
        public static bool IsStock(string path)
        {
            var top = TopFolder(path);
            return top.Equals("Squad", StringComparison.OrdinalIgnoreCase)
                   || top.Equals("SquadExpansion", StringComparison.OrdinalIgnoreCase);
        }
    }
}
