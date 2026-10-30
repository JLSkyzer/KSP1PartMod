using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
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
    /// failure stops it for the session.
    /// 0.15.1 (<c>fix_intake_mach_curve</c>): the same postfix fills <c>Data_ResourceIntake.machCurve</c>, which Redux's KSP1
    /// intake importer leaves null (the VAB and R&amp;D tooltip evaluates it), from the cfg's own <c>machCurve</c> of the
    /// matching <c>ModuleResourceIntake</c>, else with KSP2's flight default (one key (0, 1)).</summary>
    internal static class EngineCurveFix
    {
        public const string HarmonyId = "KSP1PartMod.EngineCurveFix";
        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";

        private static readonly EngineCurveLines.Tally Tally = new EngineCurveLines.Tally();
        private static readonly object Gate = new object();
        private static Dictionary<Type, FieldInfo[]> _checked;
        private static bool _broken;
        private static bool _engines = true, _intakes = true;

        /// <summary>Tests: the lines, instead of the log.</summary>
        internal static Action<string> Sink;

        internal static void Reset()
        {
            lock (Gate) Tally.Clear();
            _broken = false;
            _engines = _intakes = true;
        }

        internal static MethodInfo FindBuildConfiguredPrefab()
        {
            var translator = AccessTools.TypeByName(TranslatorTypeName);
            if (translator == null) return null;
            var candidates = AccessTools.GetDeclaredMethods(translator).Where(m =>
            {
                if (m.Name != "BuildConfiguredPrefab" || m.IsStatic || m.ReturnType != typeof(GameObject)) return false;
                var ps = m.GetParameters();
                return ps.Length == 7 && ps[1].ParameterType == typeof(Ksp1ConfigNode) && ps[2].ParameterType == typeof(PartCore);
            }).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            var engines = settings != null && settings.FixEngineEmissiveCurve;
            var intakes = settings != null && settings.FixIntakeMachCurve;
            if (!engines && !intakes)
            {
                Info(EngineCurveLines.Armed(false, false));
                return false;
            }
            var build = FindBuildConfiguredPrefab();
            var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
            if (build == null)
            {
                Warn(EngineCurveLines.Prefix + "Ksp1PartTranslator.BuildConfiguredPrefab not found, emissive and intake mach curves not fixed");
                return false;
            }
            _engines = engines;
            _intakes = intakes;
            harmony.Patch(build, postfix: new HarmonyMethod(typeof(EngineCurveFix), nameof(AfterBuildConfiguredPrefab)));
            if (complete != null) harmony.Patch(complete, postfix: new HarmonyMethod(typeof(EngineCurveFix), nameof(AfterImportComplete)));
            else Warn(EngineCurveLines.Prefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no summary line");
            Info(EngineCurveLines.Armed(engines, intakes));
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
        internal static int Fix(PartCore core, EngineCurveLines.Tally tally) => Fix(core, tally, null, true, true);

        /// <summary><see cref="Fix(PartCore, EngineCurveLines.Tally)"/> with the cfg part node (for the intakes' own mach curves)
        /// and each half's switch. Returns how many engine modules were filled.</summary>
        internal static int Fix(PartCore core, EngineCurveLines.Tally tally, IKsp1Node partNode, bool engines, bool intakes)
        {
            if (core == null) return 0;
            var seen = new HashSet<ModuleData>();
            var filled = 0;
            var intakeData = new List<Data_ResourceIntake>();
            foreach (var data in Modules(core))
            {
                if (data == null || !seen.Add(data)) continue;
                if (data is Data_ResourceIntake intake)
                {
                    intakeData.Add(intake);
                    continue;
                }
                if (data is Data_Engine engine)
                {
                    if (engines && engine.EmissiveTemperatureCurve == null)
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
            if (intakes && intakeData.Count > 0) FixIntakes(intakeData, partNode, tally);
            if (tally != null)
            {
                tally.Parts++;
                tally.FilledModules += filled;
                if (filled > 0) tally.FilledParts++;
            }
            return filled;
        }

        /// <summary>Fills each null <c>machCurve</c>: the i-th intake data takes the i-th cfg <c>ModuleResourceIntake</c>'s curve
        /// when both counts agree, else KSP2's default.</summary>
        private static void FixIntakes(List<Data_ResourceIntake> intakes, IKsp1Node partNode, EngineCurveLines.Tally tally)
        {
            var cfg = EngineCurveLines.IntakeModules(partNode);
            var filled = 0;
            for (var i = 0; i < intakes.Count; i++)
            {
                if (intakes[i].machCurve != null) continue;
                intakes[i].machCurve = NewCurve(EngineCurveLines.IntakeKeysFor(cfg, intakes.Count, i, out var fromCfg));
                filled++;
                if (tally == null) continue;
                if (fromCfg) tally.IntakesFromCfg++;
                else tally.IntakesDefault++;
            }
            if (tally == null || filled == 0) return;
            tally.IntakesFilled += filled;
            tally.IntakeParts++;
        }

        /// <summary>A curve from flattened keys: a key with both tangents through <c>Add(t, v, in, out)</c>, one without through
        /// <c>Add(t, v)</c>, as KSP1 loads it.</summary>
        internal static FloatCurve NewCurve(float[] keys)
        {
            var curve = new FloatCurve();
            for (var k = 0; k + 3 < keys.Length; k += 4)
            {
                if (float.IsNaN(keys[k + 2]) || float.IsNaN(keys[k + 3])) curve.Add(keys[k], keys[k + 1]);
                else curve.Add(keys[k], keys[k + 1], keys[k + 2], keys[k + 3]);
            }
            return curve;
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

        internal static void AfterBuildConfiguredPrefab(Ksp1ConfigNode __1, PartCore __2)
        {
            if (_broken) return;
            try
            {
                var node = _intakes && __1 != null ? new Ksp1NodeAdapter(__1) : null;
                // Counted during the import only; a later prefab rebuild is fixed but not counted again.
                lock (Gate) Fix(__2, Ksp1ImportedPartRegistry.IsComplete ? null : Tally, node, _engines, _intakes);
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
