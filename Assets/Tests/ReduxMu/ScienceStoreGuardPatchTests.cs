using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.impl;
using KSP1PartMod.Game;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), Task 4 review I-1: the store guard's targets resolve, the prefix
    /// patches the real <c>PartComponentModule_ScienceExperiment.OnStart</c>, and on a real component with a real
    /// <c>Data_ScienceExperiment</c> it drops the <c>KSP1_*</c> configurations and standings the store lacks.</summary>
    public class ScienceStoreGuardPatchTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            ScienceStoreGuardPatch.ResetForTests();
            ScienceAccess.Reset();
            _lines.Clear();
            ScienceStoreGuardPatch.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            new Harmony(ScienceStoreGuardPatch.HarmonyId).UnpatchAll(ScienceStoreGuardPatch.HarmonyId);
            ScienceStoreGuardPatch.ResetForTests();
        }

        private static ExperimentConfiguration Config(string id) =>
            new ExperimentConfiguration { ExperimentDefinitionID = id, ResourcesCost = new List<KSP.Sim.Definitions.PartModuleResourceSetting>() };

        private static (PartComponentModule_ScienceExperiment, Data_ScienceExperiment) Component(string[] configs, string[] standings)
        {
            var data = new Data_ScienceExperiment { Experiments = configs.Select(Config).ToList() };
            data.ExperimentStandings.AddRange(standings.Select(id => new ExperimentStanding { ExperimentID = id }));
            var component = new PartComponentModule_ScienceExperiment();
            component.DataModules.Add(typeof(Data_ScienceExperiment), data);
            return (component, data);
        }

        [Test] public void Resolve_EveryTarget_AndThePatchApplies()
        {
            var t = ScienceStoreGuardPatch.Resolve();
            Assert.AreEqual("", string.Join(", ", t.Missing));
            Assert.AreEqual(typeof(PartComponentModule_ScienceExperiment), t.OnStart.DeclaringType);
            Assert.AreEqual(typeof(double), t.OnStart.GetParameters().Single().ParameterType);
            Assert.AreEqual(typeof(List<ExperimentStanding>), t.Standings.FieldType);
            Assert.AreEqual("ExperimentID", t.StandingId.Name);
            Assert.AreEqual("PartName", t.PartName.Name);
            Assert.IsTrue(ScienceStoreGuardPatch.TryApply(new Harmony(ScienceStoreGuardPatch.HarmonyId)));
            Assert.IsTrue(Harmony.GetPatchInfo(t.OnStart).Prefixes.Any(p => p.owner == ScienceStoreGuardPatch.HarmonyId));
            CollectionAssert.IsEmpty(_lines);
        }

        [Test] public void SavedVessel_MissingOursDropped_ListsStayAligned_LoggedOnce()
        {
            var t = ScienceStoreGuardPatch.Resolve();
            ScienceStoreGuardPatch.KnownSource = () => id => id == "CrewObservation";
            var ids = new[] { "KSP1_bd_a", "CrewObservation", "KSP1_bd_b" };
            var (component, data) = Component(ids, ids);
            CollectionAssert.AreEqual(new[] { "KSP1_bd_a", "KSP1_bd_b" }, ScienceStoreGuardPatch.Guard(component, t));
            CollectionAssert.AreEqual(new[] { "CrewObservation" }, data.Experiments.Select(e => e.ExperimentDefinitionID));
            CollectionAssert.AreEqual(new[] { "CrewObservation" }, data.ExperimentStandings.Select(s => s.ExperimentID));
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] science: ?: KSP1_bd_a not in the experiment store, dropped (definitions missing in this campaign)",
                "[KSP1PartMod] science: ?: KSP1_bd_b not in the experiment store, dropped (definitions missing in this campaign)",
            }, _lines);

            var (again, _) = Component(ids, ids);
            ScienceStoreGuardPatch.Guard(again, t);
            Assert.AreEqual(2, _lines.Count, "once per part and id");
        }

        [Test] public void StoreNotReady_OrAllKnown_NothingTouched()
        {
            var t = ScienceStoreGuardPatch.Resolve();
            var ids = new[] { "KSP1_bd_a", "CrewObservation" };
            ScienceStoreGuardPatch.KnownSource = () => null;
            var (component, data) = Component(ids, new string[0]);
            CollectionAssert.IsEmpty(ScienceStoreGuardPatch.Guard(component, t));
            Assert.AreEqual(2, data.Experiments.Count, "the store is not ready: KSP2 as is");

            ScienceStoreGuardPatch.KnownSource = () => id => true;
            var (known, kept) = Component(ids, ids);
            CollectionAssert.IsEmpty(ScienceStoreGuardPatch.Guard(known, t));
            Assert.AreEqual(2, kept.Experiments.Count);
            Assert.AreEqual(2, kept.ExperimentStandings.Count);
            CollectionAssert.IsEmpty(_lines);
        }
    }
}
