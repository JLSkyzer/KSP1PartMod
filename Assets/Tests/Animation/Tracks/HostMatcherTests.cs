using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class HostMatcherTests
    {
        [Test] public void HostMatcher_LongestSuffixWins() => CollectionAssert.AreEqual(new[] { 1 }, HostMatcher.Match(new[] { "model/a/glow", "model/engine/a/glow" }, "engine/a/glow"));
        [Test] public void HostMatcher_TwoModelCopies_ReturnsBoth() => Assert.AreEqual(2, HostMatcher.Match(new[] { "model/x/glow", "model/x(1)/glow" }, "glow").Count);
        [Test] public void HostMatcher_TwoModelCopies_IndicesInOrder() => CollectionAssert.AreEqual(new[] { 0, 2 }, HostMatcher.Match(new[] { "model/x/glow", "model/x/other", "model/x(1)/glow" }, "root/glow"));
        [Test] public void HostMatcher_NoCommonSegment_Empty() => CollectionAssert.IsEmpty(HostMatcher.Match(new[] { "model/a", "model/b" }, "root/glow"));
        [Test] public void HostMatcher_WholeSegmentsOnly() => CollectionAssert.IsEmpty(HostMatcher.Match(new[] { "model/myglow" }, "glow"));
        [Test] public void HostMatcher_CaseSensitive() => CollectionAssert.IsEmpty(HostMatcher.Match(new[] { "model/Glow" }, "glow"));
        [Test] public void HostMatcher_FullMuPathUnderModelRoot() => CollectionAssert.AreEqual(new[] { 0 }, HostMatcher.Match(new[] { "model/engine/a/glow", "model/engine/b/glow" }, "engine/a/glow"));
        [Test] public void HostMatcher_EmptyOrNull_Empty()
        {
            CollectionAssert.IsEmpty(HostMatcher.Match(new[] { "model/a" }, ""));
            CollectionAssert.IsEmpty(HostMatcher.Match(new[] { "model/a" }, null));
            CollectionAssert.IsEmpty(HostMatcher.Match(null, "a"));
            CollectionAssert.IsEmpty(HostMatcher.Match(new string[] { null, "" }, "a"));
        }
    }
}
