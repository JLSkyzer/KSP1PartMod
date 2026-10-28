using System;
using System.Collections.Generic;

namespace KSP1PartMod.Native
{
    /// <summary>The ground probe of a converted KSP1 drill (plan (j)): the cfg's <c>ImpactTransform</c> and
    /// <c>ImpactRange</c>, which the runtime (Task 4) turns into <c>Module_Mine</c>'s two arm transforms.</summary>
    public sealed class DrillImpact
    {
        public string Transform { get; }
        public double Range { get; }
        /// <summary>The KSP2 resources the drill mines, in formula order.</summary>
        public IReadOnlyList<string> Resources { get; }

        public DrillImpact(string transform, double range, IReadOnlyList<string> resources)
        {
            Transform = transform ?? "";
            Range = range;
            Resources = resources ?? Array.Empty<string>();
        }
    }

    /// <summary>What the drill and scanner importers built this session, by part name, for the runtime (Task 4), beside
    /// <see cref="NativeRecord"/>: the impact data of each drill, and the parts that got a native scanner. A rebuild of a part
    /// replaces its entry. Thread-safe. Pure.</summary>
    public sealed class IsruRecord
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, DrillImpact> _drills = new Dictionary<string, DrillImpact>(StringComparer.Ordinal);
        private readonly HashSet<string> _scanners = new HashSet<string>(StringComparer.Ordinal);

        public void SetDrill(string part, DrillImpact impact)
        {
            if (part == null || impact == null) return;
            lock (_lock) _drills[part] = impact;
        }

        public bool TryGetDrill(string part, out DrillImpact impact)
        {
            impact = null;
            if (part == null) return false;
            lock (_lock) return _drills.TryGetValue(part, out impact);
        }

        public void AddScanner(string part)
        {
            if (part == null) return;
            lock (_lock) _scanners.Add(part);
        }

        public bool IsScanner(string part)
        {
            if (part == null) return false;
            lock (_lock) return _scanners.Contains(part);
        }

        public int DrillCount
        {
            get { lock (_lock) return _drills.Count; }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _drills.Clear();
                _scanners.Clear();
            }
        }
    }
}
