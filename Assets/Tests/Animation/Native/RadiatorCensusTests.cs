using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>Every <c>ModuleActiveRadiator</c> of the real KSP1 files maps without an exception (raw PART nodes, ModuleManager
    /// patches not applied), and the passive census (plan (f)). Resources: <see cref="IsruFakes"/> with every name known, as Redux
    /// imports the KSP1 resources it does not map.</summary>
    public class RadiatorCensusTests
    {
        private sealed class ModCount
        {
            public int Parts, Deployable, Coolers, ActiveRadiators, Dropped, Merged, ResourcesDropped, Passive;
            public double MinKw = double.MaxValue, MaxKw, TotalKw, MinEcNative = double.MaxValue, MaxEcNative, MinEcKsp1 = double.MaxValue, MaxEcKsp1;
            public readonly SortedSet<string> Resources = new SortedSet<string>(StringComparer.Ordinal);

            public override string ToString() =>
                $"{Parts} radiator part(s) ({Deployable} with ModuleDeployableRadiator): {Coolers} Module_Cooler, {ActiveRadiators} Module_ActiveRadiator, " +
                $"{Dropped} dropped, {Merged} merged, {ResourcesDropped} resource(s) dropped; kW {Range(MinKw, MaxKw)} (total {TotalKw.ToString("G6", CultureInfo.InvariantCulture)}); " +
                $"EC/s native {Range(MinEcNative, MaxEcNative)}, KSP1 {Range(MinEcKsp1, MaxEcKsp1)}; resources {string.Join(", ", Resources)}; " +
                $"passive (emissiveConstant >= 0.8, no ModuleActiveRadiator) {Passive}";

            private static string Range(double min, double max) =>
                min > max ? "none" : min.ToString("G6", CultureInfo.InvariantCulture) + " to " + max.ToString("G6", CultureInfo.InvariantCulture);
        }

        private sealed class Census
        {
            public readonly SortedDictionary<string, ModCount> PerMod = new SortedDictionary<string, ModCount>(StringComparer.OrdinalIgnoreCase);
            public readonly ModCount Total = new ModCount();
            public readonly SortedDictionary<string, int> NotReproduced = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Failures = new List<string>();
            public int WithOtherDeployableCount;
        }

        private static Census Run(string root, IEnumerable<string> files)
        {
            var r = IsruFakes.Table(exists: n => true);
            var c = new Census();
            foreach (var path in files)
            {
                IKsp1Node file;
                try { file = CfgText.Parse(File.ReadAllText(path)); }
                catch (Exception) { continue; }
                var mod = InstalledKsp1.TopFolderUnder(root, path);
                foreach (var part in file.GetNodes("PART"))
                {
                    var name = part.GetValue("name") ?? "?";
                    try { Count(c, mod, part, r); }
                    catch (Exception e) { c.Failures.Add($"{mod}/{name}: {e}"); }
                }
            }
            return c;
        }

        private static void Count(Census c, string mod, IKsp1Node part, IsruResources r)
        {
            if (!c.PerMod.TryGetValue(mod, out var m)) c.PerMod[mod] = m = new ModCount();
            void Both(Action<ModCount> a) { a(m); a(c.Total); }

            var modules = part.GetNodes("MODULE").ToList();
            var radiators = modules.Where(x => IsruCfg.IsModule(x, RadiatorMapping.Ksp1Module)).ToList();
            var deployable = modules.Where(x => IsruCfg.IsModule(x, RadiatorMapping.DeployableModule)).ToList();
            if (radiators.Count == 0)
            {
                if (IsruCfg.TryNumber(part.GetValue("emissiveConstant"), out var emissive) && emissive >= 0.8) Both(x => x.Passive++);
                return;
            }
            var map = RadiatorMapping.From(radiators, deployable.Count > 0, r, NativeCoolerRef.FallbackKwPerEc, 1, false, true);
            var kept = RadiatorMapping.From(radiators, deployable.Count > 0, r, NativeCoolerRef.FallbackKwPerEc, 1, true, true);
            Both(x =>
            {
                x.Parts++;
                if (deployable.Count > 0) x.Deployable++;
                x.ResourcesDropped += map.DroppedResources;
                if (map.Merged > 1) x.Merged++;
                if (!map.HasModule) { x.Dropped++; return; }
                if (map.Kind == RadiatorKind.Cooler) x.Coolers++; else x.ActiveRadiators++;
                x.MinKw = Math.Min(x.MinKw, map.FluxRemoved);
                x.MaxKw = Math.Max(x.MaxKw, map.FluxRemoved);
                x.TotalKw += map.FluxRemoved;
                foreach (var res in map.Resources) x.Resources.Add(res.Ksp1Name + "->" + res.Name);
                var ec = map.Resources.FirstOrDefault(q => IsruResources.IsElectricCharge(q.Name));
                var ecKept = kept.Resources.FirstOrDefault(q => IsruResources.IsElectricCharge(q.Name));
                if (ec != null) { x.MinEcNative = Math.Min(x.MinEcNative, ec.Rate); x.MaxEcNative = Math.Max(x.MaxEcNative, ec.Rate); }
                if (ecKept != null) { x.MinEcKsp1 = Math.Min(x.MinEcKsp1, ecKept.Rate); x.MaxEcKsp1 = Math.Max(x.MaxEcKsp1, ecKept.Rate); }
            });
            foreach (var f in map.NotReproduced.Concat(deployable.SelectMany(d => RadiatorMapping.DeployableNotReproduced(d))))
                c.NotReproduced[f] = c.NotReproduced.TryGetValue(f, out var n) ? n + 1 : 1;
        }

        private static void Print(string title, Census c)
        {
            TestContext.WriteLine($"== {title}: {c.Total}");
            foreach (var kv in c.PerMod)
                TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            TestContext.WriteLine("  not reproduced (parts): " +
                                  string.Join(", ", c.NotReproduced.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                                      .Select(kv => kv.Key + " " + kv.Value)));
        }

        [Test] public void Installed_EveryRadiatorMaps_ExpectedCounts()
        {
            var c = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg());
            Print("installed mods/KSP1", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(32, t.Parts, "32 radiators (Squad 6, BDB 26)");
            Assert.AreEqual(15, t.Deployable, "15 with a ModuleDeployableRadiator");
            Assert.AreEqual(15, t.ActiveRadiators, "15 Module_ActiveRadiator");
            Assert.AreEqual(17, t.Coolers, "17 Module_Cooler");
            Assert.AreEqual(0, t.Dropped, "0 dropped");
            Assert.AreEqual(0, t.ResourcesDropped);
            Assert.AreEqual(0, t.Merged, "one ModuleActiveRadiator per part");
            Assert.AreEqual(1000, t.MaxKw, 1e-9, "foldingRadLarge");
            Assert.AreEqual(6, c.PerMod["Squad"].Parts);
            Assert.AreEqual(26, c.PerMod["Bluedog_DB"].Parts);
        }

        [Test] public void Installed_StockValues()
        {
            var r = IsruFakes.Table(exists: n => true);
            var expected = new Dictionary<string, (double Kw, bool Deployable)>
            {
                ["radPanelSm"] = (50, false), ["radPanelEdge"] = (150, false), ["radPanelLg"] = (200, false),
                ["foldingRadSmall"] = (50, true), ["foldingRadMed"] = (250, true), ["foldingRadLarge"] = (1000, true),
            };
            var seen = new HashSet<string>();
            foreach (var path in InstalledKsp1.AllCfg().Where(InstalledKsp1.IsStock))
            foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
            {
                var name = part.GetValue("name")?.Trim() ?? "";
                if (!expected.TryGetValue(name, out var want)) continue;
                var modules = part.GetNodes("MODULE").ToList();
                var m = RadiatorMapping.From(modules.Where(x => IsruCfg.IsModule(x, RadiatorMapping.Ksp1Module)).ToList(),
                    modules.Any(x => IsruCfg.IsModule(x, RadiatorMapping.DeployableModule)), r, 100, 1, false, true);
                Assert.IsTrue(m.HasModule, name);
                Assert.AreEqual(want.Kw, m.FluxRemoved, 1e-9, name);
                Assert.AreEqual(want.Deployable ? RadiatorKind.ActiveRadiator : RadiatorKind.Cooler, m.Kind, name);
                Assert.AreEqual(want.Kw / 100, m.Resources.Single().Rate, 1e-9, name + ": EC at 100 kW per EC/s");
                seen.Add(name);
            }
            CollectionAssert.AreEquivalent(expected.Keys, seen);
        }

        [Test] public void GameData_EveryRadiatorMaps()
        {
            var root = InstalledKsp1.GameDataRoot;
            Assume.That(Directory.Exists(root), $"KSP1 GameData not found at {root} (set KSP1_GAMEDATA)");
            var c = Run(root, InstalledKsp1.GameDataCfg());
            Print("I:/ksp1 GameData", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(112, t.Parts, "plan (f): 112 parts with ModuleActiveRadiator");
            Assert.AreEqual(0, t.Dropped, "every radiator has a maxEnergyTransfer");
        }
    }
}
