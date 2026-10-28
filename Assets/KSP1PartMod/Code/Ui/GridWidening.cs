using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSP1PartMod.Ui
{
    /// <summary>The sub-category bar takes its width from the left of the part grid (<c>partContentRT</c>), but the grid
    /// lays its buttons out with a fixed cell size: in game (0.4.0) the third column was cut by the panel's right border.
    /// So while the bar is shown, the picker panel grows by the bar's width instead: the first ancestor of the grid's
    /// parent whose width is fixed (both horizontal anchors equal) gets that width, its left edge kept, and every
    /// horizontally stretched child in between follows; the background panel (<c>partsPickerBGPanelRT</c>, a sibling with
    /// its own fixed width) gets it too. The grid keeps its own width. Everything is given back when the bar hides.</summary>
    public static class GridWidening
    {
        /// <summary>Horizontal anchors of one RectTransform.</summary>
        public struct Node
        {
            public float AnchorMinX, AnchorMaxX;
            public Node(float anchorMinX, float anchorMaxX)
            {
                AnchorMinX = anchorMinX;
                AnchorMaxX = anchorMaxX;
            }

            /// <summary>Its width is its own (<c>sizeDelta.x</c>), not its parent's.</summary>
            public bool FixedWidth => Math.Abs(AnchorMaxX - AnchorMinX) < 1e-4f;
        }

        /// <summary>Index of the first fixed-width node in <paramref name="chain"/> (the grid's parent first, then up to and
        /// including the picker's own RectTransform), or -1 when every one stretches.</summary>
        public static int WidthOwner(IReadOnlyList<Node> chain)
        {
            if (chain == null) return -1;
            for (var i = 0; i < chain.Count; i++)
                if (chain[i].FixedWidth) return i;
            return -1;
        }

        /// <summary>New <c>anchoredPosition.x</c> and <c>sizeDelta.x</c> of a fixed-width node widened by
        /// <paramref name="delta"/> with its left edge kept: the pivot moves right by <c>delta × pivot.x</c>.</summary>
        public static void Widen(float anchoredX, float sizeX, float pivotX, float delta, out float newAnchoredX, out float newSizeX)
        {
            newAnchoredX = anchoredX + delta * pivotX;
            newSizeX = sizeX + delta;
        }

        /// <summary>Columns of a grid of <paramref name="width"/> (padding included) that fit whole.</summary>
        public static int Columns(float width, float paddingLeft, float paddingRight, float cellWidth, float spacing)
        {
            if (cellWidth <= 0f) return 0;
            var inner = width - paddingLeft - paddingRight;
            if (inner + 0.01f < cellWidth) return 0;
            return 1 + (int)Math.Floor((inner - cellWidth + 0.01f) / (cellWidth + spacing));
        }

        /// <summary>The once-per-session diagnostic of the grid during a search (bar hidden, nothing of ours applied).</summary>
        public static string SearchLine(float viewport, float cellWidth, float cellHeight, float spacing, int paddingLeft,
            int paddingRight, string constraint, int constraintCount, int columns, float contentWidth) =>
            string.Format(CultureInfo.InvariantCulture,
                "[KSP1PartMod] ui: grid during search: viewport {0:0.#}, cell {1:0.#}x{2:0.#}, spacing {3:0.#}, padding {4}/{5}, " +
                "constraint {6} {7}, columns that fit {8}, content width {9:0.#}",
                viewport, cellWidth, cellHeight, spacing, paddingLeft, paddingRight, constraint, constraintCount, columns, contentWidth);

        /// <summary>The once-per-session line.</summary>
        public static string Line(string owner, float ownerBefore, float ownerAfter, string background, float backgroundBefore,
            float backgroundAfter, float gridWidth, string gridLayout)
        {
            var c = CultureInfo.InvariantCulture;
            var bg = background == null
                ? "no background panel"
                : string.Format(c, "'{0}' {1:0.#} -> {2:0.#}", background, backgroundBefore, backgroundAfter);
            return string.Format(c, "[KSP1PartMod] ui: grid kept {0:0.#} wide: picker widened by {1:0.#} for the bar " +
                                    "('{2}' {3:0.#} -> {4:0.#}, {5}); {6}",
                gridWidth, ownerAfter - ownerBefore, owner, ownerBefore, ownerAfter, bg, gridLayout ?? "no grid layout found");
        }
    }
}
