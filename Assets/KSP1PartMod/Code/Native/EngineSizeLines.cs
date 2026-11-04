using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Native.Engines;

namespace KSP1PartMod.Native
{
    /// <summary>Every log line of 0.16.0 (engines and sizes), and one tally type per patch: engine modes and SEP values
    /// (<see cref="EngineTally"/>), sizes (<see cref="SizeTally"/>), rescale (<see cref="RescaleTally"/>). Pure.</summary>
    public static class EngineSizeLines
    {
        public const string EnginePrefix = EngineCurveLines.Prefix; // "[KSP1PartMod] engines: "
        public const string SizePrefix = "[KSP1PartMod] size: ";
        public const string RescalePrefix = "[KSP1PartMod] rescale: ";

        private static string Inv(double v) => IsruCfg.Inv(v);
        private static string OnOff(bool on) => on ? "on" : "off";

        // ---------------------------------------------------------------- engines

        public sealed class EngineTally
        {
            public readonly List<string> Sep = new List<string>(), Fallback = new List<string>(), Refused = new List<string>();

            public void Clear()
            {
                Sep.Clear();
                Fallback.Clear();
                Refused.Clear();
            }

            public bool Any => Sep.Count + Fallback.Count + Refused.Count > 0;
        }

        public static string EnginesArmed(bool sep, bool fallback) =>
            EnginePrefix + $"modes armed (sep_procedural_engines {OnOff(sep)}, ksp1_engine_modes_fallback {OnOff(fallback)})";

        public static string EnginesOff => EnginePrefix + "modes off (sep_procedural_engines and ksp1_engine_modes_fallback false)";

        public static string EnginesMemberMissing(string member) =>
            EnginePrefix + member + " not found: sep_procedural_engines and ksp1_engine_modes_fallback off, one Module_Engine per KSP1 engine module as in 0.15.1";

        public static string SepValuesMemberMissing(string member) =>
            EnginePrefix + member + " not found: SEP thrust and mass and duplicate engine ids left as Redux makes them";

        public static string EnginesStopped(string what, string error) => EnginePrefix + what + " stopped for this session: " + error;

        public static string SepOrder(string part, IEnumerable<string> ids) =>
            EnginePrefix + part + ": " + EngineModeOrder.SepSwitchModule + " order " + string.Join(", ", ids);

        public static string SepValues(string part, SepComputation c) =>
            EnginePrefix + part + ": SEP type '" + c.TypeName + "': " +
            string.Join(", ", c.Sets.Select(s => $"{s.Name} {s.Count} x {Inv(s.SingleEngineThrust)}")) + " kN; modes " +
            string.Join(" / ", c.ModeThrust.Select(Inv)) + " kN; engine mass +" + Inv(c.EngineMass) + " t (covers excluded); thrust transforms " +
            string.Join(", ", c.ThrustTransformNames.Select((n, i) => n + " " + c.ThrustTransforms[i])) +
            (c.NotReproduced.Count == 0 ? "" : "; not reproduced: " + string.Join(", ", c.NotReproduced));

        public static string SepRefused(string part, string reason) =>
            EnginePrefix + part + ": SEP values not set (" + reason + "), Redux's thrust and mass kept";

        /// <summary>The SEP outcome of one build, compared between the first import and a prefab rebuild.</summary>
        public static string SepOutcome(SepComputation c) =>
            c.Converted
                ? string.Join(", ", c.Sets.Select(s => $"{s.Name} {s.Count}")) + "; mass +" + Inv(c.EngineMass) + " t"
                : "refused (" + c.Reason + ")";

        public static string SepRebuildDiffers(string part, string first, string now) =>
            EnginePrefix + part + ": the prefab rebuild gives " + now + ", the first import gave " + first +
            "; the rebuild's values are written";

        public static string FallbackModes(string part, int modules, IEnumerable<string> ids, double mode0Thrust, double ksp1Sum) =>
            EnginePrefix + part + $": {modules} engine modules, no switch: modes " + string.Join(", ", ids) +
            $" (mode 0 {Inv(mode0Thrust)} kN, KSP1 sum {Inv(ksp1Sum)} kN)";

        public static string DuplicateId(string part, string from, string to) =>
            EnginePrefix + part + ": duplicate engineID '" + from + "' -> '" + to + "'";

        public static string ModeSyncMemberMissing(string member) =>
            EnginePrefix + member + " not found: a 0.15.x craft may show another mode in the dropdown than the one that runs";

        public static string ModeSynced(string part, string from, string to) =>
            EnginePrefix + part + ": saved mode '" + from + "' (a 0.15.x craft) shown as '" + to + "', the mode that runs";

        public static string EngineSummary(EngineTally t) =>
            EnginePrefix + $"summary: {t.Sep.Count} SEP part(s), {t.Fallback.Count} fallback part(s), {t.Refused.Count} refused" +
            (t.Refused.Count == 0 ? "" : " (" + string.Join(", ", t.Refused) + ")");

