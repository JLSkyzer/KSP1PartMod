using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using KSP.Game;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.B9;
using KSP1PartMod.Game.Animation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using VSwift.Modules.Data;
using VSwift.Modules.Transformers;
using VSwift.Modules.Variants;

namespace KSP1PartMod.Game
{
    /// <summary>B9PartSwitch overrides through V-SwiFT (plan 2026-10-28, roadmap 3.5): a subtype's mass, tank resources and
    /// part values become <b>Redux's own</b> transformers (<c>MassModifier</c>, <c>ResourceContainerRemover</c> then
    /// <c>ResourceContainerAdder</c>, <c>PartScalarTransformer</c>), and its module DATA and <c>moduleActive = false</c>
    /// become <c>ModuleDefinitionTransformer</c>s (<see cref="B9ModuleDiff"/>: the edited KSP1 module re-imported by
    /// Redux's importers and diffed), so V-SwiFT applies them in the VAB, saves them and rebuilds them in flight. No type
    /// of ours ever reaches a part definition or a save.
    /// <list type="bullet">
    /// <item>A postfix on <c>Ksp1B9TankCatalog.FromConfigFiles</c> reads the <c>B9_TANK_TYPE</c> roots of the files Redux
    /// imports (first definition wins), with Redux's built-in names as resource-only types.</item>
    /// <item>A postfix on <c>Ksp1B9PartSwitchVariantBuilder.TryBuild</c> plans the part (<see cref="B9OverridePlan"/>),
    /// appends our transformers after Redux's in Redux's variants, and appends new sets for the B9 modules Redux skips;
    /// when Redux built nothing, it creates the <c>Data_PartSwitch</c> and returns true, so Redux's own
    /// <c>CreateSerialized</c> adds <c>Module_PartSwitch</c>.</item>
    /// <item>A postfix on <c>AddLocalizationTerms</c> names the appended sets as Redux names its own.</item>
    /// </list>
    /// The import never breaks: every part is planned and built aside, each transformer is validated alone (a bad one is
    /// dropped with a line), and nothing is written into Redux's result until the whole part is ready; any exception keeps
    /// Redux's result for that part, three stop the feature for the session. Session-wide, independent of <c>enabled</c>:
    /// armed at pre-initialization, before Redux's import.</summary>
    internal static class B9Overrides
    {
        public const string HarmonyId = "KSP1PartMod.B9Overrides";
        public const string BuilderTypeName = "Redux.Ksp1Import.Ksp1B9PartSwitchVariantBuilder";
        public const string CatalogTypeName = "Redux.Ksp1Import.Ksp1B9TankCatalog";
        public const string CatalogTypesField = "_tankTypes";
        private const int MaxFailures = 3;

        /// <summary>The members, resolved by name against the loaded game.</summary>
        internal sealed class Targets
        {
            public MethodInfo TryBuild;              // public static bool (file, Ksp1ConfigNode, float, context, IReadOnlyCollection<string>, out Data_PartSwitch, out List<string>)
            public MethodInfo AddLocalizationTerms;  // public static void (Ksp1ConfigNode, string, Func<string,string>, Action<string,string>)
            public MethodInfo FromConfigFiles;       // public static Ksp1B9TankCatalog (IEnumerable<Ksp1ConfigFile>)
            /// <summary>Redux's catalog by name (built-ins included); optional: without it, no built-in tank type.</summary>
            public FieldInfo CatalogTypes;
            public readonly List<string> Missing = new List<string>();
        }

        /// <summary>One set appended to a part, for its localization terms.</summary>
        internal sealed class AppendedSet
        {
            public string SetId, Label;
            public List<(string VariantId, string Label)> Variants = new List<(string, string)>();
        }

        /// <summary>What one part got (replaced when the part is imported again).</summary>
        internal sealed class PartTally
        {
            public int Sets, NewSets, NewSetsWithModule, MassVariants, ResourceVariants, ScalarVariants, CostVariants;
            public List<AppendedSet> Appended = new List<AppendedSet>();
        }

        /// <summary>What became of one KSP1 module that a part's subtypes edit (module data, Task 3).</summary>
        internal sealed class ModuleOutcome
        {
            public string Ksp1Module;
            public int TargetIndex;
            /// <summary>The native behaviour Redux imports it as, or null.</summary>
            public string Behaviour;
            /// <summary>The DATA keys (and <c>moduleActive</c> for an off form) written by any subtype.</summary>
            public List<string> Keys = new List<string>();
            /// <summary>The native fields at least one variant edits (empty: no native effect, or skipped).</summary>
            public List<string> Fields = new List<string>();
            /// <summary>Skipped: the behaviour appears more than once on the part.</summary>
            public bool Repeated;
            /// <summary>Skipped: the re-import threw.</summary>
            public bool Failed;
            /// <summary>No native effect (counted at the end of the part).</summary>
            public bool NoEffect;
        }

        /// <summary>The module data of one part (replaced when the part is imported again).</summary>
        internal sealed class ModuleTally
        {
            public int Transformers, OffForms, NoOffForm, OffNoChange;
            public readonly Dictionary<string, int> ByField = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> NoOffFormModules = new List<string>();
            public readonly List<ModuleOutcome> Modules = new List<ModuleOutcome>();
        }

        /// <summary>Tests: receives every line instead of the game log.</summary>
        internal static Action<string> Sink;
        /// <summary>Tests: runs on the plan before anything is built (a throw must leave Redux's result as it was).</summary>
        internal static Action<B9OverridePlan> AfterPlanForTests;
        /// <summary>Tests: runs inside <see cref="Commit"/> once the sets are appended (a throw must keep their names).</summary>
        internal static Action CommitAfterSetsForTests;

        private static B9Switches _switches;
        private static volatile B9TankTypes _tankTypes;
        private static FieldInfo _catalogTypes;
        private static bool _armed, _stopped, _summaryLogged;
        private static int _failures;
        private static readonly Dictionary<string, PartTally> Tallies = new Dictionary<string, PartTally>(StringComparer.Ordinal);
        private static readonly HashSet<string> NotesLogged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> NoteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ModuleTally> ModuleTallies = new Dictionary<string, ModuleTally>(StringComparer.Ordinal);
        private static readonly HashSet<string> ModuleTypesLogged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object RegistryLock = new object();
        private static Ksp1PartModuleImporterRegistry _registry;

        internal static bool IsArmed => _armed && !_stopped;

        /// <summary>The module data of <paramref name="part"/> at its last import, or null (tests, audit).</summary>
        internal static ModuleTally ModuleTallyOf(string part) => part != null && ModuleTallies.TryGetValue(part, out var t) ? t : null;

