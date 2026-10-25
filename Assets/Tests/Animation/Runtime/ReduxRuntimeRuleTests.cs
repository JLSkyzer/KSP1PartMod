using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class ReduxRuntimeRuleTests
    {
        [Test]
        public void RestrictionRule_SynthesisedData_LeavesReduxAlone() =>
            Assert.AreEqual("no data, an empty one was made", ReduxRuntimeRule.SkipReason(true, 3, true));

        [Test]
        public void RestrictionRule_NoEntry_LeavesReduxAlone() =>
            Assert.AreEqual("no entry of ours", ReduxRuntimeRule.SkipReason(false, 0, true));

        [Test]
        public void RestrictionRule_NoReduxClipList_LeavesReduxAlone() =>
            Assert.AreEqual("no Redux clip list", ReduxRuntimeRule.SkipReason(false, 2, false));

        [Test]
        public void RestrictionRule_RealData_Restricts() =>
            Assert.IsNull(ReduxRuntimeRule.SkipReason(false, 1, true));

        [Test]
        public void RestrictionRule_KeepsReduxClipsWhenNothingFailed() =>
            CollectionAssert.AreEqual(new[] { "solar" },
                ReduxRuntimeRule.KeptClips(new[] { "solar" }, new string[0], new[] { "solar", "bay" }));

        [Test]
        public void RestrictionRule_FailedEntryClipGoesBackToRedux() =>
            CollectionAssert.AreEqual(new[] { "solar", "bay" },
                ReduxRuntimeRule.KeptClips(new[] { "solar" }, new[] { "bay" }, new[] { "solar", "bay", "door" }));

        [Test]
        public void RestrictionRule_FailedClipReduxNeverPlayedIsNotAdded() =>
            CollectionAssert.AreEqual(new[] { "solar" },
                ReduxRuntimeRule.KeptClips(new[] { "solar" }, new[] { "heat" }, new[] { "solar", "bay" }));

        [Test]
        public void RestrictionRule_NoReduxClipAndNoFailure_KeepsNone() =>
            CollectionAssert.IsEmpty(ReduxRuntimeRule.KeptClips(new string[0], new string[0], new[] { "bay" }));

        [Test]
        public void RestrictionRule_DropsBlanksAndDuplicates() =>
            CollectionAssert.AreEqual(new[] { "solar", "bay" },
                ReduxRuntimeRule.KeptClips(new[] { "solar", " ", null, "solar" }, new[] { "bay", "bay", "solar", "" }, new[] { "solar", "bay" }));

        [Test]
        public void RestrictionRule_NullRuntimeClips_KeepsReduxClipsOnly() =>
            CollectionAssert.AreEqual(new[] { "solar" },
                ReduxRuntimeRule.KeptClips(new[] { "solar" }, new[] { "bay" }, null));
    }
}
