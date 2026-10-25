using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>The planner on every plain <c>PART</c> node under the game's <c>mods/KSP1</c> (stock, KRE, BDB…).
    /// Limit: the cfgs are read before ModuleManager patches, so modules added or removed by patches are not
    /// seen here.</summary>
    public class RealPartsPlanTests
    {
        private sealed class RealPart
        {
            public string Mod, File, Name;
            public IReadOnlyList<IKsp1Node> Modules;
            public override string ToString() => $"{Mod}/{Name} ({File})";
        }

        private static List<RealPart> _parts;

        private static IEnumerable<RealPart> AllParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<RealPart>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
                    parts.Add(new RealPart
                    {
                        Mod = InstalledKsp1.TopFolder(path),
                        File = path.Substring(InstalledKsp1.Root.TrimEnd('\\', '/').Length + 1),
                        Name = part.GetValue("name"),
                        Modules = part.GetNodes("MODULE").ToList(),
                    });
            }
            Assume.That(parts.Count, Is.GreaterThan(0), "no PART node found");
            return _parts = parts;
        }

        // Redux 26w42c's source rule, copied on purpose (the planner has its own): the four module names, a
        // non-blank animationName or else animationStateName, in cfg order.
        private static readonly string[] SourceModules =
            { "ModuleAnimateGeneric", "ModuleDeployableSolarPanel", "ModuleWheelDeployment", "ModuleLandingLeg" };

        private static IEnumerable<string> ReduxSourceNames(RealPart p) =>
            p.Modules.Where(m => SourceModules.Any(s => s.Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase)))
                .Select(m => new[] { m.GetValue("animationName"), m.GetValue("animationStateName") }
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)))
                .Where(n => n != null);

        // Redux's solar importer always succeeds, so a ModuleDeployableSolarPanel means a native Module_SolarPanel.
        private static bool HasNativeSolar(RealPart p) =>
            p.Modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));

        private static PartAnimationPlan PlanOf(RealPart p, bool own = false) =>
            OwnershipPlanner.Plan(p.Modules, HasNativeSolar(p), new HashSet<string>(), own);

        private static IEnumerable<string> ClipsOf(AnimEntryDef e) =>
            new[] { e.Clip, e.SecondClip, e.ThirdClip }.Where(c => !string.IsNullOrEmpty(c));

        [TestCase(false)] [TestCase(true)]
        public void Plan_EveryRemovedReduxNameHasOurEntry_AllRealParts(bool own)
        {
            var bad = AllParts().Where(p => ReduxSourceNames(p)
                    .Except(PlanOf(p, own).ReduxClips, StringComparer.OrdinalIgnoreCase)
                    .Except(PlanOf(p, own).Entries.Select(e => e.Clip), StringComparer.OrdinalIgnoreCase).Any())
                .Select(p => p.ToString()).ToList();
            CollectionAssert.IsEmpty(bad);
        }

        [TestCase(false)] [TestCase(true)]
        public void Plan_NoEntryNamesAReduxClip_AllRealParts(bool own)
        {
            var bad = AllParts().Where(p => PlanOf(p, own).Entries
                    .Where(e => e.Kind != EntryKind.GearBridge && e.Kind != EntryKind.AeroGate)
                    .SelectMany(ClipsOf).Intersect(PlanOf(p, own).ReduxClips, StringComparer.OrdinalIgnoreCase).Any())
                .Select(p => p.ToString()).ToList();
            CollectionAssert.IsEmpty(bad);
        }

        [Test] public void Plan_EntryKeysUniquePerPart_AllRealParts()
        {
            var bad = AllParts().Where(p => PlanOf(p).Entries.GroupBy(e => e.Key).Any(g => g.Count() > 1))
                .Select(p => p.ToString()).ToList();
            CollectionAssert.IsEmpty(bad);
        }

        [Test] public void Plan_RealParts_CountsPerKind() => TestContext.WriteLine(CountsPerKindAndMod());

        private static string CountsPerKindAndMod()
        {
            var kinds = (EntryKind[])Enum.GetValues(typeof(EntryKind));
            var sb = new StringBuilder();
            sb.AppendLine("| Mod | Parts | With entries | Left to Redux only | " + string.Join(" | ", kinds) + " | Notes |");
            sb.AppendLine("|---|---|---|---|" + string.Concat(kinds.Select(_ => "---|")) + "---|");
            foreach (var mod in AllParts().GroupBy(p => p.Mod, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
            {
                var plans = mod.Select(p => PlanOf(p)).ToList();
                var counts = kinds.Select(k => plans.Sum(pl => pl.Entries.Count(e => e.Kind == k)));
                sb.AppendLine($"| {mod.Key} | {plans.Count} | {plans.Count(pl => pl.Entries.Count > 0)} | " +
                              $"{plans.Count(pl => pl.Entries.Count == 0 && pl.ReduxClips.Count > 0)} | " +
                              string.Join(" | ", counts) + $" | {plans.Sum(pl => pl.Notes.Count)} |");
            }
            sb.AppendLine("Notes:");
            foreach (var g in AllParts().SelectMany(p => PlanOf(p).Notes.Select(n => (p.Mod, Note: NoteShape(n))))
                         .GroupBy(x => x).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Count()} x [{g.Key.Mod}] {g.Key.Note}");
            return sb.ToString();
        }

        /// <summary>A note without its module index and quoted names, to group them.</summary>
        private static string NoteShape(string note) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(note, @"^MODULE\[\d+\] ", ""), "'[^']*'", "'…'");
    }
}
