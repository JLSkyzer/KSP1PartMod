using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.OAB;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Native.Fairings;
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
    /// fix stays off for the session, with one log line. Native KSP2 parts are never touched.
    /// <para>0.15.0 (fairings plan, Task 3): two guards, only on the KSP1 fairing bases our importer converted
    /// (<see cref="FairingPartRule.TakeOver"/>, <see cref="FairingImporter.Record"/>); the shroud parts keep the 0.4.1 behaviour:
    /// <list type="bullet">
    /// <item>the <c>OnInitialize</c> postfix gives a null <c>_modelTransform</c> (KSP1 imports have no <c>model</c>) the part's own
    /// root: <c>OABPart.PartTransform</c> in the VAB, the part's own transform in flight, and the base colliders under
    /// it when Redux found none (what Redux does with <c>model</c>);</item>
    /// <item>the <c>OnMaterialLoaded</c> prefix also sets <c>_materialLoaded</c>, so the panels spawn. When the material arrives
    /// inside <c>OnInitialize</c> (before <c>_modelTransform</c> is set), the flag waits for the postfix: a coroutine that
    /// spawns panels never sees a null parent.</item>
    /// </list>
    /// <see cref="GuardsArmed"/> only when both patches and every member resolved; the importers arm only then. A guard failure
    /// stops the guards for the session (Redux's code then runs as is), the 0.4.1 fix goes on.</para></summary>
    internal static class FairingFixes
    {
        public const string HarmonyId = "KSP1PartMod.FairingFixes";
        private const string TypeName = "KSP.Modules.Module_Fairing";

        private static readonly Collider[] Empty = new Collider[0];
        private static readonly HashSet<string> LoggedColliders = new HashSet<string>(FairingRule.Names);
        private static readonly HashSet<string> LoggedMaterials = new HashSet<string>(FairingRule.Names);
        private static readonly HashSet<string> LoggedFallback = new HashSet<string>(FairingRule.Names);
        private static readonly HashSet<string> LoggedAllowed = new HashSet<string>(FairingRule.Names);
        /// <summary>Converted modules whose material arrived before <c>_modelTransform</c> was set.</summary>
        private static ConditionalWeakTable<object, object> _pendingMaterial = new ConditionalWeakTable<object, object>();
        private static readonly object PendingMark = new object();
        private static FieldInfo _baseColliders, _moduleColor, _fairingMaterial, _fairingConeMaterial;
        private static FieldInfo _modelTransform, _materialLoaded;
        private static PropertyInfo _oabPartTransform;
        private static bool _broken, _guardsArmed, _guardsBroken;

        /// <summary>The two runtime guards are armed and have not failed: the fairing importers may convert.</summary>
        internal static bool GuardsArmed => _guardsArmed && !_guardsBroken && !_broken;

        public static void TryApply(Harmony harmony, bool enabled)
        {
            var log = KSP1PartModPlugin.Log;
            if (!enabled)
            {
                log?.LogInfo("[KSP1PartMod] fairing: fix disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
                return;
            }
            var type = AccessTools.TypeByName(TypeName);
            if (type == null)
            {
                log?.LogWarning($"[KSP1PartMod] fairing: {TypeName} not found, fix off");
                return;
            }
            var armed = new List<string>();
            bool initArmed = false, materialArmed = false;
            try
            {
                _baseColliders = AccessTools.Field(type, "_baseColliders");
                var init = AccessTools.Method(type, "OnInitialize", Type.EmptyTypes);
                if (_baseColliders == null || init == null || _baseColliders.FieldType != typeof(Collider[]))
                    log?.LogWarning($"[KSP1PartMod] fairing: {TypeName}.OnInitialize or _baseColliders not found, fix off");
                else
                {
                    harmony.Patch(init, prefix: new HarmonyMethod(typeof(FairingFixes), nameof(BeforeInitialize)),
                        postfix: new HarmonyMethod(typeof(FairingFixes), nameof(AfterInitialize)));
                    armed.Add("OnInitialize (empty base colliders)");
                    initArmed = true;
                }
            }
            catch (Exception e)
            {
                log?.LogError("[KSP1PartMod] fairing: could not arm the OnInitialize fix: " + e);
            }
            try
            {
                _moduleColor = AccessTools.Field(type, "_moduleColor");
                _fairingMaterial = AccessTools.Field(type, "_fairingMaterial");
                _fairingConeMaterial = AccessTools.Field(type, "_fairingConeMaterial");
                var loaded = AccessTools.Method(type, "OnMaterialLoaded", new[] { typeof(Material) });
                if (_moduleColor == null || _fairingMaterial == null || _fairingConeMaterial == null || loaded == null)
                    log?.LogWarning($"[KSP1PartMod] fairing: {TypeName}.OnMaterialLoaded or its fields not found, fix off");
                else
                {
                    harmony.Patch(loaded, prefix: new HarmonyMethod(typeof(FairingFixes), nameof(BeforeMaterialLoaded)));
                    armed.Add("OnMaterialLoaded (skipped without Module_Color)");
                    materialArmed = true;
                }
            }
            catch (Exception e)
            {
                log?.LogError("[KSP1PartMod] fairing: could not arm the OnMaterialLoaded fix: " + e);
            }
            if (armed.Count > 0)
                log?.LogInfo("[KSP1PartMod] fairing: KSP1 shroud fix armed on Module_Fairing." + string.Join(", ", armed));
            ArmGuards(type, initArmed, materialArmed);
        }

        /// <summary>The 0.15.0 guards ride on the two patches above: armed when both are and every member resolves.</summary>
        private static void ArmGuards(Type type, bool initArmed, bool materialArmed)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                var missing = new List<string>();
                if (!initArmed) missing.Add("OnInitialize patch");
                if (!materialArmed) missing.Add("OnMaterialLoaded patch");
                _modelTransform = Field(type, "_modelTransform", typeof(Transform), missing);
                _materialLoaded = Field(type, "_materialLoaded", typeof(bool), missing);
                _oabPartTransform = OabPartTransform();
                if (_oabPartTransform == null) missing.Add("IObjectAssemblyPart.PartTransform");
                if (missing.Count > 0)
                {
                    foreach (var m in missing) log?.LogWarning(FairingLines.GuardMemberMissing(m));
                    return;
                }
                _guardsArmed = true;
                log?.LogInfo(FairingLines.GuardsArmed);
            }
            catch (Exception e)
            {
                _guardsArmed = false;
                log?.LogError(FairingLines.PatchFailed(e.ToString()));
            }
        }

        private static FieldInfo Field(Type type, string name, Type expected, List<string> missing)
        {
            var f = AccessTools.Field(type, name);
            if (f != null && !f.IsStatic && (expected == null || f.FieldType == expected)) return f;
            missing.Add(name);
            return null;
        }

        private static PropertyInfo OabPartTransform()
        {
            var p = AccessTools.Property(typeof(IObjectAssemblyPart), "PartTransform");
            return p != null && p.PropertyType == typeof(Transform) && p.GetGetMethod() != null ? p : null;
        }


        /// <summary>The members the guards and the 0.4.1 fix need that the loaded game lacks (empty when all resolve). Tests.</summary>
        internal static List<string> MissingGuardMembers()
        {
            var missing = new List<string>();
            var type = AccessTools.TypeByName(TypeName);
            if (type == null)
            {
                missing.Add(TypeName);
                return missing;
            }
            Field(type, "_baseColliders", typeof(Collider[]), missing);
            Field(type, "_modelTransform", typeof(Transform), missing);
            Field(type, "_materialLoaded", typeof(bool), missing);
            Field(type, "_moduleColor", null, missing);
            Field(type, "_fairingMaterial", typeof(Material), missing);
            Field(type, "_fairingConeMaterial", typeof(Material), missing);
            if (AccessTools.Method(type, "OnInitialize", Type.EmptyTypes) == null) missing.Add("OnInitialize()");
            if (AccessTools.Method(type, "OnMaterialLoaded", new[] { typeof(Material) }) == null) missing.Add("OnMaterialLoaded(Material)");
            if (OabPartTransform() == null) missing.Add("IObjectAssemblyPart.PartTransform");
            return missing;
        }

        /// <summary>Back to the state before any arming (tests).</summary>
        internal static void Reset()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            _broken = _guardsArmed = _guardsBroken = false;
            _baseColliders = _moduleColor = _fairingMaterial = _fairingConeMaterial = _modelTransform = _materialLoaded = null;
            _oabPartTransform = null;
            _pendingMaterial = new ConditionalWeakTable<object, object>();
            LoggedColliders.Clear();
            LoggedMaterials.Clear();
            LoggedFallback.Clear();
            LoggedAllowed.Clear();
        }

        private static void BeforeInitialize(object __instance)
        {
            if (_broken) return;
            try
            {
                if (FairingRule.NeedsEmptyColliders(IsKsp1(__instance, out _), _baseColliders.GetValue(__instance) == null))
                {
                    _baseColliders.SetValue(__instance, Empty);
                    Audit.AuditProbe.Note("fairing");
                }
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void AfterInitialize(object __instance)
        {
            if (_broken) return;
            if (GuardsArmed) Guard(__instance);
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

        /// <summary>A converted base: <c>_modelTransform</c> falls back to the part's root, then a material that arrived inside
        /// <c>OnInitialize</c> is marked loaded.</summary>
        private static void Guard(object instance)
        {
            try
            {
                if (!FairingPartRule.TakeOver(IsKsp1(instance, out var part), FairingImporter.Record.Contains(part))) return;
                if (_modelTransform.GetValue(instance) as Transform == null)
                {
                    var root = FallbackRoot(instance as PartBehaviourModule);
                    if (root != null)
                    {
                        _modelTransform.SetValue(instance, root);
                        // Also collects the SAF colliders the prefab pass disabled: harmless, Module_Fairing 26w42c reads
                        // _baseColliders nowhere after OnInitialize (see FairingRule), and disabled colliders collide with nothing.
                        if (ReferenceEquals(_baseColliders.GetValue(instance), Empty))
                        {
                            var colliders = root.GetComponentsInChildren<Collider>();
                            if (colliders.Length > 0) _baseColliders.SetValue(instance, colliders);
                        }
                        Audit.AuditProbe.Note("fairing_model_fallback");
                        if (FairingRule.FirstTime(LoggedFallback, part)) KSP1PartModPlugin.Log?.LogInfo(FairingLines.ModelFallback(part, root.name));
                    }
                }
                if (_pendingMaterial.TryGetValue(instance, out _))
                {
                    _pendingMaterial.Remove(instance);
                    if (_modelTransform.GetValue(instance) as Transform != null) _materialLoaded.SetValue(instance, true);
                }
            }
            catch (Exception e)
            {
                BreakGuards(e);
            }
        }

        /// <summary>The part's own root (<see cref="FairingPartRule.FallbackOf"/>): <c>OABPart.PartTransform</c> in the VAB, the
        /// part's own transform in flight. Not <c>PartBehavior.ModelTransform</c>: <c>PartBehavior.Start</c> sets it only after every
        /// module's <c>Init</c> (so after this postfix); it is <c>FindModelTransform("model")</c> on that same transform, else the
        /// view object, which sits on it too.</summary>
        private static Transform FallbackRoot(PartBehaviourModule module)
        {
            if (module == null) return null;
            var oab = module.OABPart;
            var part = oab == null ? module.part : null;
            switch (FairingPartRule.FallbackOf(oab != null, part != null))
            {
                case FairingFallback.OabPartTransform: return _oabPartTransform.GetValue(oab) as Transform;
                case FairingFallback.FlightPartTransform: return part.transform;
                default: return null;
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
                Audit.AuditProbe.Note("fairing_material");
                if (GuardsArmed && FairingPartRule.TakeOver(ksp1, FairingImporter.Record.Contains(part))) AllowPanels(__instance, part);
                else if (FairingRule.FirstTime(LoggedMaterials, part)) KSP1PartModPlugin.Log?.LogInfo(FairingRule.MaterialLine(part));
                return false;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        /// <summary>A converted base without <c>Module_Color</c>: the panels may spawn once the parent is known.</summary>
        private static void AllowPanels(object instance, string part)
        {
            try
            {
                if (_modelTransform.GetValue(instance) as Transform != null) _materialLoaded.SetValue(instance, true);
                else
                {
                    _pendingMaterial.Remove(instance);
                    _pendingMaterial.Add(instance, PendingMark);
                }
                if (FairingRule.FirstTime(LoggedAllowed, part)) KSP1PartModPlugin.Log?.LogInfo(FairingLines.MaterialAllowed(part));
            }
            catch (Exception e)
            {
                BreakGuards(e);
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

        private static void BreakGuards(Exception e)
        {
            if (_guardsBroken) return;
            _guardsBroken = true;
            KSP1PartModPlugin.Log?.LogError(FairingLines.PatchFailed(e.ToString()));
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] fairing: fix failed, stopped for this session: " + e);
        }
    }
}
