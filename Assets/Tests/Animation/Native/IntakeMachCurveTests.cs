using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>0.15.1: the cfg side of the intake mach curve fix (<see cref="EngineCurveLines"/>).</summary>
    public class IntakeMachCurveTests
    {
        private const string PartText =
            "PART\n{\nname = testIntake\n" +
            "MODULE\n{\nname = ModuleResourceIntake\nresourceName = IntakeAir\narea = 0.006\n" +
            "machCurve\n{\nkey = 1 1 0 0\nkey = 1.5 0.9 -0.4 -0.4 // comment\nkey = 3 0.5\n}\n}\n" +
            "MODULE\n{\nname = ModuleAnimateGeneric\n}\n" +
            "MODULE\n{\nname = ModuleResourceIntake\nresourceName = IntakeAir\n}\n}\n";

        private static IKsp1Node Part => CfgText.Parse(PartText).GetNodes("PART").First();

        [Test] public void IntakeModules_InCfgOrder_OthersSkipped()
        {
            var list = EngineCurveLines.IntakeModules(Part);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("0.006", list[0].GetValue("area"));
            Assert.IsEmpty(EngineCurveLines.IntakeModules(null));
        }

        [Test] public void CfgCurve_FourValueKeysKeepTangents_TwoValueKeysAreSmooth()
        {
            var keys = EngineCurveLines.IntakeMachKeys(EngineCurveLines.IntakeModules(Part)[0]);
            Assert.AreEqual(12, keys.Length);
            CollectionAssert.AreEqual(new[] { 1f, 1f, 0f, 0f, 1.5f, 0.9f, -0.4f, -0.4f }, keys.Take(8).ToArray());
            Assert.AreEqual(3f, keys[8]);
            Assert.AreEqual(0.5f, keys[9]);
            Assert.IsTrue(float.IsNaN(keys[10]) && float.IsNaN(keys[11]), "a two-value key goes through Add(t, v)");
        }

        [Test] public void NoCurve_IsNull_AndFallsBackToKspTwosDefault()
        {
            var cfg = EngineCurveLines.IntakeModules(Part);
            Assert.IsNull(EngineCurveLines.IntakeMachKeys(cfg[1]));
            var keys = EngineCurveLines.IntakeKeysFor(cfg, 2, 1, out var fromCfg);
            Assert.IsFalse(fromCfg);
            Assert.AreEqual(4, keys.Length);
            Assert.AreEqual(0f, keys[0]);
            Assert.AreEqual(1f, keys[1]);
            Assert.IsTrue(float.IsNaN(keys[2]) && float.IsNaN(keys[3]));
        }

        [Test] public void MatchingCounts_TakeTheCfgCurve_MismatchFallsBack()
        {
            var cfg = EngineCurveLines.IntakeModules(Part);
            Assert.AreEqual(12, EngineCurveLines.IntakeKeysFor(cfg, 2, 0, out var fromCfg).Length);
            Assert.IsTrue(fromCfg);
            Assert.AreEqual(4, EngineCurveLines.IntakeKeysFor(cfg, 3, 0, out fromCfg).Length, "counts differ: no guess");
            Assert.IsFalse(fromCfg);
            EngineCurveLines.IntakeKeysFor(null, 1, 0, out fromCfg);
            Assert.IsFalse(fromCfg);
        }

        [Test] public void EmptyCurveNode_FallsBack()
        {
            var part = CfgText.Parse("PART\n{\nMODULE\n{\nname = ModuleResourceIntake\nmachCurve\n{\nkey = oops\n}\n}\n}\n").GetNodes("PART").First();
            EngineCurveLines.IntakeKeysFor(EngineCurveLines.IntakeModules(part), 1, 0, out var fromCfg);
            Assert.IsFalse(fromCfg);
        }

        [Test] public void Summary_CountsTheIntakes()
        {
            var t = new EngineCurveLines.Tally { IntakesFilled = 3, IntakeParts = 2, IntakesFromCfg = 1, IntakesDefault = 2 };
            StringAssert.EndsWith("other null curves: none; intake mach curve filled on 3 intake(s) of 2 part(s) (1 from the cfg, 2 default)",
                EngineCurveLines.Summary(t));
            t.Clear();
            Assert.AreEqual(0, t.IntakesFilled + t.IntakeParts + t.IntakesFromCfg + t.IntakesDefault);
        }

        [Test] public void Armed_NamesBothHalves()
        {
            Assert.AreEqual("[KSP1PartMod] engines: emissive curve fix off, intake mach curve fix armed (fix_intake_mach_curve)",
                EngineCurveLines.Armed(false, true));
        }
    }
}
