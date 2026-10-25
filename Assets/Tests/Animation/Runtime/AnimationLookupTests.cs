using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class AnimationLookupTests
    {
        private static AnimationCandidate C(string obj, bool hasClip, bool reduxBound = false) =>
            new AnimationCandidate(obj, hasClip, reduxBound);

        [TestCase(EntryKind.Toggle, LookupStrategy.FirstMatch)]
        [TestCase(EntryKind.Group, LookupStrategy.FirstMatch)]
        [TestCase(EntryKind.Deploy, LookupStrategy.FirstMatch)]
        [TestCase(EntryKind.Heat, LookupStrategy.AllMatches)]
        [TestCase(EntryKind.Throttle, LookupStrategy.AllMatches)]
        [TestCase(EntryKind.Gear, LookupStrategy.LastByObjectName)]
        public void Strategy_PerKind(EntryKind kind, LookupStrategy expected) =>
            Assert.AreEqual(expected, AnimationLookup.For(kind));

        [Test] public void ReduxBound_PrefersNamedHolder() =>
            Assert.AreEqual(2, AnimationLookup.SelectReduxBound(new[] { C("a", true, true), C("b", true), C("leg", true, true) }, "leg"));

        [Test] public void ReduxBound_FirstBoundWithoutName() =>
            Assert.AreEqual(1, AnimationLookup.SelectReduxBound(new[] { C("a", true), C("b", true, true), C("c", true, true) }, null));

        [Test] public void ReduxBound_NameMissing_FallsBackToFirstBound() =>
            Assert.AreEqual(0, AnimationLookup.SelectReduxBound(new[] { C("a", true, true) }, "zzz"));

        [Test] public void ReduxBound_OnlyLegacyHolders_None() =>
            Assert.AreEqual(-1, AnimationLookup.SelectReduxBound(new[] { C("a", true), C("b", false, true) }, null));

        [Test] public void ReduxBound_Null_None() => Assert.AreEqual(-1, AnimationLookup.SelectReduxBound(null, null));

        [Test]
        public void FirstMatch_TakesTheFirstHolder()
        {
            var r = AnimationLookup.Select(new[] { C("a", false), C("b", true), C("c", true) }, LookupStrategy.FirstMatch, null);
            CollectionAssert.AreEqual(new[] { 1 }, r.Picked);
            Assert.AreEqual(2, r.Holding);
        }

        [Test]
        public void AllMatches_TakesEveryHolderInOrder()
        {
            var r = AnimationLookup.Select(new[] { C("a", true), C("b", false), C("c", true) }, LookupStrategy.AllMatches, null);
            CollectionAssert.AreEqual(new[] { 0, 2 }, r.Picked);
        }

        [Test]
        public void LastByObjectName_TakesTheLastHolderWithThatName()
        {
            var r = AnimationLookup.Select(new[] { C("leg", true), C("other", true), C("leg", true), C("leg", false) },
                LookupStrategy.LastByObjectName, "leg");
            CollectionAssert.AreEqual(new[] { 2 }, r.Picked);
            Assert.IsFalse(r.ObjectNameFallback);
        }

        [Test]
        public void LastByObjectName_NoName_FallsBackToFirstMatch()
        {
            var r = AnimationLookup.Select(new[] { C("a", false), C("b", true), C("c", true) }, LookupStrategy.LastByObjectName, " ");
            CollectionAssert.AreEqual(new[] { 1 }, r.Picked);
            Assert.IsTrue(r.ObjectNameFallback);
        }

        [Test]
        public void LastByObjectName_NameHoldsNoClip_PicksNothing()
        {
            var r = AnimationLookup.Select(new[] { C("leg", false), C("b", true) }, LookupStrategy.LastByObjectName, "leg");
            CollectionAssert.IsEmpty(r.Picked);
            StringAssert.Contains("no GameObject 'leg' holds clip 'deploy'", AnimationLookup.Note(r, "deploy", "leg"));
        }

        [Test]
        public void ReduxBound_IsSkippedAndCounted()
        {
            var r = AnimationLookup.Select(new[] { C("a", true, reduxBound: true), C("b", true) }, LookupStrategy.FirstMatch, null);
            CollectionAssert.AreEqual(new[] { 1 }, r.Picked);
            Assert.AreEqual(1, r.SkippedReduxBound);
            StringAssert.Contains("skipped 1 Redux-bound Animation", AnimationLookup.Note(r, "x", null));
        }

        [Test]
        public void OnlyReduxBound_PicksNothingAndSaysWhy()
        {
            var r = AnimationLookup.Select(new[] { C("a", true, reduxBound: true) }, LookupStrategy.AllMatches, null);
            CollectionAssert.IsEmpty(r.Picked);
            StringAssert.Contains("only on 1 Redux-bound Animation", AnimationLookup.Note(r, "x", null));
        }

        [Test]
        public void NoHolder_NoteMentionsPhase2()
        {
            var r = AnimationLookup.Select(new[] { C("a", false) }, LookupStrategy.FirstMatch, null);
            CollectionAssert.IsEmpty(r.Picked);
            Assert.AreEqual("no Animation holds clip 'glow' (phase 2 if material-only)", AnimationLookup.Note(r, "glow", null));
        }

        [Test]
        public void NoCandidates_NoteSaysNoAnimation()
        {
            var r = AnimationLookup.Select(new AnimationCandidate[0], LookupStrategy.FirstMatch, null);
            StringAssert.Contains("no Animation", AnimationLookup.Note(r, "glow", null));
        }

        [Test]
        public void Bound_NoteIsNullWithoutSkipsOrFallback()
        {
            var r = AnimationLookup.Select(new[] { C("a", true) }, LookupStrategy.FirstMatch, null);
            Assert.IsNull(AnimationLookup.Note(r, "x", null));
        }

        [Test]
        public void NullCandidates_PicksNothing()
        {
            var r = AnimationLookup.Select(null, LookupStrategy.FirstMatch, null);
            CollectionAssert.IsEmpty(r.Picked);
        }
    }
}
