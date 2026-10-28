using System;
using KSP.IO;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using Newtonsoft.Json;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>The part definition carries <see cref="Data_Ksp1Animations"/> as JSON: <c>PartJson</c>, the OAB's
    /// per-part copy (<c>SetDataModuleValues</c>) and the simulation's merge all go through Newtonsoft with the game's
    /// settings. NaN tangents (a key given as time and value only) must survive.</summary>
    public class DataJsonTests
    {
        private static Data_Ksp1Animations SampleData()
        {
            var d = new Data_Ksp1Animations();
            d.Entries.Add(new AnimEntryDef
            {
                Kind = EntryKind.Toggle, Key = "toggle:door:0", Clip = "door", Layer = 1,
                Gates = new[]
                {
                    new B9Gate { SetId = "engineSwitch", InactiveIn = new[] { "A", "C" }, DefaultSubtype = "B", SubtypeIds = new[] { "A", "B", "C" } },
                    new B9Gate { SetId = null, InactiveIn = new[] { "x" }, DefaultSubtype = "y", SubtypeIds = new[] { "x", "y" } },
                },
            });
            d.Entries.Add(new AnimEntryDef
            {
                Kind = EntryKind.Color, Key = "color:_EmissiveColor:0", ModuleIndex = 3, ShaderProperty = "_EmissiveColor",
                Curves = new[]
                {
                    new[] { 0f, 0f, 0f, 0f, 1f, 1f, 0f, 0f },
                    new[] { 0f, 0.5f, 0f, 0f },
                    new[] { 0f, 0.2f, 1.5f, float.NaN },
                    new[] { 0f, 1f, float.NaN, float.NaN },
                },
                IncludedRenderers = new[] { "lamp" },
            });
            d.ReduxClips.Add("solar");
            d.State.SetValue("toggle:door:0.t=1");
            return d;
        }

        private static void AssertSame(Data_Ksp1Animations d, Data_Ksp1Animations back)
        {
            Assert.AreEqual(d.Entries.Count, back.Entries.Count);
            Assert.IsTrue(float.IsNaN(back.Entries[1].Curves[2][3]), "NaN tangent kept");
            Assert.AreEqual(d.Entries[1].Curves[2][3], back.Entries[1].Curves[2][3]);
            Assert.AreEqual(1.5f, back.Entries[1].Curves[2][2]);
            Assert.AreEqual(EntryKind.Color, back.Entries[1].Kind);
            Assert.AreEqual("door", back.Entries[0].Clip);
            CollectionAssert.AreEqual(new[] { "lamp" }, back.Entries[1].IncludedRenderers);
            CollectionAssert.AreEqual(new[] { "solar" }, back.ReduxClips);
            Assert.IsNull(back.Entries[1].Gates, "no gates stays null");
            var g = back.Entries[0].Gates;
            Assert.AreEqual(2, g.Length);
            Assert.AreEqual(("engineSwitch", "B"), (g[0].SetId, g[0].DefaultSubtype));
            CollectionAssert.AreEqual(new[] { "A", "C" }, g[0].InactiveIn);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, g[0].SubtypeIds);
            Assert.IsNull(g[1].SetId, "null set id kept");
            Assert.AreEqual("y", g[1].DefaultSubtype);
            CollectionAssert.AreEqual(new[] { "x" }, g[1].InactiveIn);
            CollectionAssert.AreEqual(new[] { "x", "y" }, g[1].SubtypeIds);
        }

        /// <summary>IOProvider's settings are built by a RuntimeInitializeOnLoadMethod, which EditMode does not run:
        /// built here, and the global Newtonsoft default it sets is put back afterwards.</summary>
        private static void WithGameSettings(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                test();
            }
            finally { JsonConvert.DefaultSettings = previous; }
        }

        [Test] public void DataJson_RoundTripsThroughIOProvider() => WithGameSettings(() =>
        {
            var d = SampleData();
            var back = IOProvider.FromJson<Data_Ksp1Animations>(IOProvider.ToJson(d));
            AssertSame(d, back);
            Assert.AreEqual("toggle:door:0.t=1", back.State.GetValue());
        });

        /// <summary>What the OAB does for each new part (<c>PartBehaviourModule.SetDataModuleValues</c>) and the
        /// simulation for the definition (<c>MergeJsonInto</c>): definition members applied onto an existing data
        /// object. The list must be replaced, not appended to.</summary>
        [Test] public void DataJson_ApplyConfigOntoExisting_ReplacesEntries() => WithGameSettings(() =>
        {
            var d = SampleData();
            var existing = new Data_Ksp1Animations();
            existing.Entries.Add(new AnimEntryDef { Clip = "stale" });
            existing.ReduxClips.Add("stale");
            ModuleStateJsonConverter.ApplyConfig(SerializedModuleData.CaptureStateJson(d), existing,
                JsonSerializer.Create(IOProvider.GetDontDeserializeKspStateSerializerSettings()));
            AssertSame(d, existing);
        });

        [Test] public void DataJson_RoundTripsThroughNewtonsoft()
        {
            var d = SampleData();
            var settings = new JsonSerializerSettings();
            var back = JsonConvert.DeserializeObject<Data_Ksp1Animations>(JsonConvert.SerializeObject(d, settings), settings);
            AssertSame(d, back);
        }

        [Test] public void Data_ModuleType_IsOurBehaviour() =>
            Assert.AreEqual(typeof(Module_Ksp1Animations), new Data_Ksp1Animations().ModuleType);

        [Test] public void Component_PointsAtBehaviour() =>
            Assert.AreEqual(typeof(Module_Ksp1Animations), new PartComponentModule_Ksp1Animations().PartBehaviourModuleType);

        /// <summary>A module whose data field was never assigned (definition not read) must not throw during part
        /// setup; it gets an empty data object. <c>InitForDataModules</c> is the game's public entry to
        /// <c>AddDataModules</c>.</summary>
        [Test] public void Module_NoData_SetupDoesNotThrowAndUsesEmptyData()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // OnDestroy logs, the game being absent
            var go = new UnityEngine.GameObject("ksp1anim-test");
            try
            {
                Module_Ksp1Animations module;
                try { module = go.AddComponent<Module_Ksp1Animations>(); }
                catch (Exception e) { Assert.Ignore("the module cannot be added in EditMode: " + e.Message); return; }
                Assert.DoesNotThrow(() => module.InitForDataModules());
                Assert.IsTrue(module.DataModules.TryGetByType<Data_Ksp1Animations>(out var data));
                CollectionAssert.IsEmpty(data.Entries);
            }
            finally
            {
                try { UnityEngine.Object.DestroyImmediate(go); }
                catch (Exception) { /* OnDestroy reaches for the game, absent in EditMode */ }
            }
        }

        [Test] public void SerializedModuleData_NamedAfterOurDataType() =>
            Assert.AreEqual(nameof(Data_Ksp1Animations), new SerializedModuleData(new Data_Ksp1Animations()).Name);
    }
}
