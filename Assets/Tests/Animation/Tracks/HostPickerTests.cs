using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    /// <summary>Which instance objects carry a TrackSet's Animation host. Paths are the instance's transforms under the
    /// part root, in hierarchy (pre-)order, as the glue collects them; two sibling copies of one model have the same
    /// path strings, so only the order tells them apart.</summary>
    public class HostPickerTests
    {
        /// <summary>Two copies of a model whose root object is "model", each with model/a/host.</summary>
        private static readonly string[] TwoCopies = { "model", "model/a", "model/a/host", "model", "model/a", "model/a/host" };

        [Test]
        public void ModelOrdinal_CountsOnlyEarlierModelsOfTheSameRootName() =>
            Assert.AreEqual((0, 1, 0, -1), (
                HostPicker.ModelOrdinal(new[] { "model", "model", "other" }, 0),
                HostPicker.ModelOrdinal(new[] { "model", "model", "other" }, 1),
                HostPicker.ModelOrdinal(new[] { "model", "model", "other" }, 2),
                HostPicker.ModelOrdinal(new[] { "model", null }, 1)));

        [Test]
        public void ModelOrdinal_UnknownRootsOrIndex_MinusOne() =>
            Assert.AreEqual((-1, -1, -1), (
                HostPicker.ModelOrdinal(null, 0),
                HostPicker.ModelOrdinal(new[] { "model" }, 1),
                HostPicker.ModelOrdinal(new[] { "model" }, -1)));

        /// <summary>Redux adds no child for a reference it cannot load (<c>TryLoadModel</c> returns false): the third
        /// reference is the second "model" object on the instance, not the third child.</summary>
        [Test]
        public void Pick_MissingModelBeforeFound_MapsByResolvedModelsOnly()
        {
            var roots = new[] { null, "model", "model" };
            CollectionAssert.AreEqual(new[] { 2 }, HostPicker.Pick(TwoCopies, "model/a/host", roots, 1, out _));
            CollectionAssert.AreEqual(new[] { 5 }, HostPicker.Pick(TwoCopies, "model/a/host", roots, 2, out _));
        }

        [Test]
        public void Pick_OtherModelBetween_Skipped()
        {
            var paths = new[] { "model", "model/a", "model/a/host", "prop", "prop/x", "model", "model/a", "model/a/host" };
            CollectionAssert.AreEqual(new[] { 7 }, HostPicker.Pick(paths, "model/a/host", new[] { "model", "prop", "model" }, 2, out _));
        }

        /// <summary>A first model of the same root name that has no such host still counts.</summary>
        [Test]
        public void Pick_EarlierSameNameModelWithoutHost_StillCounts()
        {
            var paths = new[] { "model", "model/b", "model", "model/a", "model/a/host" };
            CollectionAssert.AreEqual(new[] { 4 }, HostPicker.Pick(paths, "model/a/host", new[] { "model", "model" }, 1, out _));
            CollectionAssert.IsEmpty(HostPicker.Pick(paths, "model/a/host", new[] { "model", "model" }, 0, out var note));
            StringAssert.Contains("no object 'model/a/host' under model 1 of 2 'model'", note);
        }

        [Test]
        public void Pick_ModelRootUnderAnExtraParent_Matched()
        {
            var paths = new[] { "view", "view/model", "view/model/a", "view/model/a/host" };
            CollectionAssert.AreEqual(new[] { 3 }, HostPicker.Pick(paths, "model/a/host", new[] { "model" }, 0, out _));
        }

        [Test]
        public void Pick_HostIsTheModelRoot()
        {
            var paths = new[] { "model", "model/a", "model" };
            CollectionAssert.AreEqual(new[] { 2 }, HostPicker.Pick(paths, "model", new[] { "model", "model" }, 1, out _));
        }

        [Test]
        public void Pick_OnlyWholePathMatches()
        {
            var paths = new[] { "model", "model/b/host" };
            CollectionAssert.IsEmpty(HostPicker.Pick(paths, "model/a/host", new[] { "model" }, 0, out var note));
            StringAssert.Contains("no object 'model/a/host'", note);
        }

        [Test]
        public void Pick_InstanceDisagreesOnModelCount_NothingAndNote()
        {
            CollectionAssert.IsEmpty(HostPicker.Pick(TwoCopies, "model/a/host", new[] { "model", "model", "model" }, 2, out var note));
            StringAssert.Contains("2 object(s) 'model' for 3 model(s)", note);
        }

        [Test]
        public void Pick_UnknownModelRoots_EveryWholeMatch()
        {
            CollectionAssert.AreEqual(new[] { 2, 5 }, HostPicker.Pick(TwoCopies, "model/a/host", null, 0, out var note));
            StringAssert.Contains("model unknown", note);
        }

        /// <summary>Redux names an object with a blank name "muObject" (<c>Ksp1ModelImporter.BuildObject</c>).</summary>
        [Test]
        public void Pick_BlankMuNames_AreMuObject()
        {
            var paths = new[] { "muObject", "muObject/a", "muObject/a/muObject" };
            CollectionAssert.AreEqual(new[] { 2 }, HostPicker.Pick(paths, "/a/", new[] { "" }, 0, out _));
        }

        [Test]
        public void InstancePath_BlankSegments() =>
            Assert.AreEqual(("muObject/a/muObject", "a/b", ""), (HostPicker.InstancePath("/a/"), HostPicker.InstancePath("a/b"), HostPicker.InstancePath(null)));

        [Test]
        public void Pick_Ordinal_UsesNormalizedRootNames()
        {
            var paths = new[] { "muObject", "muObject/h", "muObject", "muObject/h" };
            CollectionAssert.AreEqual(new[] { 3 }, HostPicker.Pick(paths, "/h", new[] { null, " ", "" }, 2, out _));
        }
    }
}
