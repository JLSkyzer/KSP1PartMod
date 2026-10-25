using System.Collections.Generic;
using KSP1PartMod.Placement;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Placement
{
    public class NodeLayoutTests
    {
        [Test] public void PlaceNewNode_FreeSpot_IsAnchorPlusOffset() =>
            Assert.AreEqual((110f, 50f), NodeLayout.PlaceNewNode((100f, 0f), (10f, 50f), new List<(float, float)>()));

        [Test] public void PlaceNewNode_ShiftsUntilFree() =>
            Assert.AreEqual((0f, 340f), NodeLayout.PlaceNewNode((0f, 0f), (0f, 100f), new[] { (0f, 100f), (0f, 220f) }));

        [Test] public void PlaceNewNode_ShiftsAlongOffsetDirection() =>
            Assert.AreEqual((-320f, 0f), NodeLayout.PlaceNewNode((0f, 0f), (-200f, 0f), new[] { (-200f, 0f) }));

        [Test] public void PlaceNewNode_ZeroOffset_MovesDownwards()
        {
            var p = NodeLayout.PlaceNewNode((5f, 5f), (0f, 0f), new[] { (5f, 5f) });
            Assert.AreEqual(5f, p.x);
            Assert.AreEqual(5f + NodeLayout.Down * 120f, p.y);
        }
    }
}
