using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="ScienceDefinitionBuilder"/>: a KSP1 <c>EXPERIMENT_DEFINITION</c> into the plain model of a generated
    /// KSP2 definition (science plan (h)). Titles and flavor texts are invented.</summary>
    public class ScienceDefinitionBuilderTests
    {
        private const string Def = @"
EXPERIMENT_DEFINITION
{
    id = testProbe
    title = Invented Probe Reading
    baseValue = 10
    scienceCap = 12
    dataScale = 2
    requireAtmosphere = False
    situationMask = 63
    biomeMask = 7
    RESULTS
    {
        default = An invented default text.
        KerbinSrfLanded = An invented landed text.
        KerbinSrfLandedShores = An invented shore text.
        MunInSpaceLow = An invented Mun text.
        SunInSpaceHigh = An invented Sun text.
    }
}";

        private static IKsp1Node Node(string text) => CfgText.Parse(text).GetNodes("EXPERIMENT_DEFINITION").Single();

        private static ScienceDefinitionModel Build(string text = Def, double? x = 1, string json = "{}", System.Func<string, string> titles = null,
            int? sit = null, int? bio = null) =>
            ScienceDefinitionBuilder.From(Node(text), new DefinitionOverrides(x, sit, bio), AnimationSettings.Parse(json, out _), titles);

        [Test] public void FullTransmit_DataOnly()
        {
            var d = Build(x: 1);
            Assert.IsTrue(d.IsBuilt, d.SkipReason);
            Assert.AreEqual("KSP1_testProbe", d.ExperimentId);
            Assert.AreEqual("testProbe", d.Ksp1Id);
            Assert.AreEqual(ScienceExperimentKind.Data, d.Type);
            Assert.AreEqual(10, d.DataValue, 1e-9);
            Assert.AreEqual(0, d.SampleValue, 1e-9);
            Assert.AreEqual(20, d.TransmissionSize, 1e-9, "baseValue x dataScale kbit");
            Assert.IsFalse(d.RequiresEva);
            Assert.AreEqual("Landed+R, Splashed+R, Atmosphere+R, LowOrbit, HighOrbit", Ksp1Situations.Labels(d.Locations));
        }

        [Test] public void PartialTransmit_Both_ValuesSumToV()
        {
            var d = Build(x: 0.3);
            Assert.AreEqual(ScienceExperimentKind.Both, d.Type);
            Assert.AreEqual(3, d.DataValue, 1e-9);
            Assert.AreEqual(7, d.SampleValue, 1e-9);
            Assert.AreEqual(10, d.DataValue + d.SampleValue, 1e-9);
        }

        [Test] public void NoTransmit_SampleOnly()
        {
            var d = Build(x: 0);
            Assert.AreEqual(ScienceExperimentKind.Sample, d.Type);
            Assert.AreEqual(0, d.DataValue, 1e-9);
            Assert.AreEqual(10, d.SampleValue, 1e-9);
        }

        [Test] public void XAbove0999_Data_XClamped()
        {
            Assert.AreEqual(ScienceExperimentKind.Data, Build(x: 0.9995).Type);
            Assert.AreEqual(ScienceExperimentKind.Data, Build(x: 3).Type);
            Assert.AreEqual(1, Build(x: 3).XmitDataScalar, 1e-12);
            Assert.AreEqual(ScienceExperimentKind.Sample, Build(x: -1).Type);
        }

        [Test] public void XMissing_OneAssumed_Noted()
        {
            var d = Build(x: null);
            Assert.AreEqual(ScienceExperimentKind.Data, d.Type);
            Assert.IsTrue(d.Notes.Any(n => n.Contains("xmitDataScalar")), string.Join("\n", d.Notes));
        }

        [Test] public void TransmitSplitOff_OneFullDataReport()
        {
            var d = Build(x: 0.3, json: "{\"science_transmit_split\": false}");
            Assert.AreEqual(ScienceExperimentKind.Data, d.Type);
            Assert.AreEqual(10, d.DataValue, 1e-9);
            Assert.AreEqual(0, d.SampleValue, 1e-9);
        }

        [Test] public void ValueScale2()
        {
            var d = Build(x: 0.3, json: "{\"science_value_scale\": 2}");
            Assert.AreEqual(6, d.DataValue, 1e-9);
            Assert.AreEqual(14, d.SampleValue, 1e-9);
            Assert.AreEqual(2, d.ValueScale, 1e-12);
            Assert.AreEqual(20, d.TransmissionSize, 1e-9, "the size is not scaled");
        }

        [Test] public void MissingBaseValue_Skipped_Noted()
        {
            var d = Build(Def.Replace("baseValue = 10", ""));
            Assert.IsFalse(d.IsBuilt);
            StringAssert.Contains("baseValue", d.SkipReason);
            Assert.IsTrue(d.Notes.Any(n => n.Contains("baseValue")));
            var zero = Build(Def.Replace("baseValue = 10", "baseValue = 0"));
            Assert.IsFalse(zero.IsBuilt);
        }

        [Test] public void NoMappedSituation_Skipped()
        {
            var d = Build(Def.Replace("situationMask = 63", "situationMask = 0"));
            Assert.IsFalse(d.IsBuilt);
            StringAssert.Contains("situation", d.SkipReason);
        }

        [Test] public void Terms_AndEnglishText()
        {
            var d = Build(x: 0.3);
            Assert.AreEqual("KSP1PartMod/Science/testProbe/Name", d.NameTerm);
            Assert.AreEqual("Invented Probe Reading", d.Title);
            Assert.AreEqual("Invented Probe Reading", d.English(d.NameTerm));
            Assert.AreEqual("Invented Probe Reading", d.English(d.DataReportTerm));
            Assert.AreEqual("Invented Probe Reading (sample)", d.English(d.SampleReportTerm));
            Assert.AreEqual("Landed (region), Splashed (region), Atmosphere (region), Low orbit, High orbit", d.English(d.RequirementsTerm));
            Assert.AreEqual(d.Terms.Count, d.Terms.Select(t => t.Term).Distinct().Count(), "each term once");
            Assert.IsTrue(d.Terms.All(t => t.Term.StartsWith("KSP1PartMod/Science/testProbe/")));
        }

        [Test] public void Flavor_FromResults_BiomeSkipped_SampleCopiesData()
        {
            var d = Build(x: 0.3);
            CollectionAssert.AreEqual(new[] { "Default", "Kerbin_Landed", "Mun_LowOrbit", "Kerbol_HighOrbit" },
                d.DataFlavor.Select(f => f.LocationId).ToArray());
            Assert.AreEqual("KSP1PartMod/Science/testProbe/Flavor/Mun_LowOrbit", d.DataFlavor[2].Term);
            Assert.AreEqual("An invented Mun text.", d.English(d.DataFlavor[2].Term));
            CollectionAssert.AreEqual(d.DataFlavor.Select(f => f.Term).ToArray(), d.SampleFlavor.Select(f => f.Term).ToArray());
            Assert.AreEqual(4, d.FlavorKept);
            Assert.AreEqual(5, d.FlavorTotal);
        }

        [Test] public void DefaultFlavor_AlwaysPresent()
        {
            var noDefault = Build(Def.Replace("default = An invented default text.", ""));
            Assert.AreEqual("Default", noDefault.DataFlavor[0].LocationId);
            Assert.AreEqual("Invented Probe Reading", noDefault.English(noDefault.DataFlavor[0].Term), "the title");
            Assert.IsTrue(noDefault.Notes.Any(n => n.Contains("default")));

            var noResults = Build(@"
EXPERIMENT_DEFINITION
{
    id = bare
    title = Invented Bare
    baseValue = 4
    situationMask = 1
    biomeMask = 0
}");
            Assert.IsTrue(noResults.IsBuilt);
            Assert.AreEqual(1, noResults.DataFlavor.Count);
            Assert.AreEqual("Default", noResults.DataFlavor.Single().LocationId);
            Assert.AreEqual("Default", noResults.SampleFlavor.Single().LocationId);
            Assert.AreEqual(1, noResults.DataScale, 1e-12, "dataScale missing: 1");
        }

        [Test] public void DMagicOverride_SitMaskBioMask_OwnId()
        {
            var d = Build(sit: 48, bio: 16);
            Assert.IsTrue(d.IsBuilt);
            Assert.IsTrue(d.IsVariant);
            Assert.AreEqual("testProbe_s48_b16", d.Key);
            Assert.AreEqual("KSP1_testProbe_s48_b16", d.ExperimentId);
            Assert.AreEqual("testProbe", d.Ksp1Id);
            Assert.AreEqual("LowOrbit+R, HighOrbit", Ksp1Situations.Labels(d.Locations));
            Assert.AreEqual(48, d.SituationMask);
            Assert.AreEqual(16, d.BiomeMask);
            Assert.IsTrue(d.Terms.All(t => t.Term.StartsWith("KSP1PartMod/Science/testProbe_s48_b16/")), "its own terms");
            Assert.IsTrue(d.Notes.Any(n => n.Contains("sitMask")), string.Join("\n", d.Notes));

            var same = Build(sit: 63, bio: 7);
            Assert.IsFalse(same.IsVariant, "the same masks: the base definition");
            Assert.AreEqual("KSP1_testProbe", same.ExperimentId);
            Assert.AreEqual("KSP1_testProbe_s63_b3", Build(bio: 3).ExperimentId, "only the biome mask differs");
        }

        [Test] public void NotBuilt_KeepsItsMasks()
        {
            var d = Build(Def.Replace("baseValue = 10", "").Replace("situationMask = 63", "situationMask = 32"));
            Assert.IsFalse(d.IsBuilt);
            Assert.AreEqual(32, d.SituationMask, "read before the baseValue check, for the closest match");
            Assert.AreEqual(7, d.BiomeMask);
        }

        [Test] public void BuildAll_BaseAndOneDefinitionPerDMagicMaskPair()
        {
            var defs = new[] { Node(Def), Node(Def.Replace("id = testProbe", "id = other")) };
            var parts = new[]
            {
                (IReadOnlyList<IKsp1Node>)new[]
                {
                    CfgText.Module("name = DMModuleScienceAnimateGeneric\nexperimentID = testProbe\nxmitDataScalar = 0.4\nsitMask = 48\nbioMask = 16"),
                    CfgText.Module("name = ModuleScienceExperiment\nexperimentID = testProbe\nxmitDataScalar = 0.9"),
                },
                new[]
                {
                    CfgText.Module("name = DMModuleScienceAnimateGeneric\nexperimentID = testProbe\nxmitDataScalar = 0.2\nsitMask = 48\nbioMask = 16"),
                    CfgText.Module("name = DMModuleScienceAnimateGeneric\nexperimentID = testProbe\nsitMask = 63\nbioMask = 7"),
                    CfgText.Module("name = DMModuleScienceAnimateGeneric\nexperimentID = other\nsitMask = 1"),
                    CfgText.Module("name = ModuleScienceExperiment\nexperimentID = testProbe\nsitMask = 1"),
                },
            };
            var duplicates = new List<string>();
            var all = ScienceDefinitionBuilder.BuildAll(defs.Concat(new[] { Node(Def) }), parts, AnimationSettings.Defaults, null, duplicates);
            CollectionAssert.AreEquivalent(new[] { "testProbe", "testProbe_s48_b16", "other", "other_s1_b7" }, all.Keys);
            CollectionAssert.AreEqual(new[] { "testProbe" }, duplicates);
            Assert.IsFalse(all["testProbe"].IsVariant);
            Assert.AreEqual(63, all["testProbe"].SituationMask, "the base keeps its masks");
            Assert.AreEqual(0.4, all["testProbe"].XmitDataScalar, 1e-12, "the first module that uses the id");
            Assert.AreEqual(0.4, all["testProbe_s48_b16"].XmitDataScalar, 1e-12, "the first module with that pair");
            Assert.AreEqual("Landed+R", Ksp1Situations.Labels(all["other_s1_b7"].Locations), "bioMask of the definition kept");
            Assert.AreEqual(0.4, DefinitionOverrides.FirstUses(parts)["testProbe"].XmitDataScalar.Value, 1e-12);
            Assert.IsNull(DefinitionOverrides.FirstUses(parts)["testProbe"].SitMask, "a base definition never takes a module's masks");
        }

        [Test] public void RequireAtmosphere_NotedNotReproduced()
        {
            var d = Build(Def.Replace("requireAtmosphere = False", "requireAtmosphere = True\n    requireNoAtmosphere = True"));
            Assert.IsTrue(d.IsBuilt);
            CollectionAssert.Contains(d.NotReproduced, "requireAtmosphere");
            CollectionAssert.Contains(d.NotReproduced, "requireNoAtmosphere");
            CollectionAssert.DoesNotContain(Build().NotReproduced, "requireAtmosphere", "False is no requirement");
            CollectionAssert.Contains(d.NotReproduced, "scienceCap");
        }

        [Test] public void Title_Localized_ThroughTheResolver()
        {
            var text = Def.Replace("title = Invented Probe Reading", "title = #autoLOC_123");
            var d = Build(text, titles: t => t == "#autoLOC_123" ? "Invented Localized" : null);
            Assert.AreEqual("Invented Localized", d.Title);
            var raw = Build(text, titles: t => null);
            Assert.AreEqual("#autoLOC_123", raw.Title);
            Assert.IsTrue(raw.Notes.Any(n => n.Contains("#autoLOC_123")));
            var none = Build(Def.Replace("title = Invented Probe Reading", ""));
            Assert.AreEqual("testProbe", none.Title, "no title: the id");
        }

        [Test] public void MissingId_Skipped()
        {
            var d = Build(Def.Replace("id = testProbe", ""));
            Assert.IsFalse(d.IsBuilt);
            StringAssert.Contains("id", d.SkipReason);
        }

        [Test] public void Overrides_FromAModule_AndFirstUse()
        {
            var a = CfgText.Module("name = DMModuleScienceAnimateGeneric\nexperimentID = x1\nxmitDataScalar = 0.4\nsitMask = 48\nbioMask = 16");
            var o = DefinitionOverrides.From(a);
            Assert.AreEqual(0.4, o.XmitDataScalar.Value, 1e-12);
            Assert.AreEqual(48, o.SitMask);
            Assert.AreEqual(16, o.BioMask);
            var stock = DefinitionOverrides.From(CfgText.Module("name = ModuleScienceExperiment\nexperimentID = x1\nxmitDataScalar = 0.5\nsitMask = 1"));
            Assert.IsNull(stock.SitMask, "only DMagic modules override the masks");

            var b = CfgText.Module("name = ModuleScienceExperiment\nexperimentID = x1\nxmitDataScalar = 0.9");
            var c = CfgText.Module("name = ModuleScienceExperiment\nexperimentID = x2\nxmitDataScalar = 0.2");
            var first = DefinitionOverrides.FirstUses(new[] { new[] { b, c }, new[] { a } });
            Assert.AreEqual(0.9, first["x1"].XmitDataScalar.Value, 1e-12, "the first module that uses the id");
            Assert.AreEqual(0.2, first["x2"].XmitDataScalar.Value, 1e-12);
        }
    }
}