        /// <summary>A scratch registry for the re-imports of module data, built once per import: Redux's constructor, so
        /// 0.8.0's postfix adds our own importers to it as to Redux's.</summary>
        private static Ksp1PartModuleImporterRegistry Registry()
        {
            lock (RegistryLock) return _registry ?? (_registry = new Ksp1PartModuleImporterRegistry());
        }

        /// <summary>The switches the planner uses: the settings' own (<see cref="B9Switches.From"/>), so the appended set
        /// ids are those the gates plan with.</summary>
        internal static B9Switches Effective(AnimationSettings settings) => B9Switches.From(settings);

        internal static Targets Resolve()
        {
            var t = new Targets();
            var builder = AccessTools.TypeByName(BuilderTypeName);
            var catalog = AccessTools.TypeByName(CatalogTypeName);
            if (builder == null) t.Missing.Add(BuilderTypeName);
            if (catalog == null) t.Missing.Add(CatalogTypeName);
            if (builder == null || catalog == null) return t;

            t.TryBuild = AccessTools.Method(builder, "TryBuild", new[]
            {
                typeof(Ksp1ConfigFile), typeof(Ksp1ConfigNode), typeof(float), typeof(Ksp1PartImportContext),
                typeof(IReadOnlyCollection<string>), typeof(Data_PartSwitch).MakeByRefType(), typeof(List<string>).MakeByRefType(),
            });
            if (t.TryBuild == null || !t.TryBuild.IsStatic || t.TryBuild.ReturnType != typeof(bool))
            {
                t.TryBuild = null;
                t.Missing.Add("Ksp1B9PartSwitchVariantBuilder.TryBuild");
            }
            t.AddLocalizationTerms = AccessTools.Method(builder, "AddLocalizationTerms", new[]
            {
                typeof(Ksp1ConfigNode), typeof(string), typeof(Func<string, string>), typeof(Action<string, string>),
            });
            if (t.AddLocalizationTerms == null || !t.AddLocalizationTerms.IsStatic)
            {
                t.AddLocalizationTerms = null;
                t.Missing.Add("Ksp1B9PartSwitchVariantBuilder.AddLocalizationTerms");
            }
            t.FromConfigFiles = AccessTools.Method(catalog, "FromConfigFiles", new[] { typeof(IEnumerable<Ksp1ConfigFile>) });
            if (t.FromConfigFiles == null || !t.FromConfigFiles.IsStatic || t.FromConfigFiles.ReturnType != catalog)
            {
                t.FromConfigFiles = null;
                t.Missing.Add("Ksp1B9TankCatalog.FromConfigFiles");
            }
            var field = AccessTools.Field(catalog, CatalogTypesField);
            if (field != null && !field.IsStatic && typeof(IDictionary<string, Ksp1ConfigNode>).IsAssignableFrom(field.FieldType))
                t.CatalogTypes = field;
            return t;
        }

        /// <summary>Arms the three postfixes when at least one switch applies and every member resolves.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            var s = Effective(settings);
            if (s.ModuleData)
            {
                // Module data needs a few more of Redux's members; without them only module data (and off) is off.
                var members = B9ModuleDiff.Resolve();
                if (!B9ModuleDiff.Bind(members))
                {
                    foreach (var m in members.Missing) Warn(B9Lines.ModuleDataMemberNotFound(m));
                    s.ModuleData = false;
                    s.ModuleActive = false;
                }
            }
            if (!(s.Mass || s.Resources || s.ModuleData || s.PartScalars))
            {
                Info(B9Lines.AllOff);
                return false;
            }
            var t = Resolve();
            if (t.Missing.Count > 0)
            {
                foreach (var m in t.Missing) Warn(B9Lines.MemberNotFound(m));
                return false;
            }
            try
            {
                _switches = s;
                _catalogTypes = t.CatalogTypes;
                harmony.Patch(t.FromConfigFiles, postfix: new HarmonyMethod(typeof(B9Overrides), nameof(AfterFromConfigFiles)));
                harmony.Patch(t.TryBuild, postfix: new HarmonyMethod(typeof(B9Overrides), nameof(AfterTryBuild)));
                harmony.Patch(t.AddLocalizationTerms, postfix: new HarmonyMethod(typeof(B9Overrides), nameof(AfterAddLocalizationTerms)));
                _armed = true;
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(HarmonyId);
                _armed = false;
                Error(B9Lines.PartFailed("(arming)", e.ToString()));
                return false;
            }
            Info(B9Lines.Armed(s.Mass, s.Resources, s.ModuleData, s.ModuleActive, s.PartScalars, s.NewPartSwitches));
            // Old saves (final review I1): a set missing from a saved part state gets its default, not the first variant.
            B9SavedSets.TryApply(harmony);
            if (t.CatalogTypes == null) Warn(B9Lines.BuiltInTankTypesNotFound("Ksp1B9TankCatalog." + CatalogTypesField));
            return true;
        }

        /// <summary>Back to the state before any arming (tests).</summary>
        internal static void Reset()
        {
            _switches = null;
            _tankTypes = null;
            _catalogTypes = null;
            _armed = _stopped = _summaryLogged = _tankTypesLineLogged = false;
            _failures = 0;
            Tallies.Clear();
            ModuleTallies.Clear();
            NotesLogged.Clear();
            NoteCounts.Clear();
            LastSummary = new List<string>();
            ModuleTypesLogged.Clear();
            AfterPlanForTests = null;
            CommitAfterSetsForTests = null;
            B9ModuleDiff.Reset();
            B9SavedSets.Reset();
            lock (RegistryLock) _registry = null;
        }

        /// <summary>The tank types read at the last <c>FromConfigFiles</c> (tests).</summary>
        internal static B9TankTypes TankTypes => _tankTypes;

        /// <summary>What <paramref name="part"/> got at its last import, or null (tests, audit).</summary>
        internal static PartTally TallyOf(string part) => part != null && Tallies.TryGetValue(part, out var t) ? t : null;

        /// <summary>The <c>b9:</c> end-of-import lines of this session (import summary, module data), for the audit report
        /// header; empty before the summary.</summary>
        internal static IReadOnlyList<string> LastSummary { get; private set; } = new List<string>();

        /// <summary>How many distinct <c>b9: &lt;part&gt;: &lt;note&gt;</c> lines <paramref name="part"/> logged this session
        /// (audit).</summary>
        internal static int NoteCountOf(string part) => part != null && NoteCounts.TryGetValue(part, out var n) ? n : 0;

