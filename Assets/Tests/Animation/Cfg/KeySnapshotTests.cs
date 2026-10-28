using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
        private static string KeysOf(IReadOnlyList<IKsp1Node> modules, EntryKind lastKind)
        {
            var sb = new StringBuilder();
            foreach (var own in new[] { false, true })
            foreach (var follow in new[] { false, true })
            {
                // Plugin modules and B9 gates on (the 0.5.0 defaults): the keys of the older kinds must not move.
                var plan = OwnershipPlanner.Plan(modules, HasNativeSolar(modules), new HashSet<string>(), own, follow, null, true, true);
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
        /// hold the keys of the plan with it on.</summary>
        [Test] public void Plan_AblatorChar_KeepsEveryKey_AddsOnlyAblatorEntries_AllRealParts()
        {
            var partsWithChar = 0;
            foreach (var (name, modules) in RealParts())
            {
                var withChar = false;
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
                }
                if (withChar) partsWithChar++;
            }
            TestContext.WriteLine($"{partsWithChar} real part(s) get an ablator char");
            Assert.AreEqual(9, partsWithChar, "the 9 installed shields with useChar = True and a shieldChar");
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
