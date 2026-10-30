using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>Why a KSP1 wheel module got no KSP2 module.</summary>
    public enum WheelRefusal
    {
        /// <summary>A sub-module whose part has no converted base in the same import context.</summary>
        NoBase,
        /// <summary>A second base, or a second sub-module of a kind, on one part (KSP2 allows one wheel per part).</summary>
        Second,
        /// <summary>Its <c>animations.json</c> switch is off. Pure rules and census only: in game a switched-off kind has no
        /// importer (Redux drops it, and the arming logs the off line), so the summary, which lists non-zero reasons only, never
        /// shows it.</summary>
        SwitchOff,
        /// <summary>The mapping found nothing to convert (no radius, no curve…).</summary>
        Mapping,
        /// <summary>The conversion threw; what it added was removed.</summary>
        Failed,
    }

    /// <summary>One wheel per part (wheels plan (f)): the first <c>ModuleWheelBase</c> builds the base; a sub-module is converted
    /// only after its part's base was converted in the same import context (or in a B9 scratch context, plan (g)), and only once
    /// per kind. Pure.</summary>
    public static class WheelPartRule
    {
        public static bool ShouldConvertSub(bool baseConvertedInContext, bool scratchContext, bool kindAlreadyOnPart) =>
            (baseConvertedInContext || scratchContext) && !kindAlreadyOnPart;

        public static bool IsFirstBase(int baseIndex, int firstBaseIndex) => baseIndex == firstBaseIndex;

        /// <summary>The slot a kind fills on a part: <c>ModuleWheelMotorSteering</c> fills the motor's, because KSP2's
        /// <c>Module_WheelMotorSteering</c> is a <c>Module_WheelMotor</c> (a <c>GetComponent&lt;Module_WheelMotor&gt;</c> finds
        /// either). One module per slot.</summary>
        public static WheelKind Slot(WheelKind kind) => kind == WheelKind.MotorSteering ? WheelKind.Motor : kind;

        /// <summary>The refusal of a sub-module before mapping, or null when it may be converted.</summary>
        public static WheelRefusal? RefuseSub(bool switchOn, bool baseConvertedInContext, bool scratchContext, bool kindAlreadyOnPart)
        {
            if (!switchOn) return WheelRefusal.SwitchOff;
            if (!baseConvertedInContext && !scratchContext) return WheelRefusal.NoBase;
            if (kindAlreadyOnPart) return WheelRefusal.Second;
            return null;
        }

        /// <summary>The refusal of a base before mapping, or null when it may be converted.</summary>
        public static WheelRefusal? RefuseBase(bool switchOn, int baseIndex, int firstBaseIndex)
        {
            if (!switchOn) return WheelRefusal.SwitchOff;
            if (!IsFirstBase(baseIndex, firstBaseIndex)) return WheelRefusal.Second;
            return null;
        }

        /// <summary>The label of a refusal in the log lines.</summary>
        public static string Label(WheelRefusal r)
        {
            switch (r)
            {
                case WheelRefusal.NoBase: return "no base";
                case WheelRefusal.Second: return "second";
                case WheelRefusal.SwitchOff: return "switch off";
                case WheelRefusal.Failed: return "failed";
                default: return "mapping";
            }
        }
    }

    /// <summary>What one part got: the kinds converted, in order, and those refused with their reason.</summary>
    public sealed class WheelPartSummary
    {
        private readonly List<WheelKind> _converted = new List<WheelKind>();
        private readonly List<(WheelKind Kind, WheelRefusal Reason)> _refused = new List<(WheelKind, WheelRefusal)>();

        public string Part { get; }
        public IReadOnlyList<WheelKind> Converted => _converted;
        public IReadOnlyList<(WheelKind Kind, WheelRefusal Reason)> Refused => _refused;
        public bool BaseConverted => _converted.Contains(WheelKind.Base);

        public WheelPartSummary(string part) { Part = part; }

        public bool Has(WheelKind kind) => _converted.Contains(kind);

        /// <summary>A kind of the same slot (<see cref="WheelPartRule.Slot"/>) is already converted.</summary>
        public bool HasSlot(WheelKind kind) => _converted.Any(k => WheelPartRule.Slot(k) == WheelPartRule.Slot(kind));

        public void Convert(WheelKind kind)
        {
            if (!_converted.Contains(kind)) _converted.Add(kind);
        }

        public void Refuse(WheelKind kind, WheelRefusal reason) => _refused.Add((kind, reason));

        /// <summary>The converted kinds as the part line lists them (base first, then cfg order).</summary>
        public string KindsText() => _converted.Count == 0 ? "none" : string.Join(", ", _converted.Select(WheelMapping.Label));
    }

    /// <summary>The counts of the wheel summary line: parts with a base by wheel type, modules per kind, refusals per reason and
    /// notes per field. Pure.</summary>
    public sealed class WheelTally
    {
        private readonly int[] _converted = new int[Enum.GetValues(typeof(WheelKind)).Length];
        private readonly int[] _types = new int[Enum.GetValues(typeof(WheelTypeName)).Length];
        private readonly int[] _refused = new int[Enum.GetValues(typeof(WheelRefusal)).Length];
        private readonly SortedDictionary<string, int> _notReproduced = new SortedDictionary<string, int>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _unused = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public int Converted(WheelKind kind) => _converted[(int)kind];
        public int Bases(WheelTypeName type) => _types[(int)type];
        public int Refused(WheelRefusal reason) => _refused[(int)reason];
        public int RefusedTotal => _refused.Sum();
        public IReadOnlyDictionary<string, int> NotReproduced => _notReproduced;
        public IReadOnlyDictionary<string, int> UnusedBySolver => _unused;

        public void AddConverted(WheelKind kind) => _converted[(int)kind]++;

        public void AddBase(WheelTypeName type)
        {
            _converted[(int)WheelKind.Base]++;
            _types[(int)type]++;
        }

        public void AddRefused(WheelRefusal reason) => _refused[(int)reason]++;

        public void AddNotes(IEnumerable<string> notReproduced, IEnumerable<string> unusedBySolver)
        {
            foreach (var f in notReproduced ?? Enumerable.Empty<string>()) _notReproduced[f] = _notReproduced.TryGetValue(f, out var n) ? n + 1 : 1;
            foreach (var f in unusedBySolver ?? Enumerable.Empty<string>()) _unused[f] = _unused.TryGetValue(f, out var n) ? n + 1 : 1;
        }

        /// <summary>The non-zero refusals, in enum order.</summary>
        public IEnumerable<(string Reason, int Count)> RefusedByReason() =>
            Enum.GetValues(typeof(WheelRefusal)).Cast<WheelRefusal>().Where(r => _refused[(int)r] > 0)
                .Select(r => (WheelPartRule.Label(r), _refused[(int)r]));

        public static IEnumerable<(string Field, int Count)> ByCount(IReadOnlyDictionary<string, int> d) =>
            d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value));
    }
}
