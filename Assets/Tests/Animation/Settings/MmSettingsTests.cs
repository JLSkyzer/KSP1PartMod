using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Settings
{
    /// <summary>The eight <c>mm_*</c> keys of 0.8.0 (ModuleManager safety net, arithmetic and index).</summary>
    public class MmSettingsTests
    {
        private static readonly string ShippedPath =
            Path.Combine(Application.dataPath, "KSP1PartMod", "Copied", "animations.json");

        private static readonly string[] Keys =
        {
            "mm_plugin_safety_net", "mm_plugin_folders", "mm_plugin_folders_auto", "mm_needs_keep",
            "mm_before_after_need_mod", "mm_arithmetic", "mm_index_selectors", "mm_edit_first_match",
        };

        private static void AssertDefaults(AnimationSettings s)
        {
            Assert.IsTrue(s.MmPluginSafetyNet);
            Assert.IsTrue(s.MmPluginFoldersAuto);
            Assert.IsTrue(s.MmBeforeAfterNeedMod);
            Assert.IsTrue(s.MmArithmetic);
            Assert.IsTrue(s.MmIndexSelectors);
            Assert.IsFalse(s.MmEditFirstMatch);
            CollectionAssert.AreEqual(MmPluginRule.DefaultCurated, s.MmPluginFolders);
            CollectionAssert.AreEqual(MmPluginRule.DefaultKeep, s.MmNeedsKeep);
        }

        [Test] public void Mm_Defaults() => AssertDefaults(AnimationSettings.Defaults);

        [Test] public void Mm_OldFileWithoutTheKeys_Defaults()
        {
            var s = AnimationSettings.Parse("{\"enabled\": true, \"ksp1_engine_shrouds\": false, \"skip_modules\": []}", out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            Assert.IsFalse(s.Ksp1EngineShrouds);
        }

        [Test] public void Mm_BoolsAreRead()
        {
            var s = AnimationSettings.Parse("{\"mm_plugin_safety_net\": false, \"mm_plugin_folders_auto\": false, " +
                                            "\"mm_before_after_need_mod\": false, \"mm_arithmetic\": false, " +
                                            "\"mm_index_selectors\": false, \"mm_edit_first_match\": true}", out var e);
            Assert.IsNull(e);
            Assert.AreEqual((false, false, false, false, false, true),
                (s.MmPluginSafetyNet, s.MmPluginFoldersAuto, s.MmBeforeAfterNeedMod, s.MmArithmetic, s.MmIndexSelectors, s.MmEditFirstMatch));
            CollectionAssert.AreEqual(MmPluginRule.DefaultCurated, s.MmPluginFolders);
        }

        [Test] public void Mm_ListsAreRead_TrimmedDistinctCaseInsensitive()
        {
            var s = AnimationSettings.Parse("{\"mm_plugin_folders\": [\" RealFuels \", \"realfuels\", \"FAR\"], " +
                                            "\"mm_needs_keep\": [\"ReStock\", \"RESTOCK\", \"  \"]}", out var e);
            CollectionAssert.AreEqual(new[] { "RealFuels", "FAR" }, s.MmPluginFolders);
            CollectionAssert.AreEqual(new[] { "ReStock" }, s.MmNeedsKeep);
            StringAssert.Contains("mm_needs_keep", e); // the blank item is reported
        }

        [Test] public void Mm_EmptyListIsKept()
        {
            var s = AnimationSettings.Parse("{\"mm_plugin_folders\": []}", out var e);
            Assert.IsNull(e);
            CollectionAssert.IsEmpty(s.MmPluginFolders);
        }

        [Test] public void Mm_WrongTypes_KeepDefaultsAndReport()
        {
            var s = AnimationSettings.Parse("{\"mm_arithmetic\": 1, \"mm_plugin_folders\": \"RealFuels\", \"mm_edit_first_match\": \"yes\"}", out var e);
            AssertDefaults(s);
            StringAssert.Contains("mm_arithmetic", e);
            StringAssert.Contains("mm_plugin_folders", e);
            StringAssert.Contains("mm_edit_first_match", e);
        }

        [Test] public void Mm_ListsNotMutableThroughCast()
        {
            Assert.Throws<System.NotSupportedException>(() =>
                ((System.Collections.Generic.ICollection<string>)AnimationSettings.Defaults.MmPluginFolders).Add("X"));
            Assert.Throws<System.NotSupportedException>(() =>
                ((System.Collections.Generic.ICollection<string>)AnimationSettings.Defaults.MmNeedsKeep).Add("X"));
        }

        [Test] public void Mm_ShippedFile_HasDefaultsAndDocumentsEveryKey()
        {
            var text = File.ReadAllText(ShippedPath);
            var s = AnimationSettings.Parse(text, out var e);
            Assert.IsNull(e);
            AssertDefaults(s);
            var note = text.Substring(0, text.IndexOf("\"enabled\"", System.StringComparison.Ordinal));
            foreach (var key in Keys)
            {
                StringAssert.Contains($"\"{key}\":", text);
                StringAssert.Contains($"{key}:", note, key + " in _note");
            }
        }

        [Test] public void Mm_DefaultLists_HaveNoDuplicates()
        {
            Assert.AreEqual(MmPluginRule.DefaultCurated.Count,
                MmPluginRule.DefaultCurated.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.AreEqual(MmPluginRule.DefaultKeep.Count,
                MmPluginRule.DefaultKeep.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
