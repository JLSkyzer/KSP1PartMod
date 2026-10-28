using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>Lets Redux's <c>KSP.Modules.Module_Fairing</c> (the jettison shroud Redux makes from a KSP1
    /// <c>ModuleJettison</c>) finish initializing on KSP1 parts (<c>fix_fairing_init</c>, see <see cref="FairingRule"/>):
    /// <list type="bullet">
    /// <item>a prefix on <c>OnInitialize</c> gives a null <c>_baseColliders</c> an empty array, which Redux replaces when it
    /// finds the base model; a postfix logs once per part when it did not (the array is still ours);</item>
    /// <item>a prefix on <c>OnMaterialLoaded</c>, for a part without <c>Module_Color</c>, sets the two material fields as
    /// Redux does and skips the rest, which would throw.</item>
    /// </list>
    /// Members are resolved by name; a missing one turns its patch off. Never throws into the game: after one failure the
    /// fix stays off for the session, with one log line. Native KSP2 parts are never touched.</summary>
    internal static class FairingFixes
    {
        public const string HarmonyId = "KSP1PartMod.FairingFixes";
        private const string TypeName = "KSP.Modules.Module_Fairing";

        private static readonly Collider[] Empty = new Collider[0];
        private static readonly HashSet<string> LoggedColliders = new HashSet<string>(FairingRule.Names);
        private static readonly HashSet<string> LoggedMaterials = new HashSet<string>(FairingRule.Names);
        private static FieldInfo _baseColliders, _moduleColor, _fairingMaterial, _fairingConeMaterial;
        private static bool _broken;

        public static void TryApply(Harmony harmony, bool enabled)
        {
            var log = KSP1PartModPlugin.Log;
            if (!enabled)
            {
                log.LogInfo("[KSP1PartMod] fairing: fix disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
                return;
            }
            var type = AccessTools.TypeByName(TypeName);
            if (type == null)
            {
                log.LogWarning($"[KSP1PartMod] fairing: {TypeName} not found, fix off");
                return;
            }
            var armed = new List<string>();
            try
            {
                _baseColliders = AccessTools.Field(type, "_baseColliders");
                var init = AccessTools.Method(type, "OnInitialize", Type.EmptyTypes);
                if (_baseColliders == null || init == null || _baseColliders.FieldType != typeof(Collider[]))
                    log.LogWarning($"[KSP1PartMod] fairing: {TypeName}.OnInitialize or _baseColliders not found, fix off");
                else
                {
                    harmony.Patch(init, prefix: new HarmonyMethod(typeof(FairingFixes), nameof(BeforeInitialize)),
                        postfix: new HarmonyMethod(typeof(FairingFixes), nameof(AfterInitialize)));
                    armed.Add("OnInitialize (empty base colliders)");
                }
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] fairing: could not arm the OnInitialize fix: " + e);
            }
            try
            {
                _moduleColor = AccessTools.Field(type, "_moduleColor");
                _fairingMaterial = AccessTools.Field(type, "_fairingMaterial");
                _fairingConeMaterial = AccessTools.Field(type, "_fairingConeMaterial");
                var loaded = AccessTools.Method(type, "OnMaterialLoaded", new[] { typeof(Material) });
                if (_moduleColor == null || _fairingMaterial == null || _fairingConeMaterial == null || loaded == null)
                    log.LogWarning($"[KSP1PartMod] fairing: {TypeName}.OnMaterialLoaded or its fields not found, fix off");
                else
                {
                    harmony.Patch(loaded, prefix: new HarmonyMethod(typeof(FairingFixes), nameof(BeforeMaterialLoaded)));
                    armed.Add("OnMaterialLoaded (skipped without Module_Color)");
                }
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] fairing: could not arm the OnMaterialLoaded fix: " + e);
            }
            if (armed.Count > 0)
                log.LogInfo("[KSP1PartMod] fairing: KSP1 shroud fix armed on Module_Fairing." + string.Join(", ", armed));
        }

        private static void BeforeInitialize(object __instance)
        {
            if (_broken) return;
            try
            {
                if (FairingRule.NeedsEmptyColliders(IsKsp1(__instance, out _), _baseColliders.GetValue(__instance) == null))
                    _baseColliders.SetValue(__instance, Empty);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void AfterInitialize(object __instance)
        {
            if (_broken) return;
            try
            {
                if (!ReferenceEquals(_baseColliders.GetValue(__instance), Empty) || !IsKsp1(__instance, out var part)) return;
                if (FairingRule.FirstTime(LoggedColliders, part)) KSP1PartModPlugin.Log?.LogInfo(FairingRule.CollidersLine(part));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>False skips Redux's method.</summary>
        private static bool BeforeMaterialLoaded(object __instance, Material __0)
        {
            if (_broken) return true;
            try
            {
                var ksp1 = IsKsp1(__instance, out var part);
                var color = ksp1 ? _moduleColor.GetValue(__instance) as UnityEngine.Object : null;
                if (!FairingRule.SkipMaterialLoaded(ksp1, color != null, __0 != null)) return true;
                _fairingMaterial.SetValue(__instance, __0);
                _fairingConeMaterial.SetValue(__instance, __0);
                if (FairingRule.FirstTime(LoggedMaterials, part)) KSP1PartModPlugin.Log?.LogInfo(FairingRule.MaterialLine(part));
                return false;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        private static bool IsKsp1(object instance, out string part)
        {
            part = PartName(instance as PartBehaviourModule);
            return part != null && Ksp1ImportedPartRegistry.IsImportedPart(part);
        }

        private static string PartName(PartBehaviourModule module)
        {
            if (module == null) return null;
            try
            {
                var oab = module.OABPart;
                if (oab != null) return oab.AvailablePart?.Name;
                return module.part != null ? module.part.Name : null;
            }
            catch (Exception)
            {
                return null; // a part without its model yet: not ours to judge
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] fairing: fix failed, stopped for this session: " + e);
        }
    }
}
