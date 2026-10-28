using KSP1PartMod.Ui;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Ui
{
    public class GridWideningTests
    {
        private static GridWidening.Node Stretch => new GridWidening.Node(0f, 1f);
        private static GridWidening.Node Fixed(float x) => new GridWidening.Node(x, x);

        [Test] public void WidthOwner_FirstFixedAncestor()
        {
            // GRP-Body (stretched in mask_PartsPicker), mask_PartsPicker (fixed, as logged in game), widget_PartsPicker.
            Assert.AreEqual(1, GridWidening.WidthOwner(new[] { Stretch, Fixed(0f), Fixed(0f) }));
            Assert.AreEqual(2, GridWidening.WidthOwner(new[] { Stretch, Stretch, Fixed(0.5f) }));
            Assert.AreEqual(0, GridWidening.WidthOwner(new[] { Fixed(1f) }));
        }

        [Test] public void WidthOwner_NoneWhenEverythingStretches()
        {
            Assert.AreEqual(-1, GridWidening.WidthOwner(new[] { Stretch, Stretch }));
            Assert.AreEqual(-1, GridWidening.WidthOwner(new GridWidening.Node[0]));
            Assert.AreEqual(-1, GridWidening.WidthOwner(null));
        }

        [TestCase(0f, 100f, 0f, 40f, 0f, 140f, TestName = "left pivot: position kept")]
        [TestCase(10f, 100f, 0.5f, 40f, 30f, 140f, TestName = "centre pivot: moves half the width")]
        [TestCase(10f, 100f, 1f, 40f, 50f, 140f, TestName = "right pivot: moves the whole width")]
        [TestCase(30f, 140f, 0.5f, -40f, 10f, 100f, TestName = "negative delta undoes it")]
        public void Widen_KeepsTheLeftEdge(float x, float w, float pivot, float delta, float ex, float ew)
        {
            GridWidening.Widen(x, w, pivot, delta, out var nx, out var nw);
            Assert.AreEqual(ex, nx, 1e-4f);
            Assert.AreEqual(ew, nw, 1e-4f);
            // Left edge relative to the anchor: x - pivot * w.
            Assert.AreEqual(x - pivot * w, nx - pivot * nw, 1e-4f);
        }

        [TestCase(389f, 0f, 0f, 120f, 8f, 3)]
        [TestCase(349f, 0f, 0f, 120f, 8f, 2)]   // the 0.4.0 narrowing by 40: the third column no longer fits
        [TestCase(376f, 0f, 0f, 120f, 8f, 3)]   // exactly 3 x 120 + 2 x 8
        [TestCase(375.9f, 0f, 0f, 120f, 8f, 2)]
        [TestCase(400f, 10f, 10f, 120f, 8f, 3)]
        [TestCase(100f, 0f, 0f, 120f, 8f, 0)]
        [TestCase(100f, 0f, 0f, 0f, 8f, 0)]
        public void Columns(float width, float left, float right, float cell, float spacing, int expected) =>
            Assert.AreEqual(expected, GridWidening.Columns(width, left, right, cell, spacing));

        [Test] public void Line()
        {
            Assert.AreEqual("[KSP1PartMod] ui: grid kept 397 wide: picker widened by 40 for the bar " +
                            "('mask_PartsPicker' 455 -> 495, 'BG-panel' 455 -> 495); grid 'g' 1 column(s) fit",
                GridWidening.Line("mask_PartsPicker", 455f, 495f, "BG-panel", 455f, 495f, 397f, "grid 'g' 1 column(s) fit"));
            StringAssert.EndsWith("('o' 1 -> 2, no background panel); no grid layout found", GridWidening.Line("o", 1f, 2f, null, 0f, 0f, 3f, null));
        }
    }
}
