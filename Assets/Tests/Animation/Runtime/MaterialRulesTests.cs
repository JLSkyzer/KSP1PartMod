using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class MaterialRulesTests
    {
        [Test] public void Renderer_NoLists_Accepted() => Assert.IsTrue(MaterialRules.Accepts("window", null, null));
        [Test] public void Renderer_EmptyLists_Accepted() => Assert.IsTrue(MaterialRules.Accepts("window", new string[0], new string[0]));
        [Test] public void Renderer_Included_OnlyThose() =>
            Assert.IsFalse(MaterialRules.Accepts("hull", new[] { "window" }, null));
        [Test] public void Renderer_InIncluded_Accepted() =>
            Assert.IsTrue(MaterialRules.Accepts("window", new[] { "window", "light" }, null));
        [Test] public void Renderer_ExclusionWins() =>
            Assert.IsFalse(MaterialRules.Accepts("window", new[] { "window" }, new[] { "window" }));
        [Test] public void Renderer_Excluded_Rejected() =>
            Assert.IsFalse(MaterialRules.Accepts("hull", null, new[] { "hull" }));
        [Test] public void Renderer_NamesAreTrimmedAndCaseSensitive()
        {
            Assert.IsTrue(MaterialRules.Accepts("window", new[] { " window " }, null));
            Assert.IsFalse(MaterialRules.Accepts("Window", new[] { "window" }, null));
        }

        [Test]
        public void Candidates_FromTheMap()
        {
            var map = new Dictionary<string, string[]> { ["_EmissiveColor"] = new[] { "_EmissiveColor", "_EmissionColor" } };
            CollectionAssert.AreEqual(new[] { "_EmissiveColor", "_EmissionColor" }, MaterialRules.Candidates("_EmissiveColor", map));
        }

        [Test]
        public void Candidates_NotInMap_ThePropertyItself() =>
            CollectionAssert.AreEqual(new[] { "_TintColor" },
                MaterialRules.Candidates("_TintColor", new Dictionary<string, string[]>()));

        [Test]
        public void Candidates_NullMap_ThePropertyItself() =>
            CollectionAssert.AreEqual(new[] { "_BurnColor" }, MaterialRules.Candidates("_BurnColor", null));

        [Test]
        public void Pick_FirstTheMaterialHas()
        {
            var has = new HashSet<string> { "_EmissionColor", "_Color" };
            Assert.AreEqual("_EmissionColor", MaterialRules.Pick(new[] { "_EmissiveColor", "_EmissionColor", "_Color" }, has.Contains));
        }

        [Test]
        public void Pick_NoneThere_Null() =>
            Assert.IsNull(MaterialRules.Pick(new[] { "_EmissiveColor" }, _ => false));

        [Test]
        public void Pick_NullHas_Null() =>
            Assert.IsNull(MaterialRules.Pick(new[] { "_EmissiveColor" }, (Func<string, bool>)null));
    }
}
