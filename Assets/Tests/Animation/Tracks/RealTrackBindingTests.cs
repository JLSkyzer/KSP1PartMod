using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    /// <summary>The binding and time-source rules on every clip that a plan of a real part names (our entries, Redux's
    /// sources, the parachute clips), as found in its resolved <c>.mu</c> files under the game's <c>mods/KSP1</c>.
    /// Whether a clip has a state on the instance is <see cref="TrackBindingRule.HasState"/> (Redux keeps only the clips
    /// that keep a Transform curve). Plans use the default settings (<c>follow_redux_deployables</c> on,
    /// <c>own_first_deployable</c> off). The cfgs are read before ModuleManager patches.</summary>
    public class RealTrackBindingTests
    {
        /// <summary>One clip of one Animation host of one resolved model of a part.</summary>
        private sealed class PlannedClip
        {
            public string Part, Clip, Host;
            public int Model, WrapMode;
            /// <summary>A curve of type 0 (Transform) with keys, whatever its property.</summary>
            public bool HasTransformCurve;
            public bool HasState, HasTracks;
            public TrackSet Set;
            public PartAnimationPlan Plan;
            public override string ToString() => $"{Part} '{Clip}' on model {Model} '{Host}'";
        }

        private static List<PlannedClip> _clips;

        private static List<PlannedClip> AllPlannedClips()
        {
            if (_clips != null) return _clips;
            var root = InstalledKsp1.Root;
            var resolver = new ModelPathResolver(root, () => DirectoryIndex.Build(root));
            var read = TrackCacheBuilder.ReadOnce(MuReader.Read);
            var list = new List<PlannedClip>();
            foreach (var path in InstalledKsp1.AllCfg())
            foreach (var part in CfgText.Parse(File.ReadAllText(path)).GetNodes("PART"))
            {
                var modules = part.GetNodes("MODULE").ToList();
                var solar = modules.Any(m => "ModuleDeployableSolarPanel".Equals(m.GetValue("name"), StringComparison.OrdinalIgnoreCase));
                var plan = OwnershipPlanner.Plan(modules, solar, new HashSet<string>(), false, true);
                var s = PartSnapshot.From(part.GetValue("name"), path, root, part, plan);
                if (s.Clips.Length == 0) continue;
                var wanted = new HashSet<string>(s.Clips, StringComparer.Ordinal);
                for (var i = 0; i < s.ModelRefs.Length; i++)
                {
                    var file = resolver.Resolve(path, s.ModelRefs[i], s.RefIsMesh[i], out _);
                    if (file == null) continue; // RealModelPathsTests covers the references
                    var stack = new Stack<MuNode>();
                    stack.Push(read(file).Root);
                    while (stack.Count > 0)
                    {
                        var node = stack.Pop();
                        foreach (var child in node.Children) stack.Push(child);
                        if (node.Animation == null) continue;
                        foreach (var clip in node.Animation.Clips)
                        {
                            if (clip.Name == null || !wanted.Contains(clip.Name)) continue;
                            list.Add(new PlannedClip
                            {
                                Part = s.Part, Clip = clip.Name, Host = node.Path(), Model = i, WrapMode = clip.WrapMode, Plan = plan,
                                HasTransformCurve = clip.Curves.Any(c => c.Type == 0 && c.Keys.Count > 0),
                                HasState = TrackBindingRule.HasState(clip),
                                Set = TrackBuilder.FromClip(clip, node.Path(), i, out _),
                            });
                        }
                    }
                }
            }
            foreach (var c in list) c.HasTracks = c.Set != null;
            Assume.That(list.Count, Is.GreaterThan(0), "no planned clip found");
            return _clips = list;
        }

        private static IEnumerable<PlannedClip> ClipsWithTracks() => AllPlannedClips().Where(c => c.HasTracks);

        private static IEnumerable<string> ClipsOf(AnimEntryDef e) =>
            new[] { e.Clip, e.SecondClip, e.ThirdClip }.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal);

        /// <summary>Every owner the plan gives the clip, by the table of spec 2a.4. A GearBridge only adds an action
        /// group on Redux's clip and an AeroGate names no clip: neither gives a time. Redux's single source is drawn
        /// by our ReduxFollower entry, which runs on its own clock when the clip has no state.</summary>
        private static IEnumerable<ClipOwner> OwnersOf(PlannedClip c)
        {
            foreach (var e in c.Plan.Entries)
            {
                if (e.Kind == EntryKind.GearBridge || e.Kind == EntryKind.AeroGate) continue;
                if (!ClipsOf(e).Contains(c.Clip, StringComparer.Ordinal)) continue;
                yield return e.Kind == EntryKind.ReduxFollower && !c.HasState
                    ? ClipOwner.ReduxSingleSourceNoState : ClipOwner.OurEntry;
            }
            if (c.Plan.ReduxClips.Contains(c.Clip, StringComparer.Ordinal) && c.Plan.ReduxDeployableModule == "Ksp1Module_Deployable")
                yield return c.HasState ? ClipOwner.ReduxRuntimeWithState : ClipOwner.ReduxRuntimeNoState;
        }

        [Test]
        public void TrackBinding_ClipWithTransformCurve_NeverTracksOnly_AllRealParts() =>
            CollectionAssert.IsEmpty(AllPlannedClips().Where(c => c.HasTransformCurve && TrackBindingRule.Mode(c.HasState, c.HasTracks) == BindMode.TracksOnly)
                .Select(c => c.ToString()));

        [Test]
        public void TrackOwners_OneTimeSourcePerClip_AllRealParts() =>
            CollectionAssert.IsEmpty(ClipsWithTracks().Where(c => OwnersOf(c).Count() != 1)
                .Select(c => $"{c} owners [{string.Join(", ", OwnersOf(c))}]"));

        [Test]
        public void TrackOwners_EveryOwnerHasATimeSource_AllRealParts() =>
            CollectionAssert.IsEmpty(ClipsWithTracks().SelectMany(OwnersOf).Where(o => TimeSourceRule.For(o) == TimeSource.None));

        /// <summary>Not an assertion: the counts the report quotes (bind modes, owners, wrap modes).</summary>
        [Test]
        public void RealClips_Counts()
        {
            var all = AllPlannedClips();
            var text = new StringBuilder();
            text.AppendLine($"planned clip occurrences: {all.Count}, with tracks: {all.Count(c => c.HasTracks)}");
            text.AppendLine($"  with a state: {all.Count(c => c.HasState)}, Transform curve but no state: {all.Count(c => c.HasTransformCurve && !c.HasState)}");
            foreach (var g in all.GroupBy(c => TrackBindingRule.Mode(c.HasState, c.HasTracks)).OrderBy(g => g.Key))
                text.AppendLine($"  mode {g.Key}: {g.Count()}");
            foreach (var g in ClipsWithTracks().SelectMany(OwnersOf).GroupBy(o => o).OrderBy(g => g.Key))
                text.AppendLine($"  owner {g.Key} ({TimeSourceRule.For(g.Key)}): {g.Count()}");
            foreach (var g in ClipsWithTracks().GroupBy(c => c.WrapMode).OrderBy(g => g.Key))
                text.AppendLine($"  wrap mode {g.Key}: {g.Count()}");
            TestContext.WriteLine(text.ToString());
        }

        /// <summary>Not an assertion (review I3): the (renderer path, property) slots of a part that the tracks of two or
        /// more clips write, as the default <c>shader_property_map</c> names the property (first candidate). Per slot,
        /// the owners of its clips: entry kind and legacy layer (a Group's second clip on layer 4), or Redux.</summary>
        [Test]
        public void RealClips_SharedSlots_Counts()
        {
            var map = KSP1PartMod.Animation.Settings.AnimationSettings.Defaults.ShaderPropertyMap;
            var slots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var partOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in ClipsWithTracks())
            {
                var owner = string.Join("+", OwnerLabels(c));
                foreach (var track in c.Set.Tracks)
                {
                    var property = track.IsLight
                        ? "light:" + track.Kind
                        : KSP1PartMod.Animation.Runtime.MaterialRules.Candidates(track.Property, map).FirstOrDefault() ?? track.Property;
                    var key = $"{c.Part}|{c.Model}|{c.Host}/{track.RelativePath}|{property}";
                    if (!slots.TryGetValue(key, out var clips)) slots[key] = clips = new HashSet<string>(StringComparer.Ordinal);
                    clips.Add(c.Clip);
                    owners[key + "|" + c.Clip] = owner;
                    partOf[key] = c.Part;
                }
            }
            var shared = slots.Where(kv => kv.Value.Count > 1).ToList();
            var text = new StringBuilder();
            text.AppendLine($"slots written by tracks: {slots.Count}, by two or more clips: {shared.Count}, " +
                            $"in {shared.Select(kv => partOf[kv.Key]).Distinct().Count()} part(s)");
            foreach (var g in shared.GroupBy(kv => string.Join(" / ", kv.Value.Select(clip => owners[kv.Key + "|" + clip]).OrderBy(o => o, StringComparer.Ordinal)))
                         .OrderByDescending(g => g.Count()))
                text.AppendLine($"  owners [{g.Key}]: {g.Count()} slot(s)");
            foreach (var g in shared.GroupBy(kv => kv.Key.Substring(kv.Key.LastIndexOf('|') + 1)).OrderByDescending(g => g.Count()))
                text.AppendLine($"  property {g.Key}: {g.Count()}");
            TestContext.WriteLine(text.ToString());
        }

        private static IEnumerable<string> OwnerLabels(PlannedClip c)
        {
            foreach (var e in c.Plan.Entries)
            {
                if (e.Kind == EntryKind.GearBridge || e.Kind == EntryKind.AeroGate) continue;
                if (string.Equals(e.Clip, c.Clip, StringComparison.Ordinal)) yield return $"{e.Kind}@{e.Layer}";
                else if (ClipsOf(e).Contains(c.Clip, StringComparer.Ordinal))
                    yield return e.Kind == EntryKind.Group ? $"{e.Kind}-active@4" : $"{e.Kind}-second@{e.Layer}";
            }
            if (c.Plan.ReduxClips.Contains(c.Clip, StringComparer.Ordinal)) yield return "Redux";
        }
    }
}
