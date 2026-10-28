using System.Collections.Generic;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class EngineTooltipTests
    {
        private static KeyValuePair<string, string> Kv(string k, string v) => new KeyValuePair<string, string>(k, v);

        private static List<KeyValuePair<string, string>> Sample() => EngineTooltip.Lines(" - ",
            "Max Thrust", new[] { new EngineTooltip.SubEntry("Kerbin ASL", "205 kN"), new EngineTooltip.SubEntry("Vac", "215 kN") },
            "ISP", new[] { new EngineTooltip.SubEntry("Kerbin ASL", "290 s") },
            "Propellants", "Methalox");

        [Test] public void Lines_AsTheGameJoinsThem_ThrustThenIspThenPropellant()
        {
            CollectionAssert.AreEqual(new[]
            {
                Kv("Max Thrust - Kerbin ASL", "205 kN"), Kv("Max Thrust - Vac", "215 kN"),
                Kv("ISP - Kerbin ASL", "290 s"), Kv("Propellants", "Methalox"),
            }, Sample());
        }

        [Test] public void Lines_EmptySeparatorAndSubName_LikeStringJoin()
        {
            var lines = EngineTooltip.Lines("", "Max Thrust", new[] { new EngineTooltip.SubEntry("", "1 kN") }, "ISP", null, "P", null);
            CollectionAssert.AreEqual(new[] { Kv(string.Join("", "Max Thrust", ""), "1 kN") }, lines);
        }

        [Test] public void Lines_NullSeparator_TreatedAsEmpty() =>
            Assert.AreEqual("ISPVac", EngineTooltip.Lines(null, "T", null, "ISP", new[] { new EngineTooltip.SubEntry("Vac", "1") }, "P", "")[0].Key);

        [Test] public void Merge_PutsEngineLinesBeforeResources()
        {
            var info = new List<KeyValuePair<string, string>> { Kv("Liquid Fuel", "10 t") };
            Assert.AreEqual(4, EngineTooltip.Merge(info, Sample()));
            Assert.AreEqual("Max Thrust - Kerbin ASL", info[0].Key);
            Assert.AreEqual("Liquid Fuel", info[4].Key);
        }

        [Test] public void Merge_SkipsWhenTheGameAlreadyShowsThem()
        {
            var info = new List<KeyValuePair<string, string>> { Kv("Max Thrust - Kerbin ASL", "205 kN") };
            Assert.AreEqual(0, EngineTooltip.Merge(info, Sample()));
            Assert.AreEqual(1, info.Count);
        }

        [Test] public void Merge_NothingToAdd()
        {
            var info = new List<KeyValuePair<string, string>>();
            Assert.AreEqual(0, EngineTooltip.Merge(info, new List<KeyValuePair<string, string>>()));
            Assert.AreEqual(0, EngineTooltip.Merge(null, Sample()));
            Assert.AreEqual(0, EngineTooltip.Merge(info, null));
        }
    }
}
