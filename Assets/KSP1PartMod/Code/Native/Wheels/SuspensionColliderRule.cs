using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Mu;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>What the part's models hold under a suspension's <c>suspensionColliderName</c>.</summary>
    public enum SuspensionColliderState
    {
        /// <summary>No transform of that name: KSP2 looks it up, finds none and skips it.</summary>
        Absent,
        /// <summary>The transform exists and a collider entry sits on it or below it.</summary>
        Found,
        /// <summary>The transform exists with no collider on it or below it: KSP2's suspension would set
        /// <c>enabled</c> on a missing collider when the wheel initialises in flight.</summary>
        NoCollider,
    }

    /// <summary>The suspension collider check of the wheel importer (Task 3 review, F6), on the <c>.mu</c> records. Redux's model
    /// importer gives each object one Unity collider per box, sphere, capsule or mesh collider entry (types 3 to 6 and 25 to
    /// 28); a <c>.mu</c> WheelCollider entry (29) is skipped by Redux, so it does not count. KSP2 searches the transform by name
    /// through the part's hierarchy, then takes the first collider on it or below it. Pure.</summary>
    public static class SuspensionColliderRule
    {
        private static readonly HashSet<int> ColliderTypes = new HashSet<int>
        {
            MuEntryType.MeshCollider, MuEntryType.SphereCollider, MuEntryType.CapsuleCollider, MuEntryType.BoxCollider,
            MuEntryType.MeshCollider2, MuEntryType.SphereCollider2, MuEntryType.CapsuleCollider2, MuEntryType.BoxCollider2,
        };

        private static IEnumerable<MuNode> All(MuNode n) => n == null ? Enumerable.Empty<MuNode>() : new[] { n }.Concat(n.Children.SelectMany(All));

        public static bool HasCollider(MuNode n) => All(n).Any(x => x.EntryTypes.Any(ColliderTypes.Contains));

        /// <summary>The state of <paramref name="name"/> across the part's model roots (exact name, as KSP2 searches it). Several
        /// objects of that name: <see cref="SuspensionColliderState.Found"/> only when every one has a collider, because the search
        /// order between models is not pinned.</summary>
        public static SuspensionColliderState Check(IEnumerable<MuNode> modelRoots, string name)
        {
            var matches = (modelRoots ?? Enumerable.Empty<MuNode>()).SelectMany(All)
                .Where(n => string.Equals(n.Name, name, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) return SuspensionColliderState.Absent;
            return matches.All(HasCollider) ? SuspensionColliderState.Found : SuspensionColliderState.NoCollider;
        }

        /// <summary>The part note when the name is cleared.</summary>
        public static string ClearedNote(string name) =>
            $"ModuleWheelSuspension: suspensionColliderName '{name}' has no collider in the model, cleared (KSP2 would fail to switch it off)";
    }
}
