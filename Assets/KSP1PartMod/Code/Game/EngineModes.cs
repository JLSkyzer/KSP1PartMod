using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Engines;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.0 (<c>sep_procedural_engines</c>, <c>ksp1_engine_modes_fallback</c>): a postfix on Redux's private static
    /// <c>Ksp1PartTranslator.GetOrderedSwitchedEngineModules(Ksp1ConfigNode)</c>. Where Redux returns no list (no switch it
    /// knows), it returns the part's engine modules in SEP's switch order, or in cfg order for a part with several engines and
    /// no switch at all (<see cref="EngineModeOrder"/>): <c>TryTranslatePart</c> then imports them through Redux's own
    /// <c>TryImportEngineModes</c>, as one <c>Module_Engine</c> with one mode per module, mode 0 first. The list holds Redux's
    /// own node objects (B9's module diff, which calls the same method, compares them by reference). A list Redux returned is
    /// never touched. Parts are counted only during the import (<c>!Ksp1ImportedPartRegistry.IsComplete</c>), once by name:
    /// B9's scratch imports call the method again. Any throw stops the patch for the session, Redux's list left as is.</summary>
    internal static class EngineModes
    {
        public const string HarmonyId = "KSP1PartMod.EngineModes";
        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";
        public const string OrderedEnginesName = "GetOrderedSwitchedEngineModules";

        internal enum Kind
        {
            None,
            Sep,
            Fallback,
        }

        private static readonly object Gate = new object();
        private static readonly EngineSizeLines.EngineTally Tally = new EngineSizeLines.EngineTally();
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Every part whose order this patch made, for the session (read by <see cref="EngineModeSync"/>).</summary>
        private static readonly HashSet<string> Ordered = new HashSet<string>(StringComparer.Ordinal);
        private static volatile bool _armed, _broken;
        private static bool _sep, _fallback;

        /// <summary>Tests: the lines, instead of the log.</summary>
        internal static Action<string> Sink;
        /// <summary>Tests: runs before the mapping of each part (a throw must leave Redux's list).</summary>
        internal static Action<Ksp1ConfigNode> BeforeMapForTests;

        internal static bool IsArmed => _armed && !_broken;

        internal static void Reset()
        {
            lock (Gate)
            {
                Tally.Clear();
                Logged.Clear();
                Ordered.Clear();
            }
            _armed = _broken = false;
            _sep = _fallback = false;
            BeforeMapForTests = null;
        }

        internal static MethodInfo FindOrdered()
        {
            var translator = AccessTools.TypeByName(TranslatorTypeName);
            var m = translator == null ? null : AccessTools.Method(translator, OrderedEnginesName, new[] { typeof(Ksp1ConfigNode) });
            return m != null && m.IsStatic && m.ReturnType == typeof(List<Ksp1ConfigNode>) ? m : null;
        }

        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            var sep = settings != null && settings.SepProceduralEngines;
            var fallback = settings != null && settings.Ksp1EngineModesFallback;
            if (!sep && !fallback)
            {
                Info(EngineSizeLines.EnginesOff);
                return false;
            }
            var ordered = FindOrdered();
            if (ordered == null)
            {
                Warn(EngineSizeLines.EnginesMemberMissing("Ksp1PartTranslator." + OrderedEnginesName + "(Ksp1ConfigNode)"));
                return false;
            }
            _sep = sep;
            _fallback = fallback;
            harmony.Patch(ordered, postfix: new HarmonyMethod(typeof(EngineModes), nameof(AfterOrdered)));
            _armed = true;
            var complete = AccessTools.Method(typeof(Ksp1ImportedPartRegistry), nameof(Ksp1ImportedPartRegistry.MarkImportComplete));
            if (complete != null) harmony.Patch(complete, postfix: new HarmonyMethod(typeof(EngineModes), nameof(AfterImportComplete)));
            else Warn(EngineSizeLines.EnginePrefix + "Ksp1ImportedPartRegistry.MarkImportComplete not found, no summary line");
            Info(EngineSizeLines.EnginesArmed(sep, fallback));
            return true;
        }

        /// <summary>Which rule makes this part's engine modes, under the armed switches, and the MODULE indices in mode order.
        /// <see cref="Kind.None"/> when the patch is not armed or stopped.</summary>
        internal static Kind KindOf(IKsp1Node part, out int[] order)
        {
            order = Array.Empty<int>();
            if (!IsArmed || part == null) return Kind.None;
            if (_sep)
            {
                order = EngineModeOrder.Sep(part);
                if (order.Length > 0) return Kind.Sep;
            }
            if (_fallback)
            {
                order = EngineModeOrder.Fallback(part);
                if (order.Length > 0) return Kind.Fallback;
            }
            order = Array.Empty<int>();
            return Kind.None;
        }

        internal static Kind KindOf(Ksp1ConfigNode partNode) =>
            partNode == null ? Kind.None : KindOf(new Ksp1NodeAdapter(partNode), out _);

        internal static void AfterOrdered(Ksp1ConfigNode partNode, ref List<Ksp1ConfigNode> __result)
        {
            if (_broken || partNode == null || (__result != null && __result.Count > 0)) return;
            try
            {
                BeforeMapForTests?.Invoke(partNode);
                var part = new Ksp1NodeAdapter(partNode);
                var kind = KindOf(part, out var order);
                if (kind == Kind.None) return;
                var modules = partNode.GetNodes("MODULE").ToList();
                var list = new List<Ksp1ConfigNode>(order.Length);
                foreach (var i in order) list.Add(modules[i]);
                var name = partNode.GetValue("name") ?? "?";
                lock (Gate) Ordered.Add(name);
                if (!Ksp1ImportedPartRegistry.IsComplete) Note(kind, name, part, order);
                __result = list;
            }
            catch (Exception e)
            {
                _broken = true;
                Error(EngineSizeLines.EnginesStopped("mode order", e.ToString()));
            }
        }

        private static void Note(Kind kind, string name, IKsp1Node part, int[] order)
        {
            string line;
            lock (Gate)
            {
                if (!Logged.Add(name + "\norder")) return;
                var modules = EngineModeOrder.Modules(part);
                var ids = EngineModeOrder.UniqueIds(order.Select(i => EngineModeOrder.EngineIdOf(modules[i])).ToArray());
                if (kind == Kind.Sep)
                {
                    Tally.Sep.Add(name);
                    line = EngineSizeLines.SepOrder(name, ids);
                }
                else
                {
                    Tally.Fallback.Add(name);
                    var thrust = order.Select(i => IsruCfg.Number(modules[i], "maxThrust") ?? 1.0).ToArray(); // Redux's default 1
                    line = EngineSizeLines.FallbackModes(name, order.Length, ids, thrust[0], thrust.Sum());
                }
            }
            Info(line);
        }

        /// <summary>True when this patch ordered the engine modes of the part named <paramref name="part"/> in this session.</summary>
        internal static bool WasOrdered(string part)
        {
            if (part == null) return false;
            lock (Gate) return Ordered.Contains(part);
        }

        /// <summary>A refused SEP part (counted once by name, during the import only).</summary>
        internal static void NoteRefused(string name, string line)
        {
            lock (Gate)
            {
                if (!Logged.Add(name + "\nrefused")) return;
                Tally.Refused.Add(name);
            }
            Warn(line);
        }

        /// <summary>A line logged once per key for the session.</summary>
        internal static void LogOnce(string key, string line)
        {
            lock (Gate)
                if (!Logged.Add(key)) return;
            Info(line);
        }

        internal static void AfterImportComplete()
        {
            try
            {
                string line;
                lock (Gate)
                {
                    if (!Tally.Any) return; // a cached reload: nothing was translated
                    line = EngineSizeLines.EngineSummary(Tally);
                    Tally.Clear();
                    Logged.Clear(); // a later full import (new cache key) is counted again
                }
                Info(line);
            }
            catch (Exception e)
            {
                Error(EngineSizeLines.EnginePrefix + "summary failed: " + e);
            }
        }

        internal static void Info(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        internal static void Warn(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogWarning(line);
        }

        internal static void Error(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogError(line);
        }
    }
}
