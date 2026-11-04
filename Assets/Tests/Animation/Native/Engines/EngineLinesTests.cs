using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Engines
{
    /// <summary>The engine lines of <see cref="EngineSizeLines"/> (0.16.0).</summary>
    public class EngineLinesTests
    {
        [Test] public void Lines()
        {
            Assert.AreEqual("[KSP1PartMod] engines: modes armed (sep_procedural_engines on, ksp1_engine_modes_fallback off)", EngineSizeLines.EnginesArmed(true, false));
            Assert.AreEqual("[KSP1PartMod] engines: SEP_25: ModuleSEPEngineSwitch order AllEngines, MiddleInner, CenterThree",
                EngineSizeLines.SepOrder("SEP_25", new[] { "AllEngines", "MiddleInner", "CenterThree" }));
            Assert.AreEqual("[KSP1PartMod] engines: bluedog_LR91_3: 2 engine modules, no switch: modes basicEngine, vernier (mode 0 88.7 kN, KSP1 sum 98.7 kN)",
                EngineSizeLines.FallbackModes("bluedog_LR91_3", 2, new[] { "basicEngine", "vernier" }, 88.7, 98.7));
            Assert.AreEqual("[KSP1PartMod] engines: aj260: duplicate engineID 'Engine' -> 'Engine_2'", EngineSizeLines.DuplicateId("aj260", "Engine", "Engine_2"));
            Assert.AreEqual("; 3 engine edit(s) now on a multi-mode engine, 1 dropped on SEP clusters", EngineSizeLines.B9Engines(3, 1));
        }

        [Test] public void Summary()
        {
            var t = new EngineSizeLines.EngineTally();
            t.Sep.Add("SEP_25");
            t.Fallback.Add("a");
            t.Fallback.Add("b");
            Assert.AreEqual("[KSP1PartMod] engines: summary: 1 SEP part(s), 2 fallback part(s), 0 refused", EngineSizeLines.EngineSummary(t));
            t.Refused.Add("SEP_23");
            StringAssert.EndsWith("1 refused (SEP_23)", EngineSizeLines.EngineSummary(t));
        }
    }
}
