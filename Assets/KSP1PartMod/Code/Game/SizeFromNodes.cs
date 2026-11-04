using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Sizes;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.0 (<c>ksp1_size_from_nodes</c>): a postfix on Redux's private <c>Ksp1PartTranslator.BuildConfiguredPrefab</c>,
    /// which runs after Redux has measured each stack node on the model (<c>ApplyModelEstimatedAttachNodeSizes</c>, inside it)
    /// and before the part JSON is written; the prefab rebuild runs it again. A part with stack nodes takes its largest stack
    /// node's size (<see cref="PartSizeRule.Decide"/>) as <c>sizeKey</c> and <c>sizeCategory</c>; a part with surface nodes
    /// only keeps Redux's label, except when Redux fell back (no <c>bulkheadProfiles</c> token it knows, read with Redux's own
    /// <c>MapSize</c>) and a <c>sizeNpM</c> token parses. Every write is absolute (from the nodes and the cfg). Parts are counted
    /// once by name during the import; a summary per mod at <c>MarkImportComplete</c>. Its own Harmony id; one throw stops it
    /// for the session, Redux's label left as is.</summary>
    internal static class SizeFromNodes
    {
        public const string HarmonyId = "KSP1PartMod.SizeFromNodes";
        public const int TopMoves = 10;

        private static readonly object Gate = new object();
        private static readonly EngineSizeLines.SizeTally Tally = new EngineSizeLines.SizeTally();
        private static readonly HashSet<string> Counted = new HashSet<string>(StringComparer.Ordinal);
        private static volatile bool _broken;
        private static Func<string, MetaAssemblySizeFilterType> _mapSize;

        /// <summary>Tests: the lines, instead of the log.</summary>
        internal static Action<string> Sink;

        internal static void Reset()
        {
            lock (Gate)
            {
                Tally.Clear();
                Counted.Clear();
            }
            _broken = false;
            _mapSize = null;
        }

        internal static MethodInfo FindMapSize()
        {
            var translator = AccessTools.TypeByName(EngineModes.TranslatorTypeName);
            var m = translator == null ? null : AccessTools.Method(translator, "MapSize", new[] { typeof(string) });
            return m != null && m.IsStatic && m.ReturnType == typeof(MetaAssemblySizeFilterType) ? m : null;
        }

        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            if (settings == null || !settings.Ksp1SizeFromNodes)
            {
                Info(EngineSizeLines.SizeOff);
                return false;
            }
            var build = EngineCurveFix.FindBuildConfiguredPrefab();
            if (build == null)
            {
                Warn(EngineSizeLines.SizeMemberMissing("Ksp1PartTranslator.BuildConfiguredPrefab"));
                return false;
            }
            var map = FindMapSize();
            if (map != null) _mapSize = (Func<string, MetaAssemblySizeFilterType>)Delegate.CreateDelegate(typeof(Func<string, MetaAssemblySizeFilterType>), map);
            else Warn(EngineSizeLines.SizePrefix + "Ksp1PartTranslator.MapSize not found: bulkheadProfiles tokens are not read, stack nodes still are");
            harmony.Patch(build, postfix: new HarmonyMethod(typeof(SizeFromNodes), nameof(AfterBuildConfiguredPrefab)));
            var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
            if (complete != null) harmony.Patch(complete, postfix: new HarmonyMethod(typeof(SizeFromNodes), nameof(AfterImportComplete)));
            else Warn(EngineSizeLines.SizePrefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no summary line");
            Info(EngineSizeLines.SizeArmed);
            return true;
        }

        internal static void AfterBuildConfiguredPrefab(Ksp1ConfigFile __0, Ksp1ConfigNode __1, PartCore __2)
        {
            if (_broken) return;
            try
            {
                Apply(__0?.ModName, __1, __2);
            }
            catch (Exception e)
            {
                _broken = true;
                Error(EngineSizeLines.SizeStopped(e.ToString()));
            }
        }

        /// <summary>True when Redux found no <c>bulkheadProfiles</c> token it knows (its label is then the fallback SM).</summary>
        internal static bool ReduxFellBack(string bulkheadProfiles) =>
            _mapSize != null && _mapSize(bulkheadProfiles) == MetaAssemblySizeFilterType.Auto;

        internal static IEnumerable<(bool stack, string sizeKey)> StackKeys(List<AttachNodeDefinition> nodes)
        {
            if (nodes == null) yield break;
            foreach (var n in nodes)
                yield return (n.nodeType == AttachNodeType.Stack, PartSizeRegistry.GetAttachNodeSizeKey(n));
        }

        internal static bool IsDocking(PartData data) =>
            data?.serializedPartModules != null && data.serializedPartModules.Any(m => m.BehaviourType == typeof(Module_DockingNode));

        internal static void Apply(string mod, Ksp1ConfigNode partNode, PartCore core)
        {
            var data = core?.data;
            if (partNode == null || data == null) return;
            var before = data.sizeKey;
            var bulkhead = partNode.GetValue("bulkheadProfiles");
            var d = PartSizeRule.Decide(before, ReduxFellBack(bulkhead), bulkhead, StackKeys(data.attachNodes));
            if (d.Source != SizeSource.Redux && PartSizeRegistry.IsValidKey(d.Key) &&
                Enum.TryParse<MetaAssemblySizeFilterType>(PartSizeRule.CategoryOf(d.Key), out var category))
            {
                data.sizeKey = PartSizeRegistry.Get(d.Key).Key;
                data.sizeCategory = category;
            }
            else d = new SizeDecision { Key = before, Source = SizeSource.Redux };
            if (Ksp1ImportedPartRegistry.IsComplete) return; // a rebuild: counted at the import
            var name = partNode.GetValue("name") ?? "?";
            lock (Gate)
            {
                if (!Counted.Add(name)) return;
                Tally.Note(mod ?? "?", name, before, data.sizeKey, d.Source, d.Steps, IsDocking(data));
            }
        }

        internal static void AfterImportComplete()
        {
            try
            {
                List<string> lines;
                lock (Gate)
                {
                    if (Tally.ByMod.Count == 0) return; // a cached reload: nothing was translated
                    lines = EngineSizeLines.SizeSummary(Tally, TopMoves).ToList();
                    Tally.Clear();
                    Counted.Clear(); // a later full import (new cache key) is counted again
                }
                foreach (var l in lines) Info(l);
            }
            catch (Exception e)
            {
                Error(EngineSizeLines.SizePrefix + "summary failed: " + e);
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
