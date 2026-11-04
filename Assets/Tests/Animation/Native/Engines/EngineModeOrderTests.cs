using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native.Engines;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Engines
{
    /// <summary><see cref="EngineModeOrder"/> (0.16.0, Task 1). Cfg text written for the tests, in the shape of KSP1's files.</summary>
    public class EngineModeOrderTests
    {
        internal static IKsp1Node PartOf(string cfg) => CfgText.Parse(cfg).GetNodes("PART").First();

        private static string[] Ids(IKsp1Node part, int[] order)
        {
            var modules = EngineModeOrder.Modules(part);
            return order.Select(i => EngineModeOrder.EngineIdOf(modules[i])).ToArray();
        }

        internal static string Engine(string id, string transform, double thrust) =>
            "MODULE\n{\nname = ModuleEnginesFX\n" + (id == null ? "" : "engineID = " + id + "\n") +
            "thrustVectorTransformName = " + transform + "\nmaxThrust = " + thrust.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\nPROPELLANT\n{\nname = LiquidFuel\nratio = 0.9\n}\nPROPELLANT\n{\nname = Oxidizer\nratio = 1.1\n}\natmosphereCurve\n{\nkey = 0 320\nkey = 1 280\n}\n}\n";

        /// <summary>The switch first, the engines after, as in SEP's files (the switch's ids out of the engines' cfg order).</summary>
        internal const string SepSwitch = "MODULE\n{\nname = ModuleSEPEngineSwitch\nEngineID = AllEngines\nEngineID = MiddleInner\nEngineID = CenterThree\n}\n";

        [Test] public void Sep_SwitchFirst_GivesTheSwitchOrder()
        {
            var part = PartOf("PART\n{\nname = sep\n" + SepSwitch + Engine("CenterThree", "CenterThrustTransform", 1000) +
                              Engine("AllEngines", "OuterThrustTransform", 1000) + Engine("MiddleInner", "MiddleThrustTransform", 1000) + "}\n");
            CollectionAssert.AreEqual(new[] { "AllEngines", "MiddleInner", "CenterThree" }, Ids(part, EngineModeOrder.Sep(part)));
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, EngineModeOrder.Sep(part), "MODULE indices");
            CollectionAssert.IsEmpty(EngineModeOrder.Fallback(part), "SEP's switch is a switch");
        }

        [Test] public void Sep_AnIdNoEngineHas_IsSkipped_OthersFollow()
        {
            var part = PartOf("PART\n{\nname = sep\nMODULE\n{\nname = ModuleSEPEngineSwitch\nEngineID = Ghost\nEngineID = middleinner\n}\n" +
                              Engine("AllEngines", "a", 1) + Engine("MiddleInner", "b", 1) + "}\n");
            CollectionAssert.AreEqual(new[] { "MiddleInner", "AllEngines" }, Ids(part, EngineModeOrder.Sep(part)));
        }

        [Test] public void Sep_OneEngine_OrNoSwitch_IsEmpty()
        {
            CollectionAssert.IsEmpty(EngineModeOrder.Sep(PartOf("PART\n{\nname = a\n" + SepSwitch + Engine("AllEngines", "t", 1) + "}\n")));
            CollectionAssert.IsEmpty(EngineModeOrder.Sep(PartOf("PART\n{\nname = a\n" + Engine("x", "t", 1) + Engine("y", "t", 1) + "}\n")));
        }

        [Test] public void Fallback_Lr91Shape_MainThenVernier()
        {
            var part = PartOf("PART\n{\nname = bluedog_LR91_3\n" + Engine("basicEngine", "thrustTransform", 88.7) +
                              "MODULE\n{\nname = ModuleGimbal\n}\n" + Engine("vernier", "vernierTransform", 10) + "}\n");
            CollectionAssert.AreEqual(new[] { "basicEngine", "vernier" }, Ids(part, EngineModeOrder.Fallback(part)));
            CollectionAssert.IsEmpty(EngineModeOrder.Sep(part));
        }

        [Test] public void Fallback_Aj260Shape_NoIds_GivesEngineAndEngine2()
        {
            var part = PartOf("PART\n{\nname = aj260\n" + Engine(null, "thrustTransform", 3870) + Engine(null, "vernierTransform", 20) + "}\n");
            var ids = Ids(part, EngineModeOrder.Fallback(part));
            CollectionAssert.AreEqual(new[] { "Engine", "Engine" }, ids, "Redux's own default id");
            CollectionAssert.AreEqual(new[] { "Engine", "Engine_2" }, EngineModeOrder.UniqueIds(ids));
        }

        [Test] public void Fallback_PartWithAMultiModeEngine_IsEmpty_ReduxsOwn()
        {
            var part = PartOf("PART\n{\nname = ua120\n" + Engine("S1SRB", "a", 1311.4) + Engine("S1SRB_Abort", "b", 1311.4) +
                              "MODULE\n{\nname = MultiModeEngine\nprimaryEngineID = S1SRB\nsecondaryEngineID = S1SRB_Abort\n}\n}\n");
            CollectionAssert.IsEmpty(EngineModeOrder.Fallback(part));
            CollectionAssert.IsEmpty(EngineModeOrder.Sep(part));
        }

        [Test] public void Fallback_OneEngine_IsEmpty() =>
            CollectionAssert.IsEmpty(EngineModeOrder.Fallback(PartOf("PART\n{\nname = a\n" + Engine("x", "t", 1) + "}\n")));

        [Test] public void UniqueIds_ThirdAndCase_AndNoCollision()
        {
            CollectionAssert.AreEqual(new[] { "Engine", "engine_2", "Engine_3" }, EngineModeOrder.UniqueIds(new[] { "Engine", "engine", null }),
                "a duplicate in another case is a duplicate; the suffix keeps the module's own spelling");
            CollectionAssert.AreEqual(new[] { "A", "A_3", "A_2" }, EngineModeOrder.UniqueIds(new[] { "A", "A", "A_2" }));
            CollectionAssert.AreEqual(new[] { "main", "vernier" }, EngineModeOrder.UniqueIds(new[] { "main", "vernier" }));
        }
    }
}
