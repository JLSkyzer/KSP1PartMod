using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Game;
using KSP.Sim.Definitions;
using KSP1PartMod.Native.Science;

namespace KSP1PartMod.Game
{
    /// <summary>A prefix on <c>PartComponentModule_ScienceExperiment.OnStart(double)</c> (science plan, Task 4 review): a part whose
    /// configurations or saved standings name a <c>KSP1_*</c> experiment that the live store lacks (the mod off in the campaign, or the
    /// definitions not loaded) loses that configuration and its standing before KSP2 reads them. KSP2 drops an unknown configuration
    /// of a new vessel itself, but keeps the saved standing of a loaded vessel with a null definition, which its location check then
    /// dereferences. Armed with <c>ksp1_science</c>, never with the campaign switch: it protects the campaigns where the mod is off.
    /// Every target is resolved by name; a missing one logs one line and nothing is patched. The store not ready: nothing done.</summary>
    internal static class ScienceStoreGuardPatch
    {
        public const string HarmonyId = "KSP1PartMod.ScienceStoreGuard";
        public const string ComponentTypeName = "KSP.Sim.impl.PartComponentModule_ScienceExperiment";
        public const string Feature = "the KSP1 experiment store guard";

        internal sealed class Targets
        {
            public MethodInfo OnStart;
            public FieldInfo DataModules, Standings, StandingId;
            public PropertyInfo Part, PartName;
            public readonly List<string> Missing = new List<string>();
        }

        /// <summary>Tests: the store lookup (null result: the store is not ready). Null: the game's live store.</summary>
        internal static Func<Func<string, bool>> KnownSource;
        /// <summary>Tests: receives every line instead of the game log.</summary>
        internal static Action<string> Sink;

        private static Targets _targets;
        private static bool _broken;
        private static readonly object Lock = new object();
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        internal static Targets Resolve()
        {
            var t = new Targets();
            var type = AccessTools.TypeByName(ComponentTypeName);
            if (type == null)
            {
                t.Missing.Add(ComponentTypeName);
                return t;
            }
            t.OnStart = AccessTools.DeclaredMethod(type, "OnStart", new[] { typeof(double) });
            if (t.OnStart == null || t.OnStart.IsStatic) { t.OnStart = null; t.Missing.Add("PartComponentModule_ScienceExperiment.OnStart(double)"); }
            t.DataModules = AccessTools.Field(type, "DataModules");
            if (t.DataModules == null || t.DataModules.IsStatic || !typeof(ModuleDataList).IsAssignableFrom(t.DataModules.FieldType))
            {
                t.DataModules = null;
                t.Missing.Add("PartComponentModule.DataModules");
            }
            t.Part = AccessTools.Property(type, "Part");
            t.PartName = t.Part == null ? null : AccessTools.Property(t.Part.PropertyType, "PartName");
            if (t.Part == null || t.PartName == null || t.PartName.PropertyType != typeof(string))
            {
                t.Part = t.PartName = null;
                t.Missing.Add("PartComponentModule.Part.PartName");
            }
            var access = ScienceAccess.Get();
            t.Missing.AddRange(access.MissingExperiment);
            if (access.Data != null)
            {
                t.Standings = AccessTools.Field(access.Data, "ExperimentStandings");
                var element = t.Standings?.FieldType.IsGenericType == true && t.Standings.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                    ? t.Standings.FieldType.GetGenericArguments()[0] : null;
                t.StandingId = element == null ? null : AccessTools.Field(element, "ExperimentID");
                if (t.Standings == null || t.Standings.IsStatic || t.StandingId == null || t.StandingId.FieldType != typeof(string))
                {
                    t.Standings = t.StandingId = null;
                    t.Missing.Add("Data_ScienceExperiment.ExperimentStandings (ExperimentStanding.ExperimentID)");
                }
            }
            return t;
        }

        /// <summary>Patches when every target resolves; false (one line per missing member) otherwise.</summary>
        public static bool TryApply(Harmony harmony)
        {
            var t = Resolve();
            if (t.Missing.Count > 0)
            {
                foreach (var m in t.Missing) Warn(ScienceLines.MemberNotFound(m, Feature));
                return false;
            }
            try
            {
                _targets = t;
                harmony.Patch(t.OnStart, prefix: new HarmonyMethod(typeof(ScienceStoreGuardPatch), nameof(BeforeOnStart)));
                return true;
            }
            catch (Exception e)
            {
                _targets = null;
                harmony.UnpatchAll(HarmonyId);
                Error(ScienceLines.PatchFailed(e.ToString()));
                return false;
            }
        }

        private static void BeforeOnStart(object __instance) => Guard(__instance, _targets);

        /// <summary>The prefix's work, on one <c>PartComponentModule_ScienceExperiment</c>. Returns the ids dropped.</summary>
        internal static IReadOnlyList<string> Guard(object component, Targets t)
        {
            if (_broken || component == null || t == null) return Array.Empty<string>();
            try
            {
                var modules = t.DataModules.GetValue(component) as ModuleDataList;
                ModuleData data = null;
                if (modules != null)
                    foreach (var d in modules.Values)
                        if (ScienceAccess.IsExperiment(d)) { data = d; break; }
                if (data == null) return Array.Empty<string>();
                var configs = ScienceAccess.Get().Experiments.GetValue(data) as IList;
                var standings = t.Standings.GetValue(data) as IList;
                var configIds = new List<string>();
                if (configs != null) foreach (var c in configs) configIds.Add((string)ScienceAccess.Get().DefinitionId.GetValue(c));
                var standingIds = new List<string>();
                if (standings != null) foreach (var s in standings) standingIds.Add(s == null ? null : (string)t.StandingId.GetValue(s));
                if (!configIds.Exists(ScienceStoreGuard.IsOurs) && !standingIds.Exists(ScienceStoreGuard.IsOurs)) return Array.Empty<string>();

                var known = KnownSource != null ? KnownSource() : LiveStore();
                if (known == null) return Array.Empty<string>(); // the store is not ready: KSP2 as is
                var drop = ScienceStoreGuard.Missing(configIds, standingIds, known);
                if (!drop.Any) return Array.Empty<string>();
                foreach (var i in drop.Configurations) configs.RemoveAt(i);
                foreach (var i in drop.Standings) standings.RemoveAt(i);
                var part = PartName(component, t);
                foreach (var id in drop.Ids)
                {
                    bool first;
                    lock (Lock) first = Logged.Add(part + "\n" + id);
                    if (first) Info(ScienceLines.DroppedMissing(part, id));
                }
                return drop.Ids;
            }
            catch (Exception e)
            {
                _broken = true;
                Error(ScienceLines.PatchFailed(e.ToString()));
                return Array.Empty<string>();
            }
        }

        private static string PartName(object component, Targets t)
        {
            try { return t.PartName.GetValue(t.Part.GetValue(component)) as string ?? "?"; }
            catch (Exception) { return "?"; }
        }

        /// <summary>The live store's lookup; null when the store is not there or not initialized.</summary>
        private static Func<string, bool> LiveStore()
        {
            var store = GameManager.Instance?.Game?.ScienceManager?.ScienceExperimentsDataStore;
            if (store == null || !store.IsInitialized) return null;
            return id => store.GetExperimentDefinition(id) != null;
        }

        internal static void ResetForTests()
        {
            _targets = null;
            _broken = false;
            KnownSource = null;
            Sink = null;
            lock (Lock) Logged.Clear();
        }

        private static void Info(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        private static void Warn(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogWarning(line);
        }

        private static void Error(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogError(line);
        }
    }
}
