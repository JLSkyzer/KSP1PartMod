using System;
using System.Collections.Generic;
using HarmonyLib;
using I2.Loc;
using KSP.Game;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import;

namespace KSP1PartMod.Game
{
    /// <summary>Max thrust, Isp and propellant in the VAB tooltip of KSP1 engines (<c>engine_tooltip_stats</c>, see
    /// <see cref="EngineTooltip"/>). A Harmony postfix on the private <c>KSP.UI.PartInfoOverlay.PopulatePriorityInfoFromPart
    /// (IObjectAssemblyAvailablePart, string)</c> (Redux 26w42c), which the tooltip and the part comparison both use: for a
    /// part of a KSP1 category with a <c>Data_Engine</c>, it adds the lines the game builds for <c>PartCategories.Engine</c>
    /// (same keys, same separator, first engine mode, <c>OABPartData.DefaultOABSituationStats</c>). Display only:
    /// <c>PartData.category</c> is never changed (the per-mod categories and the sub-category bar rely on it). Never throws
    /// into the game: after one failure the fix stays off for the session, with one log line.</summary>
    internal static class EngineTooltipStats
    {
        public const string HarmonyId = "KSP1PartMod.EngineTooltipStats";
        private const string TypeName = "KSP.UI.PartInfoOverlay";
        private const string MethodName = "PopulatePriorityInfoFromPart";
        private const string PropellantKey = "PartModules/Generic/Tooltip/Propellants";

        private static bool _broken, _logged;

        public static void TryApply(Harmony harmony, bool enabled)
        {
            var log = KSP1PartModPlugin.Log;
            if (!enabled)
            {
                log.LogInfo("[KSP1PartMod] tooltip: engine stats disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
                return;
            }
            try
            {
                var type = AccessTools.TypeByName(TypeName);
                var original = type == null ? null
                    : AccessTools.Method(type, MethodName, new[] { typeof(IObjectAssemblyAvailablePart), typeof(string) });
                if (original == null || original.ReturnType != typeof(List<KeyValuePair<string, string>>))
                {
                    log.LogWarning($"[KSP1PartMod] tooltip: {TypeName}.{MethodName} not found, fix off");
                    return;
                }
                harmony.Patch(original, postfix: new HarmonyMethod(typeof(EngineTooltipStats), nameof(AfterPopulatePriority)));
                log.LogInfo($"[KSP1PartMod] tooltip: KSP1 engine stats armed on PartInfoOverlay.{MethodName}");
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] tooltip: engine stats not armed: " + e);
            }
        }

        private static void AfterPopulatePriority(IObjectAssemblyAvailablePart __0, string __1, List<KeyValuePair<string, string>> __result)
        {
            if (_broken || __result == null || !(__0 is OABPartData part)) return;
            try
            {
                if (!Ksp1OabCategoryRegistry.IsKsp1Category(part.Category)) return;
                var engine = FindEngine(part);
                if (engine?.engineModes == null || engine.engineModes.Length == 0 || engine.engineModes[0] == null) return;
                var mode = engine.engineModes[0];
                var stats = OABPartData.DefaultOABSituationStats;
                var lines = EngineTooltip.Lines(__1,
                    OABLocalization.GetTranslation(string.Join(string.Empty, "VAB/Tooltip/", "Max Thrust")),
                    Entries(engine.GetMaxThrustStrings(stats, mode)),
                    OABLocalization.GetTranslation(string.Join(string.Empty, "VAB/Tooltip/", "ISP")),
                    Entries(engine.GetIspStrings(stats, mode)),
                    LocalizationManager.GetTranslation(PropellantKey) ?? "Propellants",
                    MixtureName(mode.propellant?.mixtureName));
                var added = EngineTooltip.Merge(__result, lines);
                if (added > 0 && !_logged)
                {
                    _logged = true;
                    KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] tooltip: {added} engine line(s) added for '{part.Name}' " +
                                                   $"(first: '{lines[0].Key}' = '{lines[0].Value}'); logged once");
                }
            }
            catch (Exception e)
            {
                _broken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] tooltip: engine stats failed, stopped for this session: " + e);
            }
        }

        private static Data_Engine FindEngine(OABPartData part)
        {
            var modules = part.PartData?.serializedPartModules;
            if (modules == null) return null;
            foreach (var module in modules)
            {
                if (module.BehaviourType != typeof(Module_Engine) || module.ModuleData == null) continue;
                foreach (var data in module.ModuleData)
                    if (data.DataObject is Data_Engine engine) return engine;
                return null;
            }
            return null;
        }

        private static List<EngineTooltip.SubEntry> Entries(List<OABPartData.PartInfoModuleSubEntry> entries)
        {
            var list = new List<EngineTooltip.SubEntry>(entries?.Count ?? 0);
            if (entries == null) return list;
            foreach (var e in entries)
                if (e != null) list.Add(new EngineTooltip.SubEntry(e.SubEntryName, e.SubEntryValue));
            return list;
        }

        /// <summary>The mixture's display name from the resource database, else its name.</summary>
        private static string MixtureName(string mixture)
        {
            if (string.IsNullOrEmpty(mixture)) return null;
            try
            {
                var db = GameManager.Instance?.Game?.ResourceDefinitionDatabase;
                var data = db?.GetDefinitionData(db.GetResourceIDFromName(mixture));
                var display = data?.DisplayName;
                return string.IsNullOrEmpty(display) ? mixture : display;
            }
            catch (Exception)
            {
                return mixture;
            }
        }
    }
}
