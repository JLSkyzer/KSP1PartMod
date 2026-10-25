using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP1PartMod.Placement;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Placement
{
    /// <summary>Checks the shipped tech_mapping.json against the KSP2 tree snapshot and the mod's CSV.</summary>
    public class TechMappingTests
    {
        private static readonly string ModDir = Path.Combine(Application.dataPath, "KSP1PartMod", "Copied");
        private static readonly string DataDir = Path.Combine(Application.dataPath, "Tests", "Placement", "Data");

        private static TechMapping _shipped;
        private static TechMapping Shipped =>
            _shipped ?? (_shipped = TechMapping.Parse(File.ReadAllText(Path.Combine(ModDir, "tech_mapping.json"))));

        private static HashSet<string> Snapshot =>
            new HashSet<string>(File.ReadAllLines(Path.Combine(DataDir, "ksp2_tech_nodes_2026-10-21.txt"))
                .Select(l => l.Trim()).Where(l => l.Length > 0));

        private static LocalizationCsv Csv =>
            new LocalizationCsv(File.ReadAllText(Path.Combine(ModDir, "localizations", "ksp1partmod.csv")));

        /// <summary>TechRequired values of the KSP1 parts shipped with Redux (mods/KSP1, 2026-10-25).</summary>
        private static readonly string[] StockIdsInUse =
        {
            "Unresearcheable", "actuators", "advAerodynamics", "advConstruction", "advElectrics", "advExploration",
            "advFlightControl", "advFuelSystems", "advLanding", "advMetalworks", "advRocketry", "advScienceTech",
            "advUnmanned", "advancedMotors", "aerodynamicSystems", "aerospaceTech", "automation", "aviation",
            "basicRocketry", "basicScience", "commandModules", "composites", "electrics", "electronics",
            "engineering101", "experimentalAerodynamics", "experimentalElectrics", "experimentalScience",
            "fieldScience", "flightControl", "fuelSystems", "generalConstruction", "generalRocketry",
            "heavierRocketry", "heavyAerodynamics", "heavyLanding", "heavyRocketry", "highAltitudeFlight",
            "highPerformanceFuelSystems", "hypersonicFlight", "ionPropulsion", "landing", "largeElectrics",
            "largeUnmanned", "largeVolumeContainment", "metaMaterials", "miniaturization", "nuclearPropulsion",
            "precisionEngineering", "precisionPropulsion", "propulsionSystems", "scienceTech", "spaceExploration",
            "specializedConstruction", "specializedControl", "specializedElectrics", "stability", "start",
            "supersonicFlight", "survivability", "unmannedTech", "veryHeavyRocketry",
        };

        [Test] public void StockIdsInUse_Has62Ids() => Assert.AreEqual(62, StockIdsInUse.Distinct().Count());
        [Test] public void Snapshot_Has93Nodes() => Assert.AreEqual(93, Snapshot.Count);

        [Test] public void Shipped_Validates_AgainstSnapshot() => CollectionAssert.IsEmpty(Shipped.Validate(Snapshot));

        [Test] public void Shipped_CoversEveryStockKsp1NodeInUse() =>
            CollectionAssert.IsEmpty(StockIdsInUse.Where(id => !Shipped.Ksp1Nodes.ContainsKey(id)));

        [Test] public void Shipped_EveryNewNodeHasLocalization() =>
            CollectionAssert.IsEmpty(Shipped.NewNodes.Keys.Where(k => !Csv.HasNameAndDescription("tNode_ksp1_" + k)));

        [Test] public void Shipped_CatchAllIsANewNode() =>
            Assert.IsTrue(Shipped.NewNodes.ContainsKey("misc") && Shipped.CatchAll == "new:misc");

        [Test] public void Shipped_IgnoresCommentKeys() =>
            CollectionAssert.IsEmpty(Shipped.Ksp1Nodes.Keys.Concat(Shipped.NewNodes.Keys)
                .Concat(Shipped.CategoryFallback.Keys).Where(k => k.StartsWith("_")));

        [Test] public void Shipped_CsvUsesLfOnly() =>
            Assert.IsFalse(File.ReadAllText(Path.Combine(ModDir, "localizations", "ksp1partmod.csv")).Contains("\r"));

        [Test] public void Validate_ReportsMissingTargetAndAnchor()
        {
            var map = TechMapping.Parse(@"{ ""ksp1_nodes"": { ""a"": ""tNode_gone"", ""b"": ""new:nope"" },
                ""new_nodes"": { ""n"": { ""anchor"": ""tNode_lost"", ""offset"": { ""x"": 0, ""y"": 0 }, ""requires"": [] } },
                ""category_fallback"": {}, ""catch_all"": ""new:n"" }");
            var errors = map.Validate(new HashSet<string> { "tNode_1v_start" });
            Assert.AreEqual(3, errors.Count, string.Join("\n", errors));
        }

        [Test] public void Parse_InvalidJson_Throws() =>
            Assert.That(() => TechMapping.Parse("{ nope"), Throws.Exception);

        [Test] public void Parse_Ksp1NodeKeys_AreCaseInsensitive() =>
            Assert.IsTrue(Shipped.Ksp1Nodes.ContainsKey("BASICROCKETRY"));

        /// <summary>Minimal reader for the mod's localization CSV (quoted fields, LF lines).</summary>
        private sealed class LocalizationCsv
        {
            private readonly Dictionary<string, string[]> _rows = new Dictionary<string, string[]>();

            public LocalizationCsv(string text)
            {
                foreach (var line in text.Split('\n').Skip(1).Where(l => l.Length > 0))
                {
                    var fields = Split(line);
                    if (fields.Count >= 5) _rows[fields[0]] = fields.ToArray();
                }
            }

            public bool HasNameAndDescription(string nodeId) =>
                Filled("KSP1PartMod/TechNodes/Names/" + nodeId) && Filled("KSP1PartMod/TechNodes/Descriptions/" + nodeId);

            private bool Filled(string key) =>
                _rows.TryGetValue(key, out var f) && f[1] == "Text" && f[3].Length > 0 && f[4].Length > 0;

            private static List<string> Split(string line)
            {
                var fields = new List<string>();
                var sb = new StringBuilder();
                var quoted = false;
                for (var i = 0; i < line.Length; i++)
                {
                    var c = line[i];
                    if (quoted)
                    {
                        if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else if (c == '"') quoted = false;
                        else sb.Append(c);
                    }
                    else if (c == '"') quoted = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
                fields.Add(sb.ToString());
                return fields;
            }
        }
    }
}
