using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="ScannerMapping"/>: <c>ModuleOrbitalSurveyor</c> and <c>ModuleResourceScanner</c> →
    /// <c>Module_ResourceScanner</c> (plan (j)). Cfg values written for the tests, in the shape of the stock files.</summary>
    public class ScannerMappingTests
    {
        private static IKsp1Node Module(string name, string body) => CfgText.Module("name = " + name + "\n" + body);

        [Test] public void SurveyScanner_NativeListAndPower_ScanTimeFromTheCfg()
        {
            var s = ScannerMapping.Surveyor(Module("ModuleOrbitalSurveyor", "ScanTime = 5\nSciBonus = 10"), NativeScannerRef.Fallback);
            Assert.IsTrue(s.HasModule);
            Assert.AreEqual(ScannerKind.Surveyor, s.Kind);
            CollectionAssert.AreEqual(new[] { "Water", "CarbonRegolith", "Ammonia", "Uranium" }, s.ScannableResources);
            Assert.AreEqual("ElectricCharge", s.RequiredResources.Single().Name);
            Assert.AreEqual(14, s.RequiredResources.Single().Rate);
            Assert.AreEqual(5, s.TimeToComplete);
            Assert.AreEqual(0, s.MinAltitude);
            Assert.AreEqual(5000000, s.MaxAltitude);
            Assert.AreEqual(80, s.MinInclination);
            Assert.AreEqual(100, s.MaxInclination);
            CollectionAssert.Contains(s.NotReproduced, "SciBonus");
            Assert.That(s.Notes, Has.Some.Contains("inclination 80° to 100° from the KSP2 scanner"));
        }

        [Test] public void Surveyor_CfgAltitudes_Used_NoScanTime_NativeTime()
        {
            var s = ScannerMapping.Surveyor(Module("ModuleOrbitalSurveyor", "MinimumAltitude = 25000\nMaximumAltitude = 1500000"), NativeScannerRef.Fallback);
            Assert.AreEqual(25000, s.MinAltitude);
            Assert.AreEqual(1500000, s.MaxAltitude);
            Assert.AreEqual(5, s.TimeToComplete);
            Assert.That(s.Notes, Has.Some.Contains("ScanTime missing"));
        }

        [Test] public void SurfaceScanner_RequiresUnlockFalse_OneResource_1km()
        {
            var s = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "ScannerType = 0\nResourceName = Ore\nMaxAbundanceAltitude = 1000\nRequiresUnlock = false"), IsruFakes.Table(), NativeScannerRef.Fallback);
            Assert.IsTrue(s.HasModule);
            Assert.AreEqual(ScannerKind.Resource, s.Kind);
            Assert.AreEqual(ScannerLeftOut.None, s.LeftOut);
            CollectionAssert.AreEqual(new[] { "CarbonRegolith" }, s.ScannableResources);
            Assert.AreEqual("Ore", s.Ksp1Resource);
            CollectionAssert.IsEmpty(s.RequiredResources);
            Assert.AreEqual(0, s.MinAltitude);
            Assert.AreEqual(1000, s.MaxAltitude);
            Assert.AreEqual(-180, s.MinInclination);
            Assert.AreEqual(180, s.MaxInclination);
            Assert.AreEqual(5, s.TimeToComplete, "the native scan time");
            Assert.That(s.Notes, Has.Some.Contains("KSP2 records the scan for the whole body"));
        }

        [Test] public void OrbitalScanner_RequiresUnlockTrue_NoModule_Noted()
        {
            var s = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "MaxAbundanceAltitude = 500000\nRequiresUnlock = true\nScannerType = 0\nResourceName = Ore"), IsruFakes.Table(), NativeScannerRef.Fallback);
            Assert.IsFalse(s.HasModule);
            Assert.AreEqual(ScannerLeftOut.RequiresUnlock, s.LeftOut);
            Assert.That(s.Notes, Has.Some.EqualTo(
                "ModuleResourceScanner: RequiresUnlock, left out (KSP1 shows local abundance after a survey; KSP2's resource window shows the scanned map and has no part readout)"));
        }

        [Test] public void ScannerType1_NoModule()
        {
            var s = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "ScannerType = 1\nResourceName = Water\nRequiresUnlock = false"), IsruFakes.Table(), NativeScannerRef.Fallback);
            Assert.IsFalse(s.HasModule);
            Assert.AreEqual(ScannerLeftOut.ScannerType, s.LeftOut);
            Assert.That(s.Notes, Has.Some.EqualTo(
                "ModuleResourceScanner: ScannerType 1 (ocean) left out, KSP2 scans the ground only"));
        }

        [Test] public void ResourceWithoutAMap_NoModule()
        {
            var s = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "ResourceName = Ore\nRequiresUnlock = false"), IsruFakes.Table("TestRocks"), NativeScannerRef.Fallback);
            Assert.IsFalse(s.HasModule);
            Assert.AreEqual(ScannerLeftOut.NoScannableResource, s.LeftOut);
            Assert.That(s.Notes, Has.Some.Contains("Ore -> TestRocks has no resource map in KSP2"));
        }

        [Test] public void RequiresUnlockMissing_TakenAsTrue_Noted()
        {
            var s = ScannerMapping.Resource(Module("ModuleResourceScanner", "ResourceName = Ore"), IsruFakes.Table(), NativeScannerRef.Fallback);
            Assert.AreEqual(ScannerLeftOut.RequiresUnlock, s.LeftOut);
            Assert.That(s.Notes, Has.Some.Contains("RequiresUnlock missing, taken as true"));
        }

        [Test] public void NullReference_UsesTheFallback()
        {
            var s = ScannerMapping.Surveyor(Module("ModuleOrbitalSurveyor", "ScanTime = 7"), null);
            Assert.AreEqual(4, s.ScannableResources.Count);
            Assert.AreEqual(7, s.TimeToComplete);
        }

        /// <summary>Task 3 review: merged resource scanners take the widest window, the longest time and the highest power,
        /// not the first scanner's.</summary>
        [Test] public void MergeResources_WidestWindow_LongestTime()
        {
            var quick = new NativeScannerRef("a", 0, new[] { "Water" }, 5, 0, 1, 0, 1);
            var slow = new NativeScannerRef("b", 0, new[] { "Water" }, 9, 0, 70000, 0, 1);
            var low = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "ScannerType = 0\nResourceName = Ore\nMaxAbundanceAltitude = 1000\nRequiresUnlock = false"), IsruFakes.Table(), quick);
            var high = ScannerMapping.Resource(Module("ModuleResourceScanner",
                "ScannerType = 0\nResourceName = Water\nRequiresUnlock = false"), IsruFakes.Table(), slow);
            var m = ScannerMapping.MergeResources(new[] { low, high });
            CollectionAssert.AreEqual(new[] { "CarbonRegolith", "Water" }, m.ScannableResources);
            Assert.AreEqual(9, m.TimeToComplete, "the longest");
            Assert.AreEqual(0, m.MinAltitude);
            Assert.AreEqual(70000, m.MaxAltitude, "the second's (MaxAbundanceAltitude missing: its native bound)");
            Assert.AreEqual(-180, m.MinInclination);
            Assert.AreEqual(180, m.MaxInclination);
            Assert.That(m.Notes, Has.Some.EqualTo("ModuleResourceScanner x2 merged into one Module_ResourceScanner (one per part), " +
                                                  "the widest altitude and inclination window, the longest scan time and the highest power used"));
            Assert.AreSame(low, ScannerMapping.MergeResources(new[] { low }));
        }
    }
}
