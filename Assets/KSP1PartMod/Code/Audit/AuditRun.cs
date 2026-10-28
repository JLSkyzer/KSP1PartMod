using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Audit
{
    /// <summary>A part to audit.</summary>
    public readonly struct AuditTarget
    {
        public AuditTarget(string name, bool isControl)
        {
            Name = name;
            IsControl = isControl;
        }

        public string Name { get; }
        public bool IsControl { get; }
    }

    /// <summary>Frame budget, unload and progress cadence. The clock is injected: the caller passes the time spent.</summary>
    public sealed class AuditPacer
    {
        private readonly double _budgetMs;
        private readonly int _unloadEvery;
        private readonly int _total;

        public const int ProgressEvery = 100;

        public AuditPacer(double frameBudgetMs, int unloadEvery, int totalParts)
        {
            if (unloadEvery < 1) throw new ArgumentOutOfRangeException(nameof(unloadEvery), "must be 1 or more");
            _budgetMs = frameBudgetMs;
            _unloadEvery = unloadEvery;
            _total = totalParts;
        }

        /// <summary>True while the frame has time left, and always for the first part of the frame.</summary>
        public bool ShouldContinue(double elapsedMsThisFrame, int processedThisFrame) =>
            processedThisFrame <= 0 || elapsedMsThisFrame < _budgetMs;

        public bool ShouldUnload(int processedTotal) => processedTotal > 0 && processedTotal % _unloadEvery == 0;

        public bool ShouldLogProgress(int processedTotal) =>
            processedTotal > 0 && (processedTotal % ProgressEvery == 0 || processedTotal == _total);
    }

    public static class AuditResume
    {
        /// <summary>The parts left: not already reported, not on the skip list, distinct by name, controls first, each
        /// group in ordinal name order.</summary>
        public static IReadOnlyList<AuditTarget> Pending(IEnumerable<AuditTarget> allParts, IEnumerable<string> alreadyReported,
            IEnumerable<string> skipList)
        {
            var result = new List<AuditTarget>();
            if (allParts == null) return result;
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (alreadyReported != null) foreach (var n in alreadyReported) excluded.Add(n);
            if (skipList != null) foreach (var n in skipList) excluded.Add(n);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in allParts)
                if (t.Name != null && !excluded.Contains(t.Name) && seen.Add(t.Name)) result.Add(t);
            return result.OrderByDescending(t => t.IsControl).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>The part named by a breadcrumb file: its first non-blank line, trimmed; null for none.</summary>
        public static string FromBreadcrumb(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0) return t;
            }
            return null;
        }
    }

    public static class AuditTextureEviction
    {
        /// <summary>Keys present now and absent at the start (ordinal-ignore-case, as Redux's cache), in the order of
        /// <paramref name="keysNow"/>, one per key.</summary>
        public static IReadOnlyList<string> KeysToEvict(IEnumerable<string> keysAtStart, IEnumerable<string> keysNow)
        {
            var result = new List<string>();
            if (keysNow == null) return result;
            var start = new HashSet<string>(keysAtStart ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in keysNow)
                if (k != null && !start.Contains(k) && seen.Add(k)) result.Add(k);
            return result;
        }
    }
}
