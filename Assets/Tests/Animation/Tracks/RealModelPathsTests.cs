using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    /// <summary>The resolver and the builder on every plain <c>PART</c> node under the game's <c>mods/KSP1</c>. The
    /// cfgs are read before ModuleManager patches.</summary>
    public class RealModelPathsTests
    {
        private static List<PartSnapshot> _snapshots;
        private static ModelPathResolver _resolver;

        private static ModelPathResolver Resolver()
        {
            var root = InstalledKsp1.Root;
            return _resolver ?? (_resolver = new ModelPathResolver(root, () => DirectoryIndex.Build(root)));
        }

        private static List<PartSnapshot> AllRealSnapshots()
        {
            if (_snapshots != null) return _snapshots;
            var list = new List<PartSnapshot>();
            foreach (var path in InstalledKsp1.AllCfg())
            foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
            {
                var modules = part.GetNodes("MODULE").ToList();
                var solar = modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));
                var plan = OwnershipPlanner.Plan(modules, solar, new HashSet<string>(), false);
                list.Add(PartSnapshot.From(part.GetValue("name"), path, InstalledKsp1.Root, part, plan));
            }
            Assume.That(list.Count, Is.GreaterThan(0), "no PART node found");
            return _snapshots = list;
        }

        /// <summary>KSP1's prebuilt parts (kerbals on EVA, the flag) ship their prefab inside the game, not as a
        /// <c>.mu</c>: no rule can find their default <c>model.mu</c>. Every other reference resolves (2026-10-27:
        /// stock, KRE and BDB as installed), BDB's data bugs being clip names, not model paths.</summary>
        private static bool IsPrebuilt(PartSnapshot s) =>
            s.CfgPath.Replace(Path.DirectorySeparatorChar, '/').IndexOf("/Squad/Parts/Prebuilt/", StringComparison.OrdinalIgnoreCase) >= 0;

        private static IEnumerable<string> Unresolved() =>
            AllRealSnapshots().Where(s => !IsPrebuilt(s)).SelectMany(s => s.ModelRefs.Select((r, i) =>
                    Resolver().Resolve(s.CfgPath, r, s.RefIsMesh[i], out var tried) == null ? $"{s.Part}: {r} ({tried})" : null))
                .Where(x => x != null);

        [Test] public void Resolve_EveryModelRefOfEveryRealPart_FoundOrPrebuilt() => CollectionAssert.IsEmpty(Unresolved());

        [Test]
        public void Resolve_PrebuiltFolder_HoldsOnlyKerbalsAndTheFlag()
        {
            var prebuilt = AllRealSnapshots().Where(IsPrebuilt).Select(s => s.Part).ToList();
            CollectionAssert.IsNotEmpty(prebuilt);
            Assert.That(prebuilt, Has.All.Match("^(kerbalEVA.*|flag)$"));
        }

        [Test]
        public void Builder_AllRealParts_NoFailure()
        {
            var read = TrackCacheBuilder.ReadOnce(MuReader.Read);
            var failed = AllRealSnapshots().Select(s => (s, t: TrackCacheBuilder.Build(s, Resolver(), read)))
                .Where(x => x.t.Failed).Select(x => $"{x.s.Part}: {x.t.Note}").ToList();
            CollectionAssert.IsEmpty(failed);
        }

        [Test]
        public void Builder_AllRealParts_SomeTracks()
        {
            var read = TrackCacheBuilder.ReadOnce(MuReader.Read);
            Assert.Greater(AllRealSnapshots().Sum(s => TrackCacheBuilder.Build(s, Resolver(), read).Sets.Count), 0);
        }
    }
}
