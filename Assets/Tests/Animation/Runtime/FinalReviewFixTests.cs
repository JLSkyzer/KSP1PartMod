using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    /// <summary>The pure rules of the phase 2b final-review fixes (I1, M1, M2, M3).</summary>
    public class FinalReviewFixTests
    {
        private static readonly FloatCurve Linear = new FloatCurve(new[] { 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f });

        // ---------------------------------------------------------------- I1

        [Test] public void RcsEmissiveColor_IdleThruster_IsBlack()
        {
            var c = PluginRules.RcsEmissiveColor(Linear, 0f);
            Assert.AreEqual(0f, c.R);
            Assert.AreEqual(0f, c.G);
            Assert.AreEqual(0f, c.B);
            Assert.AreEqual(0f, c.A);
        }

        [TestCase(1f, 1f)]
        [TestCase(0.5f, 0.5f)]
        public void RcsEmissiveColor_Premultiplied(float scalar, float expected)
        {
            var c = PluginRules.RcsEmissiveColor(Linear, scalar);
            Assert.AreEqual(expected, c.R, 1e-5f);
            Assert.AreEqual(expected, c.G, 1e-5f);
            Assert.AreEqual(expected, c.B, 1e-5f);
            Assert.AreEqual(expected, c.A, 1e-5f);
        }

        [Test] public void RcsEmissiveColor_ClampedAndNaNSafe()
        {
            var high = new FloatCurve(new[] { 0f, 3f, 0f, 0f, 1f, 3f, 0f, 0f });
            Assert.AreEqual(1f, PluginRules.RcsEmissiveColor(high, 0.5f).R);
            Assert.AreEqual(0f, PluginRules.RcsEmissiveColor(null, float.NaN).R);
            Assert.AreEqual(0.25f, PluginRules.RcsEmissiveColor(null, 0.25f).G, 1e-6f);
        }

        // ---------------------------------------------------------------- M1

        [TestCase(10, 10, false)]
        [TestCase(11, 10, true)]
        [TestCase(9, 10, false)]
        public void RestHold_ReleasedOnlyOnALaterFrame(int frame, int held, bool expected) =>
            Assert.AreEqual(expected, RestPlan.ReleaseNow(frame, held));

        // ---------------------------------------------------------------- M2

        [Test] public void VariantRetry_OnlyForAGatedPartWithASwitchAndNoSet()
        {
            Assert.AreEqual(VariantRetry.None, VariantRetryRule.Next(false, true, 0, 1f, 1f), "no gate");
            Assert.AreEqual(VariantRetry.None, VariantRetryRule.Next(true, false, 0, 1f, 1f), "no switch");
            Assert.AreEqual(VariantRetry.None, VariantRetryRule.Next(true, true, 2, 1f, 1f), "sets found");
        }

        [Test] public void VariantRetry_ThrottledThenGivesUp()
        {
            Assert.AreEqual(VariantRetry.Wait, VariantRetryRule.Next(true, true, 0, 0.1f, 0.1f));
            Assert.AreEqual(VariantRetry.Retry, VariantRetryRule.Next(true, true, 0, 0.6f, VariantRetryRule.Interval));
            Assert.AreEqual(VariantRetry.Retry, VariantRetryRule.Next(true, true, 0, 9.9f, 0.7f));
            Assert.AreEqual(VariantRetry.GiveUp, VariantRetryRule.Next(true, true, 0, VariantRetryRule.Limit, 0.7f));
        }

        [Test] public void VariantRetry_Lines()
        {
            Assert.AreEqual("p variants found 1.5 s after bind: [x]", VariantRetryRule.FoundLine("p", 1.5f, "[x]"));
            Assert.AreEqual("p B9 gates on their default subtype: Module_PartSwitch had no variant set 10 s after bind",
                VariantRetryRule.GiveUpLine("p"));
        }

        // ---------------------------------------------------------------- M3

        private static AnimEntryDef Bdb() => new AnimEntryDef
        {
            Kind = EntryKind.EngineDeploy, Key = "enginedeploy:nozzle:0", PersistDeployed = true, Retractable = true, EditorToggle = true,
        };

        [Test] public void EngineDeploy_EditorPreviewOut_FlightStartsFromIgnitionOnly()
        {
            var editor = new EngineDeployMachine(Bdb(), 1f);
            Assert.IsTrue(editor.Toggle(true));
            for (var i = 0; i < 100; i++) editor.Tick(0.05f, true, false, 1f);
            Assert.AreEqual(1f, editor.Pose.NormalizedTime, "previewed out in the VAB");
            Assert.IsFalse(editor.Changed, "the preview is not a saved state");
            var saved = new System.Collections.Generic.Dictionary<string, float>();
            editor.Save(saved);
            Assert.AreEqual(0f, saved["enginedeploy:nozzle:0.deployed"]);

            var flight = new EngineDeployMachine(Bdb(), 1f);
            flight.Load(saved);
            flight.Start(false); // first flight tick, engine not ignited
            flight.Tick(0.02f, false, false, 1f);
            Assert.AreEqual(0f, flight.Pose.NormalizedTime, "starts retracted");
            Assert.AreEqual(0f, flight.Pose.Speed, "no retract motion");
        }

        [Test] public void EngineDeploy_FlightStateStillSaved()
        {
            var flight = new EngineDeployMachine(Bdb(), 1f);
            flight.Start(true);
            Assert.IsTrue(flight.Changed);
            var saved = new System.Collections.Generic.Dictionary<string, float>();
            flight.Save(saved);
            Assert.AreEqual(1f, saved["enginedeploy:nozzle:0.deployed"]);
            var reload = new EngineDeployMachine(Bdb(), 1f);
            reload.Load(saved);
            reload.Start(false);
            Assert.AreEqual(1f, reload.Pose.NormalizedTime, "a flight-saved nozzle stays out");
            Assert.IsFalse(reload.Changed);
        }
    }
}
