using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Game;
using KSP.Sim.Definitions;
using HarmonyLib;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Wheels;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>One KSP1 wheel module → the KSP2 wheel module of the same role (wheels plan, Task 3). One instance per kind,
    /// registered by <see cref="NativeModules"/> under the KSP1 module name; values from <see cref="WheelMapping"/>, data built
    /// by <see cref="WheelAccess"/> and added through Redux's own <c>CreateSerialized</c> (the list form for
    /// <c>Module_WheelMotorSteering</c>, which carries <c>Data_WheelMotor</c> and <c>Data_WheelMotorSteering</c>).
    /// <list type="bullet">
    /// <item><b>Base:</b> the part's first <c>ModuleWheelBase</c>, with its first <c>ModuleWheelDeployment</c> (the deploy
    /// timing). A converted base marks the import context; a second base gets nothing;</item>
    /// <item><b>Sub-modules:</b> converted only in a marked context or in a B9 scratch context
    /// (<see cref="B9ModuleDiff.IsScratch"/>), once per slot (<see cref="WheelPartRule.Slot"/>: the two motors share one);</item>
    /// <item>a context that already holds the kind's data type (a future Redux importer) gets nothing, noted;</item>
    /// <item>any exception removes what the call added to <c>context.ModuleData</c> and returns false: the module is then
    /// dropped by Redux, as in 0.13.0. A failed base leaves the context unmarked, so the part gets no sub-module.</item>
    /// </list>
    /// A B9 scratch context converts but records, counts and logs nothing. The per-part lines and the summary come once, after
    /// Redux's import (<see cref="SummaryLines"/>).</summary>
    internal sealed class WheelImporter : IKsp1PartModuleImporter
    {
        /// <summary>The <c>animations.json</c> keys of a kind, as the off line names them.</summary>
        internal static string SwitchKey(WheelKind kind)
        {
            switch (kind)
            {
                case WheelKind.Base:
                case WheelKind.Suspension: return "ksp1_wheels";
                case WheelKind.Steering: return "ksp1_wheels or wheel_steering";
                case WheelKind.Motor:
                case WheelKind.MotorSteering: return "ksp1_wheels or wheel_motors";
                case WheelKind.Brakes: return "ksp1_wheels or wheel_brakes";
                case WheelKind.Damage: return "ksp1_wheels or wheel_damage";
                case WheelKind.Lock: return "ksp1_wheels or wheel_leg_locks";
                default: return "ksp1_wheels or wheel_bogeys";
            }
        }

        /// <summary>The kind's switch, read with <c>ksp1_wheels</c>.</summary>
        internal static bool IsOn(AnimationSettings s, WheelKind kind)
        {
            if (s == null || !s.Ksp1Wheels) return false;
            switch (kind)
            {
                case WheelKind.Steering: return s.WheelSteering;
                case WheelKind.Motor:
                case WheelKind.MotorSteering: return s.WheelMotors;
                case WheelKind.Brakes: return s.WheelBrakes;
                case WheelKind.Damage: return s.WheelDamage;
                case WheelKind.Lock: return s.WheelLegLocks;
                case WheelKind.Bogey: return s.WheelBogeys;
                default: return true;
            }
        }

        /// <summary>The members <paramref name="kind"/> lacks, each logged once with the wheel line.</summary>
        internal static List<string> MissingMembers(WheelKind kind) => WheelAccess.Get().MissingFor(kind);

        /// <summary>The wheel line of a missing member (the converter's off line).</summary>
        internal static Func<string, string> MissingLine(WheelKind kind) => member => WheelLines.MemberNotFound(member, WheelMapping.Label(kind));

        internal static string ExistingDataNote(WheelKind kind) =>
            WheelMapping.Ksp1Module(kind) + " not converted: the part already has " + WheelAccess.Get().DataOf(kind)?.Name;

        internal static string RefusedNote(WheelKind kind, WheelRefusal reason) =>
            WheelMapping.Ksp1Module(kind) + " not converted (" + WheelPartRule.Label(reason) + ")";

        /// <summary>What the runtime and the gear bridge read about one part (Task 4).</summary>
        internal sealed class WheelPart
        {
            public string Part;
            public WheelBaseValues Base;
            public bool HasDeployment;
            public readonly WheelPartSummary Summary;
            internal readonly Dictionary<WheelKind, (IReadOnlyList<string> NotReproduced, IReadOnlyList<string> Unused)> Notes =
                new Dictionary<WheelKind, (IReadOnlyList<string>, IReadOnlyList<string>)>();

            public WheelPart(string part)
            {
                Part = part;
                Summary = new WheelPartSummary(part);
            }

            public WheelTypeName Type => Base?.WheelType ?? WheelTypeName.FREE;
            public IReadOnlyList<WheelKind> Kinds => Summary.Converted;
        }

        /// <summary>One import context: the base mark and the part's wheel record.</summary>
        private sealed class ContextState
        {
            public bool BaseConverted;
            public WheelPart Part;
        }

        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the conversion is counted (a throw = a failure).</summary>
        internal static Action<string, WheelKind> FaultForTests;
        /// <summary>Tests: the game's parts for the reference line (default: <c>GameManager…Parts.PartData</c>).</summary>
        internal static Func<IEnumerable<KeyValuePair<string, PartCore>>> ReferenceSource;

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, ContextState> States = new ConditionalWeakTable<Ksp1PartImportContext, ContextState>();
        private static readonly Dictionary<string, WheelPart> Parts = new Dictionary<string, WheelPart>(StringComparer.Ordinal);
        /// <summary>The B9 subtype edits of wheel modules (counted by <see cref="B9Overrides"/>).</summary>
        internal static readonly WheelB9Census B9 = new WheelB9Census();
        private static bool _referenceRead, _summaryLogged;

        private readonly WheelKind _kind;
        private readonly AnimationSettings _settings;

        public WheelImporter(WheelKind kind, AnimationSettings settings)
        {
            _kind = kind;
            _settings = settings;
        }

        /// <summary>The wheel record of <paramref name="part"/> (the last non-scratch import of it), or null.</summary>
        internal static WheelPart RecordOf(string part)
        {
            if (part == null) return null;
            lock (Lock) return Parts.TryGetValue(part, out var p) ? p : null;
        }

        /// <summary>Tests: sets the wheel record of <paramref name="record"/>'s part, as an import would.</summary>
        internal static void RecordForTests(WheelPart record)
        {
            lock (Lock) Parts[record.Part] = record;
        }

        /// <summary>Counts one B9 subtype edit (never throws into the B9 route).</summary>
        internal static void CountB9Edit(string part, string subtype, string ksp1Module, IEnumerable<string> dataKeys)
        {
            try
            {
                B9.Add(part, subtype, ksp1Module, dataKeys);
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "wheels: B9 census failed: " + e.Message);
            }
        }

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            ContextState state = null;
            var scratch = false;
            try
            {
                if (context == null || moduleNode == null) return false;
                scratch = B9ModuleDiff.IsScratch(context);
                lock (Lock)
                {
                    if (!States.TryGetValue(context, out state))
                    {
                        state = new ContextState { Part = new WheelPart(part) };
                        States.Add(context, state);
                        // The context's first wheel call: this import of the part replaces any earlier one (a rebuild counts once).
                        if (!scratch) Parts[part] = state.Part;
                    }
                }

                var refusal = _kind == WheelKind.Base ? RefuseBase(moduleNode, context, state, scratch) : RefuseSub(state, scratch);
                if (refusal != null) return Refuse(state, scratch, refusal.Value);

                var types = _kind == WheelKind.MotorSteering ? new[] { WheelKind.Motor, WheelKind.MotorSteering } : new[] { _kind };
                var access = WheelAccess.Get();
                if (types.Any(k => context.ModuleData.Any(d => d != null && d.GetType() == access.DataOf(k))))
                {
                    if (!scratch) NativeModules.Note(part, ExistingDataNote(_kind));
                    return false;
                }

                var node = new Ksp1NodeAdapter(moduleNode);
                var (converted, values, notReproduced, unused, notes) = Map(node, context);
                if (!converted)
                {
                    if (!scratch)
                    {
                        lock (Lock) state.Part.Notes[_kind] = (notReproduced, unused);
                        foreach (var n in notes) NativeModules.Note(part, n);
                    }
                    return Refuse(state, scratch, WheelRefusal.Mapping);
                }

                if (!scratch && _kind == WheelKind.Base) ReadReferenceOnce();
                added = context.ModuleData.Count;
                var data = Build(values);
                var behaviour = access.ModuleOf(_kind);
                serializedModule = data.Length == 1
                    ? NativeModules.CreateSerialized(behaviour, data[0], context)
                    : RadiatorAccess.CreateSerialized(behaviour, data, context);
                FaultForTests?.Invoke(part, _kind);

                // Complete: marked, recorded, then noted (a failed call leaves no mark, no count and no line).
                lock (Lock)
                {
                    if (_kind == WheelKind.Base)
                    {
                        state.BaseConverted = true;
                        state.Part.Base = (WheelBaseValues)values;
                        state.Part.HasDeployment = FirstNode(context.PartNode, WheelMapping.DeploymentModule) != null;
                    }
                    state.Part.Summary.Convert(_kind);
                    state.Part.Notes[_kind] = (notReproduced, unused);
                }
                added = -1;
                if (!scratch)
                {
                    try
                    {
                        NativeModules.Record.AddConversion(part, WheelMapping.NativeModule(_kind));
                        foreach (var n in notes) NativeModules.Note(part, n);
                    }
                    catch (Exception e)
                    {
                        // Only the record or the log failed: the module stays.
                        NativeModules.Error(NativeLines.Prefix + part + ": wheel lines not logged: " + e.Message);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                if (state != null)
                    lock (Lock) state.Part.Summary.Refuse(_kind, WheelRefusal.Failed);
                if (!scratch) NativeModules.ImporterFailed(part, WheelMapping.Ksp1Module(_kind), e);
                return false;
            }
        }

        private WheelRefusal? RefuseBase(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, ContextState state, bool scratch)
        {
            if (state.BaseConverted) return WheelRefusal.Second;
            if (scratch) return null; // B9's edited copy is not the part's node
            var modules = Modules(context.PartNode);
            var index = modules.FindIndex(m => ReferenceEquals(m, moduleNode));
            var first = modules.FindIndex(m => IsModule(m, WheelMapping.Ksp1Module(WheelKind.Base)));
            if (index < 0) index = first; // a copy of the node (not Redux's loop): read as the part's base
            return WheelPartRule.RefuseBase(true, index, first);
        }

        private WheelRefusal? RefuseSub(ContextState state, bool scratch)
        {
            bool already;
            lock (Lock) already = state.Part.Summary.HasSlot(_kind);
            return WheelPartRule.RefuseSub(true, state.BaseConverted, scratch, already);
        }

        private bool Refuse(ContextState state, bool scratch, WheelRefusal reason)
        {
            lock (Lock) state.Part.Summary.Refuse(_kind, reason);
            if (!scratch) NativeModules.Note(state.Part.Part, RefusedNote(_kind, reason));
            return false;
        }

        private (bool Converted, object Values, IReadOnlyList<string> NotReproduced, IReadOnlyList<string> Unused, IReadOnlyList<string> Notes)
            Map(IKsp1Node node, Ksp1PartImportContext context)
        {
            switch (_kind)
            {
                case WheelKind.Base:
                {
                    var deployment = FirstNode(context.PartNode, WheelMapping.DeploymentModule);
                    var m = WheelMapping.Base(node, deployment == null ? null : new Ksp1NodeAdapter(deployment),
                        _settings?.WheelKsp1Friction ?? true, _settings?.WheelKsp1DeployTiming ?? true);
                    return (m.Converted, m.Values, m.NotReproduced, m.UnusedBySolver, m.Notes);
                }
                case WheelKind.Suspension:
                {
                    var m = WheelMapping.Suspension(node);
                    var notes = m.Notes.ToList();
                    if (m.Converted) GuardSuspensionCollider(m.Values, context, notes);
                    return (m.Converted, m.Values, m.NotReproduced, m.UnusedBySolver, notes);
                }
                case WheelKind.Steering: return Of(WheelMapping.Steering(node));
                case WheelKind.Motor: return Of(WheelMapping.Motor(node, Resources()));
                case WheelKind.MotorSteering: return Of(WheelMapping.MotorSteering(node, Resources()));
                case WheelKind.Brakes: return Of(WheelMapping.Brakes(node));
                case WheelKind.Damage: return Of(WheelMapping.Damage(node));
                case WheelKind.Lock: return Of(WheelMapping.Lock(node));
                default: return Of(WheelMapping.Bogey(node));
            }
        }

        private static (bool, object, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>) Of<T>(WheelMapped<T> m) where T : class =>
            (m.Converted, m.Values, m.NotReproduced, m.UnusedBySolver, m.Notes);

        // ------------------------------------------------------------------ the suspension collider (Task 3 review, F6)

        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";

        /// <summary>The cfg file of each part node Redux translates (for its <c>.mu</c>), set by a prefix on
        /// <c>Ksp1PartTranslator.TryTranslatePart</c>; weak, it goes with the node.</summary>
        private static readonly ConditionalWeakTable<Ksp1ConfigNode, Ksp1ConfigFile> Files = new ConditionalWeakTable<Ksp1ConfigNode, Ksp1ConfigFile>();
        /// <summary>By cfg path, part name and collider name: the state read once (null: the model could not be read). One cfg
        /// file can hold several parts with different models, so the part is in the key.</summary>
        private static readonly Dictionary<string, SuspensionColliderState?> ColliderStates = new Dictionary<string, SuspensionColliderState?>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ModelPathResolver> Resolvers = new Dictionary<string, ModelPathResolver>(StringComparer.OrdinalIgnoreCase);

        internal static string NotCheckedNote(string name) =>
            $"ModuleWheelSuspension: suspensionColliderName '{name}' not checked against the model (no model path), kept";

        /// <summary>Armed with the suspension converter: remembers each translated part's cfg file. A missing member logs one
        /// line, and the suspension collider names are then kept unchecked.</summary>
        internal static void ArmTranslatorHook(Harmony harmony)
        {
            var translator = AccessTools.TypeByName(TranslatorTypeName);
            var method = translator == null ? null : AccessTools.Method(translator, "TryTranslatePart", new[]
            {
                typeof(Ksp1ConfigFile), typeof(Ksp1ConfigNode), typeof(Ksp1ImportReport), typeof(PartCore).MakeByRefType(), typeof(string).MakeByRefType(),
            });
            if (method == null)
            {
                NativeModules.Warn(WheelLines.MemberNotFound("Ksp1PartTranslator.TryTranslatePart(Ksp1ConfigFile, Ksp1ConfigNode, …)",
                    "suspension collider check"));
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(WheelImporter), nameof(BeforeTranslate)) { priority = Priority.First });
        }

        private static void BeforeTranslate(Ksp1ConfigFile __0, Ksp1ConfigNode __1)
        {
            try
            {
                if (__0 == null || __1 == null) return;
                lock (Lock)
                {
                    Files.Remove(__1);
                    Files.Add(__1, __0);
                }
            }
            catch
            {
                // Never into Redux's translation: the part's collider name is then kept unchecked.
            }
        }

        /// <summary>Tests: the cfg file of a part node, as the translator prefix records it.</summary>
        internal static void RememberFileForTests(Ksp1ConfigNode partNode, Ksp1ConfigFile file) => BeforeTranslate(file, partNode);

        /// <summary>KSP2's suspension switches off the first collider under <c>suspensionColliderName</c> without checking that
        /// one exists (it skips a missing transform, and an empty name). A name whose objects carry no collider in the part's
        /// models is cleared, with a note; an absent one is kept (KSP2 skips it).</summary>
        private static void GuardSuspensionCollider(WheelSuspensionValues v, Ksp1PartImportContext context, List<string> notes)
        {
            var name = v.SuspensionColliderName;
            if (string.IsNullOrEmpty(name)) return;
            var state = ColliderState(context.PartNode, name);
            if (state == null) notes.Add(NotCheckedNote(name));
            else if (state == SuspensionColliderState.NoCollider)
            {
                v.SuspensionColliderName = "";
                notes.Add(SuspensionColliderRule.ClearedNote(name));
            }
        }

        private static string ColliderKey(string cfgPath, string partName, string colliderName) =>
            cfgPath + "\n" + (partName ?? "") + "\n" + colliderName;

        private static SuspensionColliderState? ColliderState(Ksp1ConfigNode partNode, string name)
        {
            Ksp1ConfigFile file;
            lock (Lock)
                if (partNode == null || !Files.TryGetValue(partNode, out file)) return null;
            if (string.IsNullOrEmpty(file?.Path)) return null;
            var key = ColliderKey(file.Path, partNode.GetValue("name"), name);
            lock (Lock)
                if (ColliderStates.TryGetValue(key, out var cached)) return cached;
            SuspensionColliderState? state;
            try
            {
                var root = file.Root?.GameDataRoot ?? "";
                ModelPathResolver resolver;
                lock (Lock)
                    if (!Resolvers.TryGetValue(root, out resolver))
                        Resolvers[root] = resolver = new ModelPathResolver(root, () => DirectoryIndex.Build(root));
                var snapshot = PartSnapshot.From(partNode.GetValue("name"), file.Path, root, new Ksp1NodeAdapter(partNode), null);
                var roots = new List<KSP1PartMod.Animation.Mu.MuNode>();
                for (var i = 0; i < snapshot.ModelRefs.Length; i++)
                {
                    var path = resolver.Resolve(file.Path, snapshot.ModelRefs[i], snapshot.RefIsMesh[i], out _);
                    if (path != null) roots.Add(KSP1PartMod.Animation.Mu.MuReader.Read(path).Root);
                }
                state = roots.Count == 0 ? (SuspensionColliderState?)null : SuspensionColliderRule.Check(roots, name);
            }
            catch (Exception)
            {
                state = null;
            }
            lock (Lock) ColliderStates[key] = state;
            return state;
        }

        private static IsruResources Resources() => ConverterImporter.Resources(ConverterImporter.CurrentDatabase(), null);

        private ModuleData[] Build(object values)
        {
            switch (_kind)
            {
                case WheelKind.Base: return new[] { WheelAccess.NewBase((WheelBaseValues)values) };
                case WheelKind.Suspension: return new[] { WheelAccess.NewSuspension((WheelSuspensionValues)values) };
                case WheelKind.Steering: return new[] { WheelAccess.NewSteering((WheelSteeringValues)values) };
                case WheelKind.Motor: return new[] { WheelAccess.NewMotor((WheelMotorValues)values) };
                case WheelKind.MotorSteering: return WheelAccess.NewMotorSteering((WheelMotorSteeringValues)values);
                case WheelKind.Brakes: return new[] { WheelAccess.NewBrakes((WheelBrakesValues)values) };
                case WheelKind.Damage: return new[] { WheelAccess.NewDamage((WheelDamageValues)values) };
                case WheelKind.Lock: return new[] { WheelAccess.NewLock((WheelLockValues)values) };
                default: return new[] { WheelAccess.NewBogey((WheelBogeyValues)values) };
            }
        }

        private static List<Ksp1ConfigNode> Modules(Ksp1ConfigNode partNode) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>()).Where(n => n != null).ToList();

        private static bool IsModule(Ksp1ConfigNode node, string name) =>
            string.Equals(node.GetValue("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase);

        private static Ksp1ConfigNode FirstNode(Ksp1ConfigNode partNode, string module) =>
            Modules(partNode).FirstOrDefault(n => IsModule(n, module));

        // ------------------------------------------------------------------ the reference line

        /// <summary>Once per session, before the first KSP1 wheel: KSP2's own wheel parts, for comparison only.</summary>
        private static void ReadReferenceOnce()
        {
            lock (Lock)
            {
                if (_referenceRead) return;
                _referenceRead = true;
            }
            string line;
            try
            {
                line = ReferenceLine(ReferenceSource ?? GameParts);
            }
            catch (Exception e)
            {
                line = WheelLines.ReferenceNone(e.GetType().Name + ": " + e.Message);
            }
            NativeModules.Info(line);
        }

        private static IEnumerable<KeyValuePair<string, PartCore>> GameParts()
        {
            var parts = GameManager.Instance?.Game?.Parts?.PartData;
            if (parts == null) throw new InvalidOperationException("the game's part data is not available");
            return parts.ToList();
        }

        /// <summary>The reference line from <paramref name="source"/>: every KSP2 part (KSP1 imports and '+' variants left out)
        /// with a <c>Data_WheelBase</c>.</summary>
        internal static string ReferenceLine(Func<IEnumerable<KeyValuePair<string, PartCore>>> source)
        {
            IEnumerable<KeyValuePair<string, PartCore>> parts;
            try
            {
                parts = source();
            }
            catch (Exception e)
            {
                return WheelLines.ReferenceNone(e.Message);
            }
            var samples = new List<WheelReferenceSample>();
            foreach (var kv in parts ?? Enumerable.Empty<KeyValuePair<string, PartCore>>())
            {
                var core = kv.Value;
                if (kv.Key == null || kv.Key.Contains('+') || core?.data == null) continue;
                if (Ksp1Tags.IsKsp1Import(core.data.tags)) continue;
                var sample = Sample(core.data.partName ?? kv.Key, IsruReference.DataOf(core));
                if (sample != null) samples.Add(sample);
            }
            return WheelReferenceStats.Line(samples);
        }

        private static WheelReferenceSample Sample(string part, IEnumerable<ModuleData> data)
        {
            WheelReferenceSample s = null;
            var rest = new List<(WheelKind, ModuleData)>();
            foreach (var d in data)
            {
                var kind = WheelAccess.KindOf(d);
                if (kind == null) continue;
                if (kind == WheelKind.Base)
                {
                    s = new WheelReferenceSample { Part = part, Radius = (float)WheelAccess.Read(d, WheelKind.Base, "Radius") };
                    if (WheelMapping.TryWheelType(WheelAccess.Read(d, WheelKind.Base, "WheelType")?.ToString(), out var type)) s.Type = type;
                }
                else rest.Add((kind.Value, d));
            }
            if (s == null) return null;
            foreach (var (kind, d) in rest)
            {
                switch (kind)
                {
                    case WheelKind.Brakes: s.Brake = (float)WheelAccess.Read(d, kind, "MaxBrakeTorque"); break;
                    case WheelKind.Lock: s.Lock = (float)WheelAccess.Read(d, kind, "MaxTorque"); break;
                    case WheelKind.Suspension: s.RatedLoad = (float)WheelAccess.Read(d, kind, "maximumLoad"); break;
                    case WheelKind.Motor:
                        var keys = WheelAccess.KeysOf(WheelAccess.Read(d, kind, "torqueCurve"));
                        if (keys.Length > 0) s.MotorPeak = keys.Max(k => k.value);
                        var res = (PartModuleResourceSetting)WheelAccess.Read(d, kind, "requiredResource");
                        if ((bool)WheelAccess.Read(d, kind, "useResources") && res.ResourceName == IsruResources.ElectricCharge) s.MotorEc = res.Rate;
                        break;
                }
            }
            return s;
        }

        // ------------------------------------------------------------------ summary

        /// <summary>Once, after Redux's import: the part lines (by name), the summary and the B9 census. One call for the nine
        /// converters (the first asks).</summary>
        internal static IEnumerable<string> SummaryLines()
        {
            List<WheelPart> parts;
            lock (Lock)
            {
                if (_summaryLogged) yield break;
                _summaryLogged = true;
                parts = Parts.Values.OrderBy(p => p.Part, StringComparer.Ordinal).ToList();
            }
            var tally = new WheelTally();
            var lines = new List<string>();
            foreach (var p in parts)
            {
                lock (Lock)
                {
                    foreach (var kind in p.Summary.Converted)
                    {
                        if (kind == WheelKind.Base) tally.AddBase(p.Type);
                        else tally.AddConverted(kind);
                    }
                    foreach (var (_, reason) in p.Summary.Refused) tally.AddRefused(reason);
                    foreach (var n in p.Notes.Values) tally.AddNotes(n.NotReproduced, n.Unused);
                    if (p.Summary.BaseConverted) lines.Add(WheelLines.Part(p.Part, p.Base, p.Summary.KindsText()));
                }
            }
            foreach (var line in lines) yield return line;
            yield return WheelLines.Summary(tally);
            yield return B9.Line();
        }

        internal static void ResetSession()
        {
            lock (Lock)
            {
                Parts.Clear();
                ColliderStates.Clear();
                Resolvers.Clear();
                _referenceRead = _summaryLogged = false;
            }
            B9.Clear();
            FaultForTests = null;
            ReferenceSource = null;
        }
    }
}
