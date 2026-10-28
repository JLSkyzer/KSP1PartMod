using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary>The science rules on the real KSP1 files (raw <c>PART</c> and <c>EXPERIMENT_DEFINITION</c> nodes, ModuleManager
    /// patches not applied, science plan (g)): every definition builds and every science part resolves without an exception, with
    /// the counts printed per mod. Titles are not resolved (the <c>#autoLOC</c> reader is game glue): the raw title is kept.</summary>
    public class ScienceCensusTests
    {
        private sealed class ModCount
        {
            public int Definitions, DefinitionsSkipped, FlavorKept, FlavorSkipped;
            public int Parts, WithModule, Generated, Identical, Closest, Skipped, Duplicates, Merged;
            public int Storage, ContainerOnly, ExperimentOnly, Containers;
            public double MinData = double.MaxValue, MaxData, MinSample = double.MaxValue, MaxSample;
            public readonly SortedDictionary<string, int> Situations = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> OutOfScope = new SortedDictionary<string, int>(StringComparer.Ordinal);

            public override string ToString() =>
                $"definitions {Definitions} ({DefinitionsSkipped} skipped), flavor kept {FlavorKept} skipped {FlavorSkipped}, " +
                $"data {Range(MinData, MaxData)}, sample {Range(MinSample, MaxSample)}; " +
                $"{Parts} science part(s), {WithModule} with Module_ScienceExperiment: {Generated} generated, {Identical} identical, " +
                $"{Closest} closest, {Skipped} skipped, {Duplicates} repeated KSP2 id(s), {Merged} merged module(s); " +
                $"storage {Storage} ({Containers} with a container, {ContainerOnly} container only, {ExperimentOnly} experiment only); " +
                $"situations {string.Join(", ", Situations.Select(kv => kv.Key + " " + kv.Value))}; " +
                $"out of scope {(OutOfScope.Count == 0 ? "none" : string.Join(", ", OutOfScope.Select(kv => kv.Key + " " + kv.Value)))}";

            private static string Range(double min, double max) =>
                min > max ? "none" : min.ToString("G6", CultureInfo.InvariantCulture) + " to " + max.ToString("G6", CultureInfo.InvariantCulture);
        }

        private sealed class Census
        {
            public readonly SortedDictionary<string, ModCount> PerMod = new SortedDictionary<string, ModCount>(StringComparer.OrdinalIgnoreCase);
            public readonly ModCount Total = new ModCount();
            public readonly SortedDictionary<string, int> NotReproduced = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> SkippedIds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> ClosestIds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> DefinitionSkips = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Failures = new List<string>();
            public int DefinitionFiles, CfgFiles, MmEdits, DuplicateDefinitions, Variants, ModulesWithoutXmit;
            public readonly List<string> VariantKeys = new List<string>();
            public Dictionary<string, ScienceDefinitionModel> Definitions = new Dictionary<string, ScienceDefinitionModel>(StringComparer.Ordinal);
            public readonly List<(string Mod, string Name, SciencePart Part)> Parts = new List<(string, string, SciencePart)>();

            public ModCount Mod(string mod)
            {
                if (!PerMod.TryGetValue(mod, out var m)) PerMod[mod] = m = new ModCount();
                return m;
            }
        }

        private static bool IsMmEdit(string nodeName)
        {
            var n = nodeName?.Trim() ?? "";
            return n.Length > 0 && "@+%!-$".IndexOf(n[0]) >= 0 && n.IndexOf("EXPERIMENT_DEFINITION", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Census Run(string root, IReadOnlyList<string> files, string json = "{}")
        {
            var settings = AnimationSettings.Parse(json, out _);
            var c = new Census { CfgFiles = files.Count };
            var parsed = new List<(string Mod, IKsp1Node File)>();
            foreach (var path in files)
            {
                IKsp1Node file;
                try { file = CfgText.Parse(File.ReadAllText(path)); }
                catch (Exception) { continue; }
                parsed.Add((InstalledKsp1.TopFolderUnder(root, path), file));
            }

            // Definitions: the first of an id wins; the first module that uses the id gives its overrides.
            var uses = DefinitionOverrides.FirstUses(parsed.SelectMany(f => f.File.GetNodes("PART")).Select(p => (IReadOnlyList<IKsp1Node>)p.GetNodes("MODULE").ToList()));
            foreach (var (mod, file) in parsed)
            {
                var defs = file.AllNodes.Where(n => n.Name?.Trim() == "EXPERIMENT_DEFINITION").ToList();
                c.MmEdits += file.AllNodes.Count(n => IsMmEdit(n.Name));
                if (defs.Count > 0) c.DefinitionFiles++;
                foreach (var node in defs)
                {
                    var id = IsruCfg.Value(node, "id") ?? "";
                    try
                    {
                        uses.TryGetValue(id, out var o);
                        var d = ScienceDefinitionBuilder.From(node, o, settings, null);
                        if (c.Definitions.ContainsKey(id)) { c.DuplicateDefinitions++; continue; }
                        c.Definitions[id] = d;
                        void Both(Action<ModCount> a) { a(c.Mod(mod)); a(c.Total); }
                        Both(x =>
                        {
                            x.Definitions++;
                            x.FlavorKept += d.FlavorKept;
                            x.FlavorSkipped += d.FlavorTotal - d.FlavorKept;
                            if (!d.IsBuilt) { x.DefinitionsSkipped++; return; }
                            var label = Ksp1Situations.Labels(d.Locations);
                            x.Situations[label] = x.Situations.TryGetValue(label, out var n) ? n + 1 : 1;
                            if (d.DataValue > 0) { x.MinData = Math.Min(x.MinData, d.DataValue); x.MaxData = Math.Max(x.MaxData, d.DataValue); }
                            if (d.SampleValue > 0) { x.MinSample = Math.Min(x.MinSample, d.SampleValue); x.MaxSample = Math.Max(x.MaxSample, d.SampleValue); }
                        });
                        if (!d.IsBuilt) c.DefinitionSkips[d.SkipReason] = c.DefinitionSkips.TryGetValue(d.SkipReason, out var k) ? k + 1 : 1;
                        foreach (var f in d.NotReproduced) Add(c.NotReproduced, "definition " + f);
                    }
                    catch (Exception e) { c.Failures.Add($"{mod}/definition {id}: {e}"); }
                }
            }

            // What the resolution reads: the base definitions and one per DMagic mask pair (as the game glue builds them).
            c.Definitions = ScienceDefinitionBuilder.BuildAll(
                parsed.SelectMany(f => f.File.AllNodes.Where(n => n.Name?.Trim() == "EXPERIMENT_DEFINITION")),
                parsed.SelectMany(f => f.File.GetNodes("PART")).Select(q => (IReadOnlyList<IKsp1Node>)q.GetNodes("MODULE").ToList()), settings, null);
            foreach (var v in c.Definitions.Values.Where(d => d.IsVariant).OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                c.Variants++;
                c.VariantKeys.Add($"{v.ExperimentId} ({Ksp1Situations.Labels(v.Locations)}{(v.IsBuilt ? "" : ", not built")})");
            }

            foreach (var (mod, file) in parsed)
            foreach (var part in file.GetNodes("PART"))
            {
                var name = part.GetValue("name")?.Trim() ?? "?";
                try { Count(c, mod, name, part, settings); }
                catch (Exception e) { c.Failures.Add($"{mod}/{name}: {e}"); }
            }
            return c;
        }

        private static void Add(SortedDictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out var n) ? n + 1 : 1;

        private static void Count(Census c, string mod, string name, IKsp1Node part, AnimationSettings settings)
        {
            var modules = part.GetNodes("MODULE").ToList();
            void Both(Action<ModCount> a) { a(c.Mod(mod)); a(c.Total); }
            foreach (var scope in modules.Select(m => ScienceMapping.OutOfScope(IsruCfg.Value(m, "name"))).Where(s => s != null).Distinct())
                Both(x => Add(x.OutOfScope, scope)); // parts, as plan (g) counts them
            var experiments = modules.Count(ScienceMapping.IsExperimentModule);
            c.ModulesWithoutXmit += modules.Count(m => ScienceMapping.IsExperimentModule(m) && IsruCfg.Value(m, "xmitDataScalar") == null);
            var container = modules.Any(ScienceMapping.IsContainer);
            if (experiments == 0 && !container) return;
            var p = ScienceMapping.Resolve(modules, c.Definitions, NativeExperiments.Redux26w42c, settings);
            c.Parts.Add((mod, name, p));
            Both(x =>
            {
                if (container) x.Containers++;
                if (p.ReportStorage)
                {
                    x.Storage++;
                    if (!p.HasModule) x.ContainerOnly++;
                    else if (!container) x.ExperimentOnly++;
                }
                if (experiments == 0) return;
                x.Parts++;
                if (p.HasModule) x.WithModule++;
                x.Merged += p.MergedModules;
                x.Duplicates += p.DuplicateKsp2;
                foreach (var r in p.Resolutions)
                {
                    switch (r.Kind)
                    {
                        case ScienceResolutionKind.Generated: x.Generated++; break;
                        case ScienceResolutionKind.Identical: x.Identical++; break;
                        case ScienceResolutionKind.Closest: x.Closest++; break;
                        default: x.Skipped++; break;
                    }
                }
            });
            foreach (var r in p.Resolutions)
            {
                if (r.Kind == ScienceResolutionKind.Skipped) Add(c.SkippedIds, r.Ksp1Id + " (" + r.SkipReason + ")");
                if (r.Kind == ScienceResolutionKind.Closest) Add(c.ClosestIds, r.Ksp1Id + " -> " + r.Ksp2Id);
            }
            foreach (var f in p.NotReproduced) Add(c.NotReproduced, f);
        }

        private static string Join(SortedDictionary<string, int> d) =>
            d.Count == 0 ? "none" : string.Join(", ", d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + " " + kv.Value));

        private static void Print(string title, Census c)
        {
            TestContext.WriteLine($"== {title}: {c.CfgFiles} cfg file(s), definitions read {c.Total.Definitions} from {c.DefinitionFiles} file(s), " +
                                  $"{c.DuplicateDefinitions} duplicate id(s), {c.MmEdits} MM edit(s) of EXPERIMENT_DEFINITION; {c.Variants} definition(s) of a DMagic mask override: " +
                                  $"{(c.VariantKeys.Count == 0 ? "none" : string.Join(", ", c.VariantKeys))}; {c.ModulesWithoutXmit} experiment module(s) without xmitDataScalar (1 assumed)");
            TestContext.WriteLine($"  total: {c.Total}");
            foreach (var kv in c.PerMod.Where(kv => kv.Value.Parts > 0 || kv.Value.Definitions > 0 || kv.Value.Storage > 0 || kv.Value.OutOfScope.Count > 0))
                TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            TestContext.WriteLine("  definitions skipped: " + Join(c.DefinitionSkips));
            TestContext.WriteLine("  closest: " + Join(c.ClosestIds));
            TestContext.WriteLine("  skipped: " + Join(c.SkippedIds));
            TestContext.WriteLine("  not reproduced (parts or definitions): " + Join(c.NotReproduced));
        }

        /// <summary>Four installed DMagic modules have <c>sitMask = 48</c> (BDB's Apollo SIM bay mass spectrometer, the two OGO OPEP
        /// modules, the Ranger gamma-ray spectrometer); the distinct id and mask pairs.</summary>
        private const int VariantsInstalled = 3;

        /// <summary>The stock definitions the user copies from his own KSP1 (<c>node tools/ksp1-layout.mjs --science</c>).</summary>
        private static bool IsStockScienceDefs(string path) =>
            path.Replace('\\', '/').EndsWith("/Squad/Resources/ScienceDefs.cfg", StringComparison.OrdinalIgnoreCase);

        /// <summary>The installed cfgs without the copied stock <c>ScienceDefs.cfg</c>: the counts of plan (g).</summary>
        private static string[] InstalledWithoutStockDefs() => InstalledKsp1.AllCfg().Where(p => !IsStockScienceDefs(p)).ToArray();

        [Test] public void Installed_EveryDefinitionBuilds_EveryPartResolves_ExpectedCounts()
        {
            var c = Run(InstalledKsp1.Root, InstalledWithoutStockDefs());
            Print("installed mods/KSP1", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(55, t.Definitions, "plan (g): 55 EXPERIMENT_DEFINITIONs on disk, all BDB");
            Assert.AreEqual(55, c.PerMod["Bluedog_DB"].Definitions);
            Assert.AreEqual(0, c.MmEdits, "0 MM edits installed");
            Assert.AreEqual(298, t.Parts, "298 parts with a mapped experiment module");
            Assert.AreEqual(286, t.WithModule, "286 get Module_ScienceExperiment");
            Assert.AreEqual(269, t.Generated, "generated");
            Assert.AreEqual(48, t.Identical, "crewReport -> CrewObservation");
            Assert.AreEqual(41, t.Closest, "stock instruments without a definition on disk");
            Assert.AreEqual(18, t.Skipped, "kerbal experiments, ROCScience, bd_GooLab");
            Assert.AreEqual(VariantsInstalled, c.Variants, "the DMagic modules with sitMask = 48 get their own definitions");
            Assert.IsTrue(c.Definitions.Values.Where(d => d.IsVariant).All(d => d.IsBuilt && d.SituationMask == 48));
        }

        /// <summary>With the stock <c>ScienceDefs.cfg</c> copied under <c>mods/KSP1/Squad/Resources/</c> (18 definitions): the stock
        /// instruments get their own generated definitions instead of the closest KSP2 experiment.</summary>
        [Test] public void Installed_WithTheStockScienceDefs()
        {
            var all = InstalledKsp1.AllCfg();
            Assume.That(all.Any(IsStockScienceDefs), "the stock ScienceDefs.cfg is not under mods/KSP1/Squad/Resources");
            var c = Run(InstalledKsp1.Root, all);
            Print("installed mods/KSP1 with the stock ScienceDefs.cfg", c);
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            Assert.AreEqual(73, t.Definitions, "55 BDB + 18 stock");
            Assert.AreEqual(18, c.PerMod["Squad"].Definitions);
            Assert.AreEqual(298, t.Parts);
            var b = Run(InstalledKsp1.Root, InstalledWithoutStockDefs()).Total;
            Assert.AreEqual(b.Generated + b.Identical + b.Closest + b.Skipped, t.Generated + t.Identical + t.Closest + t.Skipped,
                "the same KSP1 ids are resolved, only their kind changes");
            TestContext.WriteLine($"with the stock file: {t.WithModule} with module, {t.Generated} generated, {t.Identical} identical, " +
                                  $"{t.Closest} closest, {t.Skipped} skipped");
            // Counted on 2026-10-28 with the stock file of the user's KSP1 1.12 install.
            Assert.AreEqual(286, t.WithModule);
            Assert.AreEqual(310, t.Generated, "269 + the 41 stock instruments");
            Assert.AreEqual(48, t.Identical);
            Assert.AreEqual(0, t.Closest, "every stock instrument now has its definition");
            Assert.AreEqual(18, t.Skipped);
        }

        private static ScienceDefinitionScan ScanFiles(IEnumerable<string> files)
        {
            var scan = new ScienceDefinitionScan();
            foreach (var path in files) scan.Add(path, File.ReadAllText(path), CfgText.Parse);
            return scan;
        }

        /// <summary>The ModuleManager part patches that edit a science key of an experiment module (not seen by the definition scan,
        /// which reads raw parts): counted for the <c>definitions: read</c> line.</summary>
        [Test] public void Installed_ScienceModuleMmEdits()
        {
            var scan = ScanFiles(InstalledKsp1.AllCfg());
            TestContext.WriteLine($"installed mods/KSP1: {scan.ScienceModuleMmEdits} MM edit(s) of science modules, {scan.MmEdits} of definitions");
            Assert.AreEqual(17, scan.ScienceModuleMmEdits, "raw cfgs, NEEDS not resolved (counted 2026-10-28)");
            Assert.AreEqual(0, scan.MmEdits);
        }

        [Test] public void GameData_ScienceModuleMmEdits_Printed()
        {
            var scan = ScanFiles(InstalledKsp1.GameDataCfg());
            TestContext.WriteLine($"KSP1 GameData: {scan.ScienceModuleMmEdits} MM edit(s) of science modules, {scan.MmEdits} of definitions");
            CollectionAssert.IsEmpty(scan.Failures);
        }

        [Test] public void Installed_StorageParts()
        {
            var c = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg());
            CollectionAssert.IsEmpty(c.Failures);
            var t = c.Total;
            TestContext.WriteLine($"storage: {t.Storage} part(s), {t.Containers} with a ModuleScienceContainer, {t.ContainerOnly} container only, " +
                                  $"{t.ExperimentOnly} experiment only");
            foreach (var kv in c.PerMod.Where(kv => kv.Value.Storage > 0))
                TestContext.WriteLine($"  {kv.Key}: {kv.Value.Storage} ({kv.Value.ContainerOnly} container only, {kv.Value.ExperimentOnly} experiment only)");
            Assert.AreEqual(74, t.Containers, "plan (g): 74 ModuleScienceContainer parts");
            Assert.AreEqual(t.Storage, t.WithModule + t.ContainerOnly, "every resolved part stores, and every container");
            var off = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg(), "{\"science_report_storage\": false}");
            Assert.AreEqual(0, off.Total.Storage);
            Assert.AreEqual(t.WithModule, off.Total.WithModule, "storage off changes no experiment");
        }

        [Test] public void Installed_SwitchesOff()
        {
            var noGen = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg(), "{\"science_generate_definitions\": false}");
            CollectionAssert.IsEmpty(noGen.Failures);
            Print("installed, science_generate_definitions false", noGen);
            Assert.AreEqual(0, noGen.Total.Generated);
            var off = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg(), "{\"ksp1_science\": false}");
            Assert.AreEqual(0, off.Total.WithModule);
            Assert.AreEqual(0, off.Total.Storage);
        }

        [Test] public void Installed_OutOfScopeModules()
        {
            var c = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg());
            foreach (var kv in c.PerMod.Where(kv => kv.Value.OutOfScope.Count > 0))
                TestContext.WriteLine($"  {kv.Key}: {string.Join(", ", kv.Value.OutOfScope.Select(x => x.Key + " " + x.Value))}");
            var t = c.Total.OutOfScope;
            Assert.AreEqual(12, t.TryGetValue("ModuleScienceLab", out var lab) ? lab : 0, "plan (g)");
            Assert.AreEqual(12, t.TryGetValue("ModuleScienceConverter", out var conv) ? conv : 0, "plan (g)");
            Assert.AreEqual(0, t.TryGetValue("specialised DMagic", out var dm) ? dm : 0, "plan (g)");
            Assert.AreEqual(247, t.TryGetValue("ModuleTestSubject", out var ts) ? ts : 0, "plan (g)");
        }

        [Test] public void GameData_EveryDefinitionBuilds_EveryPartResolves()
        {
            var root = InstalledKsp1.GameDataRoot;
            Assume.That(Directory.Exists(root), $"KSP1 GameData not found at {root} (set KSP1_GAMEDATA)");
            var c = Run(root, InstalledKsp1.GameDataCfg());
            Print("I:/ksp1 GameData", c);
            CollectionAssert.IsEmpty(c.Failures);
            Assert.AreEqual(106, c.Total.Definitions, "plan (g): 106 EXPERIMENT_DEFINITIONs");
            Assert.AreEqual(364, c.Total.Parts, "plan (g): 364 parts with a mapped experiment module");
            Assert.AreEqual(141, c.Total.Containers, "plan (g): 141 ModuleScienceContainer parts");
        }
    }
}
