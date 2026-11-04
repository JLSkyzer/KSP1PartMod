using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Rescale;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Model;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.0: KSP1's own scaling rules that Redux misses (<see cref="RescaleRule"/>), three import patches, each with
    /// its own key, Harmony id and stop flag:
    /// <list type="bullet">
    /// <item><b>default rescale</b> (<c>ksp1_default_rescale</c>): a prefix on public <c>Ksp1PartTranslator.TryTranslatePart</c>
    /// (and on <c>RebuildRegisteredPrefab</c>, which reads the same node) adds <c>rescaleFactor = 1.25</c> to the finalized cfg
    /// node of a part that has none (not <c>KerbalEVA</c> / <c>FlagSite</c>), through Redux's internal
    /// <c>Ksp1ConfigNode.AddValue</c>. It runs before Redux reads anything, so nodes, offsets, model, B9 node moves and the
    /// prefab rebuild all see it; a second call finds the value and adds nothing.</item>
    /// <item><b>legacy node scale</b> (<c>ksp1_legacy_node_scale</c>): a postfix on private static <c>ParseAttachNodes</c>
    /// (inside <c>BuildPartData</c>, before Redux measures the nodes on the model) multiplies each node's position by the
    /// cfg's <c>scale</c> met before it. Nodes are matched by order; a count mismatch skips the part with one line.</item>
    /// <item><b>MODEL position</b> (<c>ksp1_model_position_rescale</c>): a postfix on private static
    /// <c>Ksp1ModelImporter.ApplyModelNodeTransform</c> sets <c>localPosition = position × rescaleFactor</c> when the
    /// <c>MODEL</c> has a position and the factor is not 1.</item>
    /// </list>
    /// Every write is absolute (from the cfg). Counts are kept during the import only, once per part, and logged at
    /// <c>MarkImportComplete</c> (its own Harmony id). The import never breaks: one throw stops that patch for the session.</summary>
    internal static class RescalePatches
    {
        public const string DefaultId = "KSP1PartMod.RescaleDefault";
        public const string NodeScaleId = "KSP1PartMod.LegacyNodeScale";
        public const string ModelPositionId = "KSP1PartMod.ModelPositionRescale";
        public const string LinesId = "KSP1PartMod.RescaleLines";

        public const string DefaultKey = "ksp1_default_rescale";
        public const string NodeScaleKey = "ksp1_legacy_node_scale";
        public const string ModelPositionKey = "ksp1_model_position_rescale";

        private static readonly object Gate = new object();
        private static readonly EngineSizeLines.RescaleTally Tally = new EngineSizeLines.RescaleTally();
        private static readonly HashSet<string> Counted = new HashSet<string>(StringComparer.Ordinal);
        private static volatile bool _defaultBroken, _nodeScaleBroken, _modelPositionBroken, _linesArmed;
        private static Action<Ksp1ConfigNode, string, string> _addValue;

        /// <summary>Tests: the lines, instead of the log.</summary>
        internal static Action<string> Sink;

        internal static void Reset()
        {
            lock (Gate)
            {
                Tally.Clear();
                Counted.Clear();
            }
            _defaultBroken = _nodeScaleBroken = _modelPositionBroken = _linesArmed = false;
            _addValue = null;
        }

        // ------------------------------------------------------------------ members

        internal static MethodInfo FindTryTranslatePart()
        {
            var t = AccessTools.TypeByName(EngineModes.TranslatorTypeName);
            var m = t == null ? null : AccessTools.Method(t, "TryTranslatePart");
            var p = m?.GetParameters();
            return m != null && !m.IsStatic && p.Length >= 2 && p[1].ParameterType == typeof(Ksp1ConfigNode) ? m : null;
        }

        internal static MethodInfo FindRebuild()
        {
            var t = AccessTools.TypeByName(EngineModes.TranslatorTypeName);
            var m = t == null ? null : AccessTools.Method(t, "RebuildRegisteredPrefab");
            var p = m?.GetParameters();
            return m != null && !m.IsStatic && p.Length >= 2 && p[1].ParameterType == typeof(Ksp1ConfigNode) ? m : null;
        }

        internal static MethodInfo FindAddValue()
        {
            var m = AccessTools.Method(typeof(Ksp1ConfigNode), "AddValue", new[] { typeof(string), typeof(string) });
            return m != null && !m.IsStatic && m.ReturnType == typeof(void) ? m : null;
        }

        internal static MethodInfo FindParseAttachNodes()
        {
            var t = AccessTools.TypeByName(EngineModes.TranslatorTypeName);
            var m = t == null ? null : AccessTools.Method(t, "ParseAttachNodes", new[] { typeof(Ksp1ConfigNode), typeof(float) });
            return m != null && m.IsStatic && m.ReturnType == typeof(List<AttachNodeDefinition>) ? m : null;
        }

        internal static MethodInfo FindApplyModelNodeTransform()
        {
            var m = AccessTools.Method(typeof(Ksp1ModelImporter), "ApplyModelNodeTransform", new[] { typeof(Transform), typeof(Ksp1ConfigNode), typeof(float) });
            return m != null && m.IsStatic && m.ReturnType == typeof(void) ? m : null;
        }

        // ------------------------------------------------------------------ arming

        public static bool TryApplyDefault(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.Ksp1DefaultRescale)
            {
                Info(EngineSizeLines.RescaleOff(DefaultKey));
                return false;
            }
            var translate = FindTryTranslatePart();
            var add = FindAddValue();
            if (translate == null || add == null)
            {
                Warn(EngineSizeLines.RescaleMemberMissing(translate == null ? "Ksp1PartTranslator.TryTranslatePart" : "Ksp1ConfigNode.AddValue(string, string)", DefaultKey));
                return false;
            }
            _addValue = (Action<Ksp1ConfigNode, string, string>)Delegate.CreateDelegate(typeof(Action<Ksp1ConfigNode, string, string>), add);
            harmony.Patch(translate, prefix: new HarmonyMethod(typeof(RescalePatches), nameof(BeforeTranslate)) { priority = Priority.First });
            var rebuild = FindRebuild();
            if (rebuild != null) harmony.Patch(rebuild, prefix: new HarmonyMethod(typeof(RescalePatches), nameof(BeforeRebuild)) { priority = Priority.First });
            else Warn(EngineSizeLines.RescalePrefix + "Ksp1PartTranslator.RebuildRegisteredPrefab not found: a rebuild reads the node the import already completed");
            ArmLines();
            Info(EngineSizeLines.RescaleArmed(DefaultKey));
            return true;
        }

        public static bool TryApplyNodeScale(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.Ksp1LegacyNodeScale)
            {
                Info(EngineSizeLines.RescaleOff(NodeScaleKey));
                return false;
            }
            var parse = FindParseAttachNodes();
            if (parse == null)
            {
                Warn(EngineSizeLines.RescaleMemberMissing("Ksp1PartTranslator.ParseAttachNodes(Ksp1ConfigNode, float)", NodeScaleKey));
                return false;
            }
            harmony.Patch(parse, postfix: new HarmonyMethod(typeof(RescalePatches), nameof(AfterParseAttachNodes)));
            ArmLines();
            Info(EngineSizeLines.RescaleArmed(NodeScaleKey));
            return true;
        }

        public static bool TryApplyModelPosition(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.Ksp1ModelPositionRescale)
            {
                Info(EngineSizeLines.RescaleOff(ModelPositionKey));
                return false;
            }
            var apply = FindApplyModelNodeTransform();
            if (apply == null)
            {
                Warn(EngineSizeLines.RescaleMemberMissing("Ksp1ModelImporter.ApplyModelNodeTransform(Transform, Ksp1ConfigNode, float)", ModelPositionKey));
                return false;
            }
            harmony.Patch(apply, postfix: new HarmonyMethod(typeof(RescalePatches), nameof(AfterApplyModelNodeTransform)));
            ArmLines();
            Info(EngineSizeLines.RescaleArmed(ModelPositionKey));
            return true;
        }

        /// <summary>The summary at the end of the import, armed once with the first patch; a missing member costs the lines only.</summary>
        private static void ArmLines()
        {
            if (_linesArmed) return;
            _linesArmed = true;
            try
            {
                var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
                if (complete != null) new Harmony(LinesId).Patch(complete, postfix: new HarmonyMethod(typeof(RescalePatches), nameof(AfterImportComplete)));
                else Warn(EngineSizeLines.RescalePrefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no summary line");
            }
            catch (Exception e)
            {
                Warn(EngineSizeLines.RescalePrefix + "no summary line: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ default rescale

        internal static void BeforeTranslate(Ksp1ConfigNode __1) => AddDefault(__1, !Ksp1ImportedPartRegistry.IsComplete);

        internal static void BeforeRebuild(Ksp1ConfigNode __1) => AddDefault(__1, false);

        internal static void AddDefault(Ksp1ConfigNode partNode, bool counting)
        {
            if (_defaultBroken || partNode == null || _addValue == null) return;
            try
            {
                var part = new Ksp1NodeAdapter(partNode);
                var factor = RescaleRule.MissingDefault(part);
                if (factor.HasValue) _addValue(partNode, "rescaleFactor", IsruCfg.Inv(factor.Value));
                if (!counting) return;
                var excluded = !factor.HasValue && partNode.GetValue("rescaleFactor") == null && RescaleRule.IsExcluded(part);
                if (!factor.HasValue && !excluded) return;
                lock (Gate)
                {
                    if (!Counted.Add("default\n" + (partNode.GetValue("name") ?? "?"))) return;
                    if (factor.HasValue) Tally.Defaulted++;
                    else Tally.Excluded++;
                }
            }
            catch (Exception e)
            {
                _defaultBroken = true;
                Error(EngineSizeLines.RescaleStopped(DefaultKey, e.ToString()));
            }
        }

        // ------------------------------------------------------------------ legacy node scale

        internal static void AfterParseAttachNodes(Ksp1ConfigNode __0, List<AttachNodeDefinition> __result)
        {
            if (_nodeScaleBroken || __0 == null || __result == null) return;
            try
            {
                var pairs = RescaleRule.NodeScale(new Ksp1NodeAdapter(__0));
                if (pairs.All(p => p.Value == 1f)) return;
                var counting = !Ksp1ImportedPartRegistry.IsComplete;
                var name = __0.GetValue("name") ?? "?";
                if (pairs.Count != __result.Count)
                {
                    if (counting && Count("mismatch\n" + name, t => t.NodeCountMismatch++))
                        Warn(EngineSizeLines.NodeCountMismatch(name, pairs.Count, __result.Count));
                    return;
                }
                for (var i = 0; i < pairs.Count; i++)
                {
                    if (pairs[i].Value == 1f) continue;
                    var d = __result[i];
                    d.position = d.position * pairs[i].Value;
                    __result[i] = d;
                }
                if (counting) Count("nodes\n" + name, t => t.NodeScaled++);
            }
            catch (Exception e)
            {
                _nodeScaleBroken = true;
                Error(EngineSizeLines.RescaleStopped(NodeScaleKey, e.ToString()));
            }
        }

        // ------------------------------------------------------------------ MODEL position

        internal static void AfterApplyModelNodeTransform(Transform __0, Ksp1ConfigNode __1, float __2)
        {
            if (_modelPositionBroken || __0 == null || __1 == null) return;
            try
            {
                var factor = RescaleRule.ModelPositionFactor(__2);
                if (factor == 1f) return;
                // With a position: the cfg's, scaled. Without: Redux kept the freshly loaded .mu root's position (a new
                // instance per call), which KSP1 scales too; zero stays zero and is not counted.
                if (__1.GetValue("position") != null) __0.localPosition = __1.GetVector3("position", __0.localPosition) * factor;
                else if (__0.localPosition != Vector3.zero) __0.localPosition *= factor;
                else return;
                if (!Ksp1ImportedPartRegistry.IsComplete)
                    lock (Gate) Tally.ModelPositions++;
            }
            catch (Exception e)
            {
                _modelPositionBroken = true;
                Error(EngineSizeLines.RescaleStopped(ModelPositionKey, e.ToString()));
            }
        }

        // ------------------------------------------------------------------ lines

        private static bool Count(string key, Action<EngineSizeLines.RescaleTally> add)
        {
            lock (Gate)
            {
                if (!Counted.Add(key)) return false;
                add(Tally);
                return true;
            }
        }

        internal static void AfterImportComplete()
        {
            try
            {
                List<string> lines;
                lock (Gate)
                {
                    if (!Tally.Any) return; // a cached reload: nothing was translated
                    lines = EngineSizeLines.RescaleSummary(Tally).ToList();
                    Tally.Clear();
                    Counted.Clear(); // a later full import (new cache key) is counted again
                }
                foreach (var l in lines) Info(l);
            }
            catch (Exception e)
            {
                Error(EngineSizeLines.RescalePrefix + "summary failed: " + e);
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
