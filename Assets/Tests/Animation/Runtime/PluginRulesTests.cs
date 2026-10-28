using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class PluginRulesTests
    {
        [TestCase(EntryKind.EngineDeploy, PluginInput.EngineIgnited)]
        [TestCase(EntryKind.EngineMode, PluginInput.EngineMode)]
        [TestCase(EntryKind.DecouplerClip, PluginInput.Decoupled)]
        [TestCase(EntryKind.AccelSpring, PluginInput.Acceleration)]
        [TestCase(EntryKind.ScienceToggle, PluginInput.None)]
        [TestCase(EntryKind.LandingBag, PluginInput.None)]
        [TestCase(EntryKind.Toggle, PluginInput.None)]
        public void InputOf_PerKind(EntryKind kind, PluginInput expected) => Assert.AreEqual(expected, PluginRules.InputOf(kind));

        [TestCase(EntryKind.EngineDeploy, false, true)]
        [TestCase(EntryKind.EngineDeploy, true, false)]
        [TestCase(EntryKind.DecouplerClip, false, true)]
        [TestCase(EntryKind.DecouplerClip, true, false)]
        [TestCase(EntryKind.EngineMode, false, true)]
        [TestCase(EntryKind.EngineMode, true, true)]
        [TestCase(EntryKind.AccelSpring, false, false)]
        [TestCase(EntryKind.ScienceToggle, false, false)]
        [TestCase(EntryKind.LandingBag, true, false)]
        public void Requires_DisablesOnlyWhereTheInputIsRead(EntryKind kind, bool inEditor, bool expected) =>
            Assert.AreEqual(expected, PluginRules.Requires(kind, inEditor));

        [Test] public void SpringAcceleration_EditorIsOneG_EvenWithAReader()
        {
            Assert.AreEqual(AccelSpring.OneG, PluginRules.SpringAcceleration(true, false, 0f));
            Assert.AreEqual(AccelSpring.OneG, PluginRules.SpringAcceleration(true, true, 42f));
        }

        [Test] public void SpringAcceleration_Flight_ReadingElseNaN()
        {
            Assert.AreEqual(3.5f, PluginRules.SpringAcceleration(false, true, 3.5f));
            Assert.IsTrue(float.IsNaN(PluginRules.SpringAcceleration(false, false, 3.5f)));
        }

        [Test] public void SpringAcceleration_NoField_HoldsTheAnchor()
        {
            var m = new AccelSpring(new AnimEntryDef { Key = "accelspring:whip:0", SpringK = 40f, SpringDamping = 12f, AnchorDeployed = 0.5f }, 1f);
            for (var i = 0; i < 100; i++) m.Tick(0.02f, PluginRules.SpringAcceleration(false, false, 0f));
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-6f);
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        public void StartsNow_FirstFlightStepOnly(bool inEditor, bool started, bool expected) =>
            Assert.AreEqual(expected, PluginRules.StartsNow(inEditor, started));

        private static AnimEntryDef D(EntryKind kind, string clip = "A4Ndeploy", float wait = 0.99f, string mode = null) =>
            new AnimEntryDef { Kind = kind, Clip = clip, WaitForAnimation = wait, ModeAtTimeZero = mode };

        [Test] public void Lines_AreExact()
        {
            Assert.AreEqual("p engine deploy 'A4Ndeploy' follows Module_Engine.EngineIgnited; ignition not delayed (KSP1 waits until 0.99)",
                PluginRules.EngineDeployLine("p", D(EntryKind.EngineDeploy)));
            Assert.AreEqual("p engine mode 'Shift' follows Data_Engine.currentEngineModeData.engineID, time-zero mode 'Primary'",
                PluginRules.EngineModeLine("p", D(EntryKind.EngineMode, "Shift", mode: "Primary"), "Data_Engine.currentEngineModeData.engineID"));
            Assert.AreEqual("p decoupler 'deploy' plays on Data_Decouple.isDecoupled; separation not delayed (KSP1 waits until 0.5)",
                PluginRules.DecouplerLine("p", D(EntryKind.DecouplerClip, "deploy", 0.5f)));
            Assert.AreEqual("p science toggle 'boom' (visual only)", PluginRules.ScienceLine("p", D(EntryKind.ScienceToggle, "boom")));
            Assert.AreEqual("p accel spring from VesselComponent.AccelerationOrbital-graviticAcceleration",
                PluginRules.SpringLine("p", "VesselComponent.AccelerationOrbital-graviticAcceleration"));
            Assert.AreEqual("p accel spring from none, held", PluginRules.SpringLine("p", null));
            Assert.AreEqual("p EngineDeploy 'A4Ndeploy' disabled: Module_Engine with Data_Engine not found",
                PluginRules.MissingLine("p", D(EntryKind.EngineDeploy), "Module_Engine with Data_Engine"));
        }

        [Test] public void Lines_WaitIsInvariantCulture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                StringAssert.EndsWith("(KSP1 waits until 0.25)", PluginRules.DecouplerLine("p", D(EntryKind.DecouplerClip, wait: 0.25f)));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }
}
