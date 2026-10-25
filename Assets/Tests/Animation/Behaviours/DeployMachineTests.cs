using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class DeployMachineTests
    {
        private const string DKey = "deploy:dish:0", GKey = "gear:Deploy:0";

        private static DeployMachine Deploy(bool retractable = true, float len = 1f, float editorMult = 10f) =>
            new DeployMachine(new AnimEntryDef
            {
                Kind = EntryKind.Deploy, Key = DKey, Clip = "dish", WrapMode = 8,
                Retractable = retractable, EditorSpeedMult = editorMult,
            }, len);

        private static DeployMachine Deployed(bool retractable = true)
        {
            var m = Deploy(retractable);
            m.Load(SavedValues.Of(DKey, ("state", (float)DeployState.Extended), ("t", 1f)));
            return m;
        }

        private static DeployMachine Gear(bool deployForward = true, float len = 1f, float inF = 1f, float outF = 1f,
            DeployState? state = DeployState.Retracted)
        {
            var m = new DeployMachine(new AnimEntryDef
            {
                Kind = EntryKind.Gear, Key = GKey, Clip = "Deploy", WrapMode = 8, EditorSpeedMult = 1000f,
                DeployForward = deployForward, DeployedPosition = deployForward ? 1f : 0f,
                SpeedFactorIn = inF, SpeedFactorOut = outF,
            }, len);
            if (state != null) m.Load(SavedValues.Of(GKey, ("state", (float)state.Value), ("t", deployForward ? 0f : 1f)));
            return m;
        }

        private static void Run(DeployMachine m, float seconds)
        {
            for (; seconds > 1e-6f; seconds -= 0.02f) m.Tick(seconds < 0.02f ? seconds : 0.02f);
        }

        [Test] public void Deploy_NotRetractable_RefusesInFlightAllowsInEditor()
        {
            var m = Deployed(retractable: false); Assert.IsFalse(m.Retract(false)); Assert.IsTrue(m.Retract(true));
        }

        [Test] public void Deploy_Default_IsRetractedAtZero()
        {
            var m = Deploy(); Assert.AreEqual((DeployState.Retracted, 0f), (m.State, m.Pose.NormalizedTime));
        }

        [Test] public void Deploy_Extend_RunsAtFlightSpeedThenExtended()
        {
            var m = Deploy(len: 2f); Assert.IsTrue(m.Extend(false)); Run(m, 1f);
            Assert.AreEqual(DeployState.Extending, m.State); Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f);
            Run(m, 1.1f); Assert.AreEqual((DeployState.Extended, 1f, 0f), (m.State, m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void Deploy_Editor_UsesEditorSpeedMult()
        {
            var m = Deploy(len: 2f); m.Extend(true); Run(m, 0.1f);
            Assert.AreEqual(0.5f, m.Pose.NormalizedTime, 1e-4f); // speed 10 on a 2 s clip
        }

        [Test] public void Deploy_Extend_OnlyFromRetracted()
        {
            var m = Deploy(); m.Extend(false); Assert.IsFalse(m.Extend(false)); Assert.IsFalse(m.Retract(false));
        }

        [Test] public void Deploy_Retract_RunsBackToZero()
        {
            var m = Deployed(); Assert.IsTrue(m.Retract(false)); Assert.Less(m.Pose.Speed, 0f); Run(m, 1.1f);
            Assert.AreEqual((DeployState.Retracted, 0f), (m.State, m.Pose.NormalizedTime));
        }

        [Test] public void Deploy_SavedExtending_Resumes()
        {
            var m = Deploy(); m.Load(SavedValues.Of(DKey, ("state", (float)DeployState.Extending), ("t", 0.3f)));
            Assert.AreEqual((0.3f, 1f), (m.Pose.NormalizedTime, m.Pose.Speed));
        }

        [Test] public void Deploy_SaveLoad_RoundTrips()
        {
            var m = Deploy(); m.Extend(false); Run(m, 0.4f);
            var saved = new Dictionary<string, float>(); m.Save(saved);
            var n = Deploy(); n.Load(saved);
            Assert.AreEqual((m.State, m.Pose.NormalizedTime), (n.State, n.Pose.NormalizedTime));
            CollectionAssert.AreEquivalent(new[] { DKey + ".t", DKey + ".state" }, saved.Keys);
        }

        [Test] public void Deploy_UnknownSavedState_KeepsDefault()
        {
            var m = Deploy(); m.Load(SavedValues.Of(DKey, ("state", 42f), ("t", 0.5f)));
            Assert.AreEqual((DeployState.Retracted, 0f), (m.State, m.Pose.NormalizedTime));
        }

        [Test] public void Gear_Reversed_StartsAtOneWhenRetracted() => Assert.AreEqual(1f, Gear(deployForward: false).Pose.NormalizedTime);

        [Test] public void Gear_NoSavedState_StartsDeployed()
        {
            var g = Gear(deployForward: false, state: null);
            Assert.AreEqual((DeployState.Extended, 0f), (g.State, g.Pose.NormalizedTime));
        }

        [Test] public void Gear_Reversed_ExtendRunsBackward_AtExtendFactor()
        {
            var g = Gear(deployForward: false, len: 1f, inF: 0.5f); g.Extend(false);
            Assert.AreEqual(-0.5f, g.Pose.Speed); Run(g, 1f); Assert.AreEqual(0.5f, g.Pose.NormalizedTime, 1e-4f);
            Run(g, 1f); Assert.AreEqual((DeployState.Extended, 0f), (g.State, g.Pose.NormalizedTime));
        }

        [Test] public void Gear_Retract_UsesRetractFactor()
        {
            var g = Gear(outF: 0.3f, state: DeployState.Extended); g.Retract(false); Assert.AreEqual(-0.3f, g.Pose.Speed, 1e-6f);
        }

        [Test] public void Gear_EndsWithin0_01OfTarget()
        {
            var g = Gear(len: 1f); g.Extend(false); Run(g, 0.98f);
            Assert.AreEqual((DeployState.Extending, false), (g.State, g.Pose.NormalizedTime >= 1f));
            Run(g, 0.015f); Assert.AreEqual((DeployState.Extended, 1f), (g.State, g.Pose.NormalizedTime));
        }

        [Test] public void Gear_InEditor_EndsImmediately()
        {
            var g = Gear(len: 3f); g.Extend(true); Assert.AreEqual((DeployState.Extended, 1f), (g.State, g.Pose.NormalizedTime));
        }

        [Test] public void Gear_CanReverseMidMotion()
        {
            var g = Gear(len: 1f); g.Extend(false); Run(g, 0.5f); Assert.IsTrue(g.Retract(false));
            Assert.AreEqual(DeployState.Retracting, g.State); Run(g, 1f);
            Assert.AreEqual((DeployState.Retracted, 0f), (g.State, g.Pose.NormalizedTime));
        }
    }
}
