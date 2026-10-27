using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.VFX.Reentry;
using KSP1PartMod.Animation.Runtime;
using Redux.Assets.PartIconRendering;
using Redux.Ksp1Import.Assets;
using Redux.Rendering;
using Redux.VFX.ReentryMeshGeneration;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>Two visual fixes for every imported KSP1 part, whether or not it carries our module.
    /// <para><b>Material-less renderers</b> (<c>hide_material_less_renderers</c>): a Harmony postfix on Redux 26w42c's
    /// <c>PatchManager.Parts.Patchers.PartModuleLoadPatcher.ApplyOnGameObject</c>, which every part GameObject goes
    /// through: the OAB template (<c>ObjectAssemblyPartTracker.OnPartPrefabLoaded</c>), each flight view
    /// (<c>SimulationObjectView</c>) and the main-menu vessels. It runs after Redux generated the reentry hull there
    /// (<c>RuntimeReentryMeshFallback.EnsureGenerated</c>). See <see cref="RendererCleanup"/>.</para>
    /// <para><b>Icons</b> (<c>fix_part_icons</c>): a Harmony prefix on <c>PartIconRenderer.RenderTexture2D</c> that wraps
    /// the subject hook of KSP1 parts so their renderers share the rendering layers of the studio lights (and turns on
    /// <c>hotswapPartShaders</c>, a no-op so far). See <see cref="IconFix"/>.</para>
    /// Both never throw; after one failure each stays off for the session with one log line.</summary>
    internal static class RenderFixes
    {
        public const string HarmonyId = "KSP1PartMod.RenderFixes";
        private const string LoadPatcherType = "PatchManager.Parts.Patchers.PartModuleLoadPatcher";
        private const int HullLogLimit = 5;

        private static readonly HashSet<string> LoggedParts = new HashSet<string>(StringComparer.Ordinal);
        private static bool _rendererBroken, _iconBroken;
        private static int _hullLogs;
        private static int _icons;

        public static void Apply(Harmony harmony, bool hideRenderers, bool fixIcons)
        {
            var log = KSP1PartModPlugin.Log;
            if (hideRenderers)
            {
                try
                {
                    var type = AccessTools.TypeByName(LoadPatcherType);
                    var original = type == null ? null : AccessTools.Method(type, "ApplyOnGameObject");
                    if (original == null) log.LogWarning($"[KSP1PartMod] render: {LoadPatcherType}.ApplyOnGameObject not found, renderers left as they are");
                    else
                    {
                        harmony.Patch(original, postfix: new HarmonyMethod(typeof(RenderFixes), nameof(AfterApplyOnGameObject)));
                        log.LogInfo("[KSP1PartMod] render: material-less renderer cleanup armed on PartModuleLoadPatcher.ApplyOnGameObject");
                    }
                }
                catch (Exception e)
                {
                    log.LogError("[KSP1PartMod] render: could not arm the renderer cleanup: " + e);
                }
            }
            else log.LogInfo("[KSP1PartMod] render: renderer cleanup disabled by " + KSP1PartModPlugin.AnimationSettingsFile);

            if (fixIcons)
            {
                try
                {
                    var original = AccessTools.Method(typeof(PartIconRenderer), nameof(PartIconRenderer.RenderTexture2D));
                    if (original == null) log.LogWarning("[KSP1PartMod] icons: PartIconRenderer.RenderTexture2D not found, icons left as they are");
                    else
                    {
                        harmony.Patch(original, prefix: new HarmonyMethod(typeof(RenderFixes), nameof(BeforeRenderIcon)),
                            postfix: new HarmonyMethod(typeof(RenderFixes), nameof(AfterRenderIcon)));
                        log.LogInfo("[KSP1PartMod] icons: KSP1 icon fix armed on PartIconRenderer.RenderTexture2D " +
                                    $"(subject rendering layers set to the studio lights' 0x{StudioMask(new PartIconRenderSettings().studioLayer):X})");
                    }
                }
                catch (Exception e)
                {
                    log.LogError("[KSP1PartMod] icons: could not arm the icon fix: " + e);
                }
            }
            else log.LogInfo("[KSP1PartMod] icons: icon fix disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
        }

        // ---------------------------------------------------------------- renderers

        private static void AfterApplyOnGameObject(ref GameObject gameObject, PartData partData)
        {
            if (_rendererBroken) return;
            try
            {
                var name = partData?.partName;
                if (gameObject == null || name == null || !Ksp1ImportedPartRegistry.IsImportedPart(name)) return;
                Clean(gameObject, name);
            }
            catch (Exception e)
            {
                _rendererBroken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] render: renderer cleanup failed, stopped for this session: " + e);
            }
        }

        private static void Clean(GameObject root, string part)
        {
            var hidden = new List<string>();
            var hulls = new List<string>();
            int hullRenderers = 0, hullActive = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                var materials = r.sharedMaterials;
                var nulls = materials.Count(m => m == null);
                var hull = r.GetComponentInParent<ReentryMesh>(true);
                var inHull = hull != null || r.GetComponentInParent<GeneratedReentryMeshRoot>(true) != null;
                var info = new PartRendererInfo(PathOf(r.transform, root.transform), materials.Length, nulls, inHull,
                    r.gameObject.activeInHierarchy, r.enabled);
                if (inHull && info.AllMaterialsNull)
                {
                    hullRenderers++;
                    if (info.ActiveInHierarchy) hullActive++;
                }
                switch (RendererCleanup.Decide(info))
                {
                    case CleanupAction.DisableRenderer:
                        r.enabled = false;
                        hidden.Add(info.Path);
                        break;
                    case CleanupAction.DeactivateHull:
                        var hullObject = hull != null ? hull.gameObject : r.GetComponentInParent<GeneratedReentryMeshRoot>(true)?.gameObject;
                        if (hullObject != null && hullObject.activeSelf)
                        {
                            hullObject.SetActive(false);
                            hulls.Add(PathOf(hullObject.transform, root.transform));
                        }
                        break;
                }
            }
            var log = KSP1PartModPlugin.Log;
            if (!LoggedParts.Add(part)) return;
            var line = RendererCleanup.Line(part, hidden, hulls);
            if (line != null) log?.LogInfo(line);
            if (hullRenderers > 0 && _hullLogs < HullLogLimit)
            {
                _hullLogs++;
                log?.LogInfo(RendererCleanup.HullLine(part, hullRenderers, hullActive));
            }
        }

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && t != root && p != root.parent; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- icons

        /// <summary>The rendering layers Redux gives the studio lights: their <c>cullingMask</c> is
        /// <c>1 &lt;&lt; studioLayer</c>, mapped by <c>LegacyLightCullingMask.ForLight</c> (via <c>ForCullingMask</c>) when
        /// <c>CameraRenderHooksFeature</c> syncs the visible lights of <c>camera.Render</c>.</summary>
        private static uint StudioMask(int studioLayer) => LegacyLightCullingMask.ForCullingMask(1 << studioLayer);

        /// <summary>Set by the prefix for a call whose studio lights are logged by the postfix (one KSP1, one KSP2).</summary>
        private static bool _lightsPending;
        private static bool _ksp1Diagnosed, _ksp2Diagnosed;

        private static void BeforeRenderIcon(string partName, PartCore core, GameObject prefab, PartIconRenderSettings settings,
            ref Action<GameObject, PartCore> configureSubject)
        {
            _lightsPending = false;
            if (_iconBroken || settings == null) return; // null settings: Redux makes defaults (KSP2 profile)
            try
            {
                var ksp1 = partName != null && Ksp1ImportedPartRegistry.IsImportedPart(partName);
                var diagnose = KSP1PartModPlugin.Animations.Diagnostics && (ksp1 ? !_ksp1Diagnosed : !_ksp2Diagnosed);
                if (diagnose)
                {
                    if (ksp1) _ksp1Diagnosed = true;
                    else _ksp2Diagnosed = true;
                    _lightsPending = true;
                }
                if (!ksp1)
                {
                    if (diagnose) configureSubject = Wrap(configureSubject, partName, settings.studioLayer, false, true);
                    return;
                }
                // The shader swap is kept: harmless, and it matters if Redux finds KSP2/Parts/Paintable at import.
                if (IconFix.ShouldSwap(true, true, settings.hotswapPartShaders)) settings.hotswapPartShaders = true;
                configureSubject = Wrap(configureSubject, partName, settings.studioLayer, true, diagnose);
                _icons++;
                if (!IconFix.ShouldLog(_icons)) return;
                var shaders = prefab == null
                    ? Enumerable.Empty<string>()
                    : prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                        .Select(m => m == null ? null : m.shader == null ? "no shader" : m.shader.name);
                KSP1PartModPlugin.Log?.LogInfo(IconFix.Line(_icons, partName, IconFix.ShaderCensus(shaders), StudioMask(settings.studioLayer)));
            }
            catch (Exception e)
            {
                _iconBroken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] icons: icon fix failed, stopped for this session: " + e);
            }
        }

        /// <summary>Runs Redux's own subject hook first (KSP1: B9 default visual state), then, for a KSP1 subject,
        /// gives every renderer the rendering layers of the studio lights.</summary>
        private static Action<GameObject, PartCore> Wrap(Action<GameObject, PartCore> inner, string part, int studioLayer,
            bool fix, bool diagnose) => (subject, core) =>
        {
            inner?.Invoke(subject, core);
            if (_iconBroken || subject == null) return;
            try
            {
                var lightMask = StudioMask(studioLayer);
                var lines = diagnose ? new List<string>() : null;
                var changed = 0;
                foreach (var r in subject.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var before = r.renderingLayerMask;
                    if (fix && IconFix.NeedsLayerFix(before, lightMask))
                    {
                        r.renderingLayerMask = lightMask;
                        changed++;
                    }
                    if (lines != null && lines.Count < IconFix.DiagRendererLimit && !(r is ParticleSystemRenderer))
                        lines.Add(IconDiagnostics.Renderer(r, subject.transform, before));
                }
                if (lines == null) return;
                var log = KSP1PartModPlugin.Log;
                log?.LogInfo($"[KSP1PartMod] icons: diag {(fix ? "KSP1" : "KSP2")} subject '{part}': studio layer {studioLayer}, " +
                             $"lights' rendering layers 0x{lightMask:X} (Redux LegacyLightCullingMask), {changed} renderer(s) remapped");
                foreach (var l in lines) log?.LogInfo("[KSP1PartMod] icons: diag   " + l);
            }
            catch (Exception e)
            {
                _iconBroken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] icons: icon fix failed, stopped for this session: " + e);
            }
        };

        /// <summary>After the render: the studio's lights, synced by Redux during <c>camera.Render</c>, still exist
        /// (destroyed at the end of the frame unless <c>destroyTemporaryObjectsImmediately</c>).</summary>
        private static void AfterRenderIcon(string partName, Texture2D __result)
        {
            if (!_lightsPending) return;
            _lightsPending = false;
            try
            {
                KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] icons: diag lights for '{partName}': " +
                                               IconDiagnostics.StudioLights() + $"; icon {(__result != null ? "rendered" : "null")}");
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] icons: light diagnostic failed: " + e);
            }
        }
    }
}
