using System;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim.Definitions;
using KSP1PartMod.Game.Audit;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c): every member the audit uses resolves by name.</summary>
    public class AuditTargetsTests
    {
        [Test] public void Resolve_FindsEveryTarget()
        {
            var targets = AuditTargets.Resolve(out var missing);
            Assert.IsNotNull(targets, "missing: " + missing);
            Assert.AreEqual("", missing);
            Assert.IsNotNull(targets.AllPossibleParts);
            Assert.IsNotNull(targets.ApplyOnGameObjectMethod);
            Assert.IsNotNull(targets.DestroyPrefabGraphMethod);
        }

        [Test] public void PrefabAddress_DefaultsToTheRegistryKey()
        {
            var targets = AuditTargets.Resolve(out var missing);
            Assert.IsNotNull(targets, missing);
            Assert.AreEqual("noSuchPartForTheAudit.prefab", targets.PrefabAddress("noSuchPartForTheAudit"));
        }

        [Test] public void IsPartModule_ExcludesTheBaseClass()
        {
            Assert.IsTrue(AuditSpawner.IsPartModule(typeof(Module_Engine)));
            Assert.IsFalse(AuditSpawner.IsPartModule(typeof(PartBehaviourModule)));
            Assert.IsFalse(AuditSpawner.IsPartModule(typeof(ObjectAssemblyPart)));
            Assert.IsFalse(AuditSpawner.IsPartModule(null));
        }

        [Test] public void EventCounts_ReportsGrowthOncePerField()
        {
            var events = new ObjectAssemblyBuilderEvents();
            var counts = new AuditRunner.EventCounts(events);
            Assert.AreEqual(0, counts.Compare().Count);
            events.OnInternalRefresh += () => { };
            var grew = counts.Compare();
            Assert.AreEqual(1, grew.Count);
            Assert.AreEqual("OnInternalRefresh", grew[0].Field);
            Assert.AreEqual(0, grew[0].Before);
            Assert.AreEqual(1, grew[0].After);
            events.OnInternalRefresh += () => { };
            Assert.AreEqual(0, counts.Compare().Count, "once per field");
        }

        private struct FakeHandle
        {
            public int Hash;
            public object Callback;
        }

        [Test] public void MessageCounts_CountsOnlyLiveHandlesAppendedDuringTheSpawn()
        {
            var subs = new System.Collections.Generic.Dictionary<Type, System.Collections.Generic.List<FakeHandle>>
            {
                { typeof(int), new System.Collections.Generic.List<FakeHandle> { new FakeHandle { Hash = 1 } } },
            };
            var counts = new AuditRunner.MessageCounts(subs);
            counts.Snapshot();
            subs[typeof(int)].Add(new FakeHandle { Hash = 2, Callback = this });   // kept: a leak
            subs[typeof(int)].Add(default(FakeHandle));                         // subscribed then unsubscribed
            subs[typeof(string)] = new System.Collections.Generic.List<FakeHandle> { default(FakeHandle) }; // new type, released
            subs[typeof(int)][0] = default(FakeHandle);                         // an older handle released: not ours
            var kept = counts.Compare();
            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual("Int32", kept[0].Type);
            Assert.AreEqual(1, kept[0].Count);
            counts.Snapshot();
            subs[typeof(int)].Add(new FakeHandle { Hash = 3 });
            Assert.AreEqual(0, counts.Compare().Count, "once per type");
        }

        [Test] public void MessageSubscriptions_IsAListOfStructHandlesPerType()
        {
            var targets = AuditTargets.Resolve(out var missing);
            Assert.IsNotNull(targets, missing);
            Assert.IsTrue(typeof(KSP.Messages.SubscriptionHandle).IsValueType);
            Assert.AreEqual(typeof(System.Collections.Generic.Dictionary<Type, System.Collections.Generic.List<KSP.Messages.SubscriptionHandle>>),
                targets.MessageSubscriptions.FieldType);
        }

        [Test] public void HarnessFault_RecordHasItsOwnKind()
        {
            var rec = AuditRunner.HarnessFault(new KSP1PartMod.Audit.AuditTarget("p", false), null, new InvalidOperationException("x"));
            Assert.AreEqual(KSP1PartMod.Audit.AuditKind.HarnessFault, rec.Kinds);
            Assert.AreEqual("audit: p (): harness_fault: InvalidOperationException in the audit harness: x", KSP1PartMod.Audit.AuditSummary.PartLine(rec));
        }
    }
}
