using System.Linq;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="ScienceStoreGuard"/>: the KSP1 experiments missing from the live store, configurations and standings
    /// together, our ids only.</summary>
    public class ScienceStoreGuardTests
    {
        private static bool Known(string id) => id == "CrewObservation" || id == "KSP1_known";

        [Test] public void MissingOursDropped_DescendingIndices_StandingsMatchedById()
        {
            var d = ScienceStoreGuard.Missing(new[] { "KSP1_a", "CrewObservation", "KSP1_known", "KSP1_b" },
                new[] { "KSP1_a", "CrewObservation", "KSP1_known", "KSP1_b" }, Known);
            CollectionAssert.AreEqual(new[] { 3, 0 }, d.Configurations);
            CollectionAssert.AreEqual(new[] { 3, 0 }, d.Standings, "the standing of the same index goes too");
            CollectionAssert.AreEqual(new[] { "KSP1_a", "KSP1_b" }, d.Ids);
            Assert.IsTrue(d.Any);
        }

        [Test] public void SavedStandingOnly_Dropped()
        {
            // A saved vessel: KSP2 resolves standings, not configurations (its null-definition case).
            var d = ScienceStoreGuard.Missing(new string[0], new[] { "CrewObservation", "KSP1_gone" }, Known);
            CollectionAssert.IsEmpty(d.Configurations);
            CollectionAssert.AreEqual(new[] { 1 }, d.Standings);
            CollectionAssert.AreEqual(new[] { "KSP1_gone" }, d.Ids);
        }

        [Test] public void NotOurs_OrAllKnown_NothingDropped()
        {
            var d = ScienceStoreGuard.Missing(new[] { "SomeoneElse", "CrewObservation", "KSP1_known", null }, new[] { "SomeoneElse", null }, Known);
            Assert.IsFalse(d.Any, "another mod's unknown id is KSP2's own business");
            Assert.IsFalse(ScienceStoreGuard.Missing(null, null, Known).Any);
        }

        [Test] public void Line()
        {
            Assert.AreEqual("[KSP1PartMod] science: bd_probe: KSP1_bd_x not in the experiment store, dropped (definitions missing in this campaign)",
                ScienceLines.DroppedMissing("bd_probe", "KSP1_bd_x"));
        }
    }
}