        /// <summary>The <c>appendSet</c> test the 2b gates of <paramref name="part"/> plan with (B9 overrides, Task 4): the
        /// part's B9 plan again, with this import's switches and tank types, so the appended ids are those the sets got.
        /// Null (the 0.8.0 gates, which stay at their default subtype) when the part got no appended set at its import
        /// (feature off or stopped, <c>b9_new_part_switches</c> false, the part failed or kept Redux's sets), or when an
        /// appended set it got is not in the plan again (logged once). A planned set that got no transformer was not
        /// appended: its gates name a set the part does not have, which keeps the default subtype at runtime.</summary>
        internal static Func<VariantSwitch, bool> GateAppendSet(string part, IReadOnlyList<IKsp1Node> modules)
        {
            var tally = TallyOf(part);
            if (tally == null || tally.Appended.Count == 0 || modules == null) return null;
            var plan = B9OverridePlan.For(modules, _tankTypes, _switches ?? B9Switches.AllOn);
            var planned = new HashSet<string>(plan.Switches.Where(s => s.Appended).Select(s => s.SetId), StringComparer.Ordinal);
            var unknown = tally.Appended.Select(a => a.SetId).FirstOrDefault(id => !planned.Contains(id));
            if (unknown != null)
            {
                LogOnce(part + "\ngates\n", B9Lines.GatesKeptDefault(part, unknown));
                return null;
            }
            return plan.AppendSetTest();
        }

        // ------------------------------------------------------------------ tank types

        /// <summary>Redux's catalog is built (on a worker thread) from the import's files: the same files give our tank
        /// types, read in file order so that the first definition of a name wins (B9's rule).</summary>
        private static void AfterFromConfigFiles(IEnumerable<Ksp1ConfigFile> __0, object __result)
        {
            if (!_armed || _stopped) return;
            try
            {
                var roots = new List<IKsp1Node>();
                foreach (var file in __0 ?? Enumerable.Empty<Ksp1ConfigFile>())
                {
                    if (file?.Node?.Nodes == null) continue;
                    foreach (var node in file.Node.Nodes)
                        if (node != null) roots.Add(new Ksp1NodeAdapter(node));
                }
                _tankTypes = B9TankTypes.From(roots, BuiltIns(__result));
            }
            catch (Exception e)
            {
                _tankTypes = null;
                TankTypesUnavailableOnce(e.ToString());
            }
        }

        private static bool _tankTypesLineLogged;

        /// <summary>Without tank types every tank subtype would read as unknown: tank resources are off instead, with one
        /// line per session; mass, part values and the sets themselves still apply.</summary>
        private static void TankTypesUnavailableOnce(string reason)
        {
            if (_tankTypesLineLogged) return;
            _tankTypesLineLogged = true;
            Error(B9Lines.TankTypesUnavailable(reason));
        }

        /// <summary>Redux's result cannot be matched to the plan for the whole part (not counted as a failure).</summary>
        internal sealed class PlanMismatchException : Exception
        {
            public PlanMismatchException(string message) : base(message) { }
        }

        /// <summary>Every type of Redux's catalog as resource-only (only the names no file defines are used: Redux's
        /// built-ins). Redux reads a <c>percentFilled</c> up to 1 as a fraction; its built-ins say 1 for full, so such a
        /// value becomes a percentage here.</summary>
        private static IEnumerable<B9TankType> BuiltIns(object catalog)
        {
            if (catalog == null || _catalogTypes == null) yield break;
            if (!(_catalogTypes.GetValue(catalog) is IDictionary<string, Ksp1ConfigNode> types)) yield break;
            foreach (var kv in types)
            {
                if (kv.Value == null) continue;
                var resources = new List<B9ResourceDef>();
                foreach (var r in kv.Value.GetNodes("RESOURCE"))
                {
                    var def = B9ResourceDef.From(new Ksp1NodeAdapter(r));
                    if (def?.PercentFilled is double pf && pf > 0 && pf <= 1) def.PercentFilled = pf * 100;
                    if (def != null && !string.IsNullOrEmpty(def.Name)) resources.Add(def);
                }
                yield return B9TankType.ResourceOnly(kv.Key, resources);
            }
        }

        // ------------------------------------------------------------------ the builder postfix

        /// <summary><c>TryBuild(file, partNode, rescaleFactor, context, ignoredTransforms, out data, out baseline)</c>.</summary>
        private static void AfterTryBuild(Ksp1ConfigNode __1, float __2, Ksp1PartImportContext __3, ref bool __result,
            ref Data_PartSwitch __5, ref List<string> __6)
        {
            if (!_armed || _stopped || __1 == null || __3 == null) return;
            var part = __3.PartName ?? __1.GetValue("name") ?? "?";
            Built built = null;
            try
            {
                if (!HasB9(__1)) return;
                built = Build(part, __1, __2, __3, __5);
                if (built == null)
                {
                    Tallies.Remove(part);
                    return;
                }
                // Recorded before Commit writes anything: if Commit throws after appending a set, the set keeps its name.
                Tallies[part] = built.Tally;
                Commit(built, __3, ref __result, ref __5, ref __6);
            }
            catch (PlanMismatchException e)
            {
                // Deterministic for this part (its cfg against Redux's result): not a failure of the feature.
                Tallies.Remove(part);
                ModuleTallies.Remove(part);
                Warn(B9Lines.PlanMismatch(part, e.Message));
            }
            catch (Exception e)
            {
                // Once Commit has written into Redux's result, the part keeps its tally (its appended sets need their names).
                if (built == null || !built.Written)
                {
                    Tallies.Remove(part);
                    ModuleTallies.Remove(part);
                }
                Error(B9Lines.PartFailed(part, e.ToString()));
                if (++_failures >= MaxFailures && !_stopped)
                {
                    _stopped = true;
                    Error(B9Lines.Stopped);
                }
            }
        }

