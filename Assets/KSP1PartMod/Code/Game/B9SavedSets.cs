using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP1PartMod.B9;
using VSwift.Modules.Data;

namespace KSP1PartMod.Game
{
    /// <summary>Old saves and the B9 sets appended in 0.9.0 (final review I1). A part state saved before a set existed holds
    /// fewer <c>ActiveVariants</c> than the part has sets. <c>Module_PartSwitch.EnsureActiveVariant</c> fills such a gap
    /// with the set's default, but only on a loaded vessel; every other vessel is saved through
    /// <c>Data_PartSwitch.GetStoredVariantInformation</c>, which fills it with the set's FIRST variant and saves that
    /// variant's overrides. A prefix fills each missing entry first, with the default of the set at that place
    /// (<c>GetDefaultVariant</c>, the same rule as <c>EnsureActiveVariant</c>), so Redux finds nothing to fill.
    /// <para><c>ActiveVariants</c> holds only variant ids, one per set in the order of <c>VariantSets</c>, so the missing
    /// entries are those past its end; the value written is checked to be a variant of that set (looked up by its id), and
    /// the line names the set by id. Entries already saved are never touched, nor any part with as many entries as sets.
    /// Every member is resolved by name; if one is missing, or the prefix throws, Redux's own behaviour stays (one line).
    /// Armed with the B9 overrides, before any save.</para></summary>
    internal static class B9SavedSets
    {
        internal sealed class Targets
        {
            public MethodInfo GetStoredVariantInformation; // public Dictionary<...> ()
            public MethodInfo GetDefaultVariant;            // public string (int)
            public FieldInfo ActiveVariants;                // List<string>
            public FieldInfo VariantSets;                   // List<VariantSet>
            public FieldInfo VariantSetId;                  // VariantSet: string
            public FieldInfo Variants;                      // VariantSet: List<Variant>
            public FieldInfo VariantId;                     // Variant: string
            public readonly List<string> Missing = new List<string>();
        }

        private static Targets _t;
        private static readonly object LogLock = new object();
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        private static bool _failureLogged;

        internal static bool IsArmed => _t != null;

        internal static Targets Resolve()
        {
            var t = new Targets();
            var data = typeof(Data_PartSwitch);
            t.GetStoredVariantInformation = AccessTools.Method(data, "GetStoredVariantInformation", Type.EmptyTypes);
            if (t.GetStoredVariantInformation == null || t.GetStoredVariantInformation.IsStatic) t.Missing.Add("Data_PartSwitch.GetStoredVariantInformation");
            t.GetDefaultVariant = AccessTools.Method(data, "GetDefaultVariant", new[] { typeof(int) });
            if (t.GetDefaultVariant == null || t.GetDefaultVariant.IsStatic || t.GetDefaultVariant.ReturnType != typeof(string))
                t.Missing.Add("Data_PartSwitch.GetDefaultVariant(int)");
            t.ActiveVariants = Field(data, "ActiveVariants", typeof(List<string>), t);
            t.VariantSets = Field(data, "VariantSets", null, t);
            var setType = t.VariantSets?.FieldType.IsGenericType == true ? t.VariantSets.FieldType.GetGenericArguments()[0] : null;
            if (t.VariantSets != null && (setType == null || !typeof(IList).IsAssignableFrom(t.VariantSets.FieldType)))
            {
                t.Missing.Add("Data_PartSwitch.VariantSets (List<VariantSet>)");
                t.VariantSets = null;
            }
            if (setType == null) return t;
            t.VariantSetId = Field(setType, "VariantSetId", typeof(string), t);
            t.Variants = Field(setType, "Variants", null, t);
            var variantType = t.Variants?.FieldType.IsGenericType == true ? t.Variants.FieldType.GetGenericArguments()[0] : null;
            if (t.Variants != null && (variantType == null || !typeof(IList).IsAssignableFrom(t.Variants.FieldType)))
            {
                t.Missing.Add(setType.Name + ".Variants (List<Variant>)");
                t.Variants = null;
            }
            if (variantType != null) t.VariantId = Field(variantType, "VariantId", typeof(string), t);
            return t;
        }

