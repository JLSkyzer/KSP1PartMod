using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native.Fairings;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Fairings
{
    /// <summary>The fairing rules on the installed KSP1 cfgs (raw <c>PART</c> nodes, ModuleManager patches not applied):
    /// every base maps, and the animation entry keys of the fairing parts do not move when <c>Module_Fairing</c> joins the
    /// part's native modules (the importers on or off). Ignored when the KSP1 files are absent.</summary>
    public class FairingCensusTests
    {
        private static List<IKsp1Node> _parts;

        private static List<IKsp1Node> FairingParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<IKsp1Node>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                var text = File.ReadAllText(path);
                if (text.IndexOf("Fairing", StringComparison.OrdinalIgnoreCase) < 0) continue;
                parts.AddRange(CfgText.Parse(text).AllNodes.Where(IsPartNode).Where(p => Modules(p).Any(IsFairing)));
            }
            return _parts = parts;
        }

        /// <summary><c>PART</c>, or <c>PART:NEEDS[…]</c> (Redux resolves the NEEDS; the BDB SAF files use it).</summary>
        private static bool IsPartNode(IKsp1Node n) =>
            n != null && (n.Name == "PART" || n.Name.StartsWith("PART:", StringComparison.Ordinal));

        private static IEnumerable<IKsp1Node> Modules(IKsp1Node part) => part.GetNodes("MODULE").Where(m => m != null);

        private static bool IsFairing(IKsp1Node m) => Named(m, FairingMapping.ProceduralModule) || Named(m, FairingMapping.SafModule);

        private static bool Named(IKsp1Node m, string name) => string.Equals(m.GetValue("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase);

        [Test] public void Census_EveryBaseMaps_AndSafNamesFound()
        {
            var parts = FairingParts();
            int procedural = 0, proceduralOk = 0, saf = 0, safOk = 0, names = 0, twoModules = 0;
            var refused = new List<string>();
            foreach (var p in parts)
            {
                var fairings = Modules(p).Where(IsFairing).ToList();
                if (fairings.Count > 1) twoModules++;
                var first = fairings[0];
                var map = Named(first, FairingMapping.SafModule) ? FairingMapping.Saf(first, p) : FairingMapping.Procedural(first, p);
                if (map.Kind == FairingKind.Saf)
                {
                    saf++;
                    if (map.Converted) safOk++;
                    names += SafMeshNames.Collect(p).Count;
                }
                else
                {
                    procedural++;
                    if (map.Converted) proceduralOk++;
                }
                if (!map.Converted) refused.Add(p.GetValue("name") + " (" + map.Reason + ")");
            }
            TestContext.WriteLine($"fairing census: procedural {proceduralOk}/{procedural}, SAF {safOk}/{saf}, SAF mesh names {names}, " +
                                  $"parts with two fairing modules {twoModules}; refused: {string.Join(", ", refused)}");
            Assert.That(procedural + saf, Is.GreaterThan(0), "no fairing part found");
            Assert.AreEqual(procedural, proceduralOk, "every procedural base has a baseRadius");
            Assert.AreEqual(saf, safOk, "every SAF part has a top node of a known size");
            Assert.AreEqual(0, twoModules);
            Assert.That(names, Is.GreaterThanOrEqualTo(3 * saf));
        }

        /// <summary>The fairing parts' entry keys with <c>Module_Fairing</c> among the native modules are those without it,
        /// under every planner switch (Global Constraints: no animation entry key moves).</summary>
        [Test] public void Keys_SameWithAndWithoutModuleFairing()
        {
            var parts = FairingParts();
            Assume.That(parts.Count, Is.GreaterThan(0));
            var changed = new List<string>();
            foreach (var p in parts)
            {
                var modules = Modules(p).ToList();
                if (Keys(modules, new HashSet<string>()) != Keys(modules, new HashSet<string> { FairingMapping.NativeModule }))
                    changed.Add(p.GetValue("name"));
            }
            TestContext.WriteLine($"fairing keys: {parts.Count} part(s) planned");
            CollectionAssert.IsEmpty(changed);
        }

        private static string Keys(IReadOnlyList<IKsp1Node> modules, ISet<string> native)
        {
            var keys = new List<string>();
            foreach (var own in new[] { false, true })
            foreach (var follow in new[] { false, true })
            foreach (var shrouds in new[] { false, true })
            {
                var plan = OwnershipPlanner.Plan(modules, false, native, own, follow, null, true, true, shrouds);
                keys.Add(string.Join("|", plan.Entries.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal)));
            }
            return string.Join("\n", keys);
        }
    }
}
