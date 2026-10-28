using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.B9;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    /// <summary>Real-file census of the B9 override rules, on the raw cfgs (before ModuleManager): the installed set
    /// (<c>mods/KSP1</c>) and, optionally, the user's whole KSP1 GameData. Read only; the counts are printed for the plan
    /// and the CHANGELOG. In game the rules run on Redux's finalized nodes, so the numbers there differ.</summary>
    public class B9CensusTests
    {
        private const string B9Name = "ModuleB9PartSwitch";

        private sealed class Part
        {
            public string File;
            public string Name;
            public bool IsPatch;
            public double? Mass;
            public List<IKsp1Node> Modules;
        }

        private sealed class Data
        {
            public int Files;
            public List<IKsp1Node> Roots = new List<IKsp1Node>();
            public List<Part> Parts = new List<Part>();
            public int PatchEdits;
        }

        private static Data _installed;
        private static Data Installed() => _installed ?? (_installed = Load(InstalledKsp1.AllCfg()));

        private static Data Load(string[] files)
        {
            var data = new Data { Files = files.Length };
            foreach (var f in files)
            {
                IKsp1Node root;
                try { root = CfgText.Parse(File.ReadAllText(f)); }
                catch (IOException) { continue; }
                foreach (var node in root.AllNodes)
                {
                    data.Roots.Add(node);
                    var name = node.Name ?? "";
                    var isPart = name == "PART" || name.StartsWith("PART:", StringComparison.Ordinal);
                    var isPatch = name.StartsWith("+PART", StringComparison.Ordinal) || name.StartsWith("@PART", StringComparison.Ordinal);
                    if (isPart || isPatch)
                        data.Parts.Add(new Part
                        {
                            File = f, Name = node.GetValue("name") ?? name, IsPatch = isPatch,
                            Mass = Number(node.GetValue("mass")), Modules = node.GetNodes("MODULE").ToList(),
                        });
                }
                data.PatchEdits += Descendants(root).Count(n => n.Name != null
                    && (n.Name.StartsWith("@MODULE[" + B9Name, StringComparison.OrdinalIgnoreCase)
                        || n.Name.StartsWith("%MODULE[" + B9Name, StringComparison.OrdinalIgnoreCase)));
            }
            return data;
        }

        private static IEnumerable<IKsp1Node> Descendants(IKsp1Node node)
        {
            foreach (var child in node.AllNodes)
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private static double? Number(string raw) =>
            raw != null && double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

        private static bool IsB9(IKsp1Node m) => B9Name.Equals(m?.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static void Bump(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

        private static string Format(Dictionary<string, int> counts) =>
            counts.Count == 0 ? "none" : string.Join(", ", counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key} {p.Value}"));

        private static string Inv(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static readonly string[] SubtypeFields =
        {
            "addedMass", "addedCost", "tankType", "volumeAdded", "volumeMultiplier", "volumeAddedToParent", "percentFilled",
            "maxTemp", "skinMaxTemp", "crashTolerance", "CoMOffset", "CoLOffset", "CoPOffset", "defaultSubtypePriority",
            "transform", "node", "upgradeRequired",
        };

        // ---- (c) and (d): raw counts ----

        private static void PrintRawCounts(string label, Data data)
        {
            var roots = data.Parts.Where(p => !p.IsPatch).ToList();
            var patches = data.Parts.Where(p => p.IsPatch).ToList();
            int InParts(IEnumerable<Part> ps) => ps.Sum(p => p.Modules.Count(IsB9));
            var withB9 = roots.Count(p => p.Modules.Any(IsB9));
            var withTwo = roots.Count(p => p.Modules.Count(IsB9) >= 2);
            int built = 0, notBuilt = 0, notBuiltMass = 0, notBuiltModule = 0, notBuiltTank = 0;
            var fields = new Dictionary<string, int>();
            var nodes = new Dictionary<string, int>();
            var targets = new Dictionary<string, int>();
            var dataKeys = new Dictionary<string, int>();
            var inactive = new Dictionary<string, int>();
            int baseVolume = 0, baseVolumeZero = 0, baseVolumeVariable = 0, parentId = 0;
            foreach (var part in data.Parts)
            {
                var switches = VariantSwitches.All(part.Modules).Where(s => !s.IsStock).ToList();
                foreach (var sw in switches)
                {
                    var m = sw.Module;
                    if (m.GetValue("baseVolume") != null)
                    {
                        baseVolume++;
                        var bv = Number(m.GetValue("baseVolume"));
                        if (bv == 0) baseVolumeZero++;
                        if (bv == null) baseVolumeVariable++;
                    }
                    if (!string.IsNullOrWhiteSpace(m.GetValue("parentID"))) parentId++;
                    if (sw.SetId != null) built++;
                    else
                    {
                        notBuilt++;
                        if (sw.Subtypes.Any(st => st.GetValue("addedMass") != null)) notBuiltMass++;
                        if (sw.Subtypes.Any(st => st.GetNodes("MODULE").Any())) notBuiltModule++;
                        if (sw.Subtypes.Any(st => st.GetValue("tankType") != null || st.GetNodes("RESOURCE").Any())) notBuiltTank++;
                    }
                    foreach (var st in sw.Subtypes)
                    {
                        foreach (var f in SubtypeFields)
                            foreach (var _ in st.GetValues(f)) Bump(fields, f);
                        foreach (var n in new[] { "RESOURCE", "MODULE", "NODE", "TRANSFORM", "TEXTURE" })
                            foreach (var _ in st.GetNodes(n)) Bump(nodes, n);
                        foreach (var o in st.GetNodes("MODULE"))
                        {
                            var target = o.GetNodes("IDENTIFIER").FirstOrDefault()?.GetValue("name")?.Trim() ?? "(no IDENTIFIER)";
                            Bump(targets, target);
                            foreach (var d in o.GetNodes("DATA"))
                            {
                                foreach (var v in d.AllValues) Bump(dataKeys, target + "." + v.Key);
                                foreach (var c in d.AllNodes) Bump(dataKeys, target + "." + c.Name + "{}");
                            }
                            var active = o.GetValue("moduleActive")?.Trim();
                            if (active != null && (active.Equals("false", StringComparison.OrdinalIgnoreCase) || active == "0"))
                                Bump(inactive, target);
                        }
                    }
                }
            }
            var tankDefs = data.Roots.Where(r => r.Name == "B9_TANK_TYPE").ToList();
            TestContext.WriteLine($"b9 census ({label}, {data.Files} cfg, raw files before MM):");
            TestContext.WriteLine($"  B9 modules: in PART roots {InParts(roots)}, added by +PART/@PART {InParts(patches)}, patch edits of B9 modules {data.PatchEdits}");
            TestContext.WriteLine($"  PARTs with a B9 module {withB9}, with two or more {withTwo}");
            TestContext.WriteLine($"  B9 modules Redux builds {built}; not built {notBuilt}: with addedMass {notBuiltMass}, with MODULE {notBuiltModule}, with a tank {notBuiltTank}");
            TestContext.WriteLine($"  module baseVolume {baseVolume} ({baseVolumeZero} are 0, {baseVolumeVariable} not a number), parentID {parentId}");
            TestContext.WriteLine($"  subtype values: {Format(fields)}");
            TestContext.WriteLine($"  subtype nodes: {Format(nodes)}");
            TestContext.WriteLine($"  B9_TANK_TYPE roots {tankDefs.Count}, with tankMass {tankDefs.Count(t => t.GetValue("tankMass") != null)}");
            TestContext.WriteLine($"  IDENTIFIER targets: {Format(targets)}");
            TestContext.WriteLine($"  DATA keys: {Format(dataKeys)}");
            TestContext.WriteLine($"  moduleActive = false: {Format(inactive)}");
        }

        // ---- planning every B9 module ----

        private sealed class PlanCensus
        {
            public int Parts, Switches, Exceptions;
            public List<string> ExceptionTexts = new List<string>();
            public int AppendedParts, GainParts, TwoTankParts, PairParts, Pairs, UnparseableParts;
            public List<(double Delta, string Where, bool Tank)> Deltas = new List<(double, string, bool)>();
            public List<string> Insane = new List<string>();
            public Dictionary<string, int> Edits = new Dictionary<string, int>();
            public Dictionary<string, int> OffForms = new Dictionary<string, int>();
            public Dictionary<string, int> NoOffForm = new Dictionary<string, int>();
            public Dictionary<string, int> Notes = new Dictionary<string, int>();
            /// <summary>"IDENTIFIER not unique" notes by (0 matches | more than 1) and (part | patch).</summary>
            public Dictionary<string, int> IdentifierSplit = new Dictionary<string, int>();
            public int LfoxSubtypes, LfoxResolved;
            public double CostMin = double.MaxValue, CostMax = double.MinValue;
        }

        private static PlanCensus PlanAll(Data data, B9TankTypes types)
        {
            var c = new PlanCensus();
            foreach (var part in data.Parts.Where(p => p.Modules.Any(IsB9)))
            {
                c.Parts++;
                B9OverridePlan plan;
                try { plan = B9OverridePlan.For(part.Modules, types, B9Switches.AllOn); }
                catch (Exception e)
                {
                    c.Exceptions++;
                    if (c.ExceptionTexts.Count < 5) c.ExceptionTexts.Add($"{part.Name} ({Path.GetFileName(part.File)}): {e}");
                    continue;
                }
                c.Switches += plan.Switches.Count;
                if (plan.Switches.Any(s => s.Appended)) c.AppendedParts++;
                if (plan.GainsPartSwitch) c.GainParts++;
                if (plan.HasSecondTankSwitch) c.TwoTankParts++;
                if (plan.ParentChildPairs > 0) { c.PairParts++; c.Pairs += plan.ParentChildPairs; }
                if (plan.HasUnparseableVolume) c.UnparseableParts++;
                foreach (var n in plan.Notes)
                {
                    var kind = NoteKind(n);
                    Bump(c.Notes, kind);
                    if (kind == "IDENTIFIER not unique") Bump(c.IdentifierSplit, IdentifierKind(n, part.IsPatch));
                }
                foreach (var sw in plan.Switches)
                    for (var i = 0; i < sw.Subtypes.Count; i++)
                    {
                        var st = sw.Subtypes[i];
                        var where = $"{part.Name}/{sw.SetId}/{st.VariantId}";
                        var tank = st.Resources != null;
                        if (st.MassDelta != 0) c.Deltas.Add((st.MassDelta, where, tank));
                        if (!tank && part.Mass.HasValue && part.Mass.Value > 0 && Math.Abs(st.MassDelta) > part.Mass.Value * 20)
                            c.Insane.Add($"{where} {Inv(st.MassDelta)} t (part mass {Inv(part.Mass.Value)})");
                        c.CostMin = Math.Min(c.CostMin, st.Cost);
                        c.CostMax = Math.Max(c.CostMax, st.Cost);
                        foreach (var e in st.ModuleEdits)
                        {
                            foreach (var k in e.DataKeys) Bump(c.Edits, e.TargetModule + "." + k);
                            if (e.Off) Bump(e.NoOffForm ? c.NoOffForm : c.OffForms, e.TargetModule);
                        }
                        if (string.Equals(sw.Module.GetNodes("SUBTYPE").ElementAt(i).GetValue("tankType")?.Trim(), "bdbLFOX", StringComparison.Ordinal))
                        {
                            c.LfoxSubtypes++;
                            if (sw.IsTank && st.Resources != null && st.Resources.Any(r => r.Name == "LiquidFuel")) c.LfoxResolved++;
                        }
                    }
            }
            return c;
        }

        private static string NoteKind(string note)
        {
            if (note.StartsWith("second tank switch", StringComparison.Ordinal)) return "second tank switch";
            if (note.StartsWith("volume of", StringComparison.Ordinal) && note.Contains("default subtype")) return "child volume at default";
            if (note.StartsWith("volume of", StringComparison.Ordinal)) return "unparseable volume";
            if (note.StartsWith("percentFilled", StringComparison.Ordinal)) return "percentFilled <= 1";
            if (note.StartsWith("unknown tank type", StringComparison.Ordinal)) return "unknown tank type";
            if (note.StartsWith("tank type", StringComparison.Ordinal)) return "tank type without tankMass";
            if (note.StartsWith("upgradeRequired", StringComparison.Ordinal)) return "upgradeRequired ignored";
            if (note.StartsWith("node/TRANSFORM", StringComparison.Ordinal)) return "node/TRANSFORM not applied";
            if (note.Contains("IDENTIFIER")) return "IDENTIFIER not unique";
            if (note.Contains("cannot be read")) return "unparseable value";
            if (note.StartsWith("resource", StringComparison.Ordinal)) return "resource without size";
            return "other";
        }

        private static readonly System.Text.RegularExpressions.Regex MatchCount =
            new System.Text.RegularExpressions.Regex(@"matches (\d+) module\(s\)");

        /// <summary>"0 matches" or "more than 1 match", then "part" or "patch".</summary>
        private static string IdentifierKind(string note, bool isPatch)
        {
            var m = MatchCount.Match(note);
            var count = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            var matches = count == 0 ? "0 matches" : count > 1 ? "more than 1 match" : "count unread";
            return matches + " in " + (isPatch ? "patch" : "part");
        }

        private static void PrintPlan(string label, PlanCensus c)
        {
            var deltas = c.Deltas.Select(d => d.Delta).OrderBy(d => d).ToList();
            var median = deltas.Count == 0 ? 0 : deltas.Count % 2 == 1 ? deltas[deltas.Count / 2] : (deltas[deltas.Count / 2 - 1] + deltas[deltas.Count / 2]) / 2;
            TestContext.WriteLine($"b9 plan ({label}): {c.Parts} part/patch node(s) with a B9 module, {c.Switches} switch(es) planned, {c.Exceptions} exception(s)");
            foreach (var e in c.ExceptionTexts) TestContext.WriteLine("  exception: " + e);
            TestContext.WriteLine($"  parts with an appended set {c.AppendedParts}, gaining a Module_PartSwitch {c.GainParts}, two tank switches {c.TwoTankParts}, " +
                                  $"parent/child volume pairs {c.Pairs} on {c.PairParts} part(s), unparseable volumes {c.UnparseableParts}");
            TestContext.WriteLine($"  mass deltas ({deltas.Count} non-zero): min {(deltas.Count == 0 ? "-" : Inv(deltas.First()))}, " +
                                  $"median {Inv(median)}, max {(deltas.Count == 0 ? "-" : Inv(deltas.Last()))}");
            foreach (var d in c.Deltas.OrderByDescending(d => Math.Abs(d.Delta)).Take(10))
                TestContext.WriteLine($"    {Inv(d.Delta)} t {d.Where}{(d.Tank ? " (tank)" : "")}");
            TestContext.WriteLine($"  sanity (no-tank delta above 20 x part mass): {(c.Insane.Count == 0 ? "none" : string.Join("; ", c.Insane))}");
            TestContext.WriteLine($"  cost per subtype: {(c.CostMin == double.MaxValue ? "-" : Inv(c.CostMin) + ".." + Inv(c.CostMax))} (logged only)");
            TestContext.WriteLine($"  module edits by (KSP1 module, DATA key): {Format(c.Edits)}");
            TestContext.WriteLine($"  moduleActive = false: off form {Format(c.OffForms)}; no off form {Format(c.NoOffForm)}");
            TestContext.WriteLine($"  notes: {Format(c.Notes)}");
            TestContext.WriteLine($"  IDENTIFIER not unique, split: {Format(c.IdentifierSplit)}");
            TestContext.WriteLine($"  bdbLFOX subtypes {c.LfoxSubtypes}, resolved on the tank switch {c.LfoxResolved}");
        }

        private static B9TankTypes TankTypes(Data data) => B9TankTypes.From(data.Roots);

        [Test] public void Census_Installed_RawCounts() => PrintRawCounts("installed", Installed());

        [Test] public void Census_Installed_EveryB9ModulePlans()
        {
            var data = Installed();
            var types = TankTypes(data);
            TestContext.WriteLine($"tank types: {types.Count} (duplicates: {(types.Duplicates.Count == 0 ? "none" : string.Join(", ", types.Duplicates))})");
            var c = PlanAll(data, types);
            PrintPlan("installed", c);
            Assert.AreEqual(0, c.Exceptions, string.Join("\n", c.ExceptionTexts));
            Assert.Greater(c.Parts, 0, "B9 modules found");
            Assert.IsEmpty(c.Insane, "no delta above 20 x the part's mass without a tank");
            Assert.IsNotNull(types.Find("bdbLFOX"), "BDB's LFOX tank type is read");
            Assert.Greater(c.LfoxSubtypes, 0);
            Assert.AreEqual(c.LfoxSubtypes, c.LfoxResolved, "every bdbLFOX subtype resolves its resources on the tank switch");
        }

        [Test] public void Census_Installed_BdbLfoxResolves()
        {
            var data = Installed();
            var types = TankTypes(data);
            foreach (var part in data.Parts.Where(p => p.Modules.Any(IsB9)))
            {
                var plan = B9OverridePlan.For(part.Modules, types, B9Switches.AllOn);
                CollectionAssert.DoesNotContain(plan.Notes, B9Lines.Notes.UnknownTankType("bdbLFOX"), part.Name);
            }
        }

        [Test] public void Census_GameData_RawCountsAndPlans()
        {
            var data = Load(InstalledKsp1.GameDataCfg());
            PrintRawCounts("I:/ksp1", data);
            var types = TankTypes(data);
            TestContext.WriteLine($"tank types: {types.Count} ({types.Duplicates.Count} duplicate(s))");
            var c = PlanAll(data, types);
            PrintPlan("I:/ksp1", c);
            Assert.AreEqual(0, c.Exceptions, string.Join("\n", c.ExceptionTexts));
        }
    }
}
