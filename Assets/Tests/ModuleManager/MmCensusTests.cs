using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.ModuleManager;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ModuleManager
{
    /// <summary>Real-file census of the ModuleManager rules: the installed set (<c>mods/KSP1</c>) and, optionally, the
    /// user's whole KSP1 GameData. Read only; the counts are printed for the plan and the CHANGELOG.</summary>
    public class MmCensusTests
    {
        /// <summary>Redux's own aliases, always available (plan (a)).</summary>
        private static readonly string[] ReduxAliases = { "B9PartSwitch", "CommunityResourcePack", "Waterfall" };

        private static readonly string[] KeyFields = { "animationName", "animationStateName", "moduleID", "shaderProperty", "name" };

        private sealed class CfgFile
        {
            public string Path;
            public string Mod;
            public IKsp1Node Root;
        }

        private static List<CfgFile> Load(string root, string[] files) =>
            files.Select(f => new CfgFile
            {
                Path = f,
                Mod = InstalledKsp1.TopFolderUnder(root, f),
                Root = CfgText.Parse(File.ReadAllText(f)),
            }).ToList();

        private static List<CfgFile> _installed;
        private static List<CfgFile> Installed() =>
            _installed ?? (_installed = Load(InstalledKsp1.Root, InstalledKsp1.AllCfg()));

        private static IEnumerable<IKsp1Node> Descendants(IKsp1Node node)
        {
            foreach (var child in node.AllNodes)
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private static bool IsCommand(string name) =>
            !string.IsNullOrEmpty(name) && "@%+!-&".IndexOf(name[0]) >= 0;

        private static void Bump<T>(Dictionary<T, int> counts, T key) =>
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;

        /// <summary>A count pinned to its 2026-10-28 value, ±10%, so drift in the data or the parser shows up.</summary>
        private static void AssertNear(int expected, int actual, string what)
        {
            var tolerance = Math.Max(1, (int)Math.Ceiling(expected * 0.1));
            Assert.That(actual, NUnit.Framework.Is.InRange(expected - tolerance, expected + tolerance),
                $"{what}: {actual}, pinned at {expected} ±{tolerance} (2026-10-28 census); update the pin if the files changed");
        }

        private static string Format<T>(Dictionary<T, int> counts) =>
            counts.Count == 0 ? "none" : string.Join(", ", counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key.ToString())
                .Select(p => $"{p.Key} {p.Value}"));

        // ---- 1. every operator or index line of the installed set parses ----

        [Test] public void Census_Installed_EveryOperatorAndIndexLineParses()
        {
            var files = Installed();
            var operators = new Dictionary<MmOperator, int>();
            var valueIndex = new Dictionary<MmIndexKind, int>();
            var nodeIndex = new Dictionary<MmIndexKind, int>();
            int ours = 0, countedOnly = 0, vector = 0, vectorWithOperator = 0, plain = 0;
            var failures = new List<string>();
            foreach (var file in files)
            foreach (var node in new[] { file.Root }.Concat(Descendants(file.Root)))
            {
                if (node != file.Root && IsCommand(node.Name) && MmNodeIndex.Split(MmPluginRule.WithoutNeeds(node.Name), out _, out var ni))
                    Bump(nodeIndex, ni.Kind);
                foreach (var v in node.AllValues)
                {
                    var name = MmPluginRule.WithoutNeeds(v.Key).Trim();
                    if (!MmKeyEdit.MightNeedUs(name)) continue;
                    var e = MmKeyEdit.Parse(name, v.Value);
                    if (e.IsOurs || e.CountedOnly)
                    {
                        if (e.IsOurs) ours++; else countedOnly++;
                        if (e.Operator != MmOperator.None) Bump(operators, e.Operator);
                        if (e.Index.Kind != MmIndexKind.None) Bump(valueIndex, e.Index.Kind);
                    }
                    else if (e.PassedBack && e.HasBrackets)
                    {
                        vector++;
                        if (e.SkippedVectorEdit) vectorWithOperator++;
                    }
                    else if (e.PassedBack) failures.Add($"{file.Path}: '{v.Key} = {v.Value}'");
                    else plain++;
                }
            }
            var supported = operators.Where(p => p.Key != MmOperator.Pow && p.Key != MmOperator.Regex).Sum(p => p.Value);
            var unsupported = operators.Where(p => p.Key == MmOperator.Pow || p.Key == MmOperator.Regex).Sum(p => p.Value);
            TestContext.WriteLine($"mm census (installed, {files.Count} cfg): value lines ours {ours}, counted only {countedOnly}, " +
                                  $"vector component {vector} ({vectorWithOperator} with an operator, skipped), plain {plain}");
            TestContext.WriteLine($"  operators: supported {supported}, counted only {unsupported}: {Format(operators)}");
            TestContext.WriteLine($"  value indices {valueIndex.Values.Sum()}: {Format(valueIndex)}");
            TestContext.WriteLine($"  node indices {nodeIndex.Values.Sum()}: {Format(nodeIndex)}");
            TestContext.WriteLine($"  indices total {valueIndex.Values.Sum() + nodeIndex.Values.Sum()}");
            Assert.IsEmpty(failures, "lines passed back:\n" + string.Join("\n", failures.Take(30)));
            AssertNear(475, supported, "supported operators");
            AssertNear(141, unsupported, "counted-only operators");
            AssertNear(40, valueIndex.Values.Sum(), "value indices");
            AssertNear(310, nodeIndex.Values.Sum(), "node indices");
            AssertNear(8, vectorWithOperator, "vector component edits skipped");
        }

        // ---- 2. the plugin rule over the user's whole GameData ----

        [Test] public void Census_GameData_BlockedFoldersAndWhatTheyChange()
        {
            var root = InstalledKsp1.GameDataRoot;
            var cfg = InstalledKsp1.GameDataCfg();
            var files = Load(root, cfg);
            var folders = Directory.GetDirectories(root).Select(Path.GetFileName).ToList();
            var withPart = new HashSet<string>(files
                .Where(f => f.Root.AllNodes.Any(n => n.Name.Equals("PART", StringComparison.OrdinalIgnoreCase)))
                .Select(f => f.Mod), StringComparer.OrdinalIgnoreCase);
            var auto = folders.Where(f => MmPluginRule.AutoFolder(
                    Directory.EnumerateFiles(Path.Combine(root, f), "*.dll", SearchOption.AllDirectories).Any(),
                    withPart.Contains(f)))
                .ToList();
            var curated = MmPluginRule.DefaultCurated.ToArray();
            var keep = MmPluginRule.DefaultKeep.ToArray();
            Func<string, bool> blocked = n => MmPluginRule.Blocked(n, curated, keep, auto);
            var curatedPresent = curated.Where(c => folders.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var autoBlocked = auto.Where(blocked).Where(a => !curated.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            var autoKept = auto.Where(a => !blocked(a)).ToList();

            var available = new HashSet<string>(folders.Concat(ReduxAliases), StringComparer.OrdinalIgnoreCase);
            var rootNames = files.SelectMany(f => f.Root.AllNodes.Select(n => n.Name)).ToList();
            var forNames = MmPluginRule.ForNames(rootNames);

            var needsTerms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var drops = new Dictionary<string, Dictionary<MmDropReason, int>>(StringComparer.OrdinalIgnoreCase);
            var absent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                foreach (var node in Descendants(file.Root))
                {
                    foreach (var name in new[] { node.Name }.Concat(node.AllValues.Select(v => v.Key)))
                    foreach (var term in MmPluginRule.NeedsTerms(name))
                    {
                        var mod = term.TrimStart('!').Trim();
                        if (blocked(mod) && available.Contains(MmPluginRule.FirstSegment(mod))) Bump(needsTerms, MmPluginRule.FirstSegment(mod));
                    }
                }
                foreach (var node in file.Root.AllNodes)
                {
                    var d = MmPluginRule.RootPatchDrop(node.Name, file.Mod, IsCommand(node.Name), blocked, available.Contains, forNames, true);
                    if (d.Reason == MmDropReason.Keep) continue;
                    if (d.Reason == MmDropReason.DropBeforeAfterAbsent) { Bump(absent, d.Mod); continue; }
                    if (!drops.TryGetValue(d.Mod, out var perReason)) drops[d.Mod] = perReason = new Dictionary<MmDropReason, int>();
                    Bump(perReason, d.Reason);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"mm census (GameData {root}, {files.Count} cfg, {folders.Count} folders):");
            sb.AppendLine($"  curated present ({curatedPresent.Count}): {string.Join(", ", curatedPresent)}");
            sb.AppendLine($"  automatic, blocked ({autoBlocked.Count}): {string.Join(", ", autoBlocked)}");
            sb.AppendLine($"  automatic, kept by mm_needs_keep ({autoKept.Count}): {string.Join(", ", autoKept)}");
            foreach (var name in curatedPresent.Concat(autoBlocked).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                needsTerms.TryGetValue(name, out var t);
                drops.TryGetValue(name, out var perReason);
                perReason = perReason ?? new Dictionary<MmDropReason, int>();
                int Get(MmDropReason r) => perReason.TryGetValue(r, out var n) ? n : 0;
                if (t == 0 && perReason.Count == 0) continue;
                sb.AppendLine($"  {name}: {t} NEEDS term(s), {perReason.Values.Sum()} patch(es) dropped (" +
                              $"{Get(MmDropReason.DropForBlocked)} FOR, {Get(MmDropReason.DropBeforeAfterBlocked)} BEFORE/AFTER, " +
                              $"{Get(MmDropReason.DropBlockedFolder)} from its folder)");
            }
            sb.AppendLine($"  BEFORE/AFTER of an absent mod: {absent.Values.Sum()}: {Format(absent)}");
            TestContext.WriteLine(sb.ToString());
            CollectionAssert.Contains(curatedPresent, "RealFuels");
            CollectionAssert.DoesNotContain(autoBlocked, "B9PartSwitch");
            AssertNear(102, autoBlocked.Count, "folders blocked automatically");
        }

        // ---- 3. the BEFORE/AFTER rule over the installed set ----

        [Test] public void Census_Installed_BeforeAfterRule_NeverDropsTheForNames()
        {
            var files = Installed();
            var root = InstalledKsp1.Root;
            var folders = Directory.GetDirectories(root).Select(Path.GetFileName);
            var available = new HashSet<string>(folders.Concat(ReduxAliases), StringComparer.OrdinalIgnoreCase);
            var forNames = MmPluginRule.ForNames(files.SelectMany(f => f.Root.AllNodes.Select(n => n.Name)));
            var curated = MmPluginRule.DefaultCurated.ToArray();
            var keep = MmPluginRule.DefaultKeep.ToArray();
            Func<string, bool> blocked = n => MmPluginRule.Blocked(n, curated, keep, new string[0]);
            var byMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byReason = new Dictionary<MmDropReason, int>();
            var patches = 0;
            foreach (var file in files)
            foreach (var node in file.Root.AllNodes)
            {
                if (!IsCommand(node.Name)) continue;
                patches++;
                var d = MmPluginRule.RootPatchDrop(node.Name, file.Mod, true, blocked, available.Contains, forNames, true);
                if (d.Reason == MmDropReason.Keep) continue;
                Bump(byReason, d.Reason);
                Bump(byMod, d.Mod);
            }
            TestContext.WriteLine($"mm census (installed): {patches} root patch(es), FOR names {forNames.Count} " +
                                  $"({string.Join(", ", forNames.OrderBy(n => n))}); dropped by reason: {Format(byReason)}; by mod: {Format(byMod)}");
            Assert.IsTrue(forNames.Contains("zzBluedog_DB"));
            Assert.IsFalse(byMod.ContainsKey("zzBluedog_DB"), "zzBluedog_DB is a FOR name and must never be dropped");
            Assert.IsFalse(byMod.ContainsKey("Bluedog_DB"));
            AssertNear(277, byReason.Values.Sum(), "patches dropped by the BEFORE/AFTER rule");
        }

        // ---- 4. key fields touched by index or arithmetic edits (plan (g)) ----

        [Test] public void Census_Installed_KeyFieldsTouchedByIndexOrArithmetic()
        {
            var files = Installed();
            var hits = new List<string>();
            var indexedModuleDeletes = new List<string>();
            foreach (var file in files)
            foreach (var node in Descendants(file.Root))
            {
                var nodeName = MmPluginRule.WithoutNeeds(node.Name);
                var indexed = IsCommand(nodeName) && MmNodeIndex.Split(nodeName, out var bare, out var index)
                              && index.Kind != MmIndexKind.All
                              && bare.Substring(1).TrimStart().StartsWith("MODULE", StringComparison.OrdinalIgnoreCase);
                if (indexed && nodeName[0] == '!' || indexed && nodeName[0] == '-') indexedModuleDeletes.Add($"{file.Path}: {node.Name}");
                foreach (var v in node.AllValues)
                {
                    var e = MmKeyEdit.Parse(MmPluginRule.WithoutNeeds(v.Key), v.Value);
                    if (!KeyFields.Contains(e.Key, StringComparer.OrdinalIgnoreCase)) continue;
                    if (e.IsOurs || indexed && e.Command != MmCommand.None)
                        hits.Add($"{file.Path}: {node.Name} {{ {v.Key} = {v.Value} }}");
                }
            }
            TestContext.WriteLine($"mm census (installed): key fields edited by an index or arithmetic: {hits.Count}; " +
                                  $"indexed MODULE deletes (one module of several): {indexedModuleDeletes.Count}");
            foreach (var h in hits.Concat(indexedModuleDeletes).Take(30)) TestContext.WriteLine("  " + h);
            Assert.AreEqual(0, hits.Count, "key fields edited by an index or arithmetic: write a state reset in the CHANGELOG");
        }
    }
}
