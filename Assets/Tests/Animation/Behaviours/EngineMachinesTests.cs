using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class EngineMachinesTests
    {
        private const string Key = "enginedeploy:nozzle:0";

        private static EngineDeployMachine Deploy(float len = 1f, bool retractable = true, bool persist = false) =>
            new EngineDeployMachine(new AnimEntryDef
            {
                Kind = EntryKind.EngineDeploy, Key = Key, Clip = "nozzle", Retractable = retractable, PersistDeployed = persist,
            }, len);

        private static EngineModeMachine Mode(float len = 1f, float speed = 2f) =>
            new EngineModeMachine(new AnimEntryDef
            {
                Kind = EntryKind.EngineMode, Key = "enginemode:m:0", Clip = "m", ModeAtTimeZero = "Primary", SpeedMultiplier = speed,
            }, len);

        private static void Run(EngineDeployMachine m, float seconds, bool ignited, float setting = 1f) =>
            m.Tick(seconds, false, ignited, setting);

        [Test] public void EngineDeploy_FlightStart_PoseEqualsTargetNoMotion()
        {
            var m = Deploy(); m.Start(ignited: true);
            Assert.AreEqual((1f, 0f), (m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void EngineDeploy_FlightStart_NotIgnited_IsRetracted()
        {
            var m = Deploy(); m.Start(false);
            Assert.AreEqual((0f, 0f), (m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void EngineDeploy_Ignition_ExtendsAtClipSpeed()
        {
            var m = Deploy(len: 2f); m.Start(false); Run(m, 1f, ignited: true);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
            Assert.AreEqual(1f, m.Pose.Speed);
        }

        [Test] public void EngineDeploy_ExtendsToTheEndAndStops()
        {
            var m = Deploy(); m.Start(false); Run(m, 5f, true);
            Assert.AreEqual((1f, 0f), (m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void EngineDeploy_Shutdown_RetractsOnlyWhenRetractable()
        {
            var r = Deploy(len: 2f); r.Start(true); Run(r, 1f, false);
            Assert.AreEqual(0.5f, r.Pose.NormalizedTime, 1e-4f);
            var fixedOut = Deploy(len: 2f, retractable: false); fixedOut.Start(true); Run(fixedOut, 1f, false);
            Assert.AreEqual(1f, fixedOut.Pose.NormalizedTime);
        }

        [Test] public void EngineDeploy_Toggle_FlightRefusedWhenNotRetractable()
        {
            Assert.IsFalse(Deploy(retractable: false).Toggle(false));
            Assert.IsTrue(Deploy(retractable: false).Toggle(true));
        }

        [Test] public void EngineDeploy_Toggle_FlightExtendsWithoutIgnition()
        {
            var m = Deploy(); m.Start(false);
            Assert.IsTrue(m.Toggle(false)); Run(m, 5f, false);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void EngineDeploy_Editor_UsesEditorSpeed()
        {
            var m = Deploy(len: 2f); m.Toggle(true); m.Tick(0.1f, true, false, editorSpeedSetting: 1000f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
        }

        [Test] public void EngineDeploy_Editor_Setting0_UsesKsp1Value1()
        {
            var m = Deploy(len: 2f); m.Toggle(true); m.Tick(1f, true, false, 0f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void EngineDeploy_Editor_IgnoresIgnitionAndTogglesBack()
        {
            var m = Deploy(); m.Tick(1f, true, true, 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime, "the editor follows the toggle, not the engines");
            m.Toggle(true); m.Tick(2f, true, false, 1f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            m.Toggle(true); m.Tick(2f, true, false, 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime);
        }

        [Test] public void EngineDeploy_PersistDeployed_SavesOnlyWhenSet()
        {
            var off = new Dictionary<string, float>(); var on = new Dictionary<string, float>();
            var a = Deploy(); a.Start(true); a.Save(off);
            var b = Deploy(persist: true); b.Start(true); b.Save(on);
            Assert.AreEqual(0, off.Count);
            Assert.AreEqual(1f, on[Key + ".deployed"]);
        }

        [Test] public void EngineDeploy_PersistDeployed_LoadKeepsNozzleOut_AndChangedTracksIt()
        {
            var m = Deploy(retractable: false, persist: true);
            m.Load(SavedValues.Of(Key, ("deployed", 1f))); m.Start(false);
            Assert.AreEqual(1f, m.Pose.NormalizedTime);
            Assert.IsFalse(m.Changed);
            var n = Deploy(persist: true); n.Start(false); Run(n, 0.1f, true);
            Assert.IsTrue(n.Changed);
            n.Save(new Dictionary<string, float>());
            Assert.IsFalse(n.Changed);
        }

        [Test] public void EngineDeploy_NotPersisting_IgnoresSavedValue()
        {
            var m = Deploy(); m.Load(SavedValues.Of(Key, ("deployed", 1f))); m.Start(false);
            Assert.AreEqual(0f, m.Pose.NormalizedTime);
        }

        [Test] public void EngineMode_ForwardWhenNotTimeZeroMode()
        {
            var m = Mode(len: 2f, speed: 1f); m.Start("Primary"); m.Tick(1f, false, "Secondary", 1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
            m.Tick(1f, false, "Primary", 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void EngineMode_SpeedMultiplierScalesFlightSpeed()
        {
            var m = Mode(len: 4f, speed: 2f); m.Start("Primary"); m.Tick(1f, false, "Secondary", 1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void EngineMode_Start_PoseEqualsTarget()
        {
            var m = Mode(); m.Start("Secondary");
            Assert.AreEqual((1f, 0f), (m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void EngineMode_Editor_Ksp1Is2x()
        {
            var m = Mode(len: 2f, speed: 2f); m.Tick(0.5f, true, "Secondary", editorSpeedSetting: 0f);
            Assert.AreEqual(1f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void EngineMode_Editor_SettingOverridesKsp1Value()
        {
            var m = Mode(len: 2f, speed: 1f); m.Tick(0.5f, true, "Secondary", editorSpeedSetting: 1f);
            Assert.AreEqual(0.25f, m.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void EngineMode_SpeedMultiplierZero_NeverMoves()
        {
            var m = Mode(speed: 0f); m.Tick(1f, false, "Secondary", 1f); m.Tick(1f, true, "Secondary", 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime);
        }

        [Test] public void EngineMode_UnknownMode_HoldsAtTimeZero()
        {
            var m = Mode(); m.Tick(1f, false, null, 1f);
            Assert.AreEqual(0f, m.Pose.NormalizedTime);
        }
    }
}
