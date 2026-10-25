using System;
using System.Collections.Generic;

namespace KSP1PartMod.Placement
{
    /// <summary>Picks a free position for a new tech node next to its anchor.</summary>
    public static class NodeLayout
    {
        /// <summary>Sign of y that means "down" in the tree, used when the offset is zero. Assumed screen-like
        /// (y grows downwards); not verified against the KSP2 tree.</summary>
        public const float Down = 1f;

        private const int MaxSteps = 1000;

        /// <summary>Starts at anchor + offset and steps by spacing along the offset direction (downwards for a
        /// zero offset) until no taken point is closer than spacing.</summary>
        public static (float x, float y) PlaceNewNode((float x, float y) anchor, (float x, float y) offset,
            IReadOnlyCollection<(float x, float y)> taken, float spacing = 120f)
        {
            var length = (float)Math.Sqrt(offset.x * offset.x + offset.y * offset.y);
            var dir = length > 0f ? (x: offset.x / length, y: offset.y / length) : (x: 0f, y: Down);

            var p = (x: anchor.x + offset.x, y: anchor.y + offset.y);
            for (var step = 0; step < MaxSteps && !IsFree(p, taken, spacing); step++)
                p = (p.x + dir.x * spacing, p.y + dir.y * spacing);
            return p;
        }

        private static bool IsFree((float x, float y) p, IReadOnlyCollection<(float x, float y)> taken, float spacing)
        {
            if (taken == null) return true;
            foreach (var t in taken)
            {
                float dx = p.x - t.x, dy = p.y - t.y;
                if (dx * dx + dy * dy < spacing * spacing) return false;
            }
            return true;
        }
    }
}
