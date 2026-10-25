using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.OAB;
using KSP1PartMod.Ui;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;

namespace KSP1PartMod.Game.Ui
{
    /// <summary>The KSP1 sub-category bar of the VAB part picker (<c>subcategory_bar</c>).
    /// <para>One Harmony postfix, on the private <c>AssemblyPartsPicker.ApplyPartsFilter</c> (Redux 26w42c). The picker
    /// calls it from <c>SetVisiblePartButtons</c> on the parts of the current category, after which it builds the
    /// "Group by" headers and the grid from the result; the search calls it too, but with <c>currentCategory</c> =
    /// <c>none</c>, which this postfix leaves alone. When the current category is a KSP1 mod category of Redux, the
    /// postfix counts the types of the parts the stock filters kept (so no type opens on an empty grid), hands that
    /// model to the picker's <see cref="Ksp1SubcategoryBar"/>, and keeps only the selected type.</para>
    /// Never throws into the game: after one failure the feature stays off for the session, with one log line.</summary>
    internal static class OabSubcategories
    {
        public const string HarmonyId = "KSP1PartMod.OabSubcategories";

        internal static readonly SubcategorySelection Selection = new SubcategorySelection();

        private static bool _armed, _broken;
        private static SubcategoryIndex _index;
        private static int _indexCount = -1;
        private static string _indexKey;
        private static bool _indexComplete;
        private static readonly Dictionary<string, string> LastTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastFilter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Armed, not broken: the bar may show.</summary>
        public static bool Active => _armed && !_broken;

        public static void TryApply(Harmony harmony, bool enabled)
        {
            var log = KSP1PartModPlugin.Log;
            if (!enabled)
            {
                log.LogInfo("[KSP1PartMod] ui: sub-category bar disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
                return;
            }
            try
            {
                var missing = MissingMembers();
                if (missing.Count > 0)
                {
                    log.LogWarning("[KSP1PartMod] ui: sub-category bar off, not found in this Redux: " + string.Join(", ", missing));
                    return;
                }
                var original = AccessTools.Method(typeof(AssemblyPartsPicker), "ApplyPartsFilter",
                    new[] { typeof(List<IObjectAssemblyAvailablePart>) });
                harmony.Patch(original, postfix: new HarmonyMethod(typeof(OabSubcategories), nameof(AfterApplyPartsFilter)));
                _armed = true;
                log.LogInfo("[KSP1PartMod] ui: sub-category bar armed on AssemblyPartsPicker.ApplyPartsFilter");
                ArmCollapseGuard(harmony);
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] ui: sub-category bar not armed: " + e);
            }
        }

