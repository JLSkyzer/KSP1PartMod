using System;
using System.Collections.Generic;

namespace KSP1PartMod.Placement
{
    /// <summary>Picks a free position for a new tech node next to its anchor, inside the y range the tree already
    /// uses. The R&amp;D anchors its nodes at the bottom of the tree and puts +y upwards unless it inverts the
    /// placement; it scrolls down to nodes below that range but never up to nodes above it (IL of
    /// RDCenterUIController and Redux.TechTree.TechTreeLayout, 26w42c, 2026-10-25). Staying inside the range keeps the
    /// node visible whatever the sign, so the sign is never assumed here.</summary>
    public static class NodeLayout
    {
        private const int FineDivisions = 4;

        /// <summary>The [min, max] of the given y values, or null when there is none.</summary>
        public static (float min, float max)? YRange(IEnumerable<float> ys)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            if (ys != null)
                foreach (var y in ys)
                {
                    if (float.IsNaN(y) || float.IsInfinity(y)) continue;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            return min <= max ? (min, max) : ((float, float)?)null;
        }

        /// <summary>x is anchor.x + offset.x. y is the free slot nearest to anchor.y among anchor.y + k * spacing
        /// that lie inside yRange; at equal distance the side of offset.y comes first (upwards in tree units for a
        /// zero offset). With no free slot on that grid, a finer grid (spacing / 4) is tried, then the in-range point
        /// farthest from every taken point. A null yRange (empty tree) means the anchor's y only. The result's y is
        /// never outside yRange.</summary>
        public static (float x, float y) PlaceNewNode((float x, float y) anchor, (float x, float y) offset,
            IReadOnlyCollection<(float x, float y)> taken, (float min, float max)? yRange, float spacing = 120f)
        {
            if (spacing <= 0f) throw new ArgumentOutOfRangeException(nameof(spacing));
            var x = anchor.x + offset.x;
            var range = yRange ?? (anchor.y, anchor.y);
            var preferred = offset.y < 0f ? -1 : 1;
            // The anchor may itself lie outside the range of its tier (yRange is computed by the caller).
            var centre = Clamp(anchor.y, range.min, range.max);

            if (TryNearestFree(x, centre, spacing, range, preferred, taken, spacing, out var y)) return (x, y);
            if (TryNearestFree(x, centre, spacing / FineDivisions, range, preferred, taken, spacing, out y)) return (x, y);
            return (x, MostClearance(x, centre, spacing / FineDivisions, range, taken));
        }

        private static bool TryNearestFree(float x, float centre, float step, (float min, float max) range, int preferred,
            IReadOnlyCollection<(float x, float y)> taken, float spacing, out float y)
        {
            var maxK = MaxSteps(centre, step, range);
            for (var k = 0; k <= maxK; k++)
                foreach (var sign in k == 0 ? new[] { 1 } : new[] { preferred, -preferred })
                {
                    var candidate = centre + sign * k * step;
                    if (candidate < range.min || candidate > range.max) continue;
                    if (!IsFree((x, candidate), taken, spacing)) continue;
                    y = candidate;
                    return true;
                }
            y = centre;
            return false;
        }

        private static float MostClearance(float x, float centre, float step, (float min, float max) range,
            IReadOnlyCollection<(float x, float y)> taken)
        {
            var best = centre;
            var bestClearance = Clearance((x, centre), taken);
            var maxK = MaxSteps(centre, step, range);
            for (var k = 1; k <= maxK; k++)
                foreach (var sign in new[] { 1, -1 })
                {
                    var candidate = centre + sign * k * step;
                    if (candidate < range.min || candidate > range.max) continue;
                    var c = Clearance((x, candidate), taken);
                    if (c > bestClearance) { best = candidate; bestClearance = c; }
                }
            return best;
        }

        private static int MaxSteps(float centre, float step, (float min, float max) range)
        {
            var reach = Math.Max(centre - range.min, range.max - centre);
            return (int)Math.Min(10000, Math.Floor(reach / step + 1e-4));
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        private static float Clearance((float x, float y) p, IReadOnlyCollection<(float x, float y)> taken)
        {
            var best = float.PositiveInfinity;
            if (taken == null) return best;
            foreach (var t in taken)
            {
                float dx = p.x - t.x, dy = p.y - t.y;
                var d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
            return best;
        }

        private static bool IsFree((float x, float y) p, IReadOnlyCollection<(float x, float y)> taken, float spacing) =>
            Clearance(p, taken) >= spacing * spacing;
    }
}
