using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Science;
using KSP1PartMod.Native.Wheels;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>The native import hook (plan (d)): KSP1 modules that Redux drops as unsupported become KSP2's own modules,
    /// through Redux's own import path.
    /// <list type="bullet">
    /// <item>A postfix on the <c>Ksp1PartModuleImporterRegistry</c> constructor adds our importers to its private
    /// <c>_importers</c> dictionary, each under its KSP1 module name, only when Redux registered none under that name (a
    /// future Redux converter wins, with one line).</item>
    /// <item>Our importers build their module through Redux's private static <c>CreateSerialized(Type, ModuleData,
    /// Ksp1PartImportContext)</c> (bound once), which adds the data to the part and resolves its
    /// <c>PartComponentModule_*</c> as for Redux's own modules. No module type of ours.</item>
    /// </list>
    /// Each converter (<see cref="All"/>) has its own <c>animations.json</c> switch and its own targets; a missing one turns
    /// off only that converter. A missing hook member turns them all off, and Redux's import runs as in 0.7.0. Session-wide,
    /// independent of <c>enabled</c>: armed at pre-initialization, before Redux's import. Never throws into the import.</summary>
    internal static class NativeModules
    {
        public const string HarmonyId = "KSP1PartMod.NativeModules";
        public const string RegistryTypeName = "Redux.Ksp1Import.Modules.Ksp1PartModuleImporterRegistry";
        public const string ContextTypeName = "Redux.Ksp1Import.Modules.Ksp1PartImportContext";
        public const string ImportersField = "_importers";
        public const string CreateSerializedName = "CreateSerialized";

        /// <summary>The hook members, resolved by name against the loaded game.</summary>
        internal sealed class Targets
        {
            public ConstructorInfo Constructor;    // public Ksp1PartModuleImporterRegistry()
            public FieldInfo Importers;            // private Dictionary<string, IKsp1PartModuleImporter> _importers
            public MethodInfo CreateSerialized;    // private static SerializedPartModule (Type, ModuleData, Ksp1PartImportContext)
            public readonly List<string> Missing = new List<string>();
        }

        /// <summary>One KSP1 module converted to a native one: its switch, its own targets and its importer.</summary>
        internal sealed class Converter
        {
            public string Ksp1Module, NativeModule, SwitchKey;
            public Func<AnimationSettings, bool> IsOn;
            /// <summary>The members this converter needs that the game lacks (empty when all are found).</summary>
            public Func<List<string>> MissingMembers;
            /// <summary>Optional: the line of one missing member (null: <see cref="NativeLines.MemberNotFound"/>).</summary>
            public Func<string, string> MissingLine;
            public Func<IKsp1PartModuleImporter> Create;
            /// <summary>The converter's summary line, once at the end of the import.</summary>
            public Func<string> Summary;
            /// <summary>Several summary lines instead of <see cref="Summary"/> (null: <see cref="Summary"/>).</summary>
            public Func<IEnumerable<string>> SummaryLines;
            /// <summary>Optional: run once the hook is armed with this converter (its own patches, with the hook's Harmony).</summary>
            public Action<Harmony> AfterArm;
            public string Pair => NativeLines.Pair(Ksp1Module, NativeModule);
        }

        /// <summary>Every converter, in the order of the log.</summary>
        internal static IReadOnlyList<Converter> All() => new[]
        {
            new Converter
            {
                Ksp1Module = CrossfeedMapping.Ksp1Module,
                NativeModule = CrossfeedMapping.NativeModule,
                SwitchKey = "ksp1_crossfeed_toggle",
                IsOn = s => s.Ksp1CrossfeedToggle,
                MissingMembers = CrossfeedImporter.MissingMembers,
                Create = () => new CrossfeedImporter(),
                Summary = CrossfeedImporter.Summary,
            },
            new Converter
            {
                Ksp1Module = AblatorMapping.Ksp1Module,
                NativeModule = AblatorMapping.NativeModule,
                SwitchKey = "ksp1_heatshields",
                IsOn = s => s.Ksp1Heatshields,
                MissingMembers = HeatshieldImporter.MissingMembers,
                Create = () => new HeatshieldImporter(_settings?.AblatorRateScale ?? 1.0),
                Summary = HeatshieldImporter.Summary,
            },
            new Converter
            {
                Ksp1Module = ConverterMapping.Ksp1Module,
                NativeModule = ConverterMapping.NativeModule,
                SwitchKey = "ksp1_converters",
                IsOn = s => s.Ksp1Converters,
                MissingMembers = ConverterImporter.MissingMembers,
                Create = () => new ConverterImporter(_settings?.IsruHeatScale ?? 1.0, _settings?.IsruKsp1Rates ?? false),
                SummaryLines = ConverterImporter.SummaryLines,
                AfterArm = h => ConverterImporter.ArmNames(h),
            },
            new Converter
            {
                Ksp1Module = HarvesterMapping.Ksp1Module,
                NativeModule = HarvesterMapping.NativeModule,
                SwitchKey = "ksp1_drills",
                IsOn = s => s.Ksp1Drills,
                MissingMembers = HarvesterImporter.MissingMembers,
                Create = () => new HarvesterImporter(_settings?.HarvestMultiplier ?? HarvesterMapping.DefaultMultiplier,
                    _settings?.IsruHeatScale ?? 1.0, MultiplierIsDefault(_settings)),
                Summary = HarvesterImporter.Summary,
            },
            new Converter
            {
                Ksp1Module = ScannerMapping.SurveyorModule,
                NativeModule = ScannerMapping.NativeModule,
                SwitchKey = "ksp1_survey_scanners",
                IsOn = s => s.Ksp1SurveyScanners,
                MissingMembers = ScannerImporters.MissingMembers,
                Create = () => new SurveyorImporter(),
                SummaryLines = ScannerImporters.SummaryLines,
            },
            new Converter
            {
                Ksp1Module = ScannerMapping.ResourceScannerModule,
                NativeModule = ScannerMapping.NativeModule,
                SwitchKey = "ksp1_resource_scanners",
                IsOn = s => s.Ksp1ResourceScanners,
                MissingMembers = ScannerImporters.MissingMembers,
                Create = () => new ResourceScannerImporter(),
                SummaryLines = ScannerImporters.SummaryLines, // one scanners line for both (the first asks)
            },
            new Converter
            {
                Ksp1Module = RadiatorMapping.Ksp1Module,
                NativeModule = RadiatorMapping.NativeCooler,
                SwitchKey = "ksp1_radiators",
                IsOn = s => s.Ksp1Radiators,
                MissingMembers = RadiatorImporter.MissingMembers,
                Create = () => new RadiatorImporter(_settings?.RadiatorFluxScale ?? 1.0, _settings?.RadiatorKsp1Rates ?? false,
                    _settings?.RadiatorAutoOn ?? true),
                SummaryLines = RadiatorImporter.SummaryLines,
            },
            Science(ScienceMapping.StockModule),
            Science(ScienceMapping.DMagicModule),
            Science(ScienceMapping.DMagicGenericModule),
            new Converter
            {
                Ksp1Module = ScienceMapping.ContainerModule,
                NativeModule = ScienceImporter.StorageTarget,
                SwitchKey = ScienceImporter.ContainerSwitches,
                IsOn = s => s.Ksp1Science && s.ScienceReportStorage,
                MissingMembers = ScienceImporter.MissingStorageMembers,
                Create = () => new ScienceImporter(_settings, container: true),
                SummaryLines = ScienceImporter.SummaryLines, // one science line for the four (the first asks)
            },
            Wheel(WheelKind.Base),
            Wheel(WheelKind.Suspension),
            Wheel(WheelKind.Steering),
            Wheel(WheelKind.Motor),
            Wheel(WheelKind.MotorSteering),
            Wheel(WheelKind.Brakes),
            Wheel(WheelKind.Damage),
            Wheel(WheelKind.Lock),
            Wheel(WheelKind.Bogey),
        };

        /// <summary>A KSP1 wheel module → the KSP2 wheel module of the same role (wheels plan, Task 3): <c>ksp1_wheels</c> and
        /// the kind's own switch. The nine share the summary lines (the first asks).</summary>
        private static Converter Wheel(WheelKind kind) => new Converter
        {
            Ksp1Module = WheelMapping.Ksp1Module(kind),
            NativeModule = WheelMapping.NativeModule(kind),
            SwitchKey = WheelImporter.SwitchKey(kind),
            IsOn = s => WheelImporter.IsOn(s, kind),
            MissingMembers = () => WheelImporter.MissingMembers(kind),
            MissingLine = WheelImporter.MissingLine(kind),
            Create = () => new WheelImporter(kind, _settings),
            SummaryLines = WheelImporter.SummaryLines,
            // The suspension's collider check needs each part's cfg file (Task 3 review, F6).
            AfterArm = kind == WheelKind.Suspension ? WheelImporter.ArmTranslatorHook : (Action<Harmony>)null,
        };

        /// <summary>A KSP1 experiment module → <c>Module_ScienceExperiment</c> (<c>ksp1_science</c>); the three share one importer type.</summary>
        private static Converter Science(string ksp1Module) => new Converter
        {
            Ksp1Module = ksp1Module,
            NativeModule = ScienceMapping.NativeModule,
            SwitchKey = "ksp1_science",
            IsOn = s => s.Ksp1Science,
            MissingMembers = ScienceImporter.MissingMembers,
            Create = () => new ScienceImporter(_settings, container: false),
            SummaryLines = ScienceImporter.SummaryLines,
        };

        private static bool MultiplierIsDefault(AnimationSettings s) =>
            s == null || Math.Abs(s.HarvestMultiplier - HarvesterMapping.DefaultMultiplier) < 1e-12;

        /// <summary>By part name: the native modules our importers added this session (read by the audit), the notes
        /// logged, and the summary flags.</summary>
        internal static readonly NativeRecord Record = new NativeRecord();

        /// <summary>By part name: the drills' impact data and the scanner parts, for the ISRU runtime (Task 4).</summary>
        internal static readonly IsruRecord Isru = new IsruRecord();

        /// <summary>Tests: receives every line instead of the game log.</summary>
        internal static Action<string> Sink;

        private static Func<Type, ModuleData, Ksp1PartImportContext, SerializedPartModule> _createSerialized;
        private static FieldInfo _importers;
        private static List<Converter> _armed = new List<Converter>();
        private static readonly HashSet<string> RejectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _broken, _summaryLogged;
        /// <summary>The settings of the arming (<c>ablator_rate_scale</c> for the heat shield importers).</summary>
        private static AnimationSettings _settings;

        /// <summary>The hook is in place and has not failed.</summary>
        internal static bool IsArmed => _armed.Count > 0 && !_broken;

        /// <summary>The KSP1 module names our importers are offered for this session.</summary>
        internal static IReadOnlyList<string> ArmedModules => _armed.Select(c => c.Ksp1Module).ToList();

        /// <summary>The converter of <paramref name="ksp1Module"/> is armed and the hook has not failed.</summary>
        internal static bool IsConverterArmed(string ksp1Module) =>
            !_broken && _armed.Any(c => string.Equals(c.Ksp1Module, ksp1Module, StringComparison.OrdinalIgnoreCase));

        internal static Targets Resolve()
        {
            var t = new Targets();
            var registry = AccessTools.TypeByName(RegistryTypeName);
            var context = AccessTools.TypeByName(ContextTypeName);
            if (registry == null) t.Missing.Add(RegistryTypeName);
            if (context == null) t.Missing.Add(ContextTypeName);
            if (registry == null || context == null) return t;

            t.Constructor = AccessTools.Constructor(registry, Type.EmptyTypes);
            if (t.Constructor == null) t.Missing.Add("Ksp1PartModuleImporterRegistry()");

            t.Importers = AccessTools.Field(registry, ImportersField);
            if (t.Importers == null || t.Importers.IsStatic ||
                !typeof(IDictionary<string, IKsp1PartModuleImporter>).IsAssignableFrom(t.Importers.FieldType))
            {
                t.Importers = null;
                t.Missing.Add("Ksp1PartModuleImporterRegistry." + ImportersField);
            }

            t.CreateSerialized = AccessTools.Method(registry, CreateSerializedName, new[] { typeof(Type), typeof(ModuleData), context });
            if (t.CreateSerialized == null || !t.CreateSerialized.IsStatic || t.CreateSerialized.ReturnType != typeof(SerializedPartModule))
            {
                t.CreateSerialized = null;
                t.Missing.Add("Ksp1PartModuleImporterRegistry." + CreateSerializedName + "(Type, ModuleData, Ksp1PartImportContext)");
            }
            return t;
        }

        /// <summary>Arms the converters switched on whose targets resolve. Returns true when at least one is armed.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            var wanted = new List<Converter>();
            foreach (var c in All())
            {
                if (!c.IsOn(settings))
                {
                    Info(NativeLines.Off(c.Pair, c.SwitchKey));
                    continue;
                }
                List<string> missing;
                try
                {
                    missing = c.MissingMembers();
                }
                catch (Exception e)
                {
                    missing = new List<string> { c.NativeModule + " (" + e.GetType().Name + ": " + e.Message + ")" };
                }
                if (missing.Count > 0)
                {
                    foreach (var m in missing) Warn(c.MissingLine != null ? c.MissingLine(m) : NativeLines.MemberNotFound(m, c.NativeModule));
                    continue;
                }
                wanted.Add(c);
            }
            if (wanted.Count == 0)
            {
                Info(NativeLines.NoneArmed);
                return false;
            }

            var t = Resolve();
            if (t.Missing.Count > 0)
            {
                foreach (var m in t.Missing) Warn(NativeLines.MemberNotFound(m, "no native module"));
                return false;
            }
            try
            {
                _createSerialized = (Func<Type, ModuleData, Ksp1PartImportContext, SerializedPartModule>)Delegate.CreateDelegate(
                    typeof(Func<Type, ModuleData, Ksp1PartImportContext, SerializedPartModule>), t.CreateSerialized);
                _importers = t.Importers;
                _settings = settings;
                _armed = wanted;
                harmony.Patch(t.Constructor, postfix: new HarmonyMethod(typeof(NativeModules), nameof(AfterRegistryConstructor)));
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(HarmonyId);
                _armed = new List<Converter>();
                _createSerialized = null;
                Error(NativeLines.PatchFailed(e.ToString()));
                return false;
            }
            Info(NativeLines.Armed(wanted.Select(c => c.Pair)));
            foreach (var c in wanted)
            {
                if (c.AfterArm == null) continue;
                try
                {
                    c.AfterArm(harmony);
                }
                catch (Exception e)
                {
                    Error(NativeLines.Prefix + c.Ksp1Module + " extra patches failed: " + e);
                }
            }
            return true;
        }

        /// <summary>Back to the state before any arming (tests).</summary>
        internal static void Reset()
        {
            _armed = new List<Converter>();
            _createSerialized = null;
            _importers = null;
            _settings = null;
            _broken = _summaryLogged = false;
            HeatshieldImporter.ResetReference();
            ConverterImporter.ResetSession();
            HarvesterImporter.ResetSession();
            ScannerImporters.ResetSession();
            IsruReference.Reset();
            RadiatorImporter.ResetSession();
            RadiatorAccess.Reset();
            ScienceImporter.ResetSession();
            ScienceAccess.Reset();
            WheelImporter.ResetSession();
            WheelAccess.Reset();
            RejectedNames.Clear();
            Record.Clear();
            Isru.Clear();
        }

        /// <summary>Every registry Redux builds (one per translator) gets our importers where its name is free.</summary>
        private static void AfterRegistryConstructor(object __instance)
        {
            if (_broken || __instance == null) return;
            try
            {
                var importers = (IDictionary<string, IKsp1PartModuleImporter>)_importers.GetValue(__instance);
                if (importers == null) return;
                foreach (var c in _armed)
                {
                    if (importers.ContainsKey(c.Ksp1Module))
                    {
                        if (RejectedNames.Add(c.Ksp1Module)) Info(NativeLines.AlreadyRedux(c.Ksp1Module));
                        continue;
                    }
                    importers[c.Ksp1Module] = Perf.LoadTimeline.Wrap(c.Create()); // itself unless perf_load_timeline
                }
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>Redux's own <c>CreateSerialized</c>: adds <paramref name="data"/> to the part's module data.</summary>
        internal static SerializedPartModule CreateSerialized(Type behaviourType, ModuleData data, Ksp1PartImportContext context) =>
            _createSerialized(behaviourType, data, context);

        /// <summary>Logs <paramref name="note"/> once for <paramref name="part"/>.</summary>
        internal static void Note(string part, string note)
        {
            if (Record.AddNote(part, note)) Info(NativeLines.PartNote(part, note));
        }

        /// <summary>One converter failed on one part: logged once per part, Redux's 0.7.0 result kept for it.</summary>
        internal static void ImporterFailed(string part, string ksp1Module, string error)
        {
            if (Record.AddNote(part, "failed:" + ksp1Module)) Error(NativeLines.ImporterFailed(part, ksp1Module, error));
        }

        internal static void ImporterFailed(string part, string ksp1Module, Exception e)
        {
            if (Record.AddNote(part, "failed:" + ksp1Module)) Error(NativeLines.ImporterFailed(part, ksp1Module, e.ToString()));
        }

        /// <summary>Once per session, after Redux's import is complete: each armed converter's summary line.</summary>
        public static void LogImportSummaryOnce()
        {
            if (_armed.Count == 0 || _summaryLogged) return;
            _summaryLogged = true;
            foreach (var c in _armed)
            {
                try
                {
                    if (c.SummaryLines != null)
                        foreach (var line in c.SummaryLines()) Info(line);
                    else Info(c.Summary());
                }
                catch (Exception e)
                {
                    Error(NativeLines.Prefix + c.Ksp1Module + " summary failed: " + e);
                }
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            Error(NativeLines.PatchFailed(e.ToString()));
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
