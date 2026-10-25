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
            d.Entries.Add(new AnimEntryDef { Kind = EntryKind.Toggle, Key = "toggle:door:0", Clip = "door", Layer = 1 });
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

        [Test] public void SerializedModuleData_NamedAfterOurDataType() =>
            Assert.AreEqual(nameof(Data_Ksp1Animations), new SerializedModuleData(new Data_Ksp1Animations()).Name);
    }
}
