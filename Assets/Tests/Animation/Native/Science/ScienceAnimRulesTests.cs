using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Science;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Science
{
    /// <summary><see cref="ScienceAnimRules"/>: which existing entry an experiment opens, when, and the value of KSP2's deploy gate
    /// (science plan (i), Task 5). No entry is planned here.</summary>
    public class ScienceAnimRulesTests
    {
        private static AnimEntryDef Entry(EntryKind kind, int module, string key) => new AnimEntryDef { Kind = kind, ModuleIndex = module, Key = key };

        private static ScienceConfiguration Stock(params int[] fx) =>
            ScienceConfiguration.ForTests("EnvironmentSurvey", new ScienceClip(fx, -1, false, false, 0));

        private static ScienceConfiguration Toggle(int module, bool experimentAnimation = true) =>
            ScienceConfiguration.ForTests("KSP1_bd_x", new ScienceClip(new int[0], module, experimentAnimation, true, 1));

        private static readonly List<AnimEntryDef> Entries = new List<AnimEntryDef>
        {
            Entry(EntryKind.Toggle, 0, "toggle:Lights:0"),
            Entry(EntryKind.Deploy, 1, "deploy:Deploy:0"),
            Entry(EntryKind.ReduxFollower, 2, "follow:Open:0"),
            Entry(EntryKind.ScienceToggle, 3, "science:Boom:0"),
        };

        [Test] public void Stock_DeployEntryOfTheFxModule()
        {
            var t = ScienceAnimRules.TargetsFor(Stock(1), Entries, true);
            Assert.AreEqual(ScienceNoClip.None, t.Reason);
            Assert.AreEqual("deploy:Deploy:0", t.Entries.Single().Key);
            Assert.AreEqual("deploy:Deploy:0", t.First.Key);
        }

        [Test] public void Stock_ReduxFollowerEntryOfTheFxModule()
        {
            Assert.AreEqual("follow:Open:0", ScienceAnimRules.TargetsFor(Stock(2), Entries, true).First.Key);
            CollectionAssert.AreEqual(new[] { "deploy:Deploy:0", "follow:Open:0" },
                ScienceAnimRules.TargetsFor(Stock(1, 2), Entries, true).Entries.Select(e => e.Key).ToArray());
        }

        [Test] public void Stock_OnlyDeployOrFollower()
        {
            var t = ScienceAnimRules.TargetsFor(Stock(0), Entries, true);
            Assert.AreEqual(ScienceNoClip.NoEntry, t.Reason, "a Toggle entry is not a deploy clip");
            Assert.IsNull(t.First);
            Assert.AreEqual(ScienceNoClip.NoEntry, ScienceAnimRules.TargetsFor(Stock(3), Entries, true).Reason, "a ScienceToggle needs the DMagic route");
        }

        [Test] public void DMagic_ScienceToggleOfItsModule()
        {
            Assert.AreEqual("science:Boom:0", ScienceAnimRules.TargetsFor(Toggle(3), Entries, true).First.Key);
            Assert.AreEqual(ScienceNoClip.NoEntry, ScienceAnimRules.TargetsFor(Toggle(1), Entries, true).Reason, "module 1 has a Deploy entry only");
            Assert.AreEqual(ScienceNoClip.NoExperimentAnimation, ScienceAnimRules.TargetsFor(Toggle(3, false), Entries, true).Reason);
        }

        [Test] public void NoSource_AnimationsOff()
        {
            Assert.AreEqual(ScienceNoClip.NoFxModules, ScienceAnimRules.TargetsFor(Stock(), Entries, true).Reason);
            Assert.AreEqual(ScienceNoClip.AnimationsOff, ScienceAnimRules.TargetsFor(Stock(1), Entries, false).Reason);
            Assert.AreEqual(ScienceNoClip.NoEntry, ScienceAnimRules.TargetsFor(Stock(1), null, true).Reason);
        }

        [Test] public void B9Gated_Inactive_NoTarget_Noted()
        {
            var gated = Entry(EntryKind.ScienceToggle, 3, "science:Boom:0");
            gated.Gates = new B9Gate[0];
            var entries = new List<AnimEntryDef> { gated };
            var t = ScienceAnimRules.TargetsFor(Toggle(3), entries, true, e => ReferenceEquals(e, gated));
            Assert.AreEqual(ScienceNoClip.GatedByB9, t.Reason);
            CollectionAssert.IsEmpty(t.Entries);
            Assert.IsTrue(t.Notes.Any(n => n.Contains("science:Boom:0") && n.Contains("B9")), string.Join("\n", t.Notes));
            Assert.AreEqual("science:Boom:0", ScienceAnimRules.TargetsFor(Toggle(3), entries, true, e => false).First.Key, "active");

            var both = new List<AnimEntryDef> { Entry(EntryKind.Deploy, 1, "deploy:A:0"), Entry(EntryKind.Deploy, 1, "deploy:B:1") };
            var partly = ScienceAnimRules.TargetsFor(Stock(1), both, true, e => e.Key == "deploy:A:0");
            Assert.AreEqual(ScienceNoClip.None, partly.Reason);
            Assert.AreEqual("deploy:B:1", partly.First.Key);
            Assert.AreEqual(1, partly.Notes.Count);
        }

        [Test] public void ShouldOpen()
        {
            Assert.IsTrue(ScienceAnimRules.ShouldOpen(ScienceRunState.READY, ScienceRunState.RUNNING));
            Assert.IsTrue(ScienceAnimRules.ShouldOpen(ScienceRunState.PAUSED, ScienceRunState.RUNNING));
            Assert.IsTrue(ScienceAnimRules.ShouldOpen(null, ScienceRunState.RUNNING), "loaded running: the clip opens");
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(ScienceRunState.RUNNING, ScienceRunState.RUNNING));
            Assert.IsTrue(ScienceAnimRules.ShouldOpen(ScienceRunState.READY, ScienceRunState.ALREADYSTORED), "a zero time run goes straight to a stored report");
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(ScienceRunState.RUNNING, ScienceRunState.ALREADYSTORED), "already opened by RUNNING");
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(ScienceRunState.ALREADYSTORED, ScienceRunState.ALREADYSTORED));
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(null, ScienceRunState.ALREADYSTORED), "loaded with a stored report: no run");
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(ScienceRunState.NONE, ScienceRunState.READY));
            Assert.IsFalse(ScienceAnimRules.ShouldOpen(ScienceRunState.RUNNING, ScienceRunState.INVALIDLOCATION));
        }

        [Test] public void RunState_MirrorsKsp2Values()
        {
            Assert.AreEqual(0, (int)ScienceRunState.NONE);
            Assert.AreEqual(3, (int)ScienceRunState.RUNNING);
            Assert.AreEqual(10, (int)ScienceRunState.ALREADYSTORED);
            Assert.AreEqual(11, (int)ScienceRunState.BLOCKED);
        }

        [Test] public void DeployGate()
        {
            Assert.IsTrue(ScienceAnimRules.DeployGate(false, false), "open and still");
            Assert.IsFalse(ScienceAnimRules.DeployGate(false, true), "opening");
            Assert.IsFalse(ScienceAnimRules.DeployGate(true, false), "closed");
            Assert.IsFalse(ScienceAnimRules.DeployGate(true, true));
        }

        [Test] public void FromTheMapping_ConfigurationsCarryTheirClip()
        {
            var modules = CfgText.Parse(@"
PART
{
    MODULE
    {
        name = ModuleAnimateGeneric
        animationName = Deploy
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = mysteryGoo
        xmitDataScalar = 0.3
        FxModules = 0
    }
}").GetNodes("PART").Single().GetNodes("MODULE").ToList();
            var part = ScienceMapping.Resolve(modules, new Dictionary<string, ScienceDefinitionModel>(), NativeExperiments.Redux26w42c, AnimationSettings.Defaults);
            var t = ScienceAnimRules.TargetsFor(part.Configurations.Single(), new[] { Entry(EntryKind.Deploy, 0, "deploy:Deploy:0") }, true);
            Assert.AreEqual("deploy:Deploy:0", t.First.Key);
        }
    }
}