        /// <summary>The B9 summary's suffix (0.16.0).</summary>
        public static string B9Engines(int nowCompared, int droppedOnSep) =>
            $"; {nowCompared} engine edit(s) now on a multi-mode engine, {droppedOnSep} dropped on SEP clusters";

        public static string B9EngineEditDropped(string part, string variant) =>
            "[KSP1PartMod] b9: " + part + ": " + variant + ": engine edit dropped on an SEP cluster (its thrust comes from the ENGINESETs; dropped even when the SEP values are later refused, Redux's thrust then stays)";

        // ---------------------------------------------------------------- sizes

        public sealed class SizeTally
        {
            public sealed class Mod
            {
                public int FromNodes, Moved2, FromTokens, Unchanged;
            }

            public readonly Dictionary<string, Mod> ByMod = new Dictionary<string, Mod>(StringComparer.Ordinal);
            public readonly List<(string part, string from, string to, int steps)> Moves = new List<(string, string, string, int)>();
            public readonly List<string> Docking = new List<string>();

            public void Note(string mod, string part, string from, string to, Sizes.SizeSource source, int steps, bool docking)
            {
                if (!ByMod.TryGetValue(mod ?? "", out var m)) ByMod[mod ?? ""] = m = new Mod();
                var changed = !string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
                if (!changed) m.Unchanged++;
                else if (source == Sizes.SizeSource.Nodes) m.FromNodes++;
                else if (source == Sizes.SizeSource.Token) m.FromTokens++;
                if (changed && steps >= 2) m.Moved2++;
                if (changed) Moves.Add((part, from, to, steps));
                if (changed && docking) Docking.Add($"{part} {from} -> {to}");
            }

            public void Clear()
            {
                ByMod.Clear();
                Moves.Clear();
                Docking.Clear();
            }
        }

        public static IEnumerable<string> SizeSummary(SizeTally t, int top)
        {
            foreach (var kv in t.ByMod.OrderBy(k => k.Key, StringComparer.Ordinal))
                yield return SizePrefix + $"{kv.Key}: {kv.Value.FromNodes} from nodes ({kv.Value.Moved2} by 2+ steps), {kv.Value.FromTokens} from tokens, {kv.Value.Unchanged} unchanged";
            var moves = t.Moves.OrderByDescending(m => m.steps).ThenBy(m => m.part, StringComparer.Ordinal).Take(Math.Max(1, top)).ToList();
            if (moves.Count > 0)
                yield return SizePrefix + "largest moves: " + string.Join(", ", moves.Select(m => $"{m.part} {m.from} -> {m.to} ({m.steps})"));
            if (t.Docking.Count > 0)
                yield return SizePrefix + "docking parts relabelled: " + string.Join(", ", t.Docking);
        }

        public static string SizeArmed => SizePrefix + "labels from stack nodes armed (ksp1_size_from_nodes)";
        public static string SizeOff => SizePrefix + "labels from stack nodes off (ksp1_size_from_nodes false)";
        public static string SizeMemberMissing(string member) => SizePrefix + member + " not found: Redux's size labels kept";
        public static string SizeStopped(string error) => SizePrefix + "labels stopped for this session: " + error;

        // ---------------------------------------------------------------- rescale

        public sealed class RescaleTally
        {
            public int Defaulted, Excluded, NodeScaled, ModelPositions, NodeCountMismatch;

            public void Clear() => Defaulted = Excluded = NodeScaled = ModelPositions = NodeCountMismatch = 0;

            public bool Any => Defaulted + Excluded + NodeScaled + ModelPositions + NodeCountMismatch > 0;
        }

        public static IEnumerable<string> RescaleSummary(RescaleTally t)
        {
            yield return RescalePrefix + $"{t.Defaulted} part(s) without rescaleFactor set to 1.25, {t.Excluded} excluded (KerbalEVA, FlagSite)";
            yield return RescalePrefix + $"node scale on {t.NodeScaled} part(s)" +
                         (t.NodeCountMismatch == 0 ? "" : $", {t.NodeCountMismatch} skipped (node count mismatch)");
            yield return RescalePrefix + $"MODEL position x rescaleFactor on {t.ModelPositions} model(s)";
        }

        public static string RescaleArmed(string key) => RescalePrefix + key + " armed";
        public static string RescaleOff(string key) => RescalePrefix + key + " off";
        public static string RescaleMemberMissing(string member, string key) => RescalePrefix + member + " not found: " + key + " off";
        public static string RescaleStopped(string key, string error) => RescalePrefix + key + " stopped for this session: " + error;

        public static string NodeCountMismatch(string part, int values, int nodes) =>
            RescalePrefix + $"{part}: {values} node value(s) in the cfg, {nodes} node(s) parsed: node scale skipped";
    }
}
