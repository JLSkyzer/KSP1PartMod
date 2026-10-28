using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.VFX;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>0.13.1 diagnostic of the missing KSP1 engine plumes (<c>plume_diagnostics</c>): a postfix on
    /// <c>Module_Engine.OnInitialize</c> (after Redux's <c>ThrottleVFXManager.Init</c>) logs, for the first KSP1 engine of
    /// each part type in flight, its manager and its <c>[KSP1 Plume]</c> objects; one instance of that type then waits for
    /// its engine to run and logs again 3 s later. Changes nothing. Two lines per part type for the session; no
    /// per-frame allocation (one waiting coroutine per part type, polling two fields).</summary>
    internal static class PlumeDiagnostics
    {
        public const string HarmonyId = "KSP1PartMod.PlumeDiagnostics";

        private static readonly PlumeLines.Budget Budget = new PlumeLines.Budget();
        private static FieldInfo _initialized, _fxVisible;
        private static bool _broken;
        private static WaitForSeconds _delay;

        public static void TryApply(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.PlumeDiagnostics)
            {
                KSP1PartModPlugin.Log?.LogInfo(PlumeLines.Armed(false));
                return;
            }
            var init = AccessTools.DeclaredMethod(typeof(Module_Engine), "OnInitialize", Type.EmptyTypes);
            if (init == null)
            {
                KSP1PartModPlugin.Log?.LogWarning(PlumeLines.Prefix + "Module_Engine.OnInitialize not found, no diagnostics");
                return;
            }
            _initialized = AccessTools.Field(typeof(ThrottleVFXManager), "_TVMInitialized");
            _fxVisible = AccessTools.Field(typeof(ThrottleVFXManager), "_engineFXVisible");
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(PlumeDiagnostics), nameof(AfterEngineInit)));
            KSP1PartModPlugin.Log?.LogInfo(PlumeLines.Armed(true));
        }

        internal static void AfterEngineInit(Module_Engine __instance)
        {
            if (_broken || __instance == null) return;
            try
            {
                if (__instance.PartBackingMode != PartBehaviourModule.PartBackingModes.Flight) return;
                var type = PlumeLines.TypeOf(IsruRuntime.PartNameOf(__instance));
                if (string.IsNullOrEmpty(type) || !Ksp1ImportedPartRegistry.IsImportedPart(type)) return;
                if (Budget.TakeBind(type)) KSP1PartModPlugin.Log?.LogInfo(PlumeLines.Line(Snapshot(__instance, type, "at bind")));
                if (Budget.TakeWatch(type)) TechTreeLoadStep.Runner().StartCoroutine(WatchFirstRun(__instance, type));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static IEnumerator WatchFirstRun(Module_Engine engine, string type)
        {
            while (true)
            {
                if (_broken) yield break;
                if (engine == null)
                {
                    Budget.ReleaseWatch(type);
                    yield break;
                }
                var data = engine.DataEngine;
                if (data != null && (data.EngineIgnited || IsRunning(data.State))) break;
                yield return null;
            }
            yield return _delay ??= new WaitForSeconds(3f);
            if (_broken) yield break;
            if (engine == null)
            {
                Budget.ReleaseWatch(type);
                yield break;
            }
            try
            {
                var s = Snapshot(engine, type, "3 s after the first ignition");
                KSP1PartModPlugin.Log?.LogInfo(PlumeLines.Line(s));
                if (s.HasManager && s.Initialized == false) KSP1PartModPlugin.Log?.LogWarning(PlumeLines.NotInitialized(type));
                Budget.Done(type);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static bool IsRunning(EngineState s) =>
            s == EngineState.Running || s == EngineState.RunningIncreasing || s == EngineState.RunningDecreasing || s == EngineState.Stopping;

        private static PlumeLines.Snapshot Snapshot(Module_Engine engine, string type, string moment)
        {
            var s = new PlumeLines.Snapshot { Part = type, Moment = moment };
            var data = engine.DataEngine;
            s.EngineState = data == null ? "no data" : data.State + (data.EngineIgnited ? " (ignited)" : "");
            var root = engine.gameObject;
            var manager = root.GetComponent<ThrottleVFXManager>();
            if (manager != null)
            {
                s.HasManager = true;
                var events = manager.FXModeActionEvents;
                s.Events = events?.Length ?? 0;
                if (events != null)
                    foreach (var mode in events)
                        if (mode?.ActionEvents != null)
                            foreach (var a in mode.ActionEvents)
                                s.Effects += a?.EngineEffects?.Length ?? 0;
                if (_initialized != null && _initialized.FieldType == typeof(bool)) s.Initialized = (bool)_initialized.GetValue(manager);
                if (_fxVisible != null && _fxVisible.FieldType == typeof(bool)) s.FxVisible = (bool)_fxVisible.GetValue(manager);
                s.VfxActive = manager.IsVFXActive;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith(PlumeLines.PlumeObjectPrefix, StringComparison.Ordinal)) continue;
                s.PlumeCount++;
                if (s.Plumes.Count >= PlumeLines.MaxPlumes) continue;
                var scale = t.lossyScale;
                var p = new PlumeLines.Plume
                {
                    Name = t.name, ActiveInHierarchy = t.gameObject.activeInHierarchy, ScaleX = scale.x, ScaleY = scale.y, ScaleZ = scale.z,
                };
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    p.RendererCount++;
                    if (p.Renderers.Count >= PlumeLines.MaxRenderers) continue;
                    var material = r.sharedMaterial;
                    p.Renderers.Add(new PlumeLines.Renderer
                    {
                        Kind = r.GetType().Name,
                        Enabled = r.enabled,
                        HasMesh = HasMesh(r),
                        Material = material != null ? material.name : null,
                        Shader = material != null && material.shader != null ? material.shader.name : null,
                    });
                }
                s.Plumes.Add(p);
            }
            return s;
        }

        private static bool HasMesh(Renderer r)
        {
            switch (r)
            {
                case SkinnedMeshRenderer skinned: return skinned.sharedMesh != null;
                case MeshRenderer _:
                    var filter = r.GetComponent<MeshFilter>();
                    return filter != null && filter.sharedMesh != null;
                default: return true; // particles, lines: no mesh to have
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError(PlumeLines.Prefix + "diagnostics stopped for this session: " + e);
        }
    }
}
