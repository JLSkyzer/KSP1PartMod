using System.Linq;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="NativeExperiments"/>: the KSP2 experiments of 26w42c (science plan (b)) and the closest one to a KSP1
    /// situation mask (plan (h), rule 4).</summary>
    public class NativeExperimentsTests
    {
        private static readonly NativeExperiments N = NativeExperiments.Redux26w42c;

        [Test] public void List26w42c()
        {
            CollectionAssert.AreEqual(new[]
            {
                "CrewObservation", "EnvironmentSurvey", "RadiationReport", "AtmosphereSurvey", "OrbitalSurvey", "MarineSurvey",
                "SurfaceSurvey", "CrewReport", "ResourceScan",
            }, N.All.Select(e => e.Id).ToArray());
            Assert.AreEqual("HighOrbit, LowOrbit, Atmosphere, Landed+R, Splashed+R", Ksp1Situations.Labels(N.Find("CrewObservation").Locations));
            Assert.AreEqual(ScienceExperimentKind.Both, N.Find("EnvironmentSurvey").Type);
            Assert.AreEqual("Atmosphere+R", Ksp1Situations.Labels(N.Find("AtmosphereSurvey").Locations));
            Assert.AreEqual("HighOrbit, LowOrbit+R", Ksp1Situations.Labels(N.Find("OrbitalSurvey").Locations));
            Assert.IsTrue(N.Find("SurfaceSurvey").RequiresEva);
            Assert.AreEqual(1, N.All.Count(e => e.RequiresEva));
            Assert.IsTrue(N.Contains("ResourceScan"));
            Assert.IsFalse(N.Contains("KSP1_mysteryGoo"));
            Assert.IsNull(N.Find("nope"));
        }

        [Test] public void Jaccard()
        {
            Assert.AreEqual(1, NativeExperiments.Jaccard(63, N.Find("CrewObservation")), 1e-12);
            Assert.AreEqual(0.5, NativeExperiments.Jaccard(32, N.Find("OrbitalSurvey")), 1e-12);
            Assert.AreEqual(0.4, NativeExperiments.Jaccard(13, N.Find("EnvironmentSurvey")), 1e-12);
            Assert.AreEqual(0, NativeExperiments.Jaccard(0, N.Find("EnvironmentSurvey")), 1e-12);
        }

        /// <summary>The stock KSP1 masks (<c>ScienceDefs.cfg</c>) and the share their stock part transmits (every stock instrument has
        /// 0 &lt; <c>xmitDataScalar</c> &lt; 1, so its kind is Both). Rule 4 is used only when a definition exists and is not
        /// generated; without a definition the table <c>science_fallback_ids</c> decides. The rows where the two differ are
        /// documented here: the Jaccard rule sees only situations, the table also the instrument's purpose.</summary>
        [TestCase("mysteryGoo", 63, "EnvironmentSurvey", "EnvironmentSurvey")]
        [TestCase("mobileMaterialsLab", 63, "EnvironmentSurvey", "EnvironmentSurvey")]
        [TestCase("temperatureScan", 63, "EnvironmentSurvey", "EnvironmentSurvey")]
        [TestCase("barometerScan", 63, "AtmosphereSurvey", "EnvironmentSurvey")] // every situation: AtmosphereSurvey is 1/5
        [TestCase("atmosphereAnalysis", 13, "AtmosphereSurvey", "AtmosphereSurvey")] // 1/2, tie with CrewReport (Data) won by the kind
        [TestCase("seismicScan", 1, "MarineSurvey", "CrewReport")] // Landed only: CrewReport 1, MarineSurvey 1/2
        [TestCase("gravityScan", 51, "OrbitalSurvey", "EnvironmentSurvey")] // four of five situations: 4/5
        [TestCase("infraredTelescope", 32, "OrbitalSurvey", "OrbitalSurvey")] // 1/2, tie with ResourceScan (Data) won by the kind
        [TestCase("magnetometer", 48, "RadiationReport", "OrbitalSurvey")] // both orbits: OrbitalSurvey 1, RadiationReport 2/5
        public void FallbackTableRow_AgainstTheStockMask(string ksp1Id, int mask, string table, string closest)
        {
            Assert.AreEqual(table, ScienceDefaults.FallbackIds[ksp1Id], "the default table");
            Assert.IsTrue(N.Contains(table));
            Assert.AreEqual(closest, N.Closest(mask, ScienceExperimentKind.Both).Id,
                table == closest ? "the rule gives the table's target" : "documented difference from the table");
        }

        [Test] public void Ties_KindThenTableOrder()
        {
            Assert.AreEqual("CrewObservation", N.Closest(63, ScienceExperimentKind.Data).Id, "Data: the first Data row");
            Assert.AreEqual("EnvironmentSurvey", N.Closest(63, ScienceExperimentKind.Both).Id);
            Assert.AreEqual("EnvironmentSurvey", N.Closest(63, ScienceExperimentKind.Sample).Id, "Sample shares with Both");
            Assert.AreEqual("ResourceScan", N.Closest(32, ScienceExperimentKind.Data).Id);
            Assert.AreEqual("OrbitalSurvey", N.Closest(32, ScienceExperimentKind.Sample).Id);
        }

        [Test] public void SurfaceSurvey_EvaOnly_NeverChosen()
        {
            for (var mask = 1; mask <= 63; mask++)
            foreach (var kind in new[] { ScienceExperimentKind.Data, ScienceExperimentKind.Sample, ScienceExperimentKind.Both })
                Assert.AreNotEqual("SurfaceSurvey", N.Closest(mask, kind)?.Id, $"mask {mask}, {kind}");
            Assert.AreEqual("MarineSurvey", N.Closest(3, ScienceExperimentKind.Both).Id, "Landed and Splashed");
        }

        [Test] public void EmptyMask_NoCounterpart()
        {
            Assert.IsNull(N.Closest(0, ScienceExperimentKind.Data));
            Assert.IsNull(new NativeExperiments(new NativeExperiment[0]).Closest(63, ScienceExperimentKind.Data));
        }

        [Test] public void LiveList_ReplacesTheDefault()
        {
            var live = new NativeExperiments(new[]
            {
                new NativeExperiment("OnlyOne", new[] { new ScienceLocation(Ksp2Situation.Landed, true) }, ScienceExperimentKind.Data, false),
            });
            Assert.AreEqual("OnlyOne", live.Closest(63, ScienceExperimentKind.Both).Id);
            Assert.IsFalse(live.Contains("CrewObservation"));
        }
    }
}
