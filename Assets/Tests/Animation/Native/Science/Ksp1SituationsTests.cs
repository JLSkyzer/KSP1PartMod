using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="Ksp1Situations"/>: KSP1 situation and biome masks into KSP2 situations with their region flags, and
    /// KSP1 <c>RESULTS</c> keys into KSP2 location ids (science plan (e)).</summary>
    public class Ksp1SituationsTests
    {
        private static string Labels(SituationMap m) => Ksp1Situations.Labels(m.Locations);

        [Test] public void CrewReportLike_63_7_FiveSituations_AtmosphereSplitNoted()
        {
            var m = Ksp1Situations.Map(63, 7);
            Assert.AreEqual("Landed+R, Splashed+R, Atmosphere+R, LowOrbit, HighOrbit", Labels(m));
            Assert.AreEqual(1, m.Notes.Count, string.Join("\n", m.Notes));
            StringAssert.Contains("FlyingLow and FlyingHigh", m.Notes[0]);
            StringAssert.Contains("region required", m.Notes[0]);
            Assert.AreEqual("Landed (region), Splashed (region), Atmosphere (region), Low orbit, High orbit",
                Ksp1Situations.Requirements(m.Locations));
        }

        [Test] public void Landed_1_1_WithARegion()
        {
            var m = Ksp1Situations.Map(1, 1);
            Assert.AreEqual("Landed+R", Labels(m));
            CollectionAssert.IsEmpty(m.Notes);
            Assert.IsTrue(m.Has(Ksp2Situation.Landed));
            Assert.IsTrue(m.RequiresRegion(Ksp2Situation.Landed));
        }

        [Test] public void HighOrbit_32_0()
        {
            var m = Ksp1Situations.Map(32, 0);
            Assert.AreEqual("HighOrbit", Labels(m));
            Assert.IsFalse(m.RequiresRegion(Ksp2Situation.HighOrbit));
            CollectionAssert.IsEmpty(m.Notes);
        }

        [Test] public void Space_48_16_LowOrbitWithARegion_HighOrbitWithout()
        {
            var m = Ksp1Situations.Map(48, 16);
            Assert.AreEqual("LowOrbit+R, HighOrbit", Labels(m));
            Assert.AreEqual("Low orbit (region), High orbit", Ksp1Situations.Requirements(m.Locations));
        }

        [Test] public void Zero_GivesNothing()
        {
            CollectionAssert.IsEmpty(Ksp1Situations.Map(0, 0).Locations);
            CollectionAssert.IsEmpty(Ksp1Situations.Map(0, 63).Locations, "a biome bit without its situation means nothing");
        }

        [Test] public void OneAtmosphereBit_NoSplitNote()
        {
            var low = Ksp1Situations.Map(4, 4);
            Assert.AreEqual("Atmosphere+R", Labels(low));
            CollectionAssert.IsEmpty(low.Notes);
            var both = Ksp1Situations.Map(12, 12);
            Assert.AreEqual("Atmosphere+R", Labels(both));
            CollectionAssert.IsEmpty(both.Notes, "both atmosphere bits with a region: no split");
            Assert.AreEqual("Atmosphere", Labels(Ksp1Situations.Map(12, 0)));
        }

        [Test] public void BitsAbove32_IgnoredAndNoted()
        {
            var m = Ksp1Situations.Map(64 + 1, 0);
            Assert.AreEqual("Landed", Labels(m));
            Assert.IsTrue(m.Notes.Any(n => n.Contains("65")), string.Join("\n", m.Notes));
        }

        [Test] public void ResultKeys()
        {
            Assert.AreEqual("Default", Ksp1Situations.ResultKey("default").LocationId);
            var biome = Ksp1Situations.ResultKey("KerbinSrfLandedShores");
            Assert.IsNull(biome.LocationId);
            Assert.AreEqual(Ksp1Situations.SkipBiome, biome.SkipReason);
            Assert.AreEqual("Mun_LowOrbit", Ksp1Situations.ResultKey("MunInSpaceLow").LocationId);
            Assert.AreEqual("Kerbol_HighOrbit", Ksp1Situations.ResultKey("SunInSpaceHigh").LocationId);
            Assert.AreEqual("Kerbin_Landed", Ksp1Situations.ResultKey("KerbinSrfLanded").LocationId);
            Assert.AreEqual("Eve_Splashed", Ksp1Situations.ResultKey("EveSrfSplashed").LocationId);
            Assert.AreEqual("Duna_Atmosphere", Ksp1Situations.ResultKey("DunaFlyingHigh").LocationId);
            Assert.AreEqual(Ksp1Situations.SkipUnknown, Ksp1Situations.ResultKey("Whatever").SkipReason);
            Assert.AreEqual(Ksp1Situations.SkipUnknown, Ksp1Situations.ResultKey("InSpaceLow").SkipReason, "no body");
        }

        [Test] public void Results_FlyingLowAndHighOnOneBody_FirstWins()
        {
            var set = Ksp1Situations.Results(new[]
            {
                new KeyValuePair<string, string>("default", "Invented default."),
                new KeyValuePair<string, string>("KerbinFlyingHigh", "Invented high."),
                new KeyValuePair<string, string>("KerbinFlyingLow", "Invented low."),
                new KeyValuePair<string, string>("KerbinSrfLandedShores", "Invented shore."),
                new KeyValuePair<string, string>("default", "Second default."),
            });
            CollectionAssert.AreEqual(new[] { "Default", "Kerbin_Atmosphere" }, set.Kept.Select(k => k.LocationId).ToArray());
            Assert.AreEqual("Invented high.", set.Kept[1].Text, "the first one wins");
            Assert.AreEqual("Invented default.", set.Kept[0].Text);
            CollectionAssert.AreEqual(new[] { Ksp1Situations.SkipDuplicate, Ksp1Situations.SkipBiome, Ksp1Situations.SkipDuplicate },
                set.Skipped.Select(s => s.Reason).ToArray());
            Assert.AreEqual(5, set.Total);
        }
    }
}