        /// <summary>Redux's private <c>ExpandCollapsePartsWindow(bool)</c> moves <c>manipulateShipToolbarRT</c> by
        /// <c>partContentRT.rect.width - 0.5 * its width</c>, measured on collapse and on expand. Our narrowing must not
        /// be in that width, or the toolbar drifts by one bar width per cycle: a prefix restores the grid first. Optional:
        /// without it the bar still works.</summary>
        private static void ArmCollapseGuard(Harmony harmony)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                var original = AccessTools.Method(typeof(AssemblyPartsPicker), "ExpandCollapsePartsWindow", new[] { typeof(bool) });
                if (original == null)
                {
                    log.LogWarning("[KSP1PartMod] ui: AssemblyPartsPicker.ExpandCollapsePartsWindow not found, collapse guard off");
                    return;
                }
                harmony.Patch(original, prefix: new HarmonyMethod(typeof(OabSubcategories), nameof(BeforeExpandCollapse)));
                log.LogInfo("[KSP1PartMod] ui: collapse guard armed on AssemblyPartsPicker.ExpandCollapsePartsWindow");
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] ui: collapse guard not armed: " + e);
            }
        }

        private static bool _collapseGuardBroken;

        private static void BeforeExpandCollapse(AssemblyPartsPicker __instance)
        {
            if (_collapseGuardBroken || __instance == null) return;
            try
            {
                var bar = __instance.GetComponent<Ksp1SubcategoryBar>();
                if (bar != null) bar.RestoreNow();
            }
            catch (Exception e)
            {
                _collapseGuardBroken = true;
                KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] ui: collapse guard failed, stopped for this session: " + e);
            }
        }

        /// <summary>Everything the feature touches, checked by name so a renamed member turns it off cleanly.</summary>
        private static List<string> MissingMembers()
        {
            var missing = new List<string>();
            var picker = typeof(AssemblyPartsPicker);
            foreach (var f in new[] { "currentCategory", "categoryButtons", "partsCategoriesToolbarRT", "partContentRT", "SearchFilterSortRT" })
                if (AccessTools.Field(picker, f) == null) missing.Add("AssemblyPartsPicker." + f);
            if (AccessTools.Method(picker, "ApplyPartsFilter", new[] { typeof(List<IObjectAssemblyAvailablePart>) }) == null)
                missing.Add("AssemblyPartsPicker.ApplyPartsFilter");
            foreach (var m in new[] { "GetCategoryButton", "RefreshPartsPicker" })
                if (AccessTools.Method(picker, m) == null) missing.Add("AssemblyPartsPicker." + m);
            if (AccessTools.Field(typeof(ObjectAssemblyCategoryButton), "Icon") == null) missing.Add("ObjectAssemblyCategoryButton.Icon");
            if (AccessTools.Method(typeof(Ksp1OabCategoryRegistry), "TryGetCategory") == null) missing.Add("Ksp1OabCategoryRegistry.TryGetCategory");
            if (AccessTools.Method(typeof(Ksp1ImportedPartRegistry), "TryGetIcon") == null) missing.Add("Ksp1ImportedPartRegistry.TryGetIcon");
            return missing;
        }

        /// <summary>Turns the feature off for the session (called by the bar after a failure too).</summary>
        internal static void Break(string where, Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] ui: sub-category bar failed in {where}, stopped for this session: {e}");
        }

        private static void AfterApplyPartsFilter(AssemblyPartsPicker __instance, ref List<IObjectAssemblyAvailablePart> __result)
        {
            if (!Active || __instance == null || __result == null) return;
            try
            {
                __result = Filter(__instance, __result);
            }
            catch (Exception e)
            {
                Break("ApplyPartsFilter", e);
            }
        }

        private static List<IObjectAssemblyAvailablePart> Filter(AssemblyPartsPicker picker, List<IObjectAssemblyAvailablePart> parts)
        {
            var bar = picker.GetComponent<Ksp1SubcategoryBar>();
            var plugin = KSP1PartModPlugin.Instance;
            if ((plugin != null && !plugin.Enabled) || !Ksp1OabCategoryRegistry.TryGetCategory(picker.currentCategory, out var entry) || entry == null)
            {
                if (bar != null) bar.Clear();
                return parts;
            }
            if (bar == null) bar = picker.gameObject.AddComponent<Ksp1SubcategoryBar>();
            // A bar that could not be built shows nothing: its picker must not be filtered by a selection made elsewhere.
            if (bar.Failed) return parts;
            var index = Index();
            var types = new List<Ksp1Subcategory>(parts.Count);
            var firstParts = new Dictionary<Ksp1Subcategory, string>();
            foreach (var p in parts)
            {
                var sub = p == null ? Ksp1Subcategory.Other : index.Get(p.Name, p.PartData?.tags);
                types.Add(sub);
                if (p != null && !firstParts.ContainsKey(sub)) firstParts[sub] = p.Name;
            }
            var model = SubcategoryBarModel.Build(types);
            var mod = entry.DisplayName;
            LogOnChange(LastTypes, mod, SubcategoryLog.TypesLine(mod, model));
            var selected = Selection.Effective(mod, model);
            bar.SetModel(entry.Category, mod, model, selected, firstParts);
            if (!model.Show) return parts;
            LogOnChange(LastFilter, mod, SubcategoryLog.FilterLine(mod, selected, model));
            if (!selected.HasValue) return parts;
            var kept = new List<IObjectAssemblyAvailablePart>(model.CountOf(selected.Value));
            for (var i = 0; i < parts.Count; i++)
                if (SubcategorySelection.Keep(types[i], selected)) kept.Add(parts[i]);
            return kept;
        }

        /// <summary>Rebuilt whenever Redux's registry changes (count, cache key, completion).</summary>
        private static SubcategoryIndex Index()
        {
            var count = Ksp1ImportedPartRegistry.Count;
            var key = Ksp1ImportedPartRegistry.CacheKey;
            var complete = Ksp1ImportedPartRegistry.IsComplete;
            if (_index != null && count == _indexCount && key == _indexKey && complete == _indexComplete) return _index;
            _index = SubcategoryIndex.Build(ImportedPartsSource.Read());
            _indexCount = count;
            _indexKey = key;
            _indexComplete = complete;
            KSP1PartModPlugin.Log?.LogInfo(SubcategoryLog.IndexLine(_index) + (complete ? "" : " (Redux import not complete yet)"));
            return _index;
        }

        private static void LogOnChange(Dictionary<string, string> last, string mod, string line)
        {
            if (mod == null || (last.TryGetValue(mod, out var previous) && previous == line)) return;
            last[mod] = line;
            KSP1PartModPlugin.Log?.LogInfo(line);
        }
    }
}
