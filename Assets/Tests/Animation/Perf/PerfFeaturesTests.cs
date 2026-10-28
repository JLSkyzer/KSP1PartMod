using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Perf;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Perf
{
    public class PerfFeaturesTests
    {
        private static EntryKind[] KindsOf(PerfFeature f) =>
            ((EntryKind[])Enum.GetValues(typeof(EntryKind))).Where(k => PerfFeatures.Of(k) == f).ToArray();

        /// <summary>A new entry kind fails here until it is given a feature.</summary>
        [Test] public void EveryEntryKind_IsMapped()
        {
            foreach (EntryKind k in Enum.GetValues(typeof(EntryKind)))
                Assert.DoesNotThrow(() => PerfFeatures.Of(k), k.ToString());
        }

        /// <summary>The four groups of the plan's research (d).</summary>
        [Test] public void Groups()
        {
            CollectionAssert.AreEquivalent(new[]
            {
                EntryKind.Toggle, EntryKind.Deploy, EntryKind.Gear, EntryKind.Group, EntryKind.AeroGate, EntryKind.GearBridge,
                EntryKind.ReduxFollower, EntryKind.ChuteFollower,
            }, KindsOf(PerfFeature.Clips));
            CollectionAssert.AreEquivalent(new[] { EntryKind.Throttle, EntryKind.Heat, EntryKind.Color }, KindsOf(PerfFeature.Material));
            CollectionAssert.AreEquivalent(new[]
            {
                EntryKind.EngineDeploy, EntryKind.EngineMode, EntryKind.LookAt, EntryKind.DecouplerClip, EntryKind.RcsGlow,
                EntryKind.RcsEmissive, EntryKind.MeshHider, EntryKind.ScienceToggle, EntryKind.AccelSpring, EntryKind.LandingBag,
            }, KindsOf(PerfFeature.Plugins));
            CollectionAssert.AreEquivalent(new[] { EntryKind.Shroud }, KindsOf(PerfFeature.Shrouds));
            Assert.AreEqual(22, Enum.GetValues(typeof(EntryKind)).Length);
        }

        /// <summary>The runtime's form (resolved once per entry at bind): every kind as <see cref="PerfFeatures.Of"/>, and an
        /// unclassified kind in the fallback instead of a throw.</summary>
        [Test] public void OfOrDefault_NeverThrows()
        {
            foreach (EntryKind k in Enum.GetValues(typeof(EntryKind)))
                Assert.AreEqual(PerfFeatures.Of(k), PerfFeatures.OfOrDefault(k, PerfFeature.Save), k.ToString());
            Assert.AreEqual(PerfFeature.Plugins, PerfFeatures.OfOrDefault((EntryKind)9999, PerfFeature.Plugins));
        }

        [Test] public void Labels_DistinctAndNamed()
        {
            var labels = ((PerfFeature[])Enum.GetValues(typeof(PerfFeature))).Select(PerfFeatures.Label).ToList();
            Assert.AreEqual(13, labels.Count);
            CollectionAssert.AllItemsAreUnique(labels);
            Assert.AreEqual("2b plugins", PerfFeatures.Label(PerfFeature.Plugins));
        }
    }
}
