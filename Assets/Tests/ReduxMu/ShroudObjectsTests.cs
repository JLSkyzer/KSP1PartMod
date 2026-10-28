using System.Collections.Generic;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Engine shrouds (0.7.0, Task 3) on real GameObjects: the prefab postfix disables the shroud renderers (Redux's
    /// reentry hull takes every enabled renderer, inactive ones too), and the runtime on a clone shows one name with its
    /// renderers and hides the others.</summary>
    public class ShroudObjectsTests
    {
        private readonly List<Object> _made = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _made.Add(go);
            return go;
        }

        private MeshRenderer Mesh(GameObject go, bool withMaterial = true)
        {
            var r = go.AddComponent<MeshRenderer>();
            if (withMaterial)
            {
                var m = new Material(Shader.Find("Hidden/InternalErrorShader"));
                _made.Add(m);
                r.sharedMaterial = m;
            }
            else r.sharedMaterials = new Material[] { null };
            return r;
        }

        /// <summary>RE-I2-like prefab: two shrouds, each with a mesh child; the small one also a collider and a mesh with
        /// no material; plus an engine mesh outside the shrouds.</summary>
        private GameObject Prefab()
        {
            var root = Make("RE-I2");
            var model = Make("model", root.transform);
            var big = Make("Shroud2x3", model.transform);
            Mesh(Make("mesh", big.transform));
            var small = Make("Shroud1p5x3", model.transform);
            Mesh(small);
            Mesh(Make("broken", small.transform), withMaterial: false);
            Make("col", small.transform).AddComponent<BoxCollider>();
            Mesh(Make("engine", model.transform));
            return root;
        }

        [Test] public void Prefab_DisablesOwnedRenderers_AndRecordsTheirPaths()
        {
            var prefab = Prefab();
            var owned = new HashSet<string>();
            Assert.AreEqual(2, ShroudImport.HideRoots(prefab, new[] { "Shroud2x3", "Shroud1p5x3" }, out var missing, owned));
            Assert.AreEqual(0, missing.Count);
            CollectionAssert.AreEquivalent(new[] { "Shroud2x3/mesh", "Shroud1p5x3" }, owned);
            Assert.IsFalse(prefab.transform.Find("model/Shroud2x3/mesh").GetComponent<Renderer>().enabled);
            Assert.IsFalse(prefab.transform.Find("model/Shroud1p5x3/broken").GetComponent<Renderer>().enabled,
                "no material: off in the prefab so the reentry hull skips it, but not recorded");
            CollectionAssert.DoesNotContain(owned, "Shroud1p5x3/broken");
            Assert.IsTrue(prefab.transform.Find("model/engine").GetComponent<Renderer>().enabled);
        }

        [Test] public void Clone_ShowsOneNameWithItsRenderers()
        {
            var prefab = Prefab();
            var owned = new HashSet<string>();
            ShroudImport.HideRoots(prefab, new[] { "Shroud2x3", "Shroud1p5x3" }, out _, owned);
            var clone = Object.Instantiate(prefab);
            _made.Add(clone);
            var names = new[] { "Shroud1p5x3", "shroud2x3" }; // case differs from the model: still found
            var o = ShroudObjects.Find(clone.transform, names, owned);
            Assert.AreEqual(2, o.Found);
            Assert.AreEqual(2, o.RendererCount);

            var big = clone.transform.Find("model/Shroud2x3").gameObject;
            var bigMesh = big.transform.Find("mesh").GetComponent<Renderer>();
            var small = clone.transform.Find("model/Shroud1p5x3").gameObject;
            var smallMesh = small.GetComponent<Renderer>();
            var broken = small.transform.Find("broken").GetComponent<Renderer>();

            Assert.IsTrue(o.Apply(1));
            Assert.IsTrue(big.activeSelf);
            Assert.IsTrue(bigMesh.enabled);
            Assert.IsFalse(small.activeSelf);
            Assert.IsFalse(smallMesh.enabled);
            Assert.IsFalse(o.Apply(1), "no change, no write");

            Assert.IsTrue(o.Apply(0));
            Assert.IsFalse(big.activeSelf);
            Assert.IsFalse(bigMesh.enabled, "hidden: out of Redux's reentry hull");
            Assert.IsTrue(small.activeSelf);
            Assert.IsTrue(smallMesh.enabled);
            Assert.IsFalse(broken.enabled, "off from the prefab, never ours: never shown");

            Assert.IsTrue(o.Apply(-1));
            Assert.IsFalse(big.activeSelf);
            Assert.IsFalse(small.activeSelf);
            Assert.IsFalse(smallMesh.enabled);
            Assert.IsTrue(clone.transform.Find("model/engine").GetComponent<Renderer>().enabled);
        }

        [Test] public void NoRecord_SwitchesNoRenderer_AndMissingNamesAreCounted()
        {
            var prefab = Prefab();
            var o = ShroudObjects.Find(prefab.transform, new[] { "Shroud2x3", "Nope" }, null);
            Assert.AreEqual(1, o.Found);
            Assert.AreEqual(0, o.RendererCount);
            o.Apply(0);
            Assert.IsTrue(prefab.transform.Find("model/Shroud2x3").gameObject.activeSelf);
        }

        [Test] public void DisableColliders_UnderTheRootsOnly()
        {
            var prefab = Prefab();
            Make("outside", prefab.transform).AddComponent<BoxCollider>();
            var o = ShroudObjects.Find(prefab.transform, new[] { "Shroud2x3", "Shroud1p5x3" }, null);
            Assert.AreEqual(1, o.DisableColliders());
            Assert.IsFalse(prefab.transform.Find("model/Shroud1p5x3/col").GetComponent<Collider>().enabled);
            Assert.IsTrue(prefab.transform.Find("outside").GetComponent<Collider>().enabled);
            Assert.AreEqual(0, o.DisableColliders(), "already off");
        }

        [Test] public void Apply_SameIndex_DoesNotAllocate()
        {
            var prefab = Prefab();
            var o = ShroudObjects.Find(prefab.transform, new[] { "Shroud2x3", "Shroud1p5x3" }, null);
            o.Apply(0);
            Assert.That(() =>
            {
                for (var i = 0; i < 10000; i++) o.Apply(0);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
