using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class ControlLabelsTests
    {
        private static AnimEntryDef E(EntryKind kind, string start = null, string end = null, string action = null, string clip = "doors") =>
            new AnimEntryDef { Kind = kind, Clip = clip, StartLabel = start, EndLabel = end, ActionLabel = action };

        [Test]
        public void Button_FollowsTheState_StartWhenOff()
        {
            var e = E(EntryKind.Toggle, "Open Doors", "Close Doors", "Toggle Doors");
            Assert.AreEqual("Open Doors", ControlLabels.Button(e, on: false));
            Assert.AreEqual("Close Doors", ControlLabels.Button(e, on: true));
        }

        [Test]
        public void Button_MissingStateLabel_UsesTheActionLabel()
        {
            var e = E(EntryKind.Color, null, "Lights Off", "Toggle Lights");
            Assert.AreEqual("Toggle Lights", ControlLabels.Button(e, on: false));
            Assert.AreEqual("Lights Off", ControlLabels.Button(e, on: true));
        }

        [TestCase(EntryKind.Toggle, false, "Toggle doors")]
        [TestCase(EntryKind.Deploy, false, "Extend")]
        [TestCase(EntryKind.Deploy, true, "Retract")]
        [TestCase(EntryKind.Gear, false, "Extend")]
        [TestCase(EntryKind.Gear, true, "Retract")]
        [TestCase(EntryKind.Group, false, "Deploy")]
        [TestCase(EntryKind.Group, true, "Retract")]
        [TestCase(EntryKind.Color, false, "Turn on")]
        [TestCase(EntryKind.Color, true, "Turn off")]
        public void Button_NoLabels_KindDefault(EntryKind kind, bool on, string expected) =>
            Assert.AreEqual(expected, ControlLabels.Button(E(kind), on));

        [Test]
        public void Action_PrefersTheActionLabel() =>
            Assert.AreEqual("Toggle Doors", ControlLabels.Action(E(EntryKind.Toggle, "Open", "Close", "Toggle Doors")));

        [Test]
        public void Action_NoActionLabel_StartLabel() =>
            Assert.AreEqual("Open", ControlLabels.Action(E(EntryKind.Toggle, "Open", "Close")));

        [TestCase(EntryKind.Gear, "Toggle gear")]
        [TestCase(EntryKind.GearBridge, "Toggle gear")]
        [TestCase(EntryKind.Deploy, "Toggle doors")]
        public void Action_NoLabels_ToggleWithClip(EntryKind kind, string expected) =>
            Assert.AreEqual(expected, ControlLabels.Action(E(kind)));

        [Test]
        public void BlankLabels_AreIgnored() =>
            Assert.AreEqual("Extend", ControlLabels.Button(E(EntryKind.Deploy, "  ", "", " "), on: false));

        [TestCase(EntryKind.Toggle, true)]
        [TestCase(EntryKind.Deploy, true)]
        [TestCase(EntryKind.Gear, true)]
        [TestCase(EntryKind.Color, true)]
        [TestCase(EntryKind.Group, true)]
        [TestCase(EntryKind.GearBridge, false)]
        [TestCase(EntryKind.Throttle, false)]
        [TestCase(EntryKind.Heat, false)]
        [TestCase(EntryKind.AeroGate, false)]
        public void HasButton_PerKind(EntryKind kind, bool expected) => Assert.AreEqual(expected, ControlLabels.HasButton(kind));

        [TestCase(EntryKind.GearBridge, true)]
        [TestCase(EntryKind.Toggle, true)]
        [TestCase(EntryKind.Throttle, false)]
        [TestCase(EntryKind.AeroGate, false)]
        [TestCase(EntryKind.EngineDeploy, false)]
        [TestCase(EntryKind.DecouplerClip, false)]
        [TestCase(EntryKind.ScienceToggle, true)]
        [TestCase(EntryKind.LandingBag, true)]
        [TestCase(EntryKind.AccelSpring, true)]
        [TestCase(EntryKind.EngineMode, false)]
        [TestCase(EntryKind.Shroud, false)]
        public void HasAction_PerKind(EntryKind kind, bool expected) => Assert.AreEqual(expected, ControlLabels.HasAction(kind));

        [TestCase(EntryKind.EngineDeploy, true)]
        [TestCase(EntryKind.DecouplerClip, true)]
        [TestCase(EntryKind.ScienceToggle, true)]
        [TestCase(EntryKind.LandingBag, true)]
        [TestCase(EntryKind.AccelSpring, true)]
        [TestCase(EntryKind.EngineMode, false)]
        [TestCase(EntryKind.LookAt, false)]
        [TestCase(EntryKind.RcsGlow, false)]
        [TestCase(EntryKind.Shroud, true)]
        public void HasButton_PluginKinds(EntryKind kind, bool expected)
        {
            Assert.AreEqual(expected, ControlLabels.HasButton(kind));
            Assert.AreEqual(expected, ControlLabels.HasControl(kind));
        }

        [TestCase(EntryKind.EngineDeploy, false, "Deploy")]
        [TestCase(EntryKind.EngineDeploy, true, "Retract")]
        [TestCase(EntryKind.ScienceToggle, false, "Deploy")]
        [TestCase(EntryKind.ScienceToggle, true, "Retract")]
        [TestCase(EntryKind.LandingBag, false, "Deploy")]
        [TestCase(EntryKind.LandingBag, true, "Retract")]
        [TestCase(EntryKind.AccelSpring, false, "Deploy")]
        [TestCase(EntryKind.AccelSpring, true, "Stow")]
        public void Button_PluginKinds_DefaultLabels(EntryKind kind, bool on, string expected) =>
            Assert.AreEqual(expected, ControlLabels.Button(E(kind), on));

        [Test] public void Button_ScienceToggle_UsesTheCfgLabels()
        {
            var e = E(EntryKind.ScienceToggle, "Extend Magnetometer", "Retract Magnetometer");
            Assert.AreEqual("Extend Magnetometer", ControlLabels.Button(e, on: false));
            Assert.AreEqual("Retract Magnetometer", ControlLabels.Button(e, on: true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Button_Decoupler_NoEditorGuiName_FixedDefault(bool on) =>
            Assert.AreEqual(ControlLabels.DecouplerPreview, ControlLabels.Button(E(EntryKind.DecouplerClip), on));

        [TestCase(false)]
        [TestCase(true)]
        public void Button_Decoupler_EditorGuiName_BothStates(bool on) =>
            Assert.AreEqual("Test Separation", ControlLabels.Button(E(EntryKind.DecouplerClip, "Test Separation"), on));

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void Allowed_EngineDeploy_EditorPreviewOnlyWithEditorToggle(bool inEditor, bool editorToggle, bool expected) =>
            Assert.AreEqual(expected, ControlLabels.Allowed(new AnimEntryDef { Kind = EntryKind.EngineDeploy, EditorToggle = editorToggle }, inEditor));

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void Allowed_Shroud_EditorOnlyWithAllowShroudToggle(bool inEditor, bool allow, bool expected) =>
            Assert.AreEqual(expected, ControlLabels.Allowed(new AnimEntryDef { Kind = EntryKind.Shroud, AllowShroudToggle = allow }, inEditor));

        [Test] public void Button_Shroud_NamesTheNextAction()
        {
            var e = new AnimEntryDef { Kind = EntryKind.Shroud, StartLabel = "ignored" };
            Assert.AreEqual("Hide shroud", ControlLabels.Button(e, on: false), "override off: the shroud may show");
            Assert.AreEqual("Show shroud", ControlLabels.Button(e, on: true));
            Assert.IsTrue(ControlLabels.EditorOnly(EntryKind.Shroud));
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void Allowed_Decoupler_EditorOnly(bool inEditor, bool expected) =>
            Assert.AreEqual(expected, ControlLabels.Allowed(new AnimEntryDef { Kind = EntryKind.DecouplerClip }, inEditor));

        [TestCase(EntryKind.ScienceToggle)]
        [TestCase(EntryKind.Toggle)]
        [TestCase(EntryKind.AccelSpring)]
        public void Allowed_OtherKinds_FollowGuiActiveFlags(EntryKind kind)
        {
            var noEditor = new AnimEntryDef { Kind = kind, ToggleInEditor = false, ToggleInFlight = true };
            Assert.IsFalse(ControlLabels.Allowed(noEditor, inEditor: true));
            Assert.IsTrue(ControlLabels.Allowed(noEditor, inEditor: false));
            var noFlight = new AnimEntryDef { Kind = kind, ToggleInEditor = true, ToggleInFlight = false };
            Assert.IsTrue(ControlLabels.Allowed(noFlight, inEditor: true));
            Assert.IsFalse(ControlLabels.Allowed(noFlight, inEditor: false));
        }
    }
}
