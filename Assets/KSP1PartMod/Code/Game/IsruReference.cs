using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Game;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Native;
using Redux.Managers;
using Redux.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>The native ISRU references (converters and drills plan, Task 3), read once per session before the first KSP1
    /// drill, converter or scanner (the <see cref="HeatshieldImporter.Reference"/> pattern):
    /// <list type="bullet">
    /// <item>from the game's KSP2 parts (no <c>+</c> variant, no <c>KSP1</c> tag): every <c>Data_Mine</c> formula, every
    /// <c>Data_ResourceConverter</c> formula and every <c>Data_ResourceScanner</c> → <see cref="NativeIsruRef"/>. The native
    /// converters' median heat per EC/s becomes <see cref="ConverterImporter.HeatPerEc"/>;</item>
    /// <item>the minable set: the resources with at least one chart in Redux's <c>ISRUResourceManager.CbResources</c>
    /// (body → resource → chart), else the native drill's outputs, else the 26w42c fallback.</item>
    /// </list>
    /// Both are logged once. A read that throws logs one line and uses the fallback. Never throws.</summary>
    internal static class IsruReference
    {
        /// <summary>Tests: the native formulas instead of the game's parts.</summary>
        internal static Func<IEnumerable<NativeIsruFormula>> FormulaSource;
        /// <summary>Tests: the (body, resource) pairs with a chart instead of Redux's <c>CbResources</c>.</summary>
        internal static Func<IEnumerable<(string Body, string Resource)>> MapSource;

        private static readonly object Lock = new object();
        private static NativeIsruRef _reference;
        private static IReadOnlyList<string> _minable;
        private static MinableSource _minableSource;
        private static string _drillFormulaKey;
        private static readonly Dictionary<string, string> MineKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The reference, read and logged on the first call of the session.</summary>
        internal static NativeIsruRef Get()
        {
            lock (Lock)
            {
                if (_reference != null) return _reference;
                List<NativeIsruFormula> formulas;
                try
                {
                    formulas = (FormulaSource ?? GameFormulas)().Where(f => f != null).ToList();
                }
                catch (Exception e)
                {
                    NativeModules.Error(NativeLines.Prefix + "isru reference not read, fallback used: " + e);
                    formulas = new List<NativeIsruFormula>();
                }
                _reference = NativeIsruRef.From(formulas);
                if (!_reference.Drill.IsFallback) MineKeys.TryGetValue(_reference.Drill.Part, out _drillFormulaKey);
                var db = ConverterImporter.CurrentDatabase();
                foreach (var line in IsruLines.IsruReference(_reference, db == null ? null : new Func<string, double>(n => IsruAccess.MassPerUnit(db, n))))
                    NativeModules.Info(line);
                ReadMinable(_reference);
                return _reference;
            }
        }

        /// <summary>The minable KSP2 resources (read with the reference).</summary>
        internal static IReadOnlyList<string> Minable()
        {
            Get();
            lock (Lock) return _minable;
        }

        internal static MinableSource MinableFrom
        {
            get
            {
                Get();
                lock (Lock) return _minableSource;
            }
        }

        /// <summary>The native drill's <c>FormulaLocalizationKey</c> (its tooltip title), null when no KSP2 drill was read.</summary>
        internal static string DrillFormulaKey()
        {
            Get();
            lock (Lock) return _drillFormulaKey;
        }

        internal static void Reset()
        {
            lock (Lock)
            {
                _reference = null;
                _minable = null;
                _drillFormulaKey = null;
                MineKeys.Clear();
            }
            FormulaSource = null;
            MapSource = null;
        }

        private static void ReadMinable(NativeIsruRef reference)
        {
            List<(string Body, string Resource)> charts;
            try
            {
                charts = (MapSource ?? GameCharts)().Where(c => !string.IsNullOrEmpty(c.Body) && !string.IsNullOrEmpty(c.Resource)).ToList();
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "resource maps not read: " + e);
                charts = new List<(string, string)>();
            }
            var maps = charts.GroupBy(c => c.Resource, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(c => c.Body).Distinct(StringComparer.Ordinal).OrderBy(b => b, StringComparer.Ordinal).ToList()))
                .ToList();
            if (maps.Count > 0)
            {
                _minable = maps.Select(m => m.Item1).ToList();
                _minableSource = MinableSource.ResourceMaps;
            }
            else if (!reference.Drill.IsFallback && reference.MinableFromDrill.Count > 0)
            {
                _minable = reference.MinableFromDrill;
                _minableSource = MinableSource.NativeDrill;
            }
            else
            {
                _minable = NativeIsruRef.FallbackMinable;
                _minableSource = MinableSource.Fallback;
            }
            foreach (var line in IsruLines.Minable(_minable, _minableSource, maps)) NativeModules.Info(line);
        }

        /// <summary>Redux's loaded charts: every (body, resource) with a chart.</summary>
        private static IEnumerable<(string Body, string Resource)> GameCharts()
        {
            var all = ISRUResourceManager.CbResources;
            if (all == null) yield break;
            foreach (var body in all.ToList())
            {
                if (body.Value == null) continue;
                foreach (var chart in body.Value.ToList())
                    if (chart.Value != null) yield return (body.Key, chart.Key);
            }
        }

        private static IEnumerable<(string, double)> Flows(IEnumerable<PartModuleResourceSetting> settings) =>
            (settings ?? Enumerable.Empty<PartModuleResourceSetting>()).Select(s => (s.ResourceName, (double)s.Rate));

        /// <summary>The ISRU formulas of the game's KSP2 parts (not KSP1 imports, not variants with '+').</summary>
        private static IEnumerable<NativeIsruFormula> GameFormulas()
        {
            var parts = GameManager.Instance?.Game?.Parts?.PartData;
            return parts == null ? Enumerable.Empty<NativeIsruFormula>() : FormulasOf(parts.ToList());
        }

        /// <summary>The ISRU formulas of <paramref name="parts"/> (part name → <c>PartCore</c>), the KSP1 imports and the
        /// variants with '+' left out. Tests: real <c>PartCore</c>s.</summary>
        internal static IEnumerable<NativeIsruFormula> FormulasOf(IEnumerable<KeyValuePair<string, PartCore>> parts)
        {
            foreach (var kv in parts)
            {
                var core = kv.Value;
                if (kv.Key == null || kv.Key.Contains('+') || core?.data == null) continue;
                if (Ksp1Tags.IsKsp1Import(core.data.tags)) continue;
                var part = core.data.partName ?? kv.Key;
                foreach (var data in DataOf(core))
                {
                    if (data is Data_Mine mine && mine.MiningFormulaDefinition != null)
                    {
                        var f = mine.MiningFormulaDefinition;
                        if (!MineKeys.ContainsKey(part)) MineKeys[part] = f.FormulaLocalizationKey;
                        yield return NativeIsruFormula.Mine(part, Flows(f.InputResources), Flows(f.OutputResources), f.FluxGenerated);
                    }
                    else if (data is Data_ResourceConverter converter && converter.FormulaDefinitions != null)
                    {
                        foreach (var f in converter.FormulaDefinitions.Where(f => f != null))
                            yield return NativeIsruFormula.Converter(part, f.InternalName, Flows(f.InputResources), Flows(f.OutputResources), f.FluxGenerated);
                    }
                    else if (data is Data_ResourceScanner scanner)
                    {
                        yield return NativeIsruFormula.Scanner(part, Flows(scanner.RequiredResources),
                            (scanner.ScannableResources ?? new List<PartModuleResourceSetting>()).Select(s => s.ResourceName),
                            scanner.TimeToComplete, scanner.minAltitude, scanner.maxAltitude, scanner.minInclination, scanner.maxInclination);
                    }
                }
            }
        }

        /// <summary>The part's module data: the live modules, else the serialized ones.</summary>
        internal static IEnumerable<ModuleData> DataOf(PartCore core)
        {
            if (core.modules != null && core.modules.Count > 0)
            {
                foreach (var m in core.modules)
                    if (m != null) yield return m;
                yield break;
            }
            if (core.data.serializedPartModules == null) yield break;
            foreach (var module in core.data.serializedPartModules)
            {
                if (module.ModuleData == null) continue;
                foreach (var data in module.ModuleData)
                    if (data.DataObject != null) yield return data.DataObject;
            }
        }
    }
}