        private static FieldInfo Field(Type type, string name, Type fieldType, Targets t)
        {
            var f = AccessTools.Field(type, name);
            if (f == null || f.IsStatic || (fieldType != null && f.FieldType != fieldType))
            {
                t.Missing.Add(type.Name + "." + name);
                return null;
            }
            return f;
        }

        /// <summary>Arms the prefix; false (with one line per missing member) leaves Redux's behaviour.</summary>
        public static bool TryApply(Harmony harmony)
        {
            var t = Resolve();
            if (t.Missing.Count > 0)
            {
                foreach (var m in t.Missing) B9Overrides.Warn(B9Lines.SavedSetsMemberNotFound(m));
                return false;
            }
            _t = t;
            try
            {
                harmony.Patch(t.GetStoredVariantInformation, prefix: new HarmonyMethod(typeof(B9SavedSets), nameof(BeforeGetStoredVariantInformation)));
            }
            catch (Exception e)
            {
                _t = null;
                B9Overrides.Error(B9Lines.SavedSetsFailed(e.ToString()));
                return false;
            }
            B9Overrides.Info(B9Lines.SavedSetsArmed);
            return true;
        }

        internal static void Reset()
        {
            _t = null;
            lock (LogLock)
            {
                Logged.Clear();
                _failureLogged = false;
            }
        }

        private static void BeforeGetStoredVariantInformation(object __instance)
        {
            var t = _t;
            if (t == null || __instance == null) return;
            try
            {
                Fill(t, __instance);
            }
            catch (Exception e)
            {
                // Redux then fills the gap itself (first variant), as without the mod.
                lock (LogLock)
                {
                    if (_failureLogged) return;
                    _failureLogged = true;
                }
                B9Overrides.Error(B9Lines.SavedSetsFailed(e.ToString()));
            }
        }

        /// <summary>Appends, for each set past the end of <c>ActiveVariants</c>, that set's default variant.</summary>
        private static void Fill(Targets t, object data)
        {
            if (!(t.ActiveVariants.GetValue(data) is List<string> active)) return;
            if (!(t.VariantSets.GetValue(data) is IList sets) || active.Count >= sets.Count) return;
            for (var i = active.Count; i < sets.Count; i++)
            {
                var set = sets[i];
                var setId = set == null ? null : t.VariantSetId.GetValue(set) as string;
                var variants = set == null ? null : t.Variants.GetValue(set) as IList;
                if (variants == null || variants.Count == 0) return; // Redux's own path (it throws there as before)
                var def = t.GetDefaultVariant.Invoke(data, new object[] { i }) as string;
                // The set is found again by its id and the default must be one of its variants; otherwise Redux decides.
                if (!ReferenceEquals(FindSet(t, sets, setId), set) || !IsVariantOf(t, set, def)) return;
                active.Add(def);
                var first = variants[0] == null ? null : t.VariantId.GetValue(variants[0]) as string;
                bool log;
                lock (LogLock) log = Logged.Add(setId + "\n" + def);
                if (log) B9Overrides.Info(B9Lines.SavedSetFilled(setId ?? "?", def, first ?? "?"));
            }
        }

        /// <summary>The first set whose id is <paramref name="setId"/> (ids are unique within a part).</summary>
        private static object FindSet(Targets t, IList sets, string setId)
        {
            if (setId == null) return null;
            foreach (var s in sets)
                if (s != null && string.Equals(t.VariantSetId.GetValue(s) as string, setId, StringComparison.Ordinal)) return s;
            return null;
        }

        private static bool IsVariantOf(Targets t, object set, string variantId)
        {
            if (variantId == null || set == null || !(t.Variants.GetValue(set) is IList variants)) return false;
            foreach (var v in variants)
                if (v != null && string.Equals(t.VariantId.GetValue(v) as string, variantId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
