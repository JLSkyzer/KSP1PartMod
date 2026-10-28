using KSP1PartMod.Animation.Settings;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The six <c>b9_*</c> keys of 0.9.0 (B9PartSwitch overrides).</summary>
    public class B9SettingsTests
    {
        private static readonly string[] Keys =
        {
            "b9_mass", "b9_resources", "b9_module_data", "b9_module_active", "b9_part_scalars", "b9_new_part_switches",
        };

        private static bool[] Values(AnimationSettings s) =>
            new[] { s.B9Mass, s.B9Resources, s.B9ModuleData, s.B9ModuleActive, s.B9PartScalars, s.B9NewPartSwitches };

        [Test] public void B9_Defaults_AllOn() => CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, Values(AnimationSettings.Defaults));

        [Test] public void B9_OldFileWithoutTheKeys_AllOn_NoError()
        {
            var s = AnimationSettings.Parse("{\"enabled\": false, \"b9_module_gates\": false, \"mm_arithmetic\": true}", out var e);
            Assert.IsNull(e);
            CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, Values(s), "independent of enabled");
        }

        [Test] public void B9_EachKey_IsRead()
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                var s = AnimationSettings.Parse("{\"" + Keys[i] + "\": false}", out var e);
                Assert.IsNull(e, Keys[i]);
                var v = Values(s);
                for (var j = 0; j < v.Length; j++) Assert.AreEqual(i != j, v[j], Keys[i] + " -> " + Keys[j]);
            }
        }

        [Test] public void B9_WrongType_KeepsTheDefault_Reported()
        {
            foreach (var key in Keys)
            {
                var s = AnimationSettings.Parse("{\"" + key + "\": 0}", out var e);
                StringAssert.Contains(key, e);
                CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, Values(s), key);
            }
        }
    }
}
