using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="CrossfeedMapping"/>: KSP1 <c>ModuleToggleCrossfeed</c> → <c>Module_ToggleCrossfeed</c> (plan (e)).</summary>
    public class CrossfeedMappingTests
    {
        /// <summary>A stack decoupler as the stock cfgs write it (values written for the test).</summary>
        private const string Decoupler = @"
PART
{
    name = testDecoupler
    fuelCrossFeed = False
    MODULE
    {
        name = ModuleDecouple
        ejectionForce = 100
    }
    MODULE
    {
        name = ModuleToggleCrossfeed
        crossfeedStatus = false
        toggleEditor = true
        toggleFlight = true
    }
}";

        private static IKsp1Node Module(string text, int index = 0) =>
            CfgText.Parse(text).GetNodes("PART").First().GetNodes("MODULE")
                .Where(m => m.GetValue("name") == CrossfeedMapping.Ksp1Module).ElementAt(index);

        private static IKsp1Node Bare(string body) => Module("PART\n{\nname = p\nMODULE\n{\nname = ModuleToggleCrossfeed\n" + body + "\n}\n}\n");

        [Test] public void StockDecoupler_BothTogglesOn_StartsOff()
        {
            var m = CrossfeedMapping.From(Module(Decoupler), true);
            Assert.IsTrue(m.ToggleInEditor);
            Assert.IsTrue(m.ToggleInFlight);
            Assert.IsTrue(m.OverridesPart);
            Assert.IsFalse(m.StartCrossFeed, "crossfeedStatus is the part's crossfeed in KSP1");
            Assert.IsNull(m.TechRequired);
            CollectionAssert.IsEmpty(m.Notes);
        }

        [Test] public void CrossfeedStatusTrue_AnyCase_OverridesAPartAtFalse()
        {
            var m = CrossfeedMapping.From(Bare("crossfeedStatus = True\ntoggleEditor = true\ntoggleFlight = true"), false);
            Assert.IsTrue(m.OverridesPart);
            Assert.IsTrue(m.StartCrossFeed);
            CollectionAssert.IsEmpty(m.Notes);
        }

        [Test] public void MissingFields_KSP2Defaults_PartValueKept()
        {
            var m = CrossfeedMapping.From(Bare(""), false);
            Assert.IsTrue(m.ToggleInEditor);
            Assert.IsTrue(m.ToggleInFlight);
            Assert.IsFalse(m.OverridesPart);
            Assert.IsFalse(m.StartCrossFeed, "the PART value stays");
            CollectionAssert.AreEqual(new[]
            {
                CrossfeedMapping.MissingToggleNote("toggleEditor"),
                CrossfeedMapping.MissingToggleNote("toggleFlight"),
            }, m.Notes, "a missing toggle keeps KSP2's true, and says so");
            Assert.AreEqual("ModuleToggleCrossfeed: toggleEditor missing, KSP2's default true used (KSP1's default not read)",
                m.Notes[0]);

            var unknownPart = CrossfeedMapping.From(Bare(""), null);
            Assert.IsTrue(unknownPart.StartCrossFeed, "no PART value: KSP2's default");
        }

        [Test] public void TogglesOff_AreRead()
        {
            var m = CrossfeedMapping.From(Bare("toggleEditor = False\ntoggleFlight = false"), true);
            Assert.IsFalse(m.ToggleInEditor);
            Assert.IsFalse(m.ToggleInFlight);
        }

        [Test] public void TechRequired_IgnoredAndNoted()
        {
            var m = CrossfeedMapping.From(Bare("crossfeedStatus = false\ntechRequired = fuelSystems\ntoggleEditor = true\ntoggleFlight = true"), true);
            Assert.AreEqual("fuelSystems", m.TechRequired);
            CollectionAssert.AreEqual(new[] { CrossfeedMapping.TechRequiredNote("fuelSystems") }, m.Notes);
            StringAssert.Contains("techRequired 'fuelSystems' ignored", m.Notes[0]);
            Assert.IsFalse(m.StartCrossFeed);
        }

        [Test] public void NotABool_DefaultKept_Noted()
        {
            var m = CrossfeedMapping.From(Bare("toggleFlight = maybe\ncrossfeedStatus = 1\ntoggleEditor = true"), false);
            Assert.IsTrue(m.ToggleInFlight);
            Assert.IsFalse(m.OverridesPart);
            Assert.IsFalse(m.StartCrossFeed);
            Assert.AreEqual(2, m.Notes.Count);
            StringAssert.Contains("toggleFlight 'maybe' is not true or false, true kept", m.Notes[0]);
            StringAssert.Contains("crossfeedStatus '1' is not true or false", m.Notes[1]);
        }

        [Test] public void UnknownKeysAndNodes_Noted_RepeatsKeepTheFirst()
        {
            var m = CrossfeedMapping.From(Bare("moduleID = x\ncrossfeedStatus = false\ncrossfeedStatus = true\nEXTRA\n{\n}"), true);
            Assert.IsFalse(m.StartCrossFeed, "the first value wins");
            CollectionAssert.AreEqual(new[]
            {
                "ModuleToggleCrossfeed: moduleID not reproduced",
                "ModuleToggleCrossfeed: crossfeedStatus repeated, the first value kept",
                "ModuleToggleCrossfeed: node EXTRA not reproduced",
                CrossfeedMapping.MissingToggleNote("toggleEditor"),
                CrossfeedMapping.MissingToggleNote("toggleFlight"),
            }, m.Notes);
        }

        [Test] public void Lines_AreExact()
        {
            Assert.AreEqual("[KSP1PartMod] native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed",
                NativeLines.Armed(new[] { NativeLines.Pair(CrossfeedMapping.Ksp1Module, CrossfeedMapping.NativeModule) }));
            Assert.AreEqual("[KSP1PartMod] native: ModuleToggleCrossfeed already has a Redux importer, ours not added",
                NativeLines.AlreadyRedux("ModuleToggleCrossfeed"));
            Assert.AreEqual("[KSP1PartMod] native: crossfeed: 155 part(s) got Module_ToggleCrossfeed (3 start with crossfeed on), techRequired ignored on 13",
                NativeLines.CrossfeedSummary(155, 3, 13));
            Assert.AreEqual("[KSP1PartMod] native: p: n", NativeLines.PartNote("p", "n"));
        }

        [Test] public void Record_ConversionsNotesFlags_OncePerPart()
        {
            var r = new NativeRecord();
            Assert.IsTrue(r.AddConversion("a", "Module_ToggleCrossfeed"));
            Assert.IsFalse(r.AddConversion("a", "Module_ToggleCrossfeed"), "a rebuild does not count twice");
            Assert.IsTrue(r.AddConversion("a", "Module_Heatshield"));
            Assert.IsTrue(r.AddConversion("b", "Module_ToggleCrossfeed"));
            CollectionAssert.AreEqual(new[] { "Module_ToggleCrossfeed", "Module_Heatshield" }, r.ConvertedModules("a"));
            CollectionAssert.IsEmpty(r.ConvertedModules("c"));
            Assert.AreEqual(2, r.PartsWith("Module_ToggleCrossfeed"));
            Assert.AreEqual(2, r.Snapshot().Count);

            Assert.IsTrue(r.AddNote("a", "n"));
            Assert.IsFalse(r.AddNote("a", "n"));
            Assert.IsTrue(r.AddNote("b", "n"));

            r.Flag("a", "f");
            r.Flag("a", "f");
            r.Flag("b", "f");
            Assert.AreEqual(2, r.CountFlag("f"));
            Assert.AreEqual(0, r.CountFlag("g"));

            r.Clear();
            Assert.AreEqual(0, r.Snapshot().Count);
            Assert.IsTrue(r.AddNote("a", "n"));
        }

        /// <summary>Every installed <c>ModuleToggleCrossfeed</c> of a PART maps with no note but <c>techRequired</c>.</summary>
        [Test] public void Installed_EveryModuleMaps_OnlyTechRequiredNoted()
        {
            int parts = 0, modules = 0, extra = 0, tech = 0, overrides = 0, startOn = 0;
            var other = new List<string>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                IKsp1Node root;
                try { root = CfgText.Parse(File.ReadAllText(path)); }
                catch (Exception) { continue; }
                foreach (var part in root.GetNodes("PART"))
                {
                    var toggles = part.GetNodes("MODULE").Where(m => m.GetValue("name")?.Trim() == CrossfeedMapping.Ksp1Module).ToList();
                    if (toggles.Count == 0) continue;
                    parts++;
                    modules += toggles.Count;
                    extra += toggles.Count - 1;
                    var partValue = CrossfeedMapping.TryBool(part.GetValue("fuelCrossFeed"), out var b) ? b : (bool?)null;
                    var m = CrossfeedMapping.From(toggles[0], partValue);
                    if (m.TechRequired != null) tech++;
                    if (m.OverridesPart) overrides++;
                    if (m.StartCrossFeed) startOn++;
                    other.AddRange(m.Notes.Where(n => !n.Contains("techRequired")).Select(n => part.GetValue("name") + ": " + n));
                }
            }
            TestContext.WriteLine($"{parts} part(s), {modules} module(s), {extra} extra, techRequired {tech}, crossfeedStatus {overrides}, start on {startOn}");
            CollectionAssert.IsEmpty(other);
            Assert.AreEqual(13, tech);
            Assert.Greater(parts, 100);
        }
    }
}
