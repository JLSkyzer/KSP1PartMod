using System.Collections.Generic;
using KSP1PartMod.Game;
using NUnit.Framework;
using Redux.Ksp1Import.Config;
using UnityEngine;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c): the shroud import targets resolve by name, and the prefab
    /// hide follows Redux's root rule.</summary>
    public class ShroudImportTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown] public void TearDown()
        {
            foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        [Test] public void Resolve_FindsEveryTarget()
        {
            var t = ShroudImport.Resolve();
            Assert.AreEqual("", string.Join(", ", t.MissingImport));
            Assert.IsNotNull(t.AddFairingModule);
            Assert.IsNotNull(t.GetShroudNames);
            Assert.IsNotNull(t.BuildConfiguredPrefab);
            Assert.IsNotNull(t.ActivateAllModules);
            Assert.IsFalse(t.BuildConfiguredPrefab.IsPublic, "private in 26w42c");
            var ps = t.BuildConfiguredPrefab.GetParameters();
            Assert.AreEqual(7, ps.Length);
            Assert.AreEqual(typeof(Ksp1ConfigNode), ps[1].ParameterType);
            Assert.AreEqual("partName", ps[4].Name, "the postfix reads __4 as the part name");
            Assert.AreEqual(typeof(GameObject), t.BuildConfiguredPrefab.ReturnType);
            Assert.AreEqual(typeof(Ksp1ConfigNode), t.AddFairingModule.GetParameters()[0].ParameterType, "the prefix reads __0");
        }

        [Test] public void GetShroudNames_ReturnsTheConvertibleNamesOnly()
        {
            var t = ShroudImport.Resolve();
            Assert.IsNotNull(t.GetShroudNames);
            var root = Ksp1ConfigNode.Parse("PART\n{\nname = p\nMODULE\n{\nname = ModuleJettison\njettisonName = A, B\n}\n" +
                                            "MODULE\n{\nname = ModuleJettison\njettisonName = C\nignoreNodes = true\n}\n}\n");
            var part = System.Linq.Enumerable.FirstOrDefault(root.GetNodes("PART"));
            Assert.IsNotNull(part);
            Assert.AreEqual("p", part.GetValue("name"));
            var names = (IEnumerable<string>)t.GetShroudNames.Invoke(null, new object[] { part });
            CollectionAssert.AreEqual(new[] { "A", "B" }, names);
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _made.Add(go);
            return go;
        }

        [Test] public void HideRoots_HidesTheOutermostMatchesIgnoringCase_NeverTheRoot()
        {
            var root = Make("Shroud");                         // the prefab root: never hidden, even when it matches
            var model = Make("model", root.transform);
            var outer = Make("shroud", model.transform);        // case differs from the cfg: still matched
            var inner = Make("Shroud", outer.transform);        // under a root: left alone
            var twinA = Make("Fairing", model.transform);
            var twinB = Make("Fairing", model.transform);       // same path as twinA: both are roots
            var bare = Make("Bare", model.transform);

            var hidden = ShroudImport.HideRoots(root, new[] { "Shroud", "Fairing", "Missing" }, out var missing);

            Assert.AreEqual(3, hidden);
            Assert.IsTrue(root.activeSelf);
            Assert.IsFalse(outer.activeSelf);
            Assert.IsTrue(inner.activeSelf, "only roots are written");
            Assert.IsFalse(twinA.activeSelf);
            Assert.IsFalse(twinB.activeSelf);
            Assert.IsTrue(bare.activeSelf);
            CollectionAssert.AreEqual(new[] { "Missing" }, missing);
        }

        [Test] public void HideRoots_NoMatch_ReportsEveryName()
        {
            var root = Make("p");
            Make("model", root.transform);
            Assert.AreEqual(0, ShroudImport.HideRoots(root, new[] { "A", "B" }, out var missing));
            CollectionAssert.AreEqual(new[] { "A", "B" }, missing);
        }

        [Test] public void Postfix_HidesConvertibleShroudsOnly()
        {
            var cfg = Ksp1ConfigNode.Parse("PART\n{\nname = shroudTestPart\nMODULE\n{\nname = ModuleJettison\njettisonName = Shroud2x3,Shroud1p5x3\n}\n" +
                                           "MODULE\n{\nname = ModuleJettison\njettisonName = Cover\nignoreNodes = True\n}\n}\n");
            var part = System.Linq.Enumerable.First(cfg.GetNodes("PART"));
            var root = Make("shroudTestPart");
            var model = Make("model", root.transform);
            var big = Make("Shroud2x3", model.transform);
            var small = Make("Shroud1p5x3", model.transform);
            var cover = Make("Cover", model.transform);
            var postfix = typeof(ShroudImport).GetMethod("AfterBuildConfiguredPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(postfix);
            postfix.Invoke(null, new object[] { part, "shroudTestPart", root });
            Assert.IsFalse(big.activeSelf);
            Assert.IsFalse(small.activeSelf);
            Assert.IsTrue(cover.activeSelf, "ignoreNodes shrouds stay visible, as Redux leaves them");
        }

        [Test] public void Tally_CountsOncePerPart_AndRebuilds()
        {
            var tally = new ShroudImport.Tally();
            Assert.IsTrue(tally.Keep("a"));
            Assert.IsFalse(tally.Keep("a"));
            Assert.IsTrue(tally.Keep("b"));
            Assert.IsTrue(tally.Built("a", false));
            Assert.IsTrue(tally.Built("b", true));
            Assert.IsFalse(tally.Built("a", false), "a rebuild is silent");
            Assert.IsFalse(tally.Built("b", true));
            Assert.AreEqual(2, tally.Kept);
            Assert.AreEqual(1, tally.WithMissing);
            Assert.AreEqual(2, tally.Rebuilds);
        }
    }
}
