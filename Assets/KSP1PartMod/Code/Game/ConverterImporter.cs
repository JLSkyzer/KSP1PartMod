using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 <c>ModuleResourceConverter</c>s → one KSP2 <c>Module_ResourceConverter</c> per part
    /// (<c>ksp1_converters</c>, plan (j)): every KSP1 converter of the part becomes one mode (formula) of the native module,
    /// in cfg order (<see cref="ConverterMapping"/>). Names through Redux's <c>MapResourceName</c> (after the Ore remap),
    /// rates through Redux's own <c>ConvertAmount</c> (<see cref="IsruAccess"/>), as the containers, then scaled to the KSP2
    /// converter's consumption per EC/s (<see cref="ConverterPace"/>; <c>isru_ksp1_rates</c> keeps the KSP1 rates).
    /// <list type="bullet">
    /// <item>the first call of a part's import context builds the module from <c>context.PartNode</c>; the node it was
    /// called with replaces its entry (B9's scratch re-import, plan (l)); later calls in that context return false, silently;</item>
    /// <item>a context that already holds a <c>Data_ResourceConverter</c> (a future Redux importer) gets nothing (noted);</item>
    /// <item>mode names: the term <c>Parts/Ksp1Converter/&lt;part&gt;/&lt;i&gt;</c>, which a postfix on Redux's
    /// <c>Ksp1PartLocalizationBuilder.AddPart</c> registers with the English text Redux resolves (<see cref="ArmNames"/>);
    /// when that postfix is not armed, the raw <c>ConverterName</c> is the key.</item>
    /// </list>
    /// Any failure leaves the part as Redux 0.9.0 imports it (the modules dropped, nothing of ours left on the part).</summary>
    internal sealed class ConverterImporter : IKsp1PartModuleImporter
    {
        private const string FlagNotReproduced = "converter:nr:";

        private readonly double _heatScale;
        private readonly bool _ksp1Rates;

        /// <summary>Tests: the resource database (returning null: none). Null: the game's (<see cref="IsruAccess.Database"/>).</summary>
        internal static Func<ResourceDefinitionDatabase> DatabaseSource;
        /// <summary>The native converters' median heat per EC/s, read from the game's parts (<see cref="IsruReference"/>, its
        /// 26w42c fallback when none is read). Tests may replace it.</summary>
        internal static Func<double> HeatPerEc = DefaultHeatPerEc;

        private static double DefaultHeatPerEc() => IsruReference.Get().ConverterHeatPerEc;

        /// <summary>The native converter formulas the modes' pace comes from (<see cref="ConverterPace"/>), read from the game's
        /// parts (<see cref="IsruReference"/>, its 26w42c fallback when none is read). Tests may replace it.</summary>
        internal static Func<IReadOnlyList<NativeIsruFormula>> NativeConverters = DefaultNativeConverters;

        private static IReadOnlyList<NativeIsruFormula> DefaultNativeConverters() => IsruReference.Get().ConverterFormulas;
        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the conversion is counted (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;

        private sealed class PartStats
        {
            public int Modes, Dropped;
            public bool Several;
        }

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, object> Seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        private static readonly object SeenMark = new object();
        private static readonly Dictionary<string, PartStats> Stats = new Dictionary<string, PartStats>(StringComparer.Ordinal);
        private static readonly HashSet<string> Terms = new HashSet<string>(StringComparer.Ordinal);
        private static bool _namesArmed, _noDatabaseLogged, _namesFailed;
        private static string _namesMissing;
        private static Action<object, string, string> _add;
        private static System.Reflection.FieldInfo _catalog;

        /// <param name="ksp1Rates"><c>isru_ksp1_rates</c>: the modes keep their KSP1 rates (false: scaled to the KSP2
        /// converter's consumption per EC/s).</param>
        public ConverterImporter(double heatScale, bool ksp1Rates = false)
        {
            _heatScale = heatScale;
            _ksp1Rates = ksp1Rates;
        }

        /// <summary>The members the importer needs; empty when all are found.</summary>
        internal static List<string> MissingMembers() => new List<string>(IsruAccess.Get().MissingConverter);

        /// <summary>The postfix on <c>Ksp1PartLocalizationBuilder.AddPart</c>: true when the mode names will be registered.
        /// A missing member logs one line; the modes then show their raw keys.</summary>
        internal static bool ArmNames(Harmony harmony)
        {
            var t = IsruAccess.Get();
            if (t.MissingNames.Count > 0)
            {
                _namesMissing = string.Join(", ", t.MissingNames); // logged in the summary
                return false;
            }
            try
            {
                var add = t.Add;
                _add = (builder, key, english) => add.Invoke(builder, new object[] { key, english });
                _catalog = t.Catalog;
                harmony.Patch(t.AddPart, postfix: new HarmonyMethod(typeof(ConverterImporter), nameof(AfterAddPart)));
                lock (Lock) _namesArmed = true;
                return true;
            }
            catch (Exception e)
            {
                _namesMissing = "AddPart patch (" + e.GetType().Name + ": " + e.Message + ")"; // logged in the summary
                return false;
            }
        }

        private static bool NamesArmed
        {
            get { lock (Lock) return _namesArmed && !_namesFailed; }
        }

        /// <summary>One row per KSP1 converter of the part, with Redux's own resolution of <c>ConverterName</c>; Redux's
        /// <c>Register</c> then registers them with the part's terms.</summary>
        private static void AfterAddPart(object __instance, Ksp1ConfigNode partNode, string partName)
        {
            if (!NamesArmed || __instance == null || partNode == null || string.IsNullOrEmpty(partName)) return;
            try
            {
                var converters = ConverterNodes(partNode);
                if (converters.Count == 0) return;
                var catalog = _catalog.GetValue(__instance) as Ksp1LocalizationCatalog;
                for (var i = 0; i < converters.Count; i++)
                {
                    var raw = IsruCfg.Value(new Ksp1NodeAdapter(converters[i]), "ConverterName");
                    var english = string.IsNullOrWhiteSpace(raw) ? null : catalog?.Resolve(raw) ?? raw;
                    if (string.IsNullOrWhiteSpace(english)) english = string.IsNullOrWhiteSpace(raw) ? "Converter " + (i + 1) : raw;
                    var key = ConverterMapping.LabelKey(partName, i);
                    _add(__instance, key, english);
                    lock (Lock) Terms.Add(key);
                }
            }
            catch (Exception e)
            {
                bool first;
                lock (Lock)
                {
                    first = !_namesFailed;
                    _namesFailed = true;
                }
                if (first) NativeModules.Error(NativeLines.Prefix + "converter names failed, raw keys shown from now on: " + e);
            }
        }

        private static List<Ksp1ConfigNode> ConverterNodes(Ksp1ConfigNode partNode) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>())
            .Where(n => n != null && string.Equals(n.GetValue("name")?.Trim(), ConverterMapping.Ksp1Module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            try
            {
                if (context == null || moduleNode == null) return false;
                lock (Lock)
                {
                    if (Seen.TryGetValue(context, out _)) return false; // the module was built at the part's first call
                    Seen.Add(context, SeenMark);
                }
                OreRemap.EnsureVerified();
                if (context.ModuleData.Any(d => d is Data_ResourceConverter))
                {
                    NativeModules.Note(part, ConverterMapping.ExistingModuleNote);
                    return false;
                }

                var raw = ConverterNodes(context.PartNode);
                var list = raw.Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
                var at = raw.FindIndex(n => ReferenceEquals(n, moduleNode));
                var called = at >= 0 ? list[at] : new Ksp1NodeAdapter(moduleNode);

                var db = CurrentDatabase();
                var r = Resources(db, null);
                // The pace (the drills' per-EC rule); KSP1 rates with the switch, or without a database (no unit mass, and
                // Redux's ConvertAmount leaves the KSP1 units unconverted).
                IReadOnlyList<NativeIsruFormula> pace = null;
                string kept = null;
                if (_ksp1Rates) kept = "isru_ksp1_rates";
                else if (db == null) kept = "no resource database";
                else pace = NativeConverters();
                var map = ConverterMapping.From(list, 0, called, r, HeatPerEc(), _heatScale, pace, kept);
                var stats = new PartStats { Modes = map.Formulas.Count, Dropped = map.DroppedFormulas, Several = map.SourceCount > 1 };
                if (!map.HasModule)
                {
                    lock (Lock) Stats[part] = stats;
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                    return false;
                }

                var names = NamesArmed;
                var data = new Data_ResourceConverter
                {
                    ConvertByMass = map.ConvertByMass,
                    ResourceAutoShutdown = map.ResourceAutoShutdown,
                    AutoShutdownTemperatureRatio = map.AutoShutdownTemperatureRatio,
                    FormulaDefinitions = map.Formulas.Select(f => new ResourceConverterFormulaDefinition
                    {
                        InternalName = f.Id,
                        FormulaLocalizationKey = names ? ConverterMapping.LabelKey(part, f.Index) : f.LabelSource,
                        InputResources = Settings(f.Inputs),
                        OutputResources = Settings(f.Outputs),
                        FluxGenerated = (float)f.FluxGenerated,
                    }).ToList(),
                };
                // KSP1 runs at full rate (S: a state value set in the definition survives, checked in game).
                data.conversionRate.SetValue((float)map.ConversionRate);
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_ResourceConverter), data, context);

                var record = NativeModules.Record;
                var oreTonnes = OreTonnesPerUnit(r, db);
                var lines = new List<string>(map.Formulas.Count);
                for (var i = 0; i < map.Formulas.Count; i++)
                {
                    var f = map.Formulas[i];
                    var withOre = f.Inputs.Concat(f.Outputs).Any(x => string.Equals(x.Ksp1Name, OreRemap.Key, StringComparison.OrdinalIgnoreCase));
                    lines.Add(IsruLines.ConverterMode(part, f, i, withOre ? oreTonnes : (double?)null));
                }
                FaultForTests?.Invoke(part);
                // The conversion is complete: counted, then logged (a failed part leaves no mode line and no count).
                record.AddConversion(part, ConverterMapping.NativeModule);
                foreach (var field in map.NotReproduced) record.Flag(part, FlagNotReproduced + field);
                lock (Lock) Stats[part] = stats;
                added = -1;
                try
                {
                    foreach (var line in lines)
                        if (record.AddNote(part, line)) NativeModules.Info(line);
                    foreach (var note in map.Notes) NativeModules.Note(part, note);
                }
                catch (Exception e)
                {
                    // Only the log failed: the module stays.
                    NativeModules.Error(NativeLines.Prefix + part + ": converter lines not logged: " + e.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, ConverterMapping.Ksp1Module, IsruLines.ConverterFailed(e.ToString()));
                return false;
            }
        }

        private static List<PartModuleResourceSetting> Settings(IEnumerable<IsruFlow> flows) =>
            flows.Select(x => new PartModuleResourceSetting { ResourceName = x.Name, Rate = (float)x.Rate }).ToList();

        /// <summary>The resource database the ISRU importers read (<see cref="DatabaseSource"/> in tests).</summary>
        internal static ResourceDefinitionDatabase CurrentDatabase()
        {
            if (DatabaseSource != null) return DatabaseSource();
            return IsruAccess.Database();
        }

        /// <summary>Redux's resource rules over <paramref name="db"/> and <paramref name="minable"/> (shared by the ISRU
        /// importers); with no database every name is taken as known (logged once per session).</summary>
        internal static IsruResources Resources(ResourceDefinitionDatabase db, IEnumerable<string> minable)
        {
            if (db == null)
            {
                bool first;
                lock (Lock)
                {
                    first = !_noDatabaseLogged;
                    _noDatabaseLogged = true;
                }
                if (first) NativeModules.Info(IsruLines.NoResourceDatabase);
            }
            return new IsruResources(
                Ksp1ResourceMapper.MapResourceName,
                (source, target, amount) => IsruAccess.ConvertAmount(source, target, amount, db),
                db == null ? null : new Func<string, bool>(name => IsruAccess.Exists(db, name)),
                minable,
                db == null ? null : new Func<string, double>(name => IsruAccess.MassPerUnit(db, name)));
        }

        /// <summary>Redux's mass of one KSP1 Ore unit (t), through its own conversion; null when unknown.</summary>
        private static double? OreTonnesPerUnit(IsruResources r, ResourceDefinitionDatabase db)
        {
            var (name, units) = r.MappedRate(OreRemap.Key, 1);
            var mass = r.MassPerUnit(name);
            if (db == null || double.IsNaN(mass)) return null;
            return units * mass;
        }

        /// <summary>The <c>native: converters:</c> line, then the mode names line.</summary>
        internal static IEnumerable<string> SummaryLines()
        {
            var r = NativeModules.Record;
            int modes, several, dropped, terms;
            lock (Lock)
            {
                var converted = Stats.Where(kv => r.ConvertedModules(kv.Key).Contains(ConverterMapping.NativeModule)).Select(kv => kv.Value).ToList();
                modes = converted.Sum(s => s.Modes);
                several = converted.Count(s => s.Several);
                dropped = Stats.Values.Sum(s => s.Dropped);
                terms = Terms.Count;
            }
            yield return IsruLines.ConvertersSummary(r.PartsWith(ConverterMapping.NativeModule), modes, several, dropped,
                r.FlagsWithPrefix(FlagNotReproduced));
            if (NamesArmed) yield return IsruLines.ConverterNamesAdded(terms);
            else if (_namesMissing != null) yield return IsruLines.ConverterNamesNotRegistered(_namesMissing);
        }

        internal static void ResetSession()
        {
            lock (Lock)
            {
                Stats.Clear();
                Terms.Clear();
                _namesArmed = _noDatabaseLogged = _namesFailed = false;
            }
            _namesMissing = null;
            _add = null;
            _catalog = null;
            DatabaseSource = null;
            FaultForTests = null;
            HeatPerEc = DefaultHeatPerEc;
            NativeConverters = DefaultNativeConverters;
        }
    }
}
