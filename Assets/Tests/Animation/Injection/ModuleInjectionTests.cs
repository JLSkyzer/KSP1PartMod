using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Injection;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Injection
{
    public class ModuleInjectionTests
    {
        private const string Ours = "PartComponentModule_Ksp1Animations";

        private static InjectionAction Decide(IReadOnlyList<string> names, PartAnimationPlan plan) =>
            ModuleInjection.Decide(names, plan, Ours);

        private static PartAnimationPlan PlanWith(int entries, params string[] reduxClips) =>
            new PartAnimationPlan(
                Enumerable.Range(0, entries).Select(i => new AnimEntryDef { Kind = EntryKind.Toggle, Clip = "c" + i }).ToList(),
                reduxClips, null, false, new string[0]);

        private static PartAnimationPlan PlanSkippedForNative() =>
            OwnershipPlanner.Plan(new[] { CfgText.Module("name = ModuleDeployableAntenna\nanimationName = dish") }, false,
                new HashSet<string> { "Module_Antenna" }, false,
                new Dictionary<string, string[]> { ["ModuleDeployableAntenna"] = new[] { "Module_Antenna" } });

        [Test] public void Inject_NewPartWithEntries_Adds() => Assert.AreEqual(InjectionAction.Add, Decide(new[] { "Module_Engine" }, PlanWith(1)));
        [Test] public void Inject_Twice_AddsOnceAndRefreshes() => Assert.AreEqual(InjectionAction.Refresh, Decide(new[] { Ours }, PlanWith(1)));
        [Test] public void Inject_NoEntries_NotPresent_None() => Assert.AreEqual(InjectionAction.None, Decide(new string[0], PlanWith(0)));
        [Test] public void Inject_NoEntries_Present_RefreshesToEmpty() => Assert.AreEqual(InjectionAction.Refresh, Decide(new[] { "Module_Drag", Ours }, PlanWith(0)));
        [Test] public void Inject_ReduxNativeModulePresent_Skips()
        {
            var plan = PlanSkippedForNative();
            CollectionAssert.IsEmpty(plan.Entries);
            Assert.AreEqual(InjectionAction.None, Decide(new[] { "Module_Antenna" }, plan));
        }
        [Test] public void Inject_NullNames_TreatedAsEmpty() => Assert.AreEqual(InjectionAction.Add, Decide(null, PlanWith(2)));

        [Test] public void Summary_CountsPartsEntriesKindsReduxAndNone()
        {
            var group = new PartAnimationPlan(new[] { new AnimEntryDef { Kind = EntryKind.Group } }, new string[0], null, false, new string[0]);
            var line = ModuleInjection.SummaryLine("Squad", new[] { PlanWith(2, "door"), PlanWith(0, "legs"), PlanWith(0), group });
            Assert.AreEqual("[KSP1PartMod] anim: mod Squad: 4 part(s), 3 entries (Toggle 2, Group 1), 2 left to Redux, 1 none", line);
        }

        [Test] public void Summary_OneEntry_Singular() =>
            Assert.AreEqual("[KSP1PartMod] anim: mod KRE: 1 part(s), 1 entry (Toggle 1), 0 left to Redux, 0 none",
                ModuleInjection.SummaryLine("KRE", new[] { PlanWith(1) }));

        [Test] public void Followers_CountsDeployablesAndChutes()
        {
            var p = new PartAnimationPlan(new[] { new AnimEntryDef { Kind = EntryKind.ReduxFollower }, new AnimEntryDef { Kind = EntryKind.GearBridge } },
                new string[0], null, false, new string[0]);
            var c = new PartAnimationPlan(new[] { new AnimEntryDef { Kind = EntryKind.ChuteFollower } }, new string[0], null, false, new string[0]);
            Assert.AreEqual("[KSP1PartMod] anim: mod KRE: 2 Redux deployable(s) followed, 1 Redux parachute(s) followed",
                ModuleInjection.FollowerLine("KRE", new[] { p, p, c, PlanWith(1) }));
        }

        [Test] public void Summary_NoEntries_NoKindList() =>
            Assert.AreEqual("[KSP1PartMod] anim: mod ?: 1 part(s), 0 entries, 0 left to Redux, 1 none",
                ModuleInjection.SummaryLine(null, new[] { PlanWith(0) }));
    }
}
