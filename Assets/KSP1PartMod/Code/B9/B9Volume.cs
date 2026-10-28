using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>The tank volume of a B9 subtype. The fields are B9PartSwitch's (public wiki: <c>baseVolume</c>,
    /// <c>volumeAdded</c>, <c>volumeMultiplier</c>, <c>volumeAddedToParent</c>, <c>parentID</c>); the formula is
    /// <b>inferred</b> (S), the wiki gives none:
    /// <c>baseVolume × volumeMultiplier (default 1) + volumeAdded (default 0) + Σ children</c>, where each B9 module whose
    /// <c>parentID</c> is this module's <c>moduleID</c> adds the <c>volumeAddedToParent</c> of its <b>default</b> subtype
    /// (the child's choice is not followed: a limit of this item).</summary>
    public static class B9Volume
    {
        /// <summary>The volume, or null when a value it needs is not a number (a <c>#$…$</c> variable left as text).
        /// A missing <c>baseVolume</c> is 0.</summary>
        public static double? Of(IKsp1Node module, IKsp1Node subtype, IReadOnlyList<IKsp1Node> allB9Modules)
        {
            if (module == null || subtype == null) return null;
            var baseVolume = Base(module);
            var multiplier = B9Values.Optional(subtype.GetValue("volumeMultiplier"), out var badMultiplier);
            var added = B9Values.Optional(subtype.GetValue("volumeAdded"), out var badAdded);
            if (baseVolume == null || badMultiplier || badAdded) return null;
            var children = 0.0;
            foreach (var child in Children(module, allB9Modules))
            {
                var v = AddedToParent(child);
                if (v == null) return null;
                children += v.Value;
            }
            return baseVolume.Value * (multiplier ?? 1) + (added ?? 0) + children;
        }

        /// <summary><c>baseVolume</c>: 0 when missing, null when not a number.</summary>
        public static double? Base(IKsp1Node module)
        {
            var v = B9Values.Optional(module?.GetValue("baseVolume"), out var bad);
            return bad ? (double?)null : v ?? 0;
        }

        /// <summary>The B9 modules whose <c>parentID</c> is <paramref name="module"/>'s <c>moduleID</c> (trimmed, ordinal),
        /// in order; none when it has no <c>moduleID</c>.</summary>
        public static IReadOnlyList<IKsp1Node> Children(IKsp1Node module, IReadOnlyList<IKsp1Node> allB9Modules)
        {
            var id = B9Values.Trim(module?.GetValue("moduleID"));
            if (string.IsNullOrEmpty(id) || allB9Modules == null) return new IKsp1Node[0];
            return allB9Modules.Where(m => m != null && !ReferenceEquals(m, module)
                                           && string.Equals(B9Values.Trim(m.GetValue("parentID")), id, StringComparison.Ordinal)).ToList();
        }

        /// <summary>The <c>volumeAddedToParent</c> of a child's default subtype: 0 when missing or without SUBTYPE, null
        /// when not a number.</summary>
        public static double? AddedToParent(IKsp1Node child)
        {
            var st = DefaultSubtypeNode(child);
            if (st == null) return 0;
            var v = B9Values.Optional(st.GetValue("volumeAddedToParent"), out var bad);
            return bad ? (double?)null : v ?? 0;
        }

        /// <summary>The SUBTYPE Redux selects by default (<see cref="VariantSwitches.B9DefaultIndex"/>), or null.</summary>
        internal static IKsp1Node DefaultSubtypeNode(IKsp1Node b9)
        {
            var subtypes = b9?.GetNodes("SUBTYPE").ToList();
            return subtypes == null || subtypes.Count == 0 ? null : subtypes[VariantSwitches.B9DefaultIndex(b9, subtypes)];
        }
    }
}
