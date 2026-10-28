using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>0.13.1 (<c>fix_engine_emissive_curve</c>): fills <c>Data_Engine.EmissiveTemperatureCurve</c>, which Redux's
    /// KSP1 engine importer leaves null (<see cref="EngineCurveLines"/>), with Redux's own zero curve
    /// (<c>Ksp1PartModuleImporterRegistry.CreateZeroCurve</c>: keys (0, 0) and (1, 0)). A postfix on the private
    /// <c>Ksp1PartTranslator.BuildConfiguredPrefab</c>, which <c>TryTranslatePart</c> calls before
    /// <c>IOProvider.ToJson(core)</c>: the fix reaches the raw JSON the registry keeps, and every part made from it. The prefab
    /// rebuild (<c>RebuildRegisteredPrefab</c>) runs it again on the registered core, harmlessly. It composes with the shroud
    /// postfix on the same method (another Harmony id, independent bodies). Other null curves a config builder reads are
    /// counted, never filled. The summary is written when Redux marks the import complete. Never throws into the game: one
    /// failure stops it for the session.</summary>
    internal static class EngineCurveFix
    {
        public const string HarmonyId = "KSP1PartMod.EngineCurveFix";
        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";

        private static readonly EngineCurveLines.Tally Tally = new EngineCurveLines.Tally();
        private static readonly object Gate = new object();
        private static Dictionary<Type, FieldInfo[]> _checked;
        private static bool _broken;

        /// <summary>Tests: the lines, instead of the log.</summary>
        internal static Action<string> Sink;

        internal static void Reset()
        {
            lock (Gate) Tally.Clear();
            _broken = false;
        }

        internal static MethodInfo FindBuildConfiguredPrefab()
        {
            var translator = AccessTools.TypeByName(TranslatorTypeName);
            if (translator == null) return null;
            var candidates = AccessTools.GetDeclaredMethods(translator).Where(m =>
            {
                if (m.Name != "BuildConfiguredPrefab" || m.IsStatic || m.ReturnType != typeof(GameObject)) return false;
                var ps = m.GetParameters();
                return ps.Length == 7 && ps[2].ParameterType == typeof(PartCore);
            }).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.FixEngineEmissiveCurve)
            {
                Info(EngineCurveLines.Armed(false));
                return false;
            }
            var build = FindBuildConfiguredPrefab();
            var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
            if (build == null)
            {
                Warn(EngineCurveLines.Prefix + "Ksp1PartTranslator.BuildConfiguredPrefab not found, emissive curve not fixed");
                return false;
            }
            harmony.Patch(build, postfix: new HarmonyMethod(typeof(EngineCurveFix), nameof(AfterBuildConfiguredPrefab)));
            if (complete != null) harmony.Patch(complete, postfix: new HarmonyMethod(typeof(EngineCurveFix), nameof(AfterImportComplete)));
            else Warn(EngineCurveLines.Prefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no summary line");
            Info(EngineCurveLines.Armed(true));
            return true;
        }

        /// <summary>Redux's zero curve.</summary>
        internal static FloatCurve ZeroCurve()
        {
            var curve = new FloatCurve();
            curve.Add(0f, 0f, 0f, 0f);
            curve.Add(1f, 0f, 0f, 0f);
            return curve;
        }

        /// <summary>Fills the null emissive curves of <paramref name="core"/>'s engine data; counts the other null curves.
        /// Returns how many engine modules were filled.</summary>
        internal static int Fix(PartCore core, EngineCurveLines.Tally tally)
        {
            if (core == null) return 0;
            var seen = new HashSet<ModuleData>();
            var filled = 0;
            foreach (var data in Modules(core))
            {
                if (data == null || !seen.Add(data)) continue;
                if (data is Data_Engine engine)
                {
                    if (engine.EmissiveTemperatureCurve == null)
                    {
                        engine.EmissiveTemperatureCurve = ZeroCurve();
                        filled++;
                    }
                    if (engine.engineModes != null)
                        foreach (var mode in engine.engineModes)
                            if (mode != null) CountNulls(mode, "Data_Engine.EngineMode", tally);
                    continue;
                }
                CountNulls(data, data.GetType().Name, tally);
            }
            if (tally != null)
            {
                tally.Parts++;
                tally.FilledModules += filled;
                if (filled > 0) tally.FilledParts++;
            }
            return filled;
        }

        private static IEnumerable<ModuleData> Modules(PartCore core)
        {
            if (core.modules != null)
                foreach (var m in core.modules) yield return m;
            var serialized = core.data?.serializedPartModules;
            if (serialized == null) yield break;
            foreach (var module in serialized)
            {
                if (module.ModuleData == null) continue;
                foreach (var d in module.ModuleData) yield return d.DataObject;
            }
        }

        private static void CountNulls(object owner, string ownerName, EngineCurveLines.Tally tally)
        {
            if (tally == null || !EngineCurveLines.CheckedFields.TryGetValue(ownerName, out var names)) return;
            _checked ??= new Dictionary<Type, FieldInfo[]>();
            var type = owner.GetType();
            if (!_checked.TryGetValue(type, out var fields))
            {
                fields = names.Select(n => AccessTools.Field(type, n)).Where(f => f != null && f.FieldType == typeof(FloatCurve)).ToArray();
                _checked[type] = fields;
            }
            foreach (var f in fields)
                if (f.GetValue(owner) == null) tally.NoteNull(ownerName, f.Name);
        }

        internal static void AfterBuildConfiguredPrefab(PartCore core)
        {
            if (_broken) return;
            try
            {
                // Counted during the import only; a later prefab rebuild is fixed but not counted again.
                lock (Gate) Fix(core, Ksp1ImportedPartRegistry.IsComplete ? null : Tally);
            }
            catch (Exception e)
            {
                _broken = true;
                Error(EngineCurveLines.Stopped(e.ToString()));
            }
        }

        internal static void AfterImportComplete()
        {
            try
            {
                string line;
                lock (Gate)
                {
                    if (Tally.Parts == 0) return; // a cached reload: nothing was translated
                    line = EngineCurveLines.Summary(Tally);
                    Tally.Clear();
                }
                Info(line);
            }
            catch (Exception e)
            {
                Error(EngineCurveLines.Prefix + "summary failed: " + e);
            }
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
