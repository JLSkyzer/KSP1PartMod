using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Cfg
{
    /// <summary>The entry keys are the prefix of every saved value: a key that changes loses that entry's state in
    /// existing saves. <c>KeySnapshot-0.3.2.txt</c> was written by <see cref="Write_KeySnapshot"/> from the 0.3.2
    /// planner, before any phase 2 change; it holds hashes only (no part names, the KSP1 content is not ours). One
    /// line per real <c>PART</c> node: the SHA-256 of its name (12 hex), then the SHA-256 of its keys (16 hex) under
    /// the four planner switches (<c>own_first_deployable</c> × <c>follow_redux_deployables</c>), sorted.</summary>
    public class KeySnapshotTests
    {
        private const string SnapshotName = "KeySnapshot-0.3.2.txt";
        /// <summary>Written by <see cref="Write_KeySnapshot040"/> from the 0.4.0 planner (phase 2a and the 0.4.1 fixes,
        /// before phase 2b plans any plugin module), same format.</summary>
        private const string Snapshot040Name = "KeySnapshot-0.4.0.txt";

        /// <summary>The entry kinds of 0.3.2: later kinds are appended after the last one.</summary>
        private const EntryKind Last032Kind = EntryKind.ChuteFollower;

        private static string SnapshotPath(string name = SnapshotName, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here) ?? "", name);

        private static List<(string Name, IReadOnlyList<IKsp1Node> Modules)> _parts;

        private static List<(string Name, IReadOnlyList<IKsp1Node> Modules)> RealParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<(string, IReadOnlyList<IKsp1Node>)>();
            foreach (var path in InstalledKsp1.AllCfg())
            foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
                parts.Add((part.GetValue("name") ?? "", part.GetNodes("MODULE").ToList()));
            Assume.That(parts.Count, Is.GreaterThan(0), "no PART node found");
            return _parts = parts;
        }

        private static bool HasNativeSolar(IReadOnlyList<IKsp1Node> modules) =>
            modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));

        /// <summary>The sorted keys of the entries of kinds up to <paramref name="lastKind"/>, under each switch pair.</summary>
        private static string KeysOf(IReadOnlyList<IKsp1Node> modules, EntryKind lastKind, ISet<string> native = null,
            bool shrouds = false, bool ablatorChar = false)
        {
            var sb = new StringBuilder();
            foreach (var own in new[] { false, true })
            foreach (var follow in new[] { false, true })
            {
                // Plugin modules and B9 gates on (the 0.5.0 defaults): the keys of the older kinds must not move.
                var plan = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), native ?? new HashSet<string>(), own, follow, null, true, true,
                    shrouds, ablatorChar);
                sb.Append("own=").Append(own).Append(" follow=").Append(follow).Append(':');
                foreach (var key in plan.Entries.Where(e => e.Kind <= lastKind).Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal))
                    sb.Append('\n').Append(key);
                sb.Append("\n--\n");
            }
            return sb.ToString();
        }

        private static string Hex(string text, int chars)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(hash.Select(b => b.ToString("x2"))).Substring(0, chars);
            }
        }

        private static List<string> CurrentKeysOfKinds(EntryKind lastKind) =>
            RealParts().Select(p => Hex(p.Name, 12) + " " + Hex(KeysOf(p.Modules, lastKind), 16))
                .OrderBy(l => l, StringComparer.Ordinal).ToList();

        private static List<string> Snapshot(string name = SnapshotName)
        {
            var path = SnapshotPath(name);
            Assert.That(File.Exists(path), Is.True, "snapshot missing: " + path);
            return File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        }

        /// <summary>No entry of a 0.3.2 kind changes key on any real part: saves keep their values.</summary>
        [Test] public void Plan_EntryKeysOf032Kinds_Unchanged_AllRealParts() =>
            CollectionAssert.AreEqual(Snapshot(), CurrentKeysOfKinds(Last032Kind),
                "KSP1 content under mods/KSP1 changed? Regenerate with Write_KeySnapshot from the 0.3.2 planner (3c75da4) " +
                "only if no planner change is involved");

        /// <summary>Run alone, on the 0.3.2 planner only: it (re)writes the snapshot.</summary>
        [Test, Explicit("writes the 0.3.2 key snapshot")]
        public void Write_KeySnapshot()
        {
            var lines = CurrentKeysOfKinds(Last032Kind);
            File.WriteAllText(SnapshotPath(), string.Join("\n", lines) + "\n");
            TestContext.WriteLine($"{lines.Count} part line(s) written to {SnapshotPath()}");
        }

        /// <summary>No entry of a kind of 0.4.0 changes key on any real part once phase 2b plans the plugin modules.</summary>
        [Test] public void Plan_EntryKeysOf040_Unchanged_AllRealParts() =>
            CollectionAssert.AreEqual(Snapshot(Snapshot040Name), CurrentKeysOfKinds(Last032Kind),
                "KSP1 content under mods/KSP1 changed? Regenerate with Write_KeySnapshot040 from the 0.4.0 planner (4695d2b) " +
                "only if no planner change is involved");

        /// <summary>0.8.0: with <c>ablator_char</c> on, every key of the plan without it is still there, on the same module, and
        /// the only new entries are the ablator chars (planned last). The snapshot tests above run with it off, so they also
        /// hold the keys of the plan with it on. The KV pods (shieldChar and a light on <c>_EmissiveColor</c>) get no char:
        /// their light keeps the slot, with a note.</summary>
        [Test] public void Plan_AblatorChar_KeepsEveryKey_AddsOnlyAblatorEntries_AllRealParts()
        {
            var partsWithChar = 0;
            var partsKeepingTheirLight = 0;
            foreach (var (name, modules) in RealParts())
            {
                var withChar = false;
                var keepsLight = false;
                foreach (var own in new[] { false, true })
                foreach (var follow in new[] { false, true })
                foreach (var shrouds in new[] { false, true })
                {
                    var off = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), new HashSet<string>(), own, follow, null, true, true, shrouds, false);
                    var on = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), new HashSet<string>(), own, follow, null, true, true, shrouds, true);
                    var onByKey = on.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
                    foreach (var e in off.Entries)
                    {
                        Assert.IsTrue(onByKey.TryGetValue(e.Key, out var same), $"{name}: key {e.Key} lost");
                        Assert.AreEqual(e.ModuleIndex, same.ModuleIndex, $"{name}: key {e.Key} moved to another module");
                    }
                    var added = on.Entries.Where(e => off.Entries.All(o => o.Key != e.Key)).ToList();
                    Assert.IsTrue(added.All(e => e.ScalarSource == ScalarSource.Ablator), $"{name}: only ablator chars are new");
                    Assert.AreEqual(off.Entries.Count + added.Count, on.Entries.Count, name);
                    if (added.Count > 0) withChar = true;
                    if (on.Notes.Any(n => n.Contains("writes the same shader property"))) keepsLight = true;
                }
                if (withChar) partsWithChar++;
                if (keepsLight) partsKeepingTheirLight++;
                Assert.IsFalse(withChar && keepsLight, $"{name}: a char and a light on the same slot");
            }
            TestContext.WriteLine($"{partsWithChar} real part(s) get an ablator char, {partsKeepingTheirLight} keep their light instead");
            Assert.AreEqual(6, partsWithChar, "the 6 installed _BurnColor shields with useChar = True and a shieldChar");
            Assert.AreEqual(3, partsKeepingTheirLight, "the KV1, KV2 and KV3 pods");
        }

        /// <summary>0.10.0: the ISRU importers add <c>Module_ResourceConverter</c>, <c>Module_Mine</c> and
        /// <c>Module_ResourceScanner</c> to the part's native modules; no planner input is added. The keys of the stock ISRU
        /// parts are those of the 0.3.2 and 0.4.0 snapshots, and every key (2b kinds included) is the same with those native
        /// modules present (the ISRU switches on) and absent (off), with or without shrouds and ablator chars.
        /// <para>The snapshot half is a real check. The on/off half is a regression guard, not a proof: no native counterpart
        /// is listed in the planner today, so the native set is not read and no setting reaches the planner. It catches a
        /// future counterparts entry that would move a key. The real on/off check of <c>isru_active_animation</c> is
        /// <see cref="IsruActiveAnimation_OnAndOff_NoKeyMoves"/>.</para></summary>
        [Test] public void Plan_IsruParts_KeysUnchanged_WithIsruSwitchesOnAndOff()
        {
            var on = new HashSet<string>(StringComparer.Ordinal) { "Module_ResourceConverter", "Module_Mine", "Module_ResourceScanner" };
            var snapshot032 = new HashSet<string>(Snapshot(), StringComparer.Ordinal);
            var snapshot040 = new HashSet<string>(Snapshot(Snapshot040Name), StringComparer.Ordinal);
            foreach (var name in new[] { "ISRU", "RadialDrill", "MiniDrill", "OrbitalScanner", "SurveyScanner", "MiniISRU" })
            {
                var parts = RealParts().Where(p => p.Name == name).ToList();
                Assert.AreEqual(1, parts.Count, name + " installed once");
                var modules = parts[0].Modules;
                foreach (var native in new[] { null, on })
                {
                    var line = Hex(name, 12) + " " + Hex(KeysOf(modules, Last032Kind, native), 16);
                    Assert.IsTrue(snapshot032.Contains(line), $"{name}: 0.3.2 keys, native modules {(native == null ? "off" : "on")}");
                    Assert.IsTrue(snapshot040.Contains(line), $"{name}: 0.4.0 keys, native modules {(native == null ? "off" : "on")}");
                }
                foreach (var shrouds in new[] { false, true })
                foreach (var ablatorChar in new[] { false, true })
                    Assert.AreEqual(KeysOf(modules, (EntryKind)int.MaxValue, null, shrouds, ablatorChar),
                        KeysOf(modules, (EntryKind)int.MaxValue, on, shrouds, ablatorChar), $"{name}: every key, switches on and off");
                TestContext.WriteLine($"{name}: keys unchanged, ISRU native modules on and off");
            }
        }

        /// <summary>Task 4: <c>isru_active_animation</c> acts at run time only, on the Group entries' machines (an activity input
        /// bound by <see cref="IsruAnimRules.WantsActiveInput"/>). On the real stock ISRU parts, with the switch on and off:
        /// the planned keys are the same, and every Group machine, deployed and run in flight with its native module running,
        /// saves the same keys and values (<c>.deployed</c> only). The switch does act: with it on, the drills' active clips
        /// play; off, they do not.</summary>
        [Test] public void IsruActiveAnimation_OnAndOff_NoKeyMoves()
        {
            var playedOn = 0;
            foreach (var name in new[] { "ISRU", "RadialDrill", "MiniDrill", "OrbitalScanner", "SurveyScanner", "MiniISRU" })
            {
                var parts = RealParts().Where(p => p.Name == name).ToList();
                Assert.AreEqual(1, parts.Count, name + " installed once");
                var modules = parts[0].Modules;
                var results = new List<(string Keys, Dictionary<string, float> Saved, int Playing)>();
                foreach (var on in new[] { false, true })
                {
                    var plan = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), new HashSet<string>(), false, true, null, true, true, true, true);
                    var keys = string.Join("\n", plan.Entries.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));
                    var saved = new Dictionary<string, float>(StringComparer.Ordinal);
                    var playing = 0;
                    foreach (var def in plan.Entries.Where(e => e.Kind == EntryKind.Group))
                    {
                        var g = new GroupMachine(def, 2f, 3f);
                        if (IsruAnimRules.WantsActiveInput(def, on)) g.ActiveInput = () => true;
                        g.Deploy(false);
                        for (var t = 0; t < 300; t++) g.Tick(0.02f, false);
                        if (g.ActivePose.Enabled) playing++;
                        g.Save(saved);
                    }
                    results.Add((keys, saved, playing));
                }
                Assert.AreEqual(results[0].Keys, results[1].Keys, name + ": planned keys, switch off and on");
                CollectionAssert.AreEquivalent(results[0].Saved, results[1].Saved, name + ": saved keys and values, switch off and on");
                Assert.IsTrue(results[1].Saved.Keys.All(k => k.EndsWith(".deployed", StringComparison.Ordinal)), name);
                if (results[1].Playing > results[0].Playing) playedOn++;
                TestContext.WriteLine($"{name}: {results[1].Saved.Count} group key(s), active clips playing off {results[0].Playing}, on {results[1].Playing}");
            }
            Assert.That(playedOn, Is.GreaterThanOrEqualTo(2), "the switch plays the drills' active clips (RadialDrill, MiniDrill)");
        }

        /// <summary>0.11.0: the radiator rules read the cfg and plan nothing. The keys of the stock radiators and of two BDB louvers
        /// are in both snapshots and are the same before and after the radiator mapping has run under every combination of its
        /// switches (<c>ksp1_radiators</c> and <c>radiator_deploy_bridge</c> are not inputs of the planner), and the Deploy entry
        /// of a deployable radiator is the one the bridge follows.</summary>
        [Test] public void Plan_RadiatorParts_KeysUnchanged_WhateverTheRadiatorSwitches()
        {
            var names = new[] { "radPanelSm", "radPanelEdge", "radPanelLg", "foldingRadSmall", "foldingRadMed", "foldingRadLarge",
                                "bluedog_Pioneer_SmallLouver", "bluedog_Pioneer_MediumLouver" };
            var snapshot032 = new HashSet<string>(Snapshot());
            var snapshot040 = new HashSet<string>(Snapshot(Snapshot040Name));
            var table = new KSP1PartMod.Native.IsruResources(null, null, null, null);
            var found = 0;
            var bridged = 0;
            foreach (var name in names)
            {
                var part = RealParts().FirstOrDefault(p => p.Name == name);
                if (part.Name == null) continue;
                found++;
                var line = Hex(part.Name, 12) + " " + Hex(KeysOf(part.Modules, Last032Kind), 16);
                Assert.IsTrue(snapshot032.Contains(line), name + ": 0.3.2 snapshot");
                Assert.IsTrue(snapshot040.Contains(line), name + ": 0.4.0 snapshot");
                var radiators = part.Modules.Where(m => "ModuleActiveRadiator".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                var deployableAt = part.Modules.ToList().FindIndex(m => "ModuleDeployableRadiator".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase));
                Assert.AreEqual(1, radiators.Count, name);
                foreach (var hasDeployable in new[] { false, true })
                foreach (var ksp1Rates in new[] { false, true })
                foreach (var autoOn in new[] { false, true })
                {
                    Assert.IsTrue(KSP1PartMod.Native.RadiatorMapping.From(radiators, hasDeployable, table, 100, 1, ksp1Rates, autoOn).HasModule, name);
                    Assert.AreEqual(line, Hex(part.Name, 12) + " " + Hex(KeysOf(part.Modules, Last032Kind), 16),
                        $"{name}: keys moved after the radiator rules ran ({hasDeployable}, {ksp1Rates}, {autoOn})");
                }
                var plan = OwnershipPlanner.Plan(part.Modules, HasNativeSolar(part.Modules), new HashSet<string>(), false, false, null, true, true, false, false);
                var deploy = plan.Entries.FirstOrDefault(e => KSP1PartMod.Animation.Behaviours.RadiatorAnimRules.CanDriveBridge(e, deployableAt));
                Assert.AreEqual(deployableAt >= 0, deploy != null, name + ": the bridge finds the Deploy entry of the ModuleDeployableRadiator");
                if (deploy != null) bridged++;
            }
            Assert.AreEqual(names.Length, found, "the six stock radiators and two BDB louvers are installed");
            Assert.AreEqual(5, bridged, "the three folding radiators and the two BDB louvers");
        }

        /// <summary>0.12.0: the science rules read the cfg and plan nothing. The keys of the stock goo canister, materials bay and
        /// Mk1 pod, and of four BDB parts with <c>DMModuleScienceAnimateGeneric</c>, are in both snapshots, and every key (2b's
        /// <c>ScienceToggle</c> included) is the same before and after the science mapping has run with each science switch on and
        /// off. The experiments find their clip among those entries (the stock <c>FxModules</c> Deploy or Redux follower entry, the
        /// DMagic <c>ScienceToggle</c> entry).</summary>
        [Test] public void Plan_ScienceParts_KeysUnchanged_WithTheScienceSwitchesOnAndOff()
        {
            var snapshot032 = new HashSet<string>(Snapshot(), StringComparer.Ordinal);
            var snapshot040 = new HashSet<string>(Snapshot(Snapshot040Name), StringComparer.Ordinal);
            bool IsDMagic(IKsp1Node m) => "DMModuleScienceAnimateGeneric".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);
            var bdb = RealParts().Where(p => p.Name.StartsWith("bluedog", StringComparison.OrdinalIgnoreCase) && p.Modules.Any(IsDMagic))
                .Select(p => p.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(4).ToList();
            Assert.AreEqual(4, bdb.Count, "four BDB DMagic Generic parts installed");
            var names = new[] { "GooExperiment", "science_module", "mk1pod_v2" }.Concat(bdb).ToList();
            var switches = new[]
            {
                "{}", "{\"ksp1_science\": false}", "{\"science_generate_definitions\": false}", "{\"science_report_storage\": false}",
                "{\"science_transmit_split\": false}", "{\"science_deploy_animations\": false}", "{\"science_wait_for_animation\": false}",
                "{\"science_definitions_via_patch_manager\": false}", "{\"science_value_scale\": 2}", "{\"enabled\": false}",
            };
            var natives = KSP1PartMod.Native.Science.NativeExperiments.Redux26w42c;
            var clips = 0;
            foreach (var name in names)
            {
                var parts = RealParts().Where(p => p.Name == name).ToList();
                Assert.AreEqual(1, parts.Count, name + " installed once");
                var modules = parts[0].Modules;
                var line = Hex(name, 12) + " " + Hex(KeysOf(modules, Last032Kind), 16);
                Assert.IsTrue(snapshot032.Contains(line), name + ": 0.3.2 snapshot");
                Assert.IsTrue(snapshot040.Contains(line), name + ": 0.4.0 snapshot");
                var every = KeysOf(modules, (EntryKind)int.MaxValue);
                var found = false;
                // An invented definition for each id of the part, so the BDB ids resolve (the real ones are read in the census).
                var definitions = new Dictionary<string, KSP1PartMod.Native.Science.ScienceDefinitionModel>(StringComparer.Ordinal);
                foreach (var id in modules.Select(m => m.GetValue("experimentID")?.Trim()).Where(i => !string.IsNullOrEmpty(i)).Distinct())
                    definitions[id] = KSP1PartMod.Native.Science.ScienceDefinitionBuilder.From(
                        CfgText.Parse($"EXPERIMENT_DEFINITION\n{{\nid = {id}\ntitle = Invented\nbaseValue = 1\nsituationMask = 63\nbiomeMask = 0\n}}")
                            .GetNodes("EXPERIMENT_DEFINITION").First(), null, KSP1PartMod.Animation.Settings.AnimationSettings.Defaults, null);
                foreach (var json in switches)
                {
                    var settings = KSP1PartMod.Animation.Settings.AnimationSettings.Parse(json, out var error);
                    Assert.IsNull(error, json);
                    var science = KSP1PartMod.Native.Science.ScienceMapping.Resolve(modules, definitions, natives, settings);
                    Assert.AreEqual(line, Hex(name, 12) + " " + Hex(KeysOf(modules, Last032Kind), 16), $"{name}: 0.3.2 kinds after {json}");
                    Assert.AreEqual(every, KeysOf(modules, (EntryKind)int.MaxValue), $"{name}: every key after {json}");
                    var plan = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), new HashSet<string>(), false, true, null, true, true, false, false);
                    foreach (var config in science.Configurations)
                        if (KSP1PartMod.Native.Science.ScienceAnimRules.TargetsFor(config, plan.Entries, true).First != null) found = true;
                }
                if (found) clips++;
                TestContext.WriteLine($"{name}: keys unchanged with the science switches on and off; clip found {found}");
            }
            Assert.That(clips, Is.GreaterThanOrEqualTo(6), "the goo canister, the materials bay and the four BDB parts find their clip");
        }

        /// <summary>Run alone, on the 0.4.0 planner only (4695d2b, before phase 2b Task 5): it (re)writes the 0.4.0
        /// snapshot. 0.4.0 adds no entry kind: its kinds are those of 0.3.2.</summary>
        [Test, Explicit("writes the 0.4.0 key snapshot")]
        public void Write_KeySnapshot040()
        {
            var lines = CurrentKeysOfKinds(Last032Kind);
            File.WriteAllText(SnapshotPath(Snapshot040Name), string.Join("\n", lines) + "\n");
            TestContext.WriteLine($"{lines.Count} part line(s) written to {SnapshotPath(Snapshot040Name)}");
        }
    }
}
