using System.Collections.Generic;
using KSP1PartMod.Placement;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Placement
{
    public class NodeLayoutTests
    {
        private static readonly (float, float)[] None = new (float, float)[0];

        [Test] public void PlaceNewNode_FreeSpot_IsAnchorPlusOffsetX_AtAnchorY() =>
            Assert.AreEqual((300f, 0f), NodeLayout.PlaceNewNode((100f, 0f), (200f, 50f), None, (-300f, 300f)));

        [Test] public void PlaceNewNode_StaysInsideExistingRange()
        {
            // The anchor is the top node: the old layout put the node 200 above it, outside the visible tree.
            var p = NodeLayout.PlaceNewNode((0f, 100f), (0f, 200f), new[] { (0f, 100f) }, (-300f, 100f));
            Assert.AreEqual(0f, p.x);
            Assert.AreEqual(-20f, p.y);
            Assert.That(p.y, Is.InRange(-300f, 100f));
        }

        [Test] public void PlaceNewNode_PicksNearestFreeSlot()
        {
            // +120 (the offset's side) is taken, -120 is free and nearer than +240.
            var p = NodeLayout.PlaceNewNode((0f, 0f), (0f, 200f), new[] { (0f, 0f), (0f, 120f) }, (-600f, 600f));
            Assert.AreEqual((0f, -120f), p);
        }

        [Test] public void PlaceNewNode_EqualDistance_PrefersOffsetSide()
        {
            var taken = new[] { (0f, 0f) };
            Assert.AreEqual((0f, 120f), NodeLayout.PlaceNewNode((0f, 0f), (0f, 200f), taken, (-600f, 600f)));
            Assert.AreEqual((0f, -120f), NodeLayout.PlaceNewNode((0f, 0f), (0f, -200f), taken, (-600f, 600f)));
        }

        [Test] public void PlaceNewNode_NoGridSlot_UsesFinerGrid()
        {
            // Range [0, 150]: the 120 grid gives 0 and 120, both too close; on the 30 grid, 150 is the first free point.
            var p = NodeLayout.PlaceNewNode((0f, 0f), (0f, 0f), new[] { (0f, 0f), (0f, 30f) }, (0f, 150f));
            Assert.AreEqual((0f, 150f), p);
        }

        [Test] public void PlaceNewNode_FullRange_StaysInsideWithMostClearance()
        {
            var p = NodeLayout.PlaceNewNode((0f, 0f), (0f, 200f), new[] { (0f, 0f), (0f, 100f) }, (0f, 100f));
            Assert.That(p.y, Is.InRange(0f, 100f));
            Assert.AreEqual(60f, p.y, 1e-3); // 0 or 100 overlap a node; 30 apart from one, 60 is 40 from the nearest
        }

        [Test] public void PlaceNewNode_AnchorOutsideRange_StartsFromTheClampedY() =>
            Assert.AreEqual((0f, 50f), NodeLayout.PlaceNewNode((0f, 400f), (0f, 0f), None, (-50f, 50f)));

        [Test] public void PlaceNewNode_NoRange_KeepsAnchorY() =>
            Assert.AreEqual((200f, 10f), NodeLayout.PlaceNewNode((0f, 10f), (200f, -200f), new[] { (0f, 10f) }, null));

        [Test] public void YRange_OfValues_AndOfNothing()
        {
            Assert.AreEqual(((float, float)?)(-5f, 9f), NodeLayout.YRange(new List<float> { 3f, -5f, 9f, float.NaN }));
            Assert.IsNull(NodeLayout.YRange(new List<float>()));
            Assert.IsNull(NodeLayout.YRange(null));
        }
    }
}
