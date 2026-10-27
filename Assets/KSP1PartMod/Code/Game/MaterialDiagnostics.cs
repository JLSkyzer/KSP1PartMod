using System;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using KSP.OAB;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>One-shot dump of the materials of a few KSP1 parts in the VAB (<c>diag:</c> lines), to explain red
    /// regions and black icons in a bug report. Hooked as a Harmony finalizer on
    /// <c>ObjectAssemblyPartTracker.OnPartPrefabLoaded</c>, which every VAB part goes through once its prefab is
    /// loaded; it reads <c>ModelPrefab</c>, the part's template after Redux's <c>HotswapPartShaders</c> (IL, 26w42c,
    /// 2026-10-25). Not at load time: Redux releases its KSP1 prefabs once the import is complete and rebuilds them on
    /// demand (<c>Ksp1ImportedPartRegistry.TryGetPrefab</c> → <c>RebuildPrefab</c>), so asking for one would build
    /// and keep it. Icons are rendered from such a rebuilt prefab, not from this template. A finalizer runs even when
    /// the original throws, and rethrows its exception. Never throws itself; after one failure it stays silent.</summary>
    internal static class MaterialDiagnostics
    {
        public const string HarmonyId = "KSP1PartMod.MaterialDiagnostics";
        private const string Prefix = "[KSP1PartMod] diag: ";

        private static readonly string[] ColorProperties = { "_Color", "_PaintA", "_PaintB", "_EmissionColor", "_EmissiveColor" };

        private static DiagSelection _selection;
        private static bool _broken;

        public static void Apply(Harmony harmony)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                var original = AccessTools.Method(typeof(ObjectAssemblyPartTracker), "OnPartPrefabLoaded");
                if (original == null)
                {
                    log.LogWarning(Prefix + "ObjectAssemblyPartTracker.OnPartPrefabLoaded not found, no material dump");
                    return;
                }
                _selection = new DiagSelection();
                harmony.Patch(original, finalizer: new HarmonyMethod(typeof(MaterialDiagnostics), nameof(Finalizer)));
                log.LogInfo(Prefix + $"material dump armed for {string.Join(", ", DiagSelection.DefaultNames)} and " +
                            $"{DiagSelection.DefaultOthers} other KSP1 part(s)");
            }
            catch (Exception e)
            {
                log.LogError(Prefix + "could not arm the material dump: " + e);
            }
        }

        private static Exception Finalizer(object[] __args, Exception __exception)
        {
            if (_broken || _selection == null || _selection.Done) return __exception;
            try
            {
                var part = __args?.OfType<IObjectAssemblyAvailablePart>().FirstOrDefault();
                var name = part?.PartData?.partName ?? part?.Name;
                if (name == null || !Ksp1ImportedPartRegistry.IsImportedPart(name)) return __exception;
                if (!_selection.Take(name)) return __exception;
                var root = part.ModelPrefab != null ? part.ModelPrefab : __args.OfType<GameObject>().FirstOrDefault();
                Dump(name, root, __exception);
            }
            catch (Exception e)
            {
                _broken = true;
                KSP1PartModPlugin.Log?.LogError(Prefix + "material dump failed, stopped for this session: " + e);
            }
            return __exception;
        }

        private static void Dump(string part, GameObject root, Exception thrown)
        {
            var log = KSP1PartModPlugin.Log;
            if (root == null)
            {
                log.LogInfo(Prefix + part + " has no prefab");
                return;
            }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            log.LogInfo(Prefix + $"{part}: {renderers.Length} renderer(s)" +
                        (thrown != null ? $", prefab load threw {thrown.GetType().Name}" : ""));
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var path = PathOf(renderer.transform, root.transform) +
                           $"' layer {renderer.gameObject.layer} renderingLayerMask 0x{renderer.renderingLayerMask:X}";
                if (materials.Length == 0) log.LogInfo(Prefix + $"{part} renderer '{path} no material");
                for (var i = 0; i < materials.Length; i++)
                    log.LogInfo(Prefix + $"{part} renderer '{path}" + (materials.Length > 1 ? $" [{i}]" : "") + " " +
                                Describe(materials[i]));
            }
        }

        private static string Describe(Material m)
        {
            if (m == null) return "material null";
            var sb = new StringBuilder();
            sb.Append("shader '").Append(m.shader != null ? m.shader.name : "null").Append('\'');
            foreach (var p in ColorProperties)
                if (m.HasProperty(p)) sb.Append(' ').Append(p).Append('=').Append(Rgba(m.GetColor(p)));
            var masks = m.GetTexturePropertyNames()
                .Where(t => t.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => $"{t}:{(m.GetTexture(t) != null ? m.GetTexture(t).name : "none")}")
                .ToList();
            sb.Append(" mask=").Append(masks.Count > 0 ? string.Join(",", masks) : "none");
            sb.Append(" keywords=[").Append(string.Join(",", m.shaderKeywords ?? new string[0])).Append(']');
            return sb.ToString();
        }

        private static string Rgba(Color c) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###},{3:0.###})", c.r, c.g, c.b, c.a);

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && t != root && p != root.parent; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
