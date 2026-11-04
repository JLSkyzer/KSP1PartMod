using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Engines;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Engines
{
    /// <summary><see cref="SepEngineSets"/> (0.16.0, Task 1), on invented cfg text in the shape of SEP's booster clusters.</summary>
    public class SepEngineSetsTests
    {
        private static string Set(string name, string engineId, string transform, double mass) =>
            "ENGINESET\n{\nname = " + name + "\nengineId = " + engineId + "\nsingleEngineThrust = 580\nmass = " +
            mass.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\ntransformName = " + transform +
            "\nnodeTransformName = node_" + transform + "\nnodeSize = 2\n}\n";

        private const string Selectables =
            "CUSTOMSELECTABLE\n{\nname = RBCovers\nITEM\n{\nname = RBCover\ntransformName = RB Skirt\nmass = 6\n}\n}\n" +
            "CUSTOMSELECTABLE\n{\nname = RC13Heatshield\nITEM\n{\ntransformName = RC_Shield_Top\nmass = 0.3\n}\n}\n";

        /// <summary>SEP_25's shape: switch, three engines, then the GUI with one engine type.</summary>
        internal static string Sep25(string extraSets = "", string type = null) =>
            "PART\n{\nname = SEP_25_BOOSTER_CLUSTER\nmass = 0\n" + EngineModeOrderTests.SepSwitch +
            EngineModeOrderTests.Engine("AllEngines", "OuterThrustTransform", 1000) +
            EngineModeOrderTests.Engine("MiddleInner", "MiddleThrustTransform", 1000) +
            EngineModeOrderTests.Engine("CenterThree", "CenterThrustTransform", 1000) +
            "MODULE\n{\nname = ModuleSEPProceduralEngineGUI\ntransformNameSeperator = _\n" + Selectables +
            Set("RBoosts", "AllEngines", "RB", 1.25) + Set("RMiddles13", "MiddleInner", "RM_13", 1.5) + Set("RCenters13", "CenterThree", "RC_13", 1.5) +
            extraSets +
            (type ?? "ENGINETYPE\n{\nname = #LOC_SEP_switcher_raptor_mounts_33\nengineSetName = RBoosts\nengineSetName = RMiddles13\nengineSetName = RCenters13\n" +
                "customSelectableName = RBCovers\ncustomSelectableName = RC13Heatshield\n}\n") +
            "}\n}\n";

        internal static List<string> Names(int outer = 20, int middle = 10, int center = 3, string middleSet = "RM_13", string centerSet = "RC_13")
        {
            var names = new List<string> { "Cluster", "RB", "RB_Nodes", "RB Skirt", "node_RB_3", "RC_13_0_Piston1UP", "RB_Engine_14", "Engine Numbers" };
            for (var i = 0; i < outer; i++) names.AddRange(new[] { "RB_" + i, "OuterThrustTransform" });
            for (var i = 0; i < middle; i++) names.AddRange(new[] { middleSet + "_" + i, "MiddleThrustTransform" });
            for (var i = 0; i < center; i++) names.AddRange(new[] { centerSet + "_" + i, "CenterThrustTransform" });
            return names;
        }

        [Test] public void Sep25_ThrustPerMode_AndEngineMass()
        {
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(Sep25()), Names());
            Assert.IsTrue(c.Converted, c.Reason);
            CollectionAssert.AreEqual(new[] { "AllEngines", "MiddleInner", "CenterThree" }, c.ModeIds);
            CollectionAssert.AreEqual(new[] { 20, 10, 3 }, c.Sets.Select(s => s.Count).ToArray(), "decoys not counted");
            CollectionAssert.AreEqual(new[] { 19140.0, 7540.0, 1740.0 }, c.ModeThrust);
            Assert.AreEqual(44.5, c.EngineMass, 1e-9);
            CollectionAssert.AreEqual(new[] { 20, 10, 3 }, c.ThrustTransforms);
            CollectionAssert.AreEqual(new[] { "CUSTOMSELECTABLE 'RBCovers'", "CUSTOMSELECTABLE 'RC13Heatshield'" }, c.NotReproduced);
            Assert.AreEqual("[KSP1PartMod] engines: SEP_25_BOOSTER_CLUSTER: SEP type '#LOC_SEP_switcher_raptor_mounts_33': RBoosts 20 x 580, " +
                            "RMiddles13 10 x 580, RCenters13 3 x 580 kN; modes 19140 / 7540 / 1740 kN; engine mass +44.5 t (covers excluded); " +
                            "thrust transforms OuterThrustTransform 20, MiddleThrustTransform 10, CenterThrustTransform 3; " +
                            "not reproduced: CUSTOMSELECTABLE 'RBCovers', CUSTOMSELECTABLE 'RC13Heatshield'",
                EngineSizeLines.SepValues("SEP_25_BOOSTER_CLUSTER", c));
        }

        [Test] public void Sep23_BothLayouts_FirstTypeOnly()
        {
            var type = "ENGINETYPE\n{\nname = mounts_33\nengineSetName = RBoosts\nengineSetName = RMiddles13\nengineSetName = RCenters13\n}\n" +
                       "ENGINETYPE\n{\nname = mounts_29\nengineSetName = RBoosts\nengineSetName = RMiddles9\nengineSetName = RCenters9\n}\n";
            var extra = Set("RMiddles9", "MiddleInner", "RM_9", 1.5) + Set("RCenters9", "CenterThree", "RC_9", 1.5);
            var names = Names();
            names.AddRange(Names(0, 6, 3, "RM_9", "RC_9")); // the 29 layout's transforms are in the same model
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(Sep25(extra, type)), names);
            Assert.IsTrue(c.Converted, c.Reason);
            Assert.AreEqual("mounts_33", c.TypeName);
            CollectionAssert.AreEqual(new[] { "RBoosts", "RMiddles13", "RCenters13" }, c.Sets.Select(s => s.Name).ToArray());
            CollectionAssert.AreEqual(new[] { 19140.0, 7540.0, 1740.0 }, c.ModeThrust);
            Assert.AreEqual(44.5, c.EngineMass, 1e-9);
            CollectionAssert.Contains(c.NotReproduced, "ENGINETYPE 'mounts_29'");
        }

        [Test] public void ASetWhoseEngineIdIsNoMode_IsRefused()
        {
            var type = "ENGINETYPE\n{\nname = t\nengineSetName = RBoosts\nengineSetName = Stray\n}\n";
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(Sep25(Set("Stray", "Nowhere", "RB", 1), type)), Names());
            Assert.IsFalse(c.Converted);
            StringAssert.Contains("engineId 'Nowhere' is no engine mode", c.Reason);
        }

        [Test] public void ASetWithNoEngineInTheModel_IsRefused()
        {
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(Sep25()), Names(center: 0));
            Assert.IsFalse(c.Converted);
            StringAssert.Contains("ENGINESET 'RCenters13': no transform RC_13_<n> in the model", c.Reason);
        }

        [Test] public void NoModel_IsRefused_WithTheReason()
        {
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(Sep25()), new string[0]);
            Assert.IsFalse(c.Converted);
            Assert.AreEqual("[KSP1PartMod] engines: SEP_25_BOOSTER_CLUSTER: SEP values not set (ENGINESET 'RBoosts': no transform RB_<n> in the model), Redux's thrust and mass kept",
                EngineSizeLines.SepRefused("SEP_25_BOOSTER_CLUSTER", c.Reason));
        }

        [Test] public void NoSeparator_MatchesTheBareName()
        {
            var text = Sep25().Replace("transformNameSeperator = _\n", "");
            var names = new List<string>();
            for (var i = 0; i < 2; i++) names.AddRange(new[] { "RB" + i, "RM_13" + i, "RC_13" + i, "RB_" + i });
            var c = SepEngineSets.Compute(EngineModeOrderTests.PartOf(text), names);
            Assert.IsTrue(c.Converted, c.Reason);
            CollectionAssert.AreEqual(new[] { 2, 2, 2 }, c.Sets.Select(s => s.Count).ToArray());
        }

        [Test] public void NotAnSepPart_IsRefused() =>
            Assert.IsFalse(SepEngineSets.Compute(EngineModeOrderTests.PartOf("PART\n{\nname = x\n}\n"), Names()).Converted);
    }
}
