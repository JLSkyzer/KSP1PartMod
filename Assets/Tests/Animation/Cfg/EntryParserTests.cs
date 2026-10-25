using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    public class EntryParserTests
    {
        private static AnimEntryDef Parse(string body, int index = 0, int ordinal = 0) =>
            EntryParser.Parse(CfgText.Module(body), index, ordinal);

        // Shapes of the stock cfgs (Mk1-3 cockpit lights, ion engine, KRE BoosterWing), written for the test.
        private static AnimEntryDef C => Parse(
            "name = ModuleColorChanger\nshaderProperty = _EmissiveColor\nanimRate = 0.8\ndefaultActionGroup = Light\n" +
            "redCurve\n{\nkey = 0 0 0 3\nkey = 1 1 0 0\n}\ngreenCurve\n{\nkey = 0 0\nkey = 1 0.9\n}\n" +
            "blueCurve\n{\nkey = 0 0\n}\nalphaCurve\n{\nkey = 0 1\n}");
        private static AnimEntryDef T => Parse(
            "name = FXModuleAnimateThrottle\nanimationName = colorAnimation\nresponseSpeed = 0.5\nengineName = Ion");
        private static AnimEntryDef A => Parse(
            "name = ModuleDeployableAero\nDeployModuleIndex = 0 //Index of module implementing IScalarModule\n" +
            "DeployModulePos = 0 //Position (0 or 1) of animation considered deployed. Part spawns at 0.");
        private static int WrapOf(AnimEntryDef e) => e.WrapMode;

        [Test] public void Parse_AnimateGeneric_ReadsGuiNamesLayerAndGroup()
        {
            var e = Parse("name = ModuleAnimateGeneric\nanimationName = Mk2Doors\nstartEventGUIName = Open\n" +
                          "endEventGUIName = Close\ndefaultActionGroup = Custom01\nlayer = 2", 3);
            Assert.AreEqual((EntryKind.Toggle, "Mk2Doors", 2, "Open", "Close", "Custom01", 3),
                (e.Kind, e.Clip, e.Layer, e.StartLabel, e.EndLabel, e.ActionGroup, e.ModuleIndex));
        }

        [Test] public void Parse_AnimateGeneric_DefaultLayerIsOne() =>
            Assert.AreEqual(1, Parse("name = ModuleAnimateGeneric\nanimationName = a").Layer);

        [Test] public void Parse_Key_KindClipOrdinal() =>
            Assert.AreEqual("toggle:Mk2Doors:2", Parse("name = ModuleAnimateGeneric\nanimationName = Mk2Doors", 0, 2).Key);

        [Test] public void Parse_ColorChanger_ReadsCurvesAndProperty() =>
            Assert.AreEqual(("_EmissiveColor", 4), (C.ShaderProperty, C.Curves.Length));

        [Test] public void Parse_ColorChanger_CurveRowsAreTimeValueInOut()
        {
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 3f, 1f, 1f, 0f, 0f }, C.Curves[0]);
            // a two-value key leaves its tangents to Unity's AddKey(time, value): NaN marks them
            Assert.IsTrue(float.IsNaN(C.Curves[1][2]) && float.IsNaN(C.Curves[1][3]));
            Assert.AreEqual((0.8f, "Lights", "color:_EmissiveColor:0"), (C.Rate, C.ActionGroup, C.Key));
        }

        [Test] public void Parse_Throttle_DefaultsMatchKsp1() => Assert.AreEqual((false, 8), (T.PlayInEditor, WrapOf(T)));

        [Test] public void Parse_Throttle_ReadsResponseAndEngine() =>
            Assert.AreEqual((EntryKind.Throttle, "colorAnimation", 0.5f, "Ion", 1), (T.Kind, T.Clip, T.ResponseSpeed, T.EngineName, T.Layer));

        [Test] public void Parse_DeployableAero_ReadsControllerAndPosition() =>
            Assert.AreEqual((0, 0f), (A.ControllerModuleIndex, A.DeployedPosition));  // KRE BoosterWing

        [Test] public void Parse_WheelDeployment_ReadsTransformDirectionAndSpeeds()
        {
            var e = Parse("name = ModuleWheelDeployment\nanimationTrfName = landingLegSmall\nanimationStateName = Deploy\n" +
                          "deployedPosition = 1\nextendDurationFactor = 0.5\nretractDurationFactor = 0.3");
            Assert.AreEqual((EntryKind.Gear, "Deploy", "landingLegSmall", true, 0.5f, 0.3f, "Gear", 8),
                (e.Kind, e.Clip, e.AnimatorName, e.DeployForward, e.SpeedFactorIn, e.SpeedFactorOut, e.ActionGroup, e.WrapMode));
        }

        [Test] public void Parse_WheelDeployment_MisspelledRetractFactorIgnoredAsKsp1Does()
        {
            // KSP1 1.12.5 reads only `retractDurationFactor`; the cfgs' `retractDuractionFactor` never reaches it.
            var e = Parse("name = ModuleWheelDeployment\nanimationStateName = Deploy\nretractDuractionFactor = 0.3");
            Assert.AreEqual((1f, 0f, false), (e.SpeedFactorOut, e.DeployedPosition, e.DeployForward));
        }

        [Test] public void Parse_Heat_ThermalAnimAndCurve()
        {
            var e = Parse("name = ModuleAnimateHeat\nThermalAnim = Cone_Heat\nanimStateCurve\n{\nkey = 0 0 0 0\nkey = 1 1 0 0\n}");
            Assert.AreEqual((EntryKind.Heat, "Cone_Heat", 1), (e.Kind, e.Clip, e.Curves.Length));
        }

        [Test] public void Parse_Heat_DefaultClipName() =>
            Assert.AreEqual("HeatAnimationEmissive", Parse("name = ModuleAnimateHeat").Clip);

        [Test] public void Parse_Group_ReadsThreeClips()
        {
            var e = Parse("name = ModuleAnimationGroup\ndeployAnimationName = Deploy\nactiveAnimationName = Drill\nalwaysActive = true");
            Assert.AreEqual((EntryKind.Group, "Deploy", "Drill", (string)null, true, 3), (e.Kind, e.Clip, e.SecondClip, e.ThirdClip, e.AlwaysActive, e.Layer));
        }

        [Test] public void Parse_Antenna_IsDeployWithKsp1Defaults()
        {
            var e = Parse("name = ModuleDeployableAntenna\nanimationName = dish\nretractable = false");
            Assert.AreEqual((EntryKind.Deploy, "dish", false, 10f, 8), (e.Kind, e.Clip, e.Retractable, e.EditorSpeedMult, e.WrapMode));
        }

        [Test] public void Parse_UnknownModule_IsNull() => Assert.IsNull(Parse("name = ModuleParachute\nsemiDeployedAnimation = semi"));

        [Test] public void Parse_NoClip_IsNull() => Assert.IsNull(Parse("name = ModuleAnimateGeneric\nanimationName ="));

        [TestCase("Light", "Lights")] [TestCase("Custom10", "Custom10")] [TestCase("REPLACEWITHDEFAULT", null)] [TestCase("none", null)]
        [TestCase("gear", "Gear")] [TestCase("Brakes", "Brakes")] [TestCase("Abort", "Abort")] [TestCase(null, null)] [TestCase("Custom11", null)]
        public void ActionGroup_Maps(string k, string v) => Assert.AreEqual(v, ActionGroupMap.Map(k));
    }
}
