using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The objects of one shroud entry on a part instance, cached at bind: per name, its outermost transforms
    /// (<see cref="ShroudImport.FindRoots"/>) and the renderers the prefab disabled under them
    /// (<see cref="ShroudImport.OwnedRendererPaths"/>). <see cref="Apply"/> shows one name and hides the others, writing
    /// <c>SetActive</c> and <c>Renderer.enabled</c> only on a change; never a material. No allocation after construction.</summary>
    internal sealed class ShroudObjects
    {
        private static readonly GameObject[] NoObjects = new GameObject[0];
        private static readonly Renderer[] NoRenderers = new Renderer[0];

        private readonly GameObject[][] _roots;
        private readonly Renderer[][] _renderers;
        private int _shown = int.MinValue;

        private ShroudObjects(GameObject[][] roots, Renderer[][] renderers)
        {
            _roots = roots;
            _renderers = renderers;
        }

        /// <summary>The names with at least one transform found.</summary>
        public int Found { get; private set; }

        /// <summary>The renderers this entry switches, over every name.</summary>
        public int RendererCount { get; private set; }

        /// <summary>The name shown since the last <see cref="Apply"/> (-1: hidden; <see cref="int.MinValue"/>: never applied).</summary>
        public int Shown => _shown;

        /// <param name="names">The entry's <c>TransformNames</c>, in order.</param>
        /// <param name="ownedPaths">The paths the prefab disabled; null: none is switched (the prefab postfix did not run).</param>
        public static ShroudObjects Find(Transform partRoot, IReadOnlyList<string> names, IReadOnlyCollection<string> ownedPaths)
        {
            var n = names?.Count ?? 0;
            var roots = new GameObject[n][];
            var renderers = new Renderer[n][];
            var all = ShroudImport.FindRoots(partRoot, ToArray(names), out _);
            var found = 0;
            var rendererCount = 0;
            for (var i = 0; i < n; i++)
            {
                var mine = new List<GameObject>();
                var mineRenderers = new List<Renderer>();
                foreach (var t in all)
                {
                    if (!string.Equals(t.name, names[i], StringComparison.OrdinalIgnoreCase)) continue;
                    mine.Add(t.gameObject);
                    if (ownedPaths == null) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        if (r != null && Contains(ownedPaths, ShroudImport.RendererPath(t, r.transform))) mineRenderers.Add(r);
                }
                roots[i] = mine.Count == 0 ? NoObjects : mine.ToArray();
                renderers[i] = mineRenderers.Count == 0 ? NoRenderers : mineRenderers.ToArray();
                if (mine.Count > 0) found++;
                rendererCount += mineRenderers.Count;
            }
            return new ShroudObjects(roots, renderers) { Found = found, RendererCount = rendererCount };
        }

        /// <summary>Shows name <paramref name="index"/> (-1: none) and hides every other. False when the shown name did not change.</summary>
        /// <param name="reassert">Check <c>activeSelf</c> of the roots even when the index is unchanged, and write only the ones
        /// that differ: something else may have switched a root (Redux's variants on an <c>ignoreNodes</c> shroud while the VAB
        /// hide override is on). A comparison per root, no allocation.</param>
        public bool Apply(int index, bool reassert = false)
        {
            if (index == _shown)
            {
                if (reassert) Write(index);
                return false;
            }
            Write(index);
            _shown = index;
            return true;
        }

        private void Write(int index)
        {
            for (var i = 0; i < _roots.Length; i++)
            {
                var on = i == index;
                var roots = _roots[i];
                for (var k = 0; k < roots.Length; k++)
                    if (roots[k] != null && roots[k].activeSelf != on) roots[k].SetActive(on);
                var renderers = _renderers[i];
                for (var k = 0; k < renderers.Length; k++)
                    if (renderers[k] != null && renderers[k].enabled != on) renderers[k].enabled = on;
            }
        }

        /// <summary>Flight: every collider under the shroud roots is disabled for good (KSP1 destroys or disables them; the
        /// shroud is visual only here). Returns the number disabled.</summary>
        public int DisableColliders()
        {
            var n = 0;
            for (var i = 0; i < _roots.Length; i++)
                foreach (var root in _roots[i])
                {
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (c == null || !c.enabled) continue;
                        c.enabled = false;
                        n++;
                    }
                }
            return n;
        }

        private static bool Contains(IReadOnlyCollection<string> paths, string path)
        {
            if (paths is ICollection<string> c) return c.Contains(path);
            foreach (var p in paths)
                if (string.Equals(p, path, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string[] ToArray(IReadOnlyList<string> names)
        {
            var a = new string[names?.Count ?? 0];
            for (var i = 0; i < a.Length; i++) a[i] = names[i] ?? "";
            return a;
        }
    }
}
