using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary>The pure half of Task 3: <see cref="ScienceDefinitionScan"/> (which files are read, MM edits counted),
    /// <see cref="ScienceDefinitionJson"/> (the asset shape and the CSV), <see cref="NativeExperiments.Diff"/> and the extra lines.
    /// Titles and flavor texts invented.</summary>
    public class ScienceDefinitionScanTests
    {
        private const string Defs = @"
EXPERIMENT_DEFINITION
{
    id = bd_inventedA
    title = Invented ""Quoted"", Reading
    baseValue = 10
    dataScale = 2
    situationMask = 48
    biomeMask = 16
    RESULTS
    {
        default = Invented default.
        MunInSpaceLow = Invented Mun.
        KerbinSrfLandedShores = Invented shore.
    }
}
EXPERIMENT_DEFINITION
{
    id = bd_inventedB
    title = Invented B
    baseValue = 0
    situationMask = 1
}
EXPERIMENT_DEFINITION
{
    id = bd_inventedA
    title = Second A
    baseValue = 3
    situationMask = 1
}";

        private const string Part = @"
PART
{
    name = testInstrument
    MODULE
    {
        name = DMModuleScienceAnimateGeneric
        experimentID = bd_inventedA
        xmitDataScalar = 0.4
        sitMask = 32
        bioMask = 0
    }
}
PART
{
    name = testTank
    MODULE
    {
        name = ModuleFuelJettison
    }
}";

        private const string Mm = @"
@EXPERIMENT_DEFINITION[bd_inventedA]
{
    @baseValue = 20
}
+EXPERIMENT_DEFINITION[bd_inventedA]
{
    @id = copy
}
@PART[testInstrument]
{
}";

        private static ScienceDefinitionScan Scan()
        {
            var scan = new ScienceDefinitionScan();
            scan.Add("a/defs.cfg", Defs, CfgText.Parse);
            scan.Add("a/part.cfg", Part, CfgText.Parse);
            scan.Add("a/patch.cfg", Mm, CfgText.Parse);
            scan.Add("a/other.cfg", "PART\n{\nname = x\n}\n", _ => throw new System.Exception("must not be parsed"));
            scan.Add("a/loc.cfg", "Localization\n{\nen-us\n{\n#autoLOC_1 = x\n}\n}\n", _ => throw new System.Exception("must not be parsed"));
            scan.Add("a/unreadable.cfg", null, CfgText.Parse);
            scan.Add("a/bad.cfg", "EXPERIMENT_DEFINITION {", _ => throw new System.Exception("broken"));
            return scan;
        }

        [Test] public void Scan_ParsesOnlyScienceFiles_CountsMmEdits()
        {
            var s = Scan();
            Assert.AreEqual(7, s.CfgFiles);
            Assert.AreEqual(1, s.DefinitionFiles);
            Assert.AreEqual(3, s.DefinitionNodes.Count);
            Assert.AreEqual(2, s.MmEdits, "@ and + of EXPERIMENT_DEFINITION; @PART is not one");
            Assert.AreEqual(1, s.Parts.Count, "only the part with an experiment module");
            CollectionAssert.AreEqual(new[] { "a/loc.cfg" }, s.LocalizationFiles);
            Assert.AreEqual(1, s.Failures.Count);
            StringAssert.StartsWith("a/bad.cfg: broken", s.Failures[0]);
        }

        [Test] public void Build_FirstIdWins_VariantFromThePart_SkipCounts()
        {
            var s = Scan();
            var dup = new List<string>();
            var defs = s.Build(AnimationSettings.Defaults, null, dup);
            CollectionAssert.AreEqual(new[] { "bd_inventedA" }, dup);
            Assert.AreEqual("Invented \"Quoted\", Reading", defs["bd_inventedA"].Title);
            Assert.AreEqual(ScienceExperimentKind.Both, defs["bd_inventedA"].Type, "xmitDataScalar 0.4 of the first module");
            Assert.IsTrue(defs.ContainsKey("bd_inventedA_s32_b0"), string.Join(", ", defs.Keys));
            Assert.IsFalse(defs["bd_inventedB"].IsBuilt);
            var skips = ScienceDefinitionScan.SkipCounts(defs.Values, dup.Count);
            CollectionAssert.AreEqual(new[] { ("duplicate id", 1), (ScienceDefinitionBuilder.SkipNoBaseValue, 1) }, skips.ToArray());
        }

        [Test] public void Json_StockKeyNames_EnumsAsStrings_DefaultFlavor()
        {
            var d = Scan().Build(AnimationSettings.Defaults, null)["bd_inventedA"];
            var core = JObject.Parse(ScienceDefinitionJson.Core(d));
            Assert.AreEqual(0.1f, (float)core["version"], 1e-6);
            var data = (JObject)core["data"];
            CollectionAssert.AreEquivalent(new[]
            {
                "ExperimentID", "DisplayName", "DisplayRequirements", "ExperimentType", "DataFlavorDescriptions", "SampleFlavorDescriptions",
                "DataReportDisplayName", "SampleReportDisplayName", "ValidLocations", "DataValue", "SampleValue", "TransmissionSize", "RequiresEVA",
            }, data.Properties().Select(p => p.Name).ToArray());
            Assert.AreEqual("KSP1_bd_inventedA", (string)data["ExperimentID"]);
            Assert.AreEqual("Both", (string)data["ExperimentType"]);
            Assert.AreEqual("KSP1PartMod/Science/bd_inventedA/Name", (string)data["DisplayName"]);
            Assert.AreEqual(4f, (float)data["DataValue"], 1e-5);
            Assert.AreEqual(6f, (float)data["SampleValue"], 1e-5);
            Assert.AreEqual(20f, (float)data["TransmissionSize"], 1e-5);
            Assert.IsFalse((bool)data["RequiresEVA"]);
            var locations = (JArray)data["ValidLocations"];
            Assert.AreEqual(2, locations.Count);
            Assert.AreEqual("LowOrbit", (string)locations[0]["ScienceSituation"]);
            Assert.IsTrue((bool)locations[0]["RequiresRegion"]);
            Assert.AreEqual("", (string)locations[0]["BodyName"]);
            Assert.AreEqual("", (string)locations[0]["ScienceRegion"]);
            Assert.AreEqual("HighOrbit", (string)locations[1]["ScienceSituation"]);
            var flavor = (JArray)data["DataFlavorDescriptions"];
            Assert.IsTrue(flavor.Any(f => (string)f["ResearchLocationID"] == "Default"));
            Assert.IsTrue(flavor.Any(f => (string)f["ResearchLocationID"] == "Mun_LowOrbit"));
            Assert.AreEqual(flavor.ToString(), data["SampleFlavorDescriptions"].ToString());

            Assert.AreEqual("DataType", ScienceDefinitionJson.TypeName(ScienceExperimentKind.Data));
            Assert.AreEqual("SampleType", ScienceDefinitionJson.TypeName(ScienceExperimentKind.Sample));
        }

        [Test] public void Csv_EveryTermOnce_Escaped_Counts()
        {
            var defs = Scan().Build(AnimationSettings.Defaults, null);
            var csv = ScienceDefinitionJson.Csv(defs.Values, out var terms, out var names, out var flavors);
            var lines = csv.Split('\n').Where(l => l.Length > 0).ToList();
            Assert.AreEqual("Key,Type,Desc,English", lines[0]);
            var built = defs.Values.Where(d => d.IsBuilt).ToList();
            Assert.AreEqual(built.Sum(d => d.Terms.Count), terms);
            Assert.AreEqual(terms, lines.Count - 1);
            Assert.AreEqual(built.Count, names);
            Assert.AreEqual(built.Sum(d => d.DataFlavor.Count), flavors);
            Assert.IsTrue(lines.Contains("\"KSP1PartMod/Science/bd_inventedA/Name\",Text,,\"Invented \"\"Quoted\"\", Reading\""), csv);
            Assert.IsFalse(csv.Contains("bd_inventedB"), "a definition not built has no term");
            Assert.AreEqual("\"a b\"", ScienceDefinitionJson.Escape("a\r\nb").Replace("  ", " "));
        }

        [Test] public void Diff_AddedRemovedChanged()
        {
            var live = new NativeExperiments(NativeExperiments.Redux26w42c.All
                .Where(e => e.Id != "CrewReport")
                .Select(e => e.Id == "OrbitalSurvey" ? new NativeExperiment(e.Id, e.Locations.Take(1), e.Type, e.RequiresEva) : e)
                .Concat(new[] { new NativeExperiment("NewOne", new[] { new ScienceLocation(Ksp2Situation.Landed, true) }, ScienceExperimentKind.Data, false) }));
            var (added, removed, changed) = NativeExperiments.Diff(NativeExperiments.Redux26w42c, live);
            CollectionAssert.AreEqual(new[] { "NewOne" }, added);
            CollectionAssert.AreEqual(new[] { "CrewReport" }, removed);
            CollectionAssert.AreEqual(new[] { "OrbitalSurvey" }, changed);
            var same = NativeExperiments.Diff(NativeExperiments.Redux26w42c, NativeExperiments.Redux26w42c);
            Assert.AreEqual(0, same.Added.Count + same.Removed.Count + same.Changed.Count);
        }

        [Test] public void ExtraLines()
        {
            Assert.AreEqual("[KSP1PartMod] science: store: KSP2 experiments differ from 26w42c: added NewOne; removed none; changed OrbitalSurvey, MarineSurvey",
                ScienceLines.StoreDiffers(new[] { "NewOne" }, new string[0], new[] { "OrbitalSurvey", "MarineSurvey" }));
            Assert.AreEqual("[KSP1PartMod] science: store: the experiment store is not initialized after 'Loading Science Data', not checked", ScienceLines.StoreNotReady);
            Assert.AreEqual("[KSP1PartMod] science: store: 55 KSP1 definition(s) added to the experiment store, 2 already there (skipped)", ScienceLines.StoreAdded(55, 2));
            Assert.AreEqual("[KSP1PartMod] science: definitions: a.cfg not read: denied", ScienceLines.ReadFailed("a.cfg", "denied"));
            Assert.AreEqual("[KSP1PartMod] science: store check failed: x", ScienceLines.Failed("store check", "x"));
        }
    }
}
