using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The resource side of the ISRU rules (plan (g)), through injected delegates so the rules stay pure:
    /// <list type="bullet">
    /// <item><c>map</c>: KSP1 name → KSP2 name (Redux's <c>Ksp1ResourceMapper.MapResourceName</c> in game, after the Ore remap).
    /// Null, or a null or empty result: the KSP1 name;</item>
    /// <item><c>convert(source, target, amount)</c>: KSP1 units of <c>source</c> → KSP2 units of <c>target</c> (Redux's own
    /// <c>ConvertAmount</c>, bound once by the glue, so a rate moves exactly what the containers hold). Null, or a result
    /// that is not a finite number: the amount unchanged;</item>
    /// <item><c>exists(name)</c>: the KSP2 resource database knows the name. Null: every name is taken as known;</item>
    /// <item>the minable set: the KSP2 resources with at least one density map (ordinal names);</item>
    /// <item><c>massPerUnit(name)</c> (optional): the KSP2 unit mass in tonnes, for the logged mass balance. Null, or not a
    /// finite number of 0 or more: unknown (NaN).</item>
    /// </list>
    /// Pure.</summary>
    public sealed class IsruResources
    {
        public const string ElectricCharge = "ElectricCharge";

        private readonly Func<string, string> _map;
        private readonly Func<string, string, double, double> _convert;
        private readonly Func<string, bool> _exists;
        private readonly Func<string, double> _massPerUnit;
        private readonly HashSet<string> _minable;

        /// <summary>The minable KSP2 resources, in the order given.</summary>
        public IReadOnlyCollection<string> Minable { get; }

        public IsruResources(Func<string, string> map, Func<string, string, double, double> convert, Func<string, bool> exists,
            IEnumerable<string> minable, Func<string, double> massPerUnit = null)
        {
            _map = map;
            _convert = convert;
            _exists = exists;
            _massPerUnit = massPerUnit;
            var list = (minable ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
            _minable = new HashSet<string>(list, StringComparer.Ordinal);
            Minable = new ReadOnlyCollection<string>(list);
        }

        /// <summary>The KSP2 name of a KSP1 resource.</summary>
        public string Map(string ksp1Name)
        {
            var mapped = _map?.Invoke(ksp1Name);
            return string.IsNullOrEmpty(mapped) ? ksp1Name : mapped;
        }

        /// <summary>The KSP2 name and the KSP2 rate of a KSP1 rate (units/s of <paramref name="ksp1Name"/>).</summary>
        public (string Name, double Rate) MappedRate(string ksp1Name, double ksp1Rate)
        {
            var name = Map(ksp1Name);
            if (_convert == null) return (name, ksp1Rate);
            var rate = _convert(ksp1Name, name, ksp1Rate);
            return (name, double.IsNaN(rate) || double.IsInfinity(rate) ? ksp1Rate : rate);
        }

        /// <summary>The KSP2 resource has a density map on at least one body.</summary>
        public bool IsMinable(string ksp2Name) => ksp2Name != null && _minable.Contains(ksp2Name);

        /// <summary>The KSP2 resource exists in the game's database.</summary>
        public bool IsKnown(string ksp2Name) => _exists == null || (ksp2Name != null && _exists(ksp2Name));

        /// <summary>The KSP2 unit mass (t), or NaN when unknown.</summary>
        public double MassPerUnit(string ksp2Name)
        {
            if (_massPerUnit == null || ksp2Name == null) return double.NaN;
            var m = _massPerUnit(ksp2Name);
            return m >= 0 && !double.IsInfinity(m) ? m : double.NaN;
        }

        /// <summary>KSP1's and KSP2's electric charge share this name.</summary>
        public static bool IsElectricCharge(string name) => string.Equals(name, ElectricCharge, StringComparison.OrdinalIgnoreCase);
    }
}
