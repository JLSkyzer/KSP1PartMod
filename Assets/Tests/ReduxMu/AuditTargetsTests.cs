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
    }
}
