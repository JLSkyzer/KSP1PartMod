using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.Sim.impl;

namespace KSP1PartMod.Game
{
    /// <summary>Saved crafts name our module types with their assembly-qualified name
    /// (<c>KSP1PartMod.Game.Animation.PartComponentModule_Ksp1Animations, KSP1PartMod, Version=0.0.0.0, …</c>).
    /// Space Warp loads mod assemblies with <c>Assembly.LoadFile</c>, which .NET does not find by name; in Redux 26w46a
    /// nothing resolved it any more (in game, 2026-11-18: <c>Error converting value "KSP1PartMod…" to type
    /// 'System.Type'</c>), the module's <c>ComponentType</c> stayed null, <c>PartComponent.MergePartModuleData</c> threw on
    /// <c>Activator.CreateInstance(null)</c> and the whole vessel was skipped as corrupted.
    /// <list type="bullet">
    /// <item>An <c>AssemblyResolve</c> handler hands our own assembly back for the name "KSP1PartMod" (root cause).</item>
    /// <item>A prefix on <c>MergePartModuleData</c> drops a saved module whose type is still null, with one line, so a
    /// vessel loads without that module instead of being lost.</item>
    /// </list>
    /// Both are installed first in <c>OnPreInitialized</c>, before the save is read, and never throw.</summary>
    internal static class SavedTypeResolver
    {
        public const string HarmonyId = "KSP1PartMod.SavedTypeResolver";
        private static readonly Assembly Own = typeof(SavedTypeResolver).Assembly;
        private static readonly string OwnName = Own.GetName().Name;
        private static bool _installed, _servedLogged, _broken;

        public static void InstallResolver()
        {
            if (_installed) return;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            _installed = true;
        }

        internal static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                if (args?.Name == null || !string.Equals(new AssemblyName(args.Name).Name, OwnName, StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!_servedLogged)
                {
                    _servedLogged = true;
                    KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] save: assembly '{args.Name}' resolved to the loaded KSP1PartMod (saved module types)");
                }
                return Own;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool TryApplyGuard(Harmony harmony)
        {
            var merge = AccessTools.Method(typeof(PartComponent), "MergePartModuleData", new[] { typeof(List<SerializedPartModule>) });
            if (merge == null || merge.IsStatic)
            {
                KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] save: PartComponent.MergePartModuleData(List) not found, no guard for unresolved module types");
                return false;
            }
            harmony.Patch(merge, prefix: new HarmonyMethod(typeof(SavedTypeResolver), nameof(BeforeMerge)));
            return true;
        }

        internal static void BeforeMerge(List<SerializedPartModule> __0)
        {
            if (_broken || __0 == null) return;
            try
            {
                for (var i = __0.Count - 1; i >= 0; i--)
                {
                    if (__0[i].ComponentType != null) continue;
                    KSP1PartModPlugin.Log?.LogWarning($"[KSP1PartMod] save: dropped saved module '{__0[i].Name}' (behaviour {__0[i].BehaviourType?.Name ?? "unknown"}): its type could not be resolved, the vessel loads without it");
                    __0.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                _broken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] save: module type guard failed, stopped for this session: " + e);
            }
        }
    }
}