        private static bool HasB9(Ksp1ConfigNode part)
        {
            foreach (var m in part.GetNodes("MODULE"))
                if (string.Equals(m.GetValue("name")?.Trim(), "ModuleB9PartSwitch", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Everything one part gets, built aside from Redux's result.</summary>
        internal sealed class Built
        {
            public List<(Variant Target, List<ITransformer> Add)> ToRedux = new List<(Variant, List<ITransformer>)>();
            public List<(VariantSet Set, string Default)> NewSets = new List<(VariantSet, string)>();
            public bool AddResourceCapacities;
            /// <summary>Set by <see cref="Commit"/> before its first write into Redux's result.</summary>
            public bool Written;
            public PartTally Tally = new PartTally();
            public List<string> Lines = new List<string>();
        }

        /// <summary>Plans and builds the part's transformers without touching <paramref name="data"/>; null when there is
        /// nothing to add. A Redux set or variant the plan names but Redux did not build skips that switch (noted); an
        /// appended id Redux already uses throws <see cref="PlanMismatchException"/>.</summary>
        internal static Built Build(string part, Ksp1ConfigNode partNode, float rescaleFactor, Ksp1PartImportContext context,
            Data_PartSwitch data)
        {
            var switches = _switches ?? B9Switches.AllOn;
            ModuleTallies.Remove(part);
            var modules = Ksp1NodeAdapter.Modules(partNode);
            var plan = B9OverridePlan.For(modules, _tankTypes, switches);
            AfterPlanForTests?.Invoke(plan);
            foreach (var note in plan.Notes) LogNoteOnce(part, note);
            if (plan.Switches.Count == 0) return null;

            var built = new Built();
            var reduxSets = data?.VariantSets ?? new List<VariantSet>();
            var usedIds = new HashSet<string>(reduxSets.Select(s => s.VariantSetId), StringComparer.OrdinalIgnoreCase);
            var baseNames = (context.PartData?.resourceContainers ?? new List<ContainedResourceDefinition>())
                .Where(c => c != null && !string.IsNullOrEmpty(c.name)).Select(c => c.name).Distinct().ToList();
            var ownResources = partNode.GetNodes("RESOURCE").ToList();
            var database = ResourceDatabase();
            var anyAdder = false;
            // Module data (d.5, d.7): what became of each edited KSP1 module, and which set owns each native field.
            var moduleTally = new ModuleTally();
            var moduleStates = new Dictionary<int, ModuleOutcome>();
            var claims = new Dictionary<string, string>(StringComparer.Ordinal);

            // Tank resources are applied only when they can be exact: tank types read, one tank switch on the part.
            var applyResources = true;
            if (_tankTypes == null)
            {
                applyResources = false;
                TankTypesUnavailableOnce("Redux's tank catalog was not seen before the first part");
            }
            var tankSwitch = plan.Switches.FirstOrDefault(s => s.IsTank && s.Subtypes.Any(st => st.Resources != null));
            if (applyResources && plan.HasSecondTankSwitch && tankSwitch != null)
            {
                applyResources = false;
                LogNoteOnce(part, B9Lines.Notes.ResourcesKeptSecondTank(
                    B9Values.FirstNonBlank(tankSwitch.Module.GetValue("moduleID"), tankSwitch.Module.GetValue("switcherDescription"), tankSwitch.SetId)));
            }

            foreach (var sw in plan.Switches)
            {
                VariantSet reduxSet = null;
                AppendedSet appended = null;
                if (!sw.Appended)
                {
                    // A Redux set the plan expected but Redux did not build (or built differently): that switch only is
                    // skipped, Redux's set stays as it is; the other switches of the part are unaffected.
                    reduxSet = reduxSets.FirstOrDefault(s => string.Equals(s.VariantSetId, sw.SetId, StringComparison.OrdinalIgnoreCase));
                    if (reduxSet == null)
                    {
                        LogNoteOnce(part, B9Lines.Notes.ReduxSetMissing(sw.SetId, reduxSets.Select(s => s.VariantSetId)));
                        continue;
                    }
                    var missing = sw.Subtypes.Select(st => st.VariantId)
                        .FirstOrDefault(id => !reduxSet.Variants.Any(v => string.Equals(v.VariantId, id, StringComparison.Ordinal)));
                    if (missing != null)
                    {
                        LogNoteOnce(part, B9Lines.Notes.ReduxVariantMissing(sw.SetId, missing));
                        continue;
                    }
                }
                else if (!usedIds.Add(sw.SetId))
                    // Our ids would no longer be those the gates plan with: the whole part keeps Redux's sets.
                    throw new PlanMismatchException($"appended set id '{sw.SetId}' is already used by Redux");

                var set = sw.Appended
                    ? new VariantSet { VariantSetId = sw.SetId, VariantSetLocalizationKey = SetKey(part, sw.SetId), IsPopout = false }
                    : null;
                if (sw.Appended)
                    appended = new AppendedSet
                    {
                        SetId = sw.SetId,
                        Label = B9Values.FirstNonBlank(sw.Module.GetValue("switcherDescription"), sw.SetId),
                    };

                var masses = new List<double>();
                var resourceNames = new List<string>();
                var scalarKeys = new List<string>();
                var anyTransformer = false;
                var subtypeNodes = sw.Module.GetNodes("SUBTYPE").ToList();
                for (var i = 0; i < sw.Subtypes.Count; i++)
                {
                    var st = sw.Subtypes[i];
                    var list = new List<ITransformer>();
                    if (st.MassDelta != 0)
                        Add(part, list, new MassModifier { Modifier = (float)st.MassDelta }, () => built.Tally.MassVariants++);
                    masses.Add(list.OfType<MassModifier>().Sum(m => (double)m.Modifier));

                    if (st.Resources != null && applyResources)
                    {
                        // Remover then Adder, kept or dropped together: a Remover alone would empty the tank.
                        var mapped = MapResources(part, ownResources, st.Resources, database, context);
                        var pair = new List<ITransformer>();
                        if (baseNames.Count > 0) pair.Add(new ResourceContainerRemover { Containers = new List<string>(baseNames) });
                        if (mapped.Count > 0) pair.Add(new ResourceContainerAdder { Containers = mapped });
                        if (pair.Count > 0 && AllValid(part, pair))
                        {
                            list.AddRange(pair);
                            built.Tally.ResourceVariants++;
                            if (mapped.Count > 0) anyAdder = true;
                            foreach (var r in mapped)
                                if (!resourceNames.Contains(r.name)) resourceNames.Add(r.name);
                        }
                    }

                    var scalarsBefore = list.Count;
                    foreach (var sc in st.Scalars)
                    {
                        var t = ScalarTransformer(sc, rescaleFactor);
                        if (t == null) continue;
                        Add(part, list, t, null);
                        if (list.Contains(t) && !scalarKeys.Contains(sc.Key)) scalarKeys.Add(sc.Key);
                    }
                    if (list.Count > scalarsBefore) built.Tally.ScalarVariants++;
                    if (st.ModuleEdits.Count > 0)
                        AddModuleEdits(part, partNode, context, sw.SetId, st, list, built, moduleTally, moduleStates, claims);
                    if (st.Cost != 0) built.Tally.CostVariants++;
                    if (list.Count > 0) anyTransformer = true;

                    if (sw.Appended)
                    {
                        set.Variants.Add(new Variant
                        {
                            VariantId = st.VariantId,
                            VariantLocalizationKey = VariantKey(part, sw.SetId, st.VariantId),
                            Transformers = list,
                        });
                        var title = i < subtypeNodes.Count ? subtypeNodes[i].GetValue("title") : null;
                        appended.Variants.Add((st.VariantId, B9Values.FirstNonBlank(title, st.VariantId)));
                    }
                    else if (list.Count > 0)
                    {
                        var target = reduxSet.Variants.First(v => string.Equals(v.VariantId, st.VariantId, StringComparison.Ordinal));
                        built.ToRedux.Add((target, list));
                    }
                }

                if (!anyTransformer) continue; // nothing survived validation: no set, Redux's set untouched
                built.Tally.Sets++;
                built.Lines.Add(B9Lines.SetSummary(part, sw.SetId, sw.Subtypes.Count, masses.Min(), masses.Max(), resourceNames, scalarKeys));
                if (!sw.Appended) continue;
                built.NewSets.Add((set, sw.DefaultVariant ?? set.Variants[0].VariantId));
                built.Tally.Appended.Add(appended);
                built.Tally.NewSets++;
                if (data == null) built.Tally.NewSetsWithModule++;
                var moduleId = sw.Module.GetValue("moduleID");
                built.Lines.Add(B9Lines.NewSet(part, sw.SetId, string.IsNullOrWhiteSpace(moduleId) ? "B9 #" + sw.B9Index.ToString(CultureInfo.InvariantCulture) : moduleId.Trim(),
                    sw.Subtypes.Count, sw.DefaultVariant ?? set.Variants[0].VariantId));
            }

            FinishModules(part, moduleTally);
            if (built.ToRedux.Count == 0 && built.NewSets.Count == 0) return null;
            // Redux adds Module_ResourceCapacities after TryBuild only when the base definition has resources.
            if (anyAdder && baseNames.Count == 0 && context.PartData != null &&
                !context.PartData.serializedPartModules.Any(m => m.BehaviourType == typeof(Module_ResourceCapacities)))
                built.AddResourceCapacities = true;
            return built;
        }

        /// <summary>Writes a built part into Redux's result. Only list appends, after every check has passed; everything
        /// that allocates comes first, so a throw there leaves Redux's result untouched.</summary>
        private static void Commit(Built built, Ksp1PartImportContext context, ref bool result, ref Data_PartSwitch data, ref List<string> baseline)
        {
            var newData = built.NewSets.Count > 0 && data == null ? new Data_PartSwitch() : null;
            Data_ResourceCapacities capacities = null;
            SerializedPartModule? capacitiesModule = null;
            if (built.AddResourceCapacities)
            {
                // Written from the public types; the shape mirrors Redux's own AddResourceCapacitiesModule because the API
                // allows only this one: a SerializedPartModule initializer naming the behaviour, its component and its data.
                capacities = new Data_ResourceCapacities();
                capacitiesModule = new SerializedPartModule
                {
                    Name = "PartComponentModule_ResourceCapacities",
                    BehaviourType = typeof(Module_ResourceCapacities),
                    ComponentType = typeof(PartComponentModule_ResourceCapacities),
                    ModuleData = new List<SerializedModuleData> { new SerializedModuleData(capacities) },
                };
            }

            built.Written = true;
            if (newData != null) data = newData;
            foreach (var (target, add) in built.ToRedux) target.Transformers.AddRange(add);
            foreach (var (set, def) in built.NewSets)
            {
                data.VariantSets.Add(set);
                data.DefaultActiveVariants.Add(def);
                data.ActiveVariants.Add(def);
            }
            CommitAfterSetsForTests?.Invoke();
            if (data != null && data.VariantSets.Count > 0) result = true;
            if (baseline == null) baseline = new List<string>();
            if (capacitiesModule.HasValue)
            {
                context.ModuleData.Add(capacities);
                context.PartData.serializedPartModules.Add(capacitiesModule.Value);
                LogNoteOnce(context.PartName, B9Lines.Notes.ResourceCapacitiesAdded);
            }
            foreach (var line in built.Lines) Info(line);
        }

        // ------------------------------------------------------------------ module data and moduleActive

        /// <summary>The module edits of one subtype (d.2 to d.7): each edited KSP1 module re-imported by Redux and diffed;
        /// each native field that differs becomes a validated <c>ModuleDefinitionTransformer</c> in <paramref name="list"/>,
        /// unless its behaviour appears more than once on the part or another set already owns that field. Every edit
        /// of the subtype on the engines Redux switches is written into one engine list and diffed once, so the variant
        /// gets one <c>engineModes</c> with all its modes.</summary>
        private static void AddModuleEdits(string part, Ksp1ConfigNode partNode, Ksp1PartImportContext context, string setId,
            B9SubtypePlan st, List<ITransformer> list, Built built, ModuleTally tally, Dictionary<int, ModuleOutcome> states,
            Dictionary<string, string> claims)
        {
            var switched = B9ModuleDiff.SwitchedEngineIndices(partNode);
            var groups = new List<List<B9ModuleEditPlan>>();
            List<B9ModuleEditPlan> engineGroup = null;
            foreach (var edit in st.ModuleEdits)
            {
                if (!states.TryGetValue(edit.TargetIndex, out var o))
                {
                    o = new ModuleOutcome { Ksp1Module = edit.TargetModule ?? "?", TargetIndex = edit.TargetIndex };
                    states[edit.TargetIndex] = o;
                    tally.Modules.Add(o);
                }
                foreach (var k in edit.DataKeys)
                    if (!o.Keys.Contains(k)) o.Keys.Add(k);
                if (edit.Off && edit.NoOffForm)
                {
                    tally.NoOffForm++;
                    if (!tally.NoOffFormModules.Contains(o.Ksp1Module)) tally.NoOffFormModules.Add(o.Ksp1Module);
                    LogOnce(part + "\noff-none\n" + o.Ksp1Module + "\n" + st.VariantId,
                        Ksp1PartModuleImporterRegistry.IsEngineModuleName(o.Ksp1Module)
                            ? B9Lines.ModuleOffEngine(part, o.Ksp1Module, st.VariantId)
                            : B9Lines.ModuleOffNone(part, o.Ksp1Module, st.VariantId));
                }
                if (edit.Off && !edit.NoOffForm && !o.Keys.Contains("moduleActive")) o.Keys.Add("moduleActive");
                if (edit.Edited == null) continue;
                if (switched.Contains(edit.TargetIndex))
                {
                    if (engineGroup == null) groups.Add(engineGroup = new List<B9ModuleEditPlan>());
                    engineGroup.Add(edit);
                }
                else groups.Add(new List<B9ModuleEditPlan> { edit });
            }

            foreach (var group in groups)
            {
                var outcomes = group.Select(e => states[e.TargetIndex]).Distinct().ToList();
                var names = string.Join(", ", outcomes.Select(o => o.Ksp1Module).Distinct());
                if (outcomes.Any(o => o.Repeated)) continue;
                var offEdit = group.FirstOrDefault(e => e.Off && !e.NoOffForm);

                B9ModuleDiff.Result r;
                try
                {
                    r = B9ModuleDiff.Diff(Registry(), partNode, context,
                        group.Select(e => new KeyValuePair<int, IKsp1Node>(e.TargetIndex, e.Edited)).ToList());
                }
                catch (Exception e)
                {
                    foreach (var o in outcomes) o.Failed = true;
                    Warn(B9Lines.TransformerDropped(part, nameof(ModuleDefinitionTransformer),
                        names + " in " + st.VariantId + ": " + (e.InnerException?.Message ?? e.Message)));
                    continue;
                }
                if (r.Behaviour != null)
                {
                    foreach (var o in outcomes) o.Behaviour = r.Behaviour.Name;
                    ModuleTypeOnce(r.Behaviour);
                }
                var behaviour = r.Behaviour?.Name;
                if (r.Outcome == B9ModuleDiff.Outcome.Repeated)
                {
                    foreach (var o in outcomes) o.Repeated = true;
                    LogOnce(part + "\nrepeated\n" + behaviour, B9Lines.BehaviourRepeated(part, behaviour, r.Count));
                    continue;
                }
                if (r.Outcome != B9ModuleDiff.Outcome.Compared)
                {
                    // No native module: no native effect, decided for the whole part. An off form cannot apply: it is
                    // counted under "no off form", with the reason, and its moduleActive key is not a native effect.
                    if (offEdit != null)
                        OffNotImported(part, tally, states[offEdit.TargetIndex], st.VariantId,
                            r.Outcome == B9ModuleDiff.Outcome.NotImported ? r.Reason : "the native module is not on the part");
                    continue;
                }

                var candidates = r.Transformers;
                if (offEdit != null && Ksp1PartModuleImporterRegistry.IsDecouplerModuleName(offEdit.TargetModule))
                {
                    // Staging is per part: a decoupler made non-stageable takes the part's other stageable module out of
                    // staging too. The off form applies only when nothing else on the part stages.
                    var other = OtherStageable(partNode, context, offEdit.TargetIndex);
                    if (other != null)
                    {
                        candidates = candidates.Where(t => t.Key != DecoupleStageableField).ToList();
                        LogOnce(part + "\noff-staging\n" + offEdit.TargetModule + "\n" + st.VariantId,
                            B9Lines.ModuleOffStaging(part, offEdit.TargetModule, st.VariantId, other));
                        tally.NoOffForm++;
                        if (!tally.NoOffFormModules.Contains(offEdit.TargetModule)) tally.NoOffFormModules.Add(offEdit.TargetModule);
                        states[offEdit.TargetIndex].Keys.Remove("moduleActive");
                        offEdit = null;
                    }
                }

                var fields = new List<string>();
                foreach (var t in candidates)
                {
                    // V-SwiFT replaces a whole field: a later set would overwrite (and its reverter revert) the first's.
                    var claim = t.BehaviourType + "." + t.DataType + "." + t.Key;
                    if (claims.TryGetValue(claim, out var owner) && !string.Equals(owner, setId, StringComparison.Ordinal))
                    {
                        var conflict = B9Lines.FieldConflict(part, t.BehaviourType, t.Key, owner);
                        if (!built.Lines.Contains(conflict)) built.Lines.Add(conflict);
                        continue;
                    }
                    Add(part, list, t, null);
                    if (!list.Contains(t)) continue;
                    claims[claim] = setId;
                    fields.Add(t.Key);
                    tally.Transformers++;
                    var byField = t.BehaviourType + "." + t.Key;
                    tally.ByField[byField] = tally.ByField.TryGetValue(byField, out var n) ? n + 1 : 1;
                }
                if (fields.Count == 0)
                {
                    if (offEdit != null && r.Transformers.Count == 0) OffChangesNothing(part, tally, states[offEdit.TargetIndex], st.VariantId);
                    continue;
                }
                foreach (var o in outcomes)
                {
                    var firstEdit = o.Fields.Count == 0;
                    foreach (var f in fields)
                        if (!o.Fields.Contains(f)) o.Fields.Add(f);
                    if (offEdit != null && offEdit.TargetIndex == o.TargetIndex)
                    {
                        tally.OffForms++;
                        built.Lines.Add(B9Lines.ModuleOff(part, o.Ksp1Module, st.VariantId, behaviour, fields));
                    }
                    else if (firstEdit) // once per part and module
                    {
                        var line = B9Lines.ModuleEdit(part, setId, st.VariantId, o.Ksp1Module, behaviour, fields);
                        if (!built.Lines.Contains(line)) built.Lines.Add(line);
                    }
                }
            }
        }

        /// <summary>The <c>Data_Decouple</c> field the decoupler off form (<c>staged = false</c>) sets.</summary>
        internal const string DecoupleStageableField = "IsStageable";

        /// <summary>The native modules that put a part in staging besides a decoupler (Redux's stage type: engine,
        /// parachute, decoupler; KSP2: a docking port added to staging, a fairing).</summary>
        private static readonly string[] StageableBehaviours = { "Module_Engine", "Module_Parachute", "Module_DockingNode", "Module_Fairing" };

        /// <summary>Another module of the part that stages (KSP1 engine, parachute, decoupler or docking port, or a native
        /// engine, parachute, docking port or fairing), or null.</summary>
        private static string OtherStageable(Ksp1ConfigNode partNode, Ksp1PartImportContext context, int decouplerIndex)
        {
            var modules = partNode.GetNodes("MODULE").ToList();
            for (var i = 0; i < modules.Count; i++)
            {
                if (i == decouplerIndex) continue;
                var n = modules[i].GetValue("name")?.Trim();
                if (n == null) continue;
                if (Ksp1PartModuleImporterRegistry.IsEngineModuleName(n) || Ksp1PartModuleImporterRegistry.IsDecouplerModuleName(n) ||
                    n.Equals("ModuleParachute", StringComparison.OrdinalIgnoreCase) || n.Equals("ModuleDockingNode", StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            var native = context.PartData?.serializedPartModules?
                .Select(m => m.BehaviourType?.Name).FirstOrDefault(b => b != null && StageableBehaviours.Contains(b));
            return native;
        }

        /// <summary>An off form whose re-import changes no native field: logged and counted once, as "changing nothing".
        /// Its <c>moduleActive</c> key leaves the module's keys, as on the off-staging path, so the module is not also
        /// counted as "no native effect" for it (its DATA keys still are).</summary>
        private static void OffChangesNothing(string part, ModuleTally tally, ModuleOutcome o, string variant)
        {
            tally.OffNoChange++;
            o.Keys.Remove("moduleActive");
            LogOnce(part + "\noff-nochange\n" + o.Ksp1Module + "\n" + variant, B9Lines.ModuleOffNoChange(part, o.Ksp1Module, variant));
        }

        /// <summary>An off form on a module Redux does not import natively (a gimbal other than the first, a module with
        /// no native counterpart): logged with Redux's reason and counted under "no off form". Its <c>moduleActive</c> key
        /// leaves the module's keys, as on the off-staging path.</summary>
        private static void OffNotImported(string part, ModuleTally tally, ModuleOutcome o, string variant, string reason)
        {
            tally.NoOffForm++;
            if (!tally.NoOffFormModules.Contains(o.Ksp1Module)) tally.NoOffFormModules.Add(o.Ksp1Module);
            o.Keys.Remove("moduleActive");
            LogOnce(part + "\noff-notimported\n" + o.Ksp1Module + "\n" + variant, B9Lines.ModuleOffNotImported(part, o.Ksp1Module, variant, reason));
        }

        /// <summary>End of a part's module data: an edited module no variant could change natively gets its
        /// <c>no native effect</c> line; the tally is kept even when the part gets no set.</summary>
        private static void FinishModules(string part, ModuleTally tally)
        {
            if (tally.Modules.Count == 0) return;
            foreach (var o in tally.Modules)
            {
                if (o.Fields.Count > 0 || o.Repeated || o.Failed || o.Keys.Count == 0) continue;
                o.NoEffect = true;
                LogOnce(part + "\nno-effect\n" + o.Ksp1Module + "\n" + o.TargetIndex, B9Lines.NoNativeEffect(part, o.Ksp1Module, o.Keys));
            }
            ModuleTallies[part] = tally;
        }

        /// <summary>Once per behaviour type: whether V-SwiFT finds it by its short name (<c>PartModuleTypes</c>), as
        /// <c>ModuleDefinitionTransformer</c> will.</summary>
        private static void ModuleTypeOnce(Type behaviour)
        {
            if (behaviour == null || !ModuleTypesLogged.Add(behaviour.Name)) return;
            bool resolves;
            try
            {
                resolves = SpaceWarp2.API.Parts.PartModuleTypes.ComponentModules.TryGetValue(behaviour.Name, out var found) &&
                           found.behaviour == behaviour;
            }
            catch
            {
                resolves = false;
            }
            Info(B9Lines.ModuleTypeResolves(behaviour.Name, resolves));
        }

        private static void LogOnce(string key, string line)
        {
            if (NotesLogged.Add(key)) Info(line);
        }

        // ------------------------------------------------------------------ transformers

        /// <summary>Validates every transformer of <paramref name="group"/>; the first that fails is logged and the whole
        /// group is dropped.</summary>
        private static bool AllValid(string part, List<ITransformer> group)
        {
            foreach (var t in group)
            {
                try
                {
                    Validate(t);
                }
                catch (Exception e)
                {
                    Warn(B9Lines.TransformerDropped(part, t.GetType().Name,
                        (e.InnerException?.Message ?? e.Message) + (group.Count > 1 ? " (with its pair, Redux's resources kept)" : "")));
                    return false;
                }
            }
            return true;
        }

        /// <summary>Validates <paramref name="t"/> and adds it to <paramref name="list"/>, or drops it with a line.</summary>
        private static void Add(string part, List<ITransformer> list, ITransformer t, Action counted)
        {
            try
            {
                Validate(t);
            }
            catch (Exception e)
            {
                Warn(B9Lines.TransformerDropped(part, t.GetType().Name, e.InnerException?.Message ?? e.Message));
                return;
            }
            list.Add(t);
            counted?.Invoke();
        }

        /// <summary>Throws when V-SwiFT could not use <paramref name="t"/>: its reverter is read (as
        /// <c>StoreOriginalState</c> does), its saved information is built (as <c>GetStoredVariantInformation</c> does), and
        /// its values are checked the way its loader will read them.</summary>
        internal static void Validate(ITransformer t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            _ = t.Reverter;
            if (t.SavesInformation)
            {
                var saved = t.SaveInformation();
                if (saved.savedType == null || saved.savedValue == null) throw new InvalidOperationException("no saved information");
            }
            switch (t)
            {
                case MassModifier m:
                    if (float.IsNaN(m.Modifier) || float.IsInfinity(m.Modifier)) throw new InvalidOperationException("mass is not a number");
                    break;
                case ResourceContainerRemover r:
                    if (r.Containers == null || r.Containers.Count == 0 || r.Containers.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("no container name");
                    break;
                case ResourceContainerAdder a:
                    if (a.Containers == null || a.Containers.Count == 0) throw new InvalidOperationException("no container");
                    var db = ResourceDatabase();
                    foreach (var c in a.Containers)
                    {
                        if (c == null || string.IsNullOrWhiteSpace(c.name)) throw new InvalidOperationException("container without a name");
                        if (!(c.capacityUnits > 0) || double.IsInfinity(c.capacityUnits) || double.IsNaN(c.initialUnits) || c.initialUnits < 0)
                            throw new InvalidOperationException($"container '{c.name}' has capacity {c.capacityUnits} and {c.initialUnits} unit(s)");
                        if (db != null && !db.IsValidResourceID(db.GetResourceIDFromName(c.name)))
                            throw new InvalidOperationException($"resource '{c.name}' is not in the game's resource database");
                    }
                    break;
                case ModuleDefinitionTransformer md:
                    // Reverter read above: the behaviour and data types resolve and the field exists.
                    var dataField = md.ActualDataType.GetField(md.Key ?? "", BindingFlags.Public | BindingFlags.Instance)
                                    ?? throw new InvalidOperationException($"{md.DataType} has no public field '{md.Key}'");
                    if (md.Value == null) throw new InvalidOperationException("no value");
                    var read = IOProvider.FromJson(md.Value.ToString(Formatting.None), dataField.FieldType);
                    if (!JToken.DeepEquals(JToken.Parse(IOProvider.ToJson(read)), md.Value))
                        throw new InvalidOperationException($"{md.DataType}.{md.Key} does not survive Redux's JSON");
                    break;
                case PartScalarTransformer p:
                    var field = typeof(PartData).GetField(p.Key ?? "", BindingFlags.Public | BindingFlags.Instance)
                                ?? throw new InvalidOperationException($"PartData has no field '{p.Key}'");
                    if (p.Value == null) throw new InvalidOperationException("no value");
                    var value = IOProvider.FromJson(p.Value.ToString(Formatting.None), field.FieldType);
                    if (value == null) throw new InvalidOperationException($"'{p.Value.ToString(Formatting.None)}' is not a {field.FieldType.Name}");
                    if (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) throw new InvalidOperationException("value is not a number");
                    break;
            }
        }

        /// <summary>The <c>PartScalarTransformer</c> of one part value: <c>maxTemp</c>, <c>skinMaxTemp</c> and
        /// <c>crashTolerance</c> as numbers; <c>CoMOffset</c> as <c>coMassOffset</c>, scaled by the part's rescale factor
        /// as Redux scales the PART's own <c>CoMOffset</c>.</summary>
        internal static PartScalarTransformer ScalarTransformer(B9PartScalar sc, float rescaleFactor)
        {
            if (sc?.Values == null || sc.Values.Length == 0) return null;
            if (sc.Key == "CoMOffset")
            {
                if (sc.Values.Length != 3) return null;
                return new PartScalarTransformer
                {
                    Key = "coMassOffset",
                    Value = new JObject
                    {
                        ["x"] = (float)(sc.Values[0] * rescaleFactor),
                        ["y"] = (float)(sc.Values[1] * rescaleFactor),
                        ["z"] = (float)(sc.Values[2] * rescaleFactor),
                    },
                };
            }
            return new PartScalarTransformer { Key = sc.Key, Value = new JValue(sc.Values[0]) };
        }

        /// <summary>The KSP2 containers of a tank subtype: the part's own RESOURCE nodes plus the subtype's, mapped together
        /// by Redux's <c>Ksp1ResourceMapper.MapPartResources</c> (it merges LiquidFuel and Oxidizer into one KSP2 resource,
        /// keeping their mass), as Redux maps the part's base resources.</summary>
        internal static List<ContainedResourceDefinition> MapResources(string part, IEnumerable<Ksp1ConfigNode> own,
            IEnumerable<B9Resource> subtype, ResourceDefinitionDatabase database, Ksp1PartImportContext context)
        {
            var nodes = new List<Ksp1ConfigNode>(own ?? Enumerable.Empty<Ksp1ConfigNode>());
            foreach (var r in subtype ?? Enumerable.Empty<B9Resource>())
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Name) || r.Name.IndexOfAny(new[] { '{', '}', '=', '\n', '\r' }) >= 0) continue;
                nodes.Add(ResourceNode(r));
            }
            return Ksp1ResourceMapper.MapPartResources(nodes, database, context.Report, part, null)
                   ?? new List<ContainedResourceDefinition>();
        }

        /// <summary>A KSP1 RESOURCE node built through Redux's public parser.</summary>
        private static Ksp1ConfigNode ResourceNode(B9Resource r)
        {
            var text = new StringBuilder("RESOURCE\n{\n")
                .Append("name = ").Append(r.Name.Trim()).Append('\n')
                .Append("amount = ").Append(r.Amount.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
                .Append("maxAmount = ").Append(r.MaxAmount.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
                .Append("}\n").ToString();
            return Ksp1ConfigNode.Parse(text).GetNodes("RESOURCE").First();
        }

        private static ResourceDefinitionDatabase ResourceDatabase()
        {
            try
            {
                return GameManager.Instance?.Game?.ResourceDefinitionDatabase;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ localization

        /// <summary>The key of a set's name, in Redux's format (checked against Redux's private method by a test).</summary>
        internal static string SetKey(string part, string set) => "KSP1/Parts/" + part + "/Variants/" + set + "/Name";

        /// <summary>The key of a variant's name, in Redux's format.</summary>
        internal static string VariantKey(string part, string set, string variant) =>
            "KSP1/Parts/" + part + "/Variants/" + set + "/" + variant + "/Name";

        /// <summary>Redux names its own (visual) sets; the sets we appended to <paramref name="partName"/> are named here, from
        /// <c>switcherDescription</c> and the subtype <c>title</c>s, through the same resolver.</summary>
        private static void AfterAddLocalizationTerms(string __1, Func<string, string> __2, Action<string, string> __3)
        {
            if (!_armed || __1 == null || __3 == null) return;
            try
            {
                if (!Tallies.TryGetValue(__1, out var tally) || tally.Appended.Count == 0) return;
                foreach (var set in tally.Appended)
                {
                    __3(SetKey(__1, set.SetId), Resolve(__2, set.Label));
                    foreach (var (variant, label) in set.Variants) __3(VariantKey(__1, set.SetId, variant), Resolve(__2, label));
                }
            }
            catch (Exception e)
            {
                Error(B9Lines.PartFailed(__1, "localization: " + e));
            }
        }

        private static string Resolve(Func<string, string> resolve, string text) => resolve != null ? resolve(text) ?? text : text;

        // ------------------------------------------------------------------ lines

        private static void LogNoteOnce(string part, string note)
        {
            if (!NotesLogged.Add(part + "\n" + note)) return;
            NoteCounts[part] = NoteCounts.TryGetValue(part, out var n) ? n + 1 : 1;
            Info(B9Lines.PartNote(part, note));
        }

        /// <summary>Once per session, after Redux's import is complete.</summary>
        public static void LogImportSummaryOnce()
        {
            if (!_armed || _summaryLogged) return;
            _summaryLogged = true;
            var ts = Tallies.Values.ToList();
            var lines = new List<string>
            {
                B9Lines.ImportSummary(ts.Count, ts.Sum(t => t.Sets), ts.Sum(t => t.NewSets), ts.Sum(t => t.NewSetsWithModule),
                    ts.Sum(t => t.MassVariants), ts.Sum(t => t.ResourceVariants), ts.Sum(t => t.ScalarVariants), ts.Sum(t => t.CostVariants)),
            };
            if (_switches?.ModuleData == true) lines.Add(ModuleDataSummary());
            LastSummary = lines;
            foreach (var line in lines) Info(line);
            lock (RegistryLock) _registry = null;
        }

        /// <summary>The end-of-import module data line, from every part's <see cref="ModuleTally"/>.</summary>
        internal static string ModuleDataSummary()
        {
            var ms = ModuleTallies.Values.ToList();
            var byField = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in ms)
            foreach (var kv in m.ByField)
                byField[kv.Key] = byField.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
            var noEffect = ms.SelectMany(m => m.Modules).Where(o => o.NoEffect).ToList();
            return B9Lines.ModuleDataSummary(ms.Sum(m => m.Transformers), ms.Count(m => m.Transformers > 0),
                byField.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)),
                ms.Sum(m => m.OffForms), ms.Sum(m => m.NoOffForm), ms.SelectMany(m => m.NoOffFormModules).Distinct().OrderBy(n => n, StringComparer.Ordinal),
                noEffect.Count, noEffect.Select(o => o.Ksp1Module).Distinct().OrderBy(n => n, StringComparer.Ordinal),
                ms.Sum(m => m.OffNoChange));
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
