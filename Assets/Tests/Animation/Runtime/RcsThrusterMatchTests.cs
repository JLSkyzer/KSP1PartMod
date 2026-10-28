using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class RcsThrusterMatchTests
    {
        [Test] public void SameOrder_PairsByIndex() =>
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, RcsThrusterMatch.Pair(new[] { 10, 20, 30, 40 }, new[] { 11, 21, 31, 41 }));

        [Test] public void BothSidesPutInHierarchyOrder()
        {
            // Samplers bound set by set (host 40 first), thrusters filled by Redux registry names first.
            var map = RcsThrusterMatch.Pair(new[] { 40, 10, 30, 20 }, new[] { 31, 11, 41, 21 });
            // host 40 is the 4th host -> the 4th thruster in hierarchy (41, index 2); host 10 -> 11 (index 1)...
            CollectionAssert.AreEqual(new[] { 2, 1, 0, 3 }, map);
        }

        [Test] public void SamplersOnOneHost_ShareItsThruster() =>
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, RcsThrusterMatch.Pair(new[] { 5, 5, 9 }, new[] { 6, 10 }));

        [Test] public void ThrusterOutsideThePart_KeptLast() =>
            CollectionAssert.AreEqual(new[] { 1, 0 }, RcsThrusterMatch.Pair(new[] { 1, 2 }, new[] { -1, 3 }));

        [TestCase(new[] { 1, 2, 3 }, new[] { 1, 2 })]
        [TestCase(new[] { 1 }, new[] { 1, 2, 3, 4 })]
        [TestCase(new int[0], new int[0])]
        public void CountMismatchOrNothing_Null(int[] hosts, int[] thrusters) =>
            Assert.IsNull(RcsThrusterMatch.Pair(hosts, thrusters));

        [Test] public void NullInputs_Null()
        {
            Assert.IsNull(RcsThrusterMatch.Pair(null, new[] { 1 }));
            Assert.IsNull(RcsThrusterMatch.Pair(new[] { 1 }, null));
        }
    }
}
