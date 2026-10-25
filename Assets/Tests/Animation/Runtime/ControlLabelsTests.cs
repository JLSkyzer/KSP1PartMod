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
        public void HasAction_PerKind(EntryKind kind, bool expected) => Assert.AreEqual(expected, ControlLabels.HasAction(kind));
    }
}
