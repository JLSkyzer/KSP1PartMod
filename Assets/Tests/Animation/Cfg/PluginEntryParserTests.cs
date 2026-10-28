using System.Linq;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    public class PluginEntryParserTests
    {
        private static AnimEntryDef P(string body, int index = 0, int ordinal = 0) =>
            PluginEntryParser.Parse(CfgText.Module(body), index, ordinal);

        private static AnimEntryDef M => P("name = ModuleMultiStateEngine\nEngineAnimationName = deploy\nSpeedMultiplier = 0.5\nEngineIDAtTimeZero = Primary");
        private static AnimEntryDef L => P("name = ModuleAdvancedLookAtConstraint\naxisConstrained = true,false,true\naxisScales = 1,0,0.5\n" +
            "CONSTRAINLOOKFX\n{\ntargetName = tA\nrotatorsName = rA\n}\nCONSTRAINLOOKFX\n{\ntargetName = tB\nrotatorsName = rB\n}");
        private static AnimEntryDef D => P("name = ModuleBdbDecouplerAnimation\nanimationName = deploy\nwaitForAnimation = 0.9\neditorGUIName = Toggle Umbilical");
        private static AnimEntryDef R => P("name = ModuleBdbRCSEmissive\nalphaCurve\n{\nkey = 0 0.0\nkey = 0.05 1.0\n}");
        private static AnimEntryDef H => P("name = ModuleBDBDeployableMeshHider\ntransformName = wires\ntransformName = skinnedMesh");
        private static AnimEntryDef S => P("name = DMModuleScienceAnimateGeneric\nanimationName = boom\nstartEventGUIName = Deploy Boom\n" +
            "endEventGUIName = Retract Boom\nshowEditorEvents = true\noneWayAnimation = true\nanimSpeed = 2");

        [Test] public void EntryKind_ExistingOrdinals_Unchanged() =>
            CollectionAssert.AreEqual(Enumerable.Range(0, 11), new[] { EntryKind.Toggle, EntryKind.Deploy, EntryKind.Gear,
                EntryKind.Throttle, EntryKind.Heat, EntryKind.Color, EntryKind.Group, EntryKind.AeroGate, EntryKind.GearBridge,
                EntryKind.ReduxFollower, EntryKind.ChuteFollower }.Select(k => (int)k));

        [Test] public void EntryKind_NewKinds_AppendedInOrder() =>
            CollectionAssert.AreEqual(Enumerable.Range(11, 10), new[] { EntryKind.EngineDeploy, EntryKind.EngineMode, EntryKind.LookAt,
                EntryKind.DecouplerClip, EntryKind.RcsGlow, EntryKind.RcsEmissive, EntryKind.MeshHider, EntryKind.ScienceToggle,
                EntryKind.AccelSpring, EntryKind.LandingBag }.Select(k => (int)k));

        [Test] public void Parse_DeployableEngine()
        {
            var e = P("name = ModuleDeployableEngine\nEngineAnimationName = NozzleA\nWaitForAnimation = 0.9");
            Assert.AreEqual((EntryKind.EngineDeploy, "NozzleA", 0.9f, true, true), (e.Kind, e.Clip, e.WaitForAnimation, e.Retractable, e.EditorToggle));
        }

        [Test] public void Parse_BdbAnimatedEngine_OneShotByDefault()
        {
            var e = P("name = ModuleBdbAnimatedEngine\nanimationName = x");
            Assert.AreEqual((false, false, true, true), (e.Retractable, e.EditorToggle, e.FirstAnimatorOnly, e.PersistDeployed));
        }

        [Test] public void Parse_BdbAnimatedEngine_NotOneShot_Retracts()
        {
            var e = P("name = ModuleBdbAnimatedEngine\nanimationName = x\nisOneShot = false");
            Assert.AreEqual((true, true), (e.Retractable, e.EditorToggle));
        }

        [Test] public void Parse_MultiStateEngine() => Assert.AreEqual(("Primary", 0.5f), (M.ModeAtTimeZero, M.SpeedMultiplier));
        [Test] public void Parse_MultiStateEngine_KindAndClip() => Assert.AreEqual((EntryKind.EngineMode, "deploy"), (M.Kind, M.Clip));

        [Test] public void Parse_LookAt_ReadsAllConstraintsAndAxes()
        {
            Assert.AreEqual(2, L.TargetNames.Length);
            CollectionAssert.AreEqual(new[] { "rA", "rB" }, L.RotatorNames);
            CollectionAssert.AreEqual(new[] { true, false, true }, L.AxisConstrained);
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0.5f }, L.AxisScales);
        }

        [Test] public void Parse_LookAt_NoConstraints_Null() =>
            Assert.IsNull(P("name = ModuleAdvancedLookAtConstraint"));

        [Test] public void Parse_Decoupler_LabelFromEditorGuiName() => Assert.AreEqual("Toggle Umbilical", D.StartLabel);
        [Test] public void Parse_Decoupler_ClipAndWait() => Assert.AreEqual((EntryKind.DecouplerClip, "deploy", 0.9f), (D.Kind, D.Clip, D.WaitForAnimation));

        [Test] public void Parse_RcsGlow()
        {
            var e = P("name = FXModuleAnimateRCS\nanimationName = rcsGlow\nthrustForceMult = 4");
            Assert.AreEqual((EntryKind.RcsGlow, "rcsGlow", 4f), (e.Kind, e.Clip, e.ThrustForceMult));
        }

        [Test] public void Parse_RcsEmissive_AlphaCurve() => Assert.AreEqual(1, R.Curves.Length);
        [Test] public void Parse_RcsEmissive_PropertyAndKeys() =>
            Assert.AreEqual(("_EmissiveColor", 8), (R.ShaderProperty, R.Curves[0].Length));
        [Test] public void Parse_MeshHider_AllTransformNames() => Assert.AreEqual(2, H.TransformNames.Length);

        [Test] public void Parse_DmScience_VisualToggle() =>
            Assert.AreEqual((EntryKind.ScienceToggle, "Deploy Boom", "Retract Boom", true), (S.Kind, S.StartLabel, S.EndLabel, S.OneWay));
        [Test] public void Parse_DmScience_EditorAndSpeed() => Assert.AreEqual((true, 2f), (S.ToggleInEditor, S.SpeedMultiplier));

        [Test] public void Parse_Accel_ReadsSpring()
        {
            var e = P("name = ModuleBdbAccelAnimation\nanimationName = whip\nspringK = 200\nspringDamping = 2.5\ndeployedAnchor = 0.5");
            Assert.AreEqual((EntryKind.AccelSpring, 200f, 2.5f, 0.5f, 0f), (e.Kind, e.SpringK, e.SpringDamping, e.AnchorDeployed, e.AnchorRetracted));
        }

        [Test] public void Parse_LandingBag_RetractSpeed()
        {
            var e = P("name = ModuleBdbMercuryLandingBag\nanimationName = deploy");
            Assert.AreEqual((EntryKind.LandingBag, "deploy", 0.2f), (e.Kind, e.Clip, e.RetractSpeed));
        }

        [Test] public void Parse_Keys_UseOwnKindOrdinals() =>
            Assert.AreEqual("enginedeploy:NozzleA:0", P("name = ModuleDeployableEngine\nEngineAnimationName = NozzleA").Key);

        [Test] public void Parse_Key_OrdinalAndIndexPassedThrough()
        {
            var e = P("name = ModuleBdbDecouplerAnimation\nanimationName = deploy", 4, 2);
            Assert.AreEqual(("decouplerclip:deploy:2", 4), (e.Key, e.ModuleIndex));
        }

        [Test] public void Parse_UnknownModule_Null() => Assert.IsNull(PluginEntryParser.Parse(CfgText.Module("name = ModuleFoo"), 0, 0));

        [Test] public void ModuleNames_ListsTheElevenModules() => Assert.AreEqual(11, PluginEntryParser.ModuleNames.Count);

        [Test] public void Parse_EveryListedModuleName_IsHandled() =>
            Assert.IsTrue(PluginEntryParser.ModuleNames.All(PluginEntryParser.Handles));

        [Test] public void EntryParser_DelegatesPluginModules() =>
            Assert.AreEqual("enginedeploy:a:1", EntryParser.Parse(CfgText.Module("name = ModuleDeployableEngine\nEngineAnimationName = a"), 0, 1).Key);
    }
}
