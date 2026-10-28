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
        [TestCase(EntryKind.RcsGlow, PluginInput.RcsForce)]
        [TestCase(EntryKind.RcsEmissive, PluginInput.RcsForce)]
        [TestCase(EntryKind.MeshHider, PluginInput.None)]
        [TestCase(EntryKind.LookAt, PluginInput.None)]
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
        [TestCase(EntryKind.RcsGlow, false, true)]
        [TestCase(EntryKind.RcsGlow, true, false)]
        [TestCase(EntryKind.RcsEmissive, false, true)]
        [TestCase(EntryKind.RcsEmissive, true, false)]
        [TestCase(EntryKind.MeshHider, false, false)]
        [TestCase(EntryKind.LookAt, false, false)]
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

        // Task 7 review fix (3)/(4): the start pose is taken on the first tick, never at bind; the nozzle in flight only,
        // the engine mode in both scenes.
        [TestCase(EntryKind.EngineDeploy, false, false, true)]
        [TestCase(EntryKind.EngineDeploy, false, true, false)]
        [TestCase(EntryKind.EngineDeploy, true, false, false)]
        [TestCase(EntryKind.EngineMode, false, false, true)]
        [TestCase(EntryKind.EngineMode, true, false, true)]
        [TestCase(EntryKind.EngineMode, true, true, false)]
        [TestCase(EntryKind.DecouplerClip, false, false, false)]
        [TestCase(EntryKind.Toggle, false, false, false)]
        public void WaitsForStart_PerKindAndScene(EntryKind kind, bool inEditor, bool started, bool expected) =>
            Assert.AreEqual(expected, PluginRules.WaitsForStart(kind, inEditor, started));

        [Test] public void StartsNow_NeverAtTheBindStep_OnTheFirstTick()
        {
            Assert.IsFalse(PluginRules.StartsNow(EntryKind.EngineDeploy, false, false, ticking: false), "bind step");
            Assert.IsTrue(PluginRules.StartsNow(EntryKind.EngineDeploy, false, false, ticking: true), "first flight tick");
            Assert.IsFalse(PluginRules.StartsNow(EntryKind.EngineDeploy, false, true, ticking: true), "once only");
            Assert.IsFalse(PluginRules.StartsNow(EntryKind.EngineDeploy, true, false, ticking: true), "editor preview");
            Assert.IsFalse(PluginRules.StartsNow(EntryKind.EngineMode, true, false, ticking: false), "editor bind step");
            Assert.IsTrue(PluginRules.StartsNow(EntryKind.EngineMode, true, false, ticking: true), "editor first tick");
        }

        [Test] public void StartRule_RunningEngineLoaded_NozzleOutWithNoMotion()
        {
            // The glue's order: bind step (no tick, no pose), then first tick: Start from the loaded EngineIgnited.
            var m = new EngineDeployMachine(new AnimEntryDef { Kind = EntryKind.EngineDeploy, Key = "e" }, 2f);
            var started = false;
            Assert.IsTrue(PluginRules.WaitsForStart(EntryKind.EngineDeploy, false, started), "the bind step shows nothing");
            if (PluginRules.StartsNow(EntryKind.EngineDeploy, false, started, ticking: true))
            {
                m.Start(true);
                started = true;
            }
            m.Tick(0.02f, false, true, 1f);
            Assert.IsTrue(started);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            Assert.AreEqual(0f, m.Pose.Speed);
        }

        [Test] public void StartRule_EditorMode_ShowsThePoseAtOnce()
        {
            var m = new EngineModeMachine(new AnimEntryDef { Kind = EntryKind.EngineMode, Key = "m", ModeAtTimeZero = "A" }, 2f);
            Assert.IsTrue(PluginRules.StartsNow(EntryKind.EngineMode, true, false, ticking: true));
            m.Start("B");
            m.Tick(0.02f, true, "B", 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime, "pose at once, not played at 2x");
            Assert.AreEqual(0f, m.Pose.Speed);
        }

        [TestCase(2f, true, 2f)]
        [TestCase(2f, false, 1f)]
        public void RcsReference_MaxThrustOrOneWithoutFuel(float max, bool requiresFuel, float expected) =>
            Assert.AreEqual(expected, PluginRules.RcsReference(max, requiresFuel));

        [Test] public void LeftToRedux_IgnoresCase_NullSafe()
        {
            var redux = new[] { "gimbal1_Upper", "strutLower1" };
            Assert.IsTrue(PluginRules.LeftToRedux("GIMBAL1_upper", redux));
            Assert.IsFalse(PluginRules.LeftToRedux("gimbal1_Lower", redux));
            Assert.IsFalse(PluginRules.LeftToRedux(null, redux));
            Assert.IsFalse(PluginRules.LeftToRedux("x", null));
            Assert.IsFalse(PluginRules.LeftToRedux("x", new string[0]));
        }

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
            Assert.AreEqual("p MeshHider 'meshhider::0' disabled: x not found",
                PluginRules.MissingLine("p", new AnimEntryDef { Kind = EntryKind.MeshHider, Key = "meshhider::0" }, "x"));
            Assert.AreEqual("p rcs glow: 4 thruster(s), 4 host(s) matched", PluginRules.RcsGlowLine("p", 4, 4));
            Assert.AreEqual("p rcs glow: 4 thruster(s), 1 host(s) count mismatch", PluginRules.RcsGlowLine("p", 4, 1));
            Assert.AreEqual("p rcs glow first values: force 0.000125, maxThrust 1, scalar 0.5",
                PluginRules.RcsFirstValuesLine("p", 0.000125f, 1f, 0.5f));
            Assert.AreEqual("p mesh hider: 2 object(s) follow our entry 'deploy:panel:0'", PluginRules.MeshHiderLine("p", 2, "deploy:panel:0"));
            Assert.AreEqual("p mesh hider: 1 object(s) follow Redux Data_Deployable", PluginRules.MeshHiderLine("p", 1, null));
            Assert.AreEqual("p advanced look-at: 3 constraint(s), 1 missing transform(s)", PluginRules.LookAtLine("p", 3, 1));
            Assert.AreEqual("p look-at 'gimbal1_Upper' left to Redux", PluginRules.LookAtLeftToReduxLine("p", "gimbal1_Upper"));
            Assert.AreEqual("p rcs emissive -> 4 renderer(s) via [_EmissionColor]",
                PluginRules.RcsEmissiveLine("p", "-> 4 renderer(s) via [_EmissionColor]"));
        }

        [Test] public void Lines_WaitIsInvariantCulture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                StringAssert.EndsWith("(KSP1 waits until 0.25)", PluginRules.DecouplerLine("p", D(EntryKind.DecouplerClip, wait: 0.25f)));
                Assert.AreEqual("p rcs glow first values: force 1.5, maxThrust 0.25, scalar 6",
                    PluginRules.RcsFirstValuesLine("p", 1.5f, 0.25f, 6f));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }
    }
}
