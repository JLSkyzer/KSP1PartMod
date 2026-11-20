using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>Antenna gating (0.18.0): the planner marks the Deploy entry of a <c>ModuleDeployableAntenna</c> that sits on a part with
    /// a transmitter; the rules map its state to the bridge; a census of the installed cfg files.</summary>
    public class AntennaGatingTests
    {
        private static IReadOnlyList<IKsp1Node> Mods(params string[] modules) =>
            CfgText.Parse(string.Concat(modules)).GetNodes("MODULE").ToList();

        private static string Module(string body) => "MODULE\n{\n" + body + "\n}\n";

        private static string Antenna(string clip = "deploy") => Module("name = ModuleDeployableAntenna\nanimationName = " + clip);

        private static string Transmitter() => Module("name = ModuleDataTransmitter\nantennaPower = 500000");

        private static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> mods, bool antennas = true) =>
            OwnershipPlanner.Plan(mods, false, new HashSet<string>(), false, false, null, true, true, antennas: antennas);

        [Test] public void Planner_AntennaWithTransmitter_IsMarked()
        {
            var e = Plan(Mods(Antenna(), Transmitter())).Entries.Single(x => x.Kind == EntryKind.Deploy);
            Assert.IsTrue(e.GatesTransmitter);
            Assert.IsTrue(AntennaRules.CanDriveBridge(e));
        }

        [Test] public void Planner_AntennaWithoutTransmitter_IsNotMarked() =>
            Assert.IsFalse(Plan(Mods(Antenna())).Entries.Single(x => x.Kind == EntryKind.Deploy).GatesTransmitter);

        [Test] public void Planner_SwitchOff_IsNotMarked() =>
            Assert.IsFalse(Plan(Mods(Antenna(), Transmitter()), antennas: false).Entries.Single(x => x.Kind == EntryKind.Deploy).GatesTransmitter);

        [Test] public void Planner_OtherDeployable_IsNotMarked()
        {
            var plan = Plan(Mods(Module("name = ModuleDeployablePart\nanimationName = unfold"), Transmitter()));
            Assert.IsFalse(plan.Entries.Single(x => x.Kind == EntryKind.Deploy).GatesTransmitter);
        }

        [Test] public void Planner_Marking_MovesNoKey()
        {
            var with = Plan(Mods(Antenna(), Transmitter())).Entries.Select(e => e.Key).ToArray();
            var without = Plan(Mods(Antenna(), Transmitter()), antennas: false).Entries.Select(e => e.Key).ToArray();
            CollectionAssert.AreEqual(without, with);
        }

        [TestCase(DeployState.Extended, false, ExpectedResult = BridgeDeploy.Extended)]
        [TestCase(DeployState.Retracted, false, ExpectedResult = BridgeDeploy.Retracted)]
        [TestCase(DeployState.Extending, false, ExpectedResult = BridgeDeploy.Extending)]
        [TestCase(DeployState.Retracting, false, ExpectedResult = BridgeDeploy.Retracting)]
        [TestCase(DeployState.Retracted, true, ExpectedResult = BridgeDeploy.Extended)]
        public BridgeDeploy Rules_StateOf(DeployState state, bool inactive) => AntennaRules.StateOf(state, inactive);

        [Test] public void Rules_MarksModule_NeedsAntennaAndTransmitter()
        {
            Assert.IsTrue(AntennaRules.MarksModule("ModuleDeployableAntenna", true));
            Assert.IsTrue(AntennaRules.MarksModule(" moduledeployableantenna ", true));
            Assert.IsFalse(AntennaRules.MarksModule("ModuleDeployableAntenna", false));
            Assert.IsFalse(AntennaRules.MarksModule("ModuleDeployablePart", true));
        }

        [Test] public void Census_InstalledCfg_EveryDeployableAntennaWithATransmitterIsMarked()
        {
            var files = InstalledKsp1.AllCfg().Where(f => File.ReadAllText(f).Contains("ModuleDeployableAntenna")).ToArray();
            if (files.Length == 0) Assert.Ignore("no KSP1 cfg files installed");
            int parts = 0, marked = 0, unmarkedWithTransmitter = 0;
            foreach (var f in files)
                foreach (var part in CfgText.Parse(File.ReadAllText(f)).GetNodes("PART"))
                {
                    var mods = part.GetNodes("MODULE").ToList();
                    if (!mods.Any(m => m.GetValue("name")?.Trim() == "ModuleDeployableAntenna")) continue;
                    parts++;
                    var hasTx = mods.Any(m => m.GetValue("name")?.Trim() == "ModuleDataTransmitter");
                    var m2 = Plan(mods).Entries.Count(e => e.GatesTransmitter);
                    marked += m2;
                    if (hasTx && m2 == 0) unmarkedWithTransmitter++;
                }
            TestContext.WriteLine($"antenna census: {files.Length} cfg file(s), {parts} part(s), {marked} entry(ies) marked, {unmarkedWithTransmitter} with a transmitter but no marked entry");
            Assert.Greater(parts, 20);
            Assert.Greater(marked, 0);
        }
    }
}
