using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    /// <summary>Which legacy states an entry closed by a B9 gate samples at rest (review I1): only those no active entry
    /// of the part holds.</summary>
    public class RestPlanTests
    {
        private sealed class S { }

        [Test] public void Unshared_NoActiveEntry_AllSampled()
        {
            var a = new S();
            var b = new S();
            CollectionAssert.AreEqual(new[] { true, true }, RestPlan.Unshared(new[] { a, b }, new S[0][]));
            CollectionAssert.AreEqual(new[] { true }, RestPlan.Unshared(new[] { a }, null));
        }

        /// <summary>Two entries on one clip kept apart by gates: the clip's state is the active entry's; the inactive one
        /// samples its rest only on a state of its own.</summary>
        [Test] public void Unshared_StateHeldByActiveEntry_LeftToIt()
        {
            var shared = new S();
            var own = new S();
            var other = new S();
            var plan = RestPlan.Unshared(new[] { shared, own }, new[] { new[] { other }, new[] { shared } });
            CollectionAssert.AreEqual(new[] { false, true }, plan);
        }

        [Test] public void Unshared_NullsAndEmpty()
        {
            CollectionAssert.AreEqual(new[] { false }, RestPlan.Unshared(new S[] { null }, new[] { new S[] { null } }));
            Assert.AreEqual(0, RestPlan.Unshared<S>(null, null).Length);
            CollectionAssert.AreEqual(new[] { true }, RestPlan.Unshared(new[] { new S() }, new S[][] { null }));
        }
    }
}
