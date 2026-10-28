using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>Every converter, harvester and scanner of the real KSP1 files maps without an exception (raw PART nodes,
    /// ModuleManager patches not applied). Resources: <see cref="IsruFakes"/> with every name known, as Redux imports the
    /// KSP1 resources it does not map; minable: the 26w42c fallback.</summary>
    public class IsruCensusTests
    {
        private sealed class ModCount
        {
            public int ConverterModules, ConverterParts, ConverterModes, ConverterDropped;
            public int Harvesters, DrillParts, HarvesterNoMap, HarvesterOcean, HarvesterAtmospheric, HarvesterExospheric, HarvesterOther;
            public int Surveyors, ResourceScanners, ResourceScannersKept, ScannerRequiresUnlock, ScannerNoResource, ScannerType;
            public int AsteroidDrillParts;

            public override string ToString() =>
                $"converters {ConverterModules} on {ConverterParts} part(s) -> {ConverterModes} mode(s), {ConverterDropped} dropped; " +
                $"harvesters {Harvesters} -> {DrillParts} drill part(s), skipped {HarvesterOcean} ocean, {HarvesterAtmospheric} atmospheric, " +
                $"{HarvesterExospheric} exospheric, {HarvesterNoMap} without a map, {HarvesterOther} other; asteroid/comet drill parts {AsteroidDrillParts}; " +
                $"surveyors {Surveyors}; resource scanners {ResourceScanners} -> {ResourceScannersKept} kept, left out {ScannerRequiresUnlock} RequiresUnlock, " +
                $"{ScannerType} ScannerType, {ScannerNoResource} without a scannable resource";
        }

        private sealed class Census
        {
            public readonly SortedDictionary<string, ModCount> PerMod = new SortedDictionary<string, ModCount>(StringComparer.OrdinalIgnoreCase);
            public readonly ModCount Total = new ModCount();
            public readonly SortedDictionary<string, string> Resources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> NotReproduced = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Failures = new List<string>();
        }

        private static bool Named(IKsp1Node m, string name) => string.Equals(m.GetValue("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase);

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
                    var modules = part.GetNodes("MODULE").ToList();
                    try { Count(c, mod, name, modules, r); }
                    catch (Exception e) { c.Failures.Add($"{mod}/{name}: {e}"); }
                }
            }
            return c;
        }

        private static void Count(Census c, string mod, string part, List<IKsp1Node> modules, IsruResources r)
        {
            if (!c.PerMod.TryGetValue(mod, out var m)) c.PerMod[mod] = m = new ModCount();
            void Both(Action<ModCount> a) { a(m); a(c.Total); }
            void Fields(IEnumerable<string> fields)
            {
                foreach (var f in fields) c.NotReproduced[f] = c.NotReproduced.TryGetValue(f, out var n) ? n + 1 : 1;
            }
            void Resource(string ksp1) => c.Resources[ksp1] = r.Map(ksp1);

            var converters = modules.Where(x => Named(x, ConverterMapping.Ksp1Module)).ToList();
            if (converters.Count > 0)
            {
                var map = ConverterMapping.From(converters, 0, null, r, NativeIsruRef.FallbackConverterHeatPerEc, 1);
                Both(x => { x.ConverterModules += converters.Count; x.ConverterModes += map.Formulas.Count; x.ConverterDropped += map.DroppedFormulas; });
                if (map.HasModule) Both(x => x.ConverterParts++);
                Fields(map.NotReproduced);
                foreach (var flow in map.Formulas.SelectMany(f => f.Inputs.Concat(f.Outputs))) Resource(flow.Ksp1Name);
            }

            var harvesters = modules.Where(x => Named(x, HarvesterMapping.Ksp1Module)).ToList();
            if (harvesters.Count > 0)
            {
                var map = HarvesterMapping.From(harvesters, r, 0.05, NativeIsruRef.FallbackDrill.HeatPerEc, 1);
                Both(x =>
                {
                    x.Harvesters += harvesters.Count;
                    x.HarvesterNoMap += map.SkippedNoMap;
                    x.HarvesterOcean += map.SkippedOcean;
                    x.HarvesterAtmospheric += map.SkippedAtmospheric;
                    x.HarvesterExospheric += map.SkippedExospheric;
                    x.HarvesterOther += map.SkippedOther;
                    if (map.HasModule) x.DrillParts++;
                });
                Fields(map.NotReproduced);
                foreach (var h in harvesters) if (h.GetValue("ResourceName") is string res) Resource(res.Trim());
            }
            if (HarvesterMapping.CountAsteroidDrills(modules) > 0) Both(x => x.AsteroidDrillParts++);

            var surveyor = modules.FirstOrDefault(x => Named(x, ScannerMapping.SurveyorModule));
            if (surveyor != null && ScannerMapping.Surveyor(surveyor, NativeScannerRef.Fallback).HasModule) Both(x => x.Surveyors++);

            foreach (var scanner in modules.Where(x => Named(x, ScannerMapping.ResourceScannerModule)))
            {
                var s = ScannerMapping.Resource(scanner, r, NativeScannerRef.Fallback);
                if (scanner.GetValue("ResourceName") is string res) Resource(res.Trim());
                Both(x =>
                {
                    x.ResourceScanners++;
                    switch (s.LeftOut)
                    {
                        case ScannerLeftOut.None: x.ResourceScannersKept++; break;
                        case ScannerLeftOut.RequiresUnlock: x.ScannerRequiresUnlock++; break;
                        case ScannerLeftOut.ScannerType: x.ScannerType++; break;
                        default: x.ScannerNoResource++; break;
                    }
                });
            }
        }

        private static void Print(string title, Census c)
        {
            TestContext.WriteLine($"== {title}: {c.Total}");
            foreach (var kv in c.PerMod.Where(kv => kv.Value.ConverterModules + kv.Value.Harvesters + kv.Value.Surveyors + kv.Value.ResourceScanners > 0))
                TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            TestContext.WriteLine("  KSP1 resource -> KSP2: " + string.Join(", ", c.Resources.Select(kv => kv.Key + " -> " + kv.Value)));
            TestContext.WriteLine("  not reproduced (parts): " +
                                  string.Join(", ", c.NotReproduced.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                                      .Select(kv => kv.Key + " " + kv.Value)));
        }

        [Test] public void Installed_EveryModuleMaps_ExpectedCounts()
        {
            var c = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg());
            Print("installed mods/KSP1", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(24, t.ConverterModules, "24 KSP1 converters");
            Assert.AreEqual(18, t.ConverterParts, "into 18 modules");
            Assert.AreEqual(24, t.ConverterModes, "every converter is a mode");
            Assert.AreEqual(2, t.Harvesters);
            Assert.AreEqual(2, t.DrillParts, "RadialDrill and MiniDrill");
            Assert.AreEqual(2, t.AsteroidDrillParts, "the same two drills carry asteroid and comet drills");
            Assert.AreEqual(1, t.Surveyors, "SurveyScanner");
            Assert.AreEqual(20, t.ResourceScanners);
            Assert.AreEqual(1, t.ResourceScannersKept, "SurfaceScanner");
            Assert.AreEqual(19, t.ScannerRequiresUnlock + t.ScannerType + t.ScannerNoResource, "19 left out");
            Assert.AreEqual(19, t.ScannerRequiresUnlock);
        }

        [Test] public void GameData_EveryModuleMaps()
        {
            var root = InstalledKsp1.GameDataRoot;
            Assume.That(Directory.Exists(root), $"KSP1 GameData not found at {root} (set KSP1_GAMEDATA)");
            var c = Run(root, InstalledKsp1.GameDataCfg());
            Print("I:/ksp1 GameData", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(75, t.ConverterModules, "plan (i): 75 converters");
            Assert.AreEqual(15, t.Harvesters, "plan (i): 15 harvesters");
            Assert.AreEqual(3, t.HarvesterOcean);
            Assert.AreEqual(4, t.HarvesterAtmospheric);
            Assert.AreEqual(21, t.ResourceScanners, "plan (i): 21 resource scanners");
            Assert.AreEqual(1, t.ResourceScannersKept);
        }
    }
}
