using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ModuleManager
{
    public class MmPluginRuleTests
    {
        private static readonly string[] Curated = MmPluginRule.DefaultCurated.ToArray();
        private static readonly string[] Keep = MmPluginRule.DefaultKeep.ToArray();
        private static readonly string[] NoAuto = new string[0];

        private static Func<string, bool> BlockedBy(string[] auto) => n => MmPluginRule.Blocked(n, Curated, Keep, auto);

        private static Func<string, bool> Set(params string[] names)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        [Test] public void Blocked_Curated() => Assert.IsTrue(MmPluginRule.Blocked("RealFuels", Curated, Keep, NoAuto));

        [Test] public void Blocked_CaseInsensitive() => Assert.IsTrue(MmPluginRule.Blocked("realfuels", Curated, Keep, NoAuto));

        [Test] public void Blocked_NotListed_False() => Assert.IsFalse(MmPluginRule.Blocked("Bluedog_DB", Curated, Keep, NoAuto));

        [Test] public void Blocked_KeptEvenWithDll() =>
            Assert.IsFalse(MmPluginRule.Blocked("B9PartSwitch", Curated, Keep, new[] { "B9PartSwitch", "Kopernicus" }));

        [Test] public void Blocked_KeepWinsOverCurated() =>
            Assert.IsFalse(MmPluginRule.Blocked("RealFuels", Curated, new[] { "RealFuels" }, NoAuto));

        [Test] public void Blocked_AutoFolder() => Assert.IsTrue(MmPluginRule.Blocked("Kopernicus", Curated, Keep, new[] { "Kopernicus" }));

        [Test] public void Blocked_PathFormFollowsFirstSegment()
        {
            Assert.IsFalse(MmPluginRule.Blocked("Bluedog_DB/Parts/Generic", Curated, Keep, NoAuto));
            Assert.IsTrue(MmPluginRule.Blocked("Bluedog_DB/Parts/Generic", new[] { "Bluedog_DB" }, Keep, NoAuto));
            Assert.IsTrue(MmPluginRule.Blocked("RealFuels\\Engine_Configs", Curated, Keep, NoAuto));
        }

        [Test] public void Blocked_EmptyName_False()
        {
            Assert.IsFalse(MmPluginRule.Blocked("", Curated, Keep, NoAuto));
            Assert.IsFalse(MmPluginRule.Blocked(null, Curated, Keep, NoAuto));
        }

        [Test] public void AutoFolder_DllWithoutPart()
        {
            Assert.IsTrue(MmPluginRule.AutoFolder(true, false));
            Assert.IsFalse(MmPluginRule.AutoFolder(true, true));
            Assert.IsFalse(MmPluginRule.AutoFolder(false, false));
        }

        [Test] public void NeedsTerm_NegatedTrueWhenBlocked()
        {
            var blocked = BlockedBy(NoAuto);
            // the RealFuels folder exists, but the name is blocked
            Func<string, bool> available = n => !blocked(n) && Set("RealFuels", "Bluedog_DB")(n);
            Assert.IsTrue(MmPluginRule.NeedsMet("!RealFuels", available));
            Assert.IsFalse(MmPluginRule.NeedsMet("RealFuels", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("Bluedog_DB,!RealFuels", available));
            Assert.IsFalse(MmPluginRule.NeedsMet("SystemHeat,!RealFuels", available));
        }

        [Test] public void NeedsMet_AndOrNot()
        {
            var available = Set("A", "B");
            Assert.IsTrue(MmPluginRule.NeedsMet("A,B", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("A&B", available));
            Assert.IsFalse(MmPluginRule.NeedsMet("A,C", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("C|A", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("A, C|B", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("!C", available));
            Assert.IsFalse(MmPluginRule.NeedsMet("!A", available));
            Assert.IsTrue(MmPluginRule.NeedsMet("", available));
        }

        [Test] public void NeedsTerms_EveryTermOfEveryNeeds()
        {
            CollectionAssert.AreEqual(new[] { "SystemHeat", "!RealFuels", "B", "C" },
                MmPluginRule.NeedsTerms("@PART[x]:NEEDS[SystemHeat,!RealFuels]:HAS[#a[b]]:NEEDS[B|C]").ToArray());
            CollectionAssert.IsEmpty(MmPluginRule.NeedsTerms("@PART[x]:HAS[#a[NEEDS]]").ToArray());
        }

        [Test] public void WithoutNeeds_StripsTheTag()
        {
            Assert.AreEqual("@mass *", MmPluginRule.WithoutNeeds("@mass:NEEDS[RealFuels] *"));
            Assert.AreEqual("@PART[x]:FOR[A]", MmPluginRule.WithoutNeeds("@PART[x]:NEEDS[B,!C]:FOR[A]"));
            Assert.AreEqual("key", MmPluginRule.WithoutNeeds("key"));
        }

        [Test] public void Tags_TopLevelOnly()
        {
            var tags = MmPluginRule.Tags("@PART[a:FOR[x]]:HAS[@MODULE[b]]:AFTER[Y]:FINAL").ToArray();
            CollectionAssert.AreEqual(new[] { "HAS", "AFTER", "FINAL" }, tags.Select(t => t.Key).ToArray());
            CollectionAssert.AreEqual(new[] { "@MODULE[b]", "Y", null }, tags.Select(t => t.Value).ToArray());
        }

        [Test] public void ForNames_HarvestsEveryFor()
        {
            var names = MmPluginRule.ForNames(new[] { "@PART[*]:FOR[zzBluedog_DB]", "PART", "@PART[a]:for[RealFuels]:NEEDS[X]", "@PART[b]:AFTER[Q]" });
            Assert.AreEqual(2, names.Count);
            Assert.IsTrue(names.Contains("zzbluedog_db"));
            Assert.IsTrue(names.Contains("RealFuels"));
        }

        // ---- root patches ----

        private static MmDrop Drop(string rootName, string fileMod, bool isPatch, ICollection<string> forNames,
            bool beforeAfterRule = true) =>
            MmPluginRule.RootPatchDrop(rootName, fileMod, isPatch, BlockedBy(NoAuto), Set("Bluedog_DB", "Squad", "RealFuels"),
                forNames, beforeAfterRule);

        private static readonly HashSet<string> NoFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Test] public void RootPatch_PartFromBlockedFolder_Kept() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("PART", "RealFuels", false, NoFor).Reason);

        [Test] public void RootPatch_PatchFromBlockedFolder_Dropped()
        {
            var d = Drop("@PART[*]:HAS[@MODULE[ModuleEngines*]]", "RealFuels", true, NoFor);
            Assert.AreEqual(MmDropReason.DropBlockedFolder, d.Reason);
            Assert.AreEqual("RealFuels", d.Mod);
        }

        [Test] public void RootPatch_ForBlocked_Dropped()
        {
            var d = Drop("@PART[*]:FOR[RealFuels]", "Bluedog_DB", true, NoFor);
            Assert.AreEqual(MmDropReason.DropForBlocked, d.Reason);
            Assert.AreEqual("RealFuels", d.Mod);
        }

        [Test] public void RootPatch_BeforeBlocked_Dropped() =>
            Assert.AreEqual(MmDropReason.DropBeforeAfterBlocked, Drop("@PART[*]:BEFORE[RealFuels]", "Bluedog_DB", true, NoFor).Reason);

        [Test] public void RootPatch_AfterForName_Kept()
        {
            var forNames = MmPluginRule.ForNames(new[] { "@PART[bluedog*]:FOR[zzBluedog_DB]" });
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]:AFTER[zzBluedog_DB]", "Bluedog_DB", true, forNames).Reason);
        }

        [Test] public void RootPatch_BeforeAbsent_Dropped()
        {
            var d = Drop("@PART[*]:BEFORE[RemoteTech]", "Bluedog_DB", true, NoFor);
            Assert.AreEqual(MmDropReason.DropBeforeAfterAbsent, d.Reason);
            Assert.AreEqual("RemoteTech", d.Mod);
        }

        [Test] public void RootPatch_ForNames_IgnoreCaseWhateverTheSet()
        {
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]:AFTER[ZZBLUEDOG_DB]", "Bluedog_DB", true, new List<string> { "zzBluedog_DB" }).Reason);
            Assert.AreEqual(MmDropReason.Keep,
                Drop("@PART[*]:AFTER[zzbluedog_db]", "Bluedog_DB", true, new HashSet<string>(StringComparer.Ordinal) { "zzBluedog_DB" }).Reason);
        }

        [Test] public void RootPatch_BeforeAbsent_KeptWhenRuleOff() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]:BEFORE[SomeAbsentMod]", "Bluedog_DB", true, NoFor, beforeAfterRule: false).Reason);

        [Test] public void RootPatch_BeforeAbsentAndCurated_BlockedWhenRuleOff()
        {
            var d = Drop("@PART[*]:BEFORE[RemoteTech]", "Bluedog_DB", true, NoFor, beforeAfterRule: false);
            Assert.AreEqual(MmDropReason.DropBeforeAfterBlocked, d.Reason);
            Assert.AreEqual("RemoteTech", d.Mod);
        }

        [Test] public void RootPatch_AfterBlockedButForName_Blocked()
        {
            // a FOR name counts as present, so only the safety net drops it
            var forNames = MmPluginRule.ForNames(new[] { "@PART[x]:FOR[TweakScale]" });
            Assert.AreEqual(MmDropReason.DropBeforeAfterBlocked, Drop("@PART[*]:AFTER[TweakScale]", "Bluedog_DB", true, forNames).Reason);
        }

        [Test] public void RootPatch_BlockedStillDroppedWhenAbsentRuleOff() =>
            Assert.AreEqual(MmDropReason.DropForBlocked, Drop("@PART[*]:FOR[RealFuels]", "Bluedog_DB", true, NoFor, beforeAfterRule: false).Reason);

        [Test] public void RootPatch_LastAbsent_Kept() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]:LAST[RemoteTech]", "Bluedog_DB", true, NoFor).Reason);

        [Test] public void RootPatch_AfterAvailable_Kept() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]:AFTER[Bluedog_DB]", "Bluedog_DB", true, NoFor).Reason);

        [Test] public void RootPatch_NotAPatch_NeverDropped() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("PART:FOR[RealFuels]", "RealFuels", false, NoFor).Reason);

        [Test] public void RootPatch_NullFileMod_Fine() =>
            Assert.AreEqual(MmDropReason.Keep, Drop("@PART[*]", null, true, NoFor).Reason);

        [Test] public void Defaults_AreThePlanLists()
        {
            CollectionAssert.AreEqual(new[]
            {
                "RealFuels", "RealFuels-Stockalike", "SolverEngines", "AJE", "AJEExtended", "FerramAerospaceResearch",
                "GroundEffectFAR", "RealismOverhaul", "Kerbalism", "KerbalismConfig", "SystemHeat", "RealAntennas",
                "RemoteTech", "TweakScale", "TestFlight", "EngineIgnitor", "DeadlyReentry", "KerbalJointReinforcement",
                "RealPlume", "SmokeScreen",
            }, MmPluginRule.DefaultCurated.ToArray());
            CollectionAssert.AreEqual(new[]
            {
                "B9PartSwitch", "CommunityResourcePack", "Waterfall", "DeployableEngines", "B9AnimationModules",
                "DMagicScienceAnimate", "AnimatedDecouplers", "ReStock", "SimpleAdjustableFairings", "ModuleManager",
            }, MmPluginRule.DefaultKeep.ToArray());
        }
    }
}
