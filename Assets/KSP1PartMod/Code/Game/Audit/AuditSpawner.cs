using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using KSP.VFX.Reentry;
using KSP1PartMod.Audit;
using Redux.Ksp1Import.Assets;
using Redux.VFX.ReentryMeshGeneration;
using UnityEngine;
using VSwift.Modules.Data;
using VSwift.Modules.Transformers;
using Object = UnityEngine.Object;

namespace KSP1PartMod.Game.Audit
{
    /// <summary>Spawns one part the way the VAB does, on a detached copy, and destroys it in the same call (plan finding
    /// (a), steps 1 to 6):
    /// <list type="number">
    /// <item>prefab: an existing template is used as it is; otherwise the registry's prefab
    /// (<c>Ksp1ImportedPartRegistry.TryGetPrefab</c>, which rebuilds it when it was released), or the prefab the caller
    /// loaded through the asset system (controls, Patch Manager remaps);</item>
    /// <item>template: <c>PrefabUtils.GetOrCloneTemplate</c>, then on creation <c>ApplyOnGameObject(…, false)</c> and
    /// <c>HotswapPartShaders</c>, as <c>ObjectAssemblyPartTracker.OnPartPrefabLoaded</c> does;</item>
    /// <item>a new <c>OABPartData(core)</c> whose <c>ModelPrefab</c> is the template (the tracker's own is never touched);</item>
    /// <item>a private <c>ObjectAssemblyBuilderEvents</c>: <c>IsLoadingVessel</c> (no staging refresh) and a no-op
    /// <c>PostOnPartFinalized</c>, which <c>FinalizeLoad</c> calls without a null check;</item>
    /// <item><c>CreateEditorPart</c>, a new <c>UniqueId</c>, then <c>FinalizeLoad</c> inside our try/catch, then the
    /// inspection;</item>
    /// <item>disposal: the part, the template when we created it and the VAB does not use it, and the prefab when we
    /// caused its rebuild (textures kept when they may be shared).</item>
    /// </list>
    /// Never throws: every step has its own try/catch, and the log capture is always detached.</summary>
    internal sealed class AuditSpawner
    {
        private const string Placeholder = "model-placeholder";
        private const string ErrorShader = "Hidden/InternalErrorShader";
        private const int SampleChars = 300;
        private const int Samples = 3;
        private const int OabPartLayer = 9;

        private static readonly Action<IObjectAssemblyPart> NoOp = _ => { };
        /// <summary>The B9 column failed once this session (logged once).</summary>
        private static bool _b9FailureLogged;

        private readonly AuditTargets _targets;
        private readonly ObjectAssemblyBuilder _builder;
        private readonly HashSet<string> _available;

        /// <summary>Registry entries by prefab key (<c>&lt;part&gt;.prefab</c>), for Patch Manager remaps to another KSP1
        /// part's prefab.</summary>
        private readonly Dictionary<string, Ksp1ImportedPartRegistry.Entry> _byPrefabKey =
            new Dictionary<string, Ksp1ImportedPartRegistry.Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Paths our spawns loaded fresh into the shared texture cache: the only entries the audit may evict.</summary>
        public readonly HashSet<string> TextureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public AuditSpawner(AuditTargets targets, ObjectAssemblyBuilder builder)
        {
            _targets = targets;
            _builder = builder;
            foreach (var e in Ksp1ImportedPartRegistry.ImportedParts)
                if (e?.PartName != null) _byPrefabKey[Ksp1ImportedPartRegistry.GetPrefabKey(e.PartName)] = e;
            _available = new HashSet<string>(StringComparer.Ordinal);
            var parts = builder.Stats?.AvailableParts;
            if (parts != null)
                foreach (var p in parts)
                    if (p?.Name != null) _available.Add(p.Name);
        }

        /// <summary>The template key of <c>OnPartPrefabLoaded</c>.</summary>
        public static string TemplateKey(PartCore core) => core.data.partName;

        public bool HasTemplate(PartCore core)
        {
            try
            {
                return _targets.FindTemplate(TemplateKey(core), out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The asset address the VAB would load, when it is not a KSP1 registry prefab (a control part, or a Patch
        /// Manager remap to a non-KSP1 asset): the caller loads it and waits. Null when the registry builds the prefab
        /// (<see cref="RegistryKey"/>), inside the spawn and its log capture.</summary>
        public string AsyncAddress(PartCore core, bool isControl)
        {
            var address = _targets.PrefabAddress(core.data.partName);
            if (isControl) return address;
            return RegistryKey(core) != null ? null : address;
        }

        /// <summary>The registry key of the prefab the VAB would get: the part's own key, or the key a Patch Manager
        /// remap points to when that is another KSP1 part (the asset system would call the same
        /// <c>TryGetPrefab</c>). Null otherwise.</summary>
        public string RegistryKey(PartCore core)
        {
            var name = core.data.partName;
            var address = _targets.PrefabAddress(name);
            if (string.Equals(address, Ksp1ImportedPartRegistry.GetPrefabKey(name), StringComparison.Ordinal)) return address;
            return address != null && _byPrefabKey.ContainsKey(address) ? address : null;
        }

        /// <summary>One part, start to end, in this frame. <paramref name="loadedPrefab"/> is the prefab the caller loaded
        /// (null otherwise); <paramref name="loadError"/> says why a load gave nothing.</summary>
        public AuditRecord Spawn(PartCore core, Ksp1ImportedPartRegistry.Entry entry, bool isControl, GameObject loadedPrefab,
            string loadError)
        {
            var name = core.data.partName;
            var rec = new AuditRecord
            {
                Part = name,
                Mod = isControl ? "KSP2" : entry?.ModName ?? "",
                Control = isControl,
                Available = _available.Contains(name),
                Crew = core.data.crewCapacity,
            };
            var obs = new AuditObservations();
            var capture = new LogCapture();
            Exception error = null;
            GameObject prefab = null, template = null, createdTemplate = null, root = null;
            var rebuilt = false;
            string key = null;
            Ksp1ImportedPartRegistry.Entry source = entry;
            ObjectAssemblyPart part = null;
            List<string> present = null;
            var clock = Stopwatch.StartNew();
            capture.Attach();
            AuditProbe.Begin();
            Ksp1Textures.RecordFreshLoads(TextureKeys);
            try
            {
                try
                {
                    // 1. Prefab, unless a template already exists (then the VAB would not need one either).
                    if (!_targets.FindTemplate(TemplateKey(core), out template))
                    {
                        template = null;
                        if (loadedPrefab != null) prefab = loadedPrefab;
                        else if (!isControl && loadError == null && (key = RegistryKey(core)) != null)
                        {
                            // A remap to another KSP1 part: that part's entry holds the prefab we may rebuild.
                            if (!_byPrefabKey.TryGetValue(key, out source)) source = entry;
                            var wasReleased = source == null || source.Prefab == null;
                            Ksp1ImportedPartRegistry.TryGetPrefab(key, out prefab);
                            rebuilt = wasReleased && source != null && prefab != null;
                        }
                        if (prefab == null)
                        {
                            obs.NoPrefab = true;
                            if (loadError != null) rec.ExceptionMessage = loadError;
                        }
                    }
                }
                catch (Exception e)
                {
                    obs.NoPrefab = true;
                    error = Unwrap(e); // a throw inside the rebuild leaves Entry.Prefab null: nothing to release
                }

                // 2. Template.
                if (template == null && prefab != null)
                {
                    try
                    {
                        template = _targets.CloneTemplate(prefab, TemplateKey(core), out var created);
                        if (created)
                        {
                            createdTemplate = template;
                            template = _targets.ApplyPatcher(template, core.data);
                            _targets.Hotswap(template);
                        }
                    }
                    catch (Exception e)
                    {
                        obs.TemplateFailed = true;
                        error = Unwrap(e);
                        template = null;
                    }
                }
                rec.PrefabMs = clock.Elapsed.TotalMilliseconds;
                rec.Rebuilt = rebuilt;

                // 3 to 5. Detached part, private events, FinalizeLoad.
                if (template != null)
                {
                    clock.Restart();
                    try
                    {
                        var detached = new OABPartData(core) { ModelPrefab = template };
                        var events = new ObjectAssemblyBuilderEvents { IsLoadingVessel = true, PostOnPartFinalized = NoOp };
                        part = detached.CreateEditorPart(null, _builder.BuilderAssets) as ObjectAssemblyPart;
                        if (part == null) throw new InvalidOperationException("CreateEditorPart gave no ObjectAssemblyPart");
                        root = part.gameObject;
                        SetLayer(root.transform, OabPartLayer);
                        part.UniqueId = IGGuid.NewGuid();
                        part.FinalizeLoad(events, detached);
                    }
                    catch (Exception e)
                    {
                        error = Unwrap(e);
                    }
                    rec.FinalizeMs = clock.Elapsed.TotalMilliseconds;
                    obs.IsFinalized = part != null && part.IsFinalized;
                    present = Inspect(part, root, rec, obs);
                }
                if (root == null) SanityFromDefinition(core, rec);
            }
            catch (Exception e)
            {
                error = error ?? Unwrap(e); // a fault in our own inspection: recorded, never thrown
            }
            finally
            {
                try
                {
                    Dispose(name, root, createdTemplate, template, rebuilt ? prefab : null, source, rec);
                }
                finally
                {
                    Ksp1Textures.RecordFreshLoads(null);
                    rec.ModFixes = AuditProbe.End();
                    capture.Detach();
                }
            }

            if (error != null) Describe(error, rec, obs);
            if (!isControl)
            {
                try
                {
                    rec.B9Overrides = B9Items(core, rec, present);
                }
                catch (Exception e)
                {
                    // read-only bookkeeping: never fails the audit of a part; one line per session
                    if (!_b9FailureLogged)
                    {
                        _b9FailureLogged = true;
                        KSP1PartModPlugin.Log?.LogWarning(KSP1PartMod.B9.B9Lines.AuditB9Failed(name, e.ToString()));
                    }
                }
            }
            obs.ImportWarnings = capture.ImportWarnings;
            obs.ImportErrors = capture.ImportErrors;
            obs.LoggedErrors = capture.LoggedErrors;
            rec.ImportWarnings = capture.ImportWarnings;
            rec.ImportErrors = capture.ImportErrors;
            rec.ImportWarningSamples = capture.ImportSamples.ToArray();
            rec.LoggedErrors = capture.LoggedErrors;
            rec.LoggedErrorSamples = capture.ErrorSamples.ToArray();
            obs.Materialless = rec.Materialless;
            obs.ErrorShader = rec.ErrorShader;
            obs.UninitializedModules = rec.UninitializedModules;
            rec.Kinds = AuditClassifier.Kinds(obs);
            return rec;
        }

        /// <summary>A part skipped because the previous run stopped during it.</summary>
        public static AuditRecord Crashed(string name, string mod, bool isControl) => new AuditRecord
        {
            Part = name,
            Mod = mod ?? "",
            Control = isControl,
            Kinds = AuditClassifier.Kinds(new AuditObservations { CrashedPreviousRun = true }),
        };

        // ---- inspection ----

        /// <summary>Inspects the spawned part; returns the type names of its part modules (empty when it has none), or null
        /// when nothing was spawned.</summary>
        private static List<string> Inspect(ObjectAssemblyPart part, GameObject root, AuditRecord rec, AuditObservations obs)
        {
            if (root == null) return null;
            Transform placeholder = null;
            foreach (Transform child in root.transform)
                if (child.name == Placeholder) placeholder = child;
            obs.PlaceholderPresent = placeholder != null;

            var uninitialized = new List<string>();
            var modules = part?.Modules;
            if (modules != null)
                for (int i = 0; i < modules.Count; i++)
                    if (modules.At(i) is PartBehaviourModule m && m != null && !m.IsInitialized)
                        uninitialized.Add(m.GetType().Name);
            rec.UninitializedModules = uninitialized.ToArray();

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (placeholder != null && r.transform.IsChildOf(placeholder)) continue;
                var materials = r.sharedMaterials;
                if (materials.Any(m => m == null))
                {
                    // Reentry hulls get their material from ReentryMesh.OnEnable, as RenderFixes assumes.
                    var inHull = r.GetComponentInParent<ReentryMesh>(true) != null ||
                                 r.GetComponentInParent<GeneratedReentryMeshRoot>(true) != null;
                    if (!inHull)
                    {
                        rec.Materialless++;
                        if (!r.enabled) rec.MateriallessHidden++;
                    }
                }
                if (materials.Any(m => m != null && m.shader != null && m.shader.name == ErrorShader)) rec.ErrorShader++;
            }

            var command = root.GetComponentInChildren<Module_Command>(true);
            rec.HasCommand = command != null;
            rec.CommandInitialized = command != null && command.IsInitialized;
            rec.Engines = root.GetComponentsInChildren<Module_Engine>(true).Length;
            rec.ReduxFairing = root.GetComponentInChildren<Module_Fairing>(true) != null;

            var present = new List<string>();
            try
            {
                if (modules != null)
                    for (int i = 0; i < modules.Count; i++)
                        if (modules.At(i) != null) present.Add(modules.At(i).GetType().Name);
                var converted = NativeModules.Record.ConvertedModules(rec.Part);
                if (converted.Count > 0) rec.NativeModules = AuditNative.Tag(converted, present, uninitialized);
            }
            catch (Exception)
            {
                // read-only bookkeeping: never fails the audit of a part
            }
            return present;
        }

        /// <summary>The <c>b9_overrides</c> items (B9 overrides, 0.9.0; <see cref="AuditB9"/>): read only, from the part
        /// definition's <c>Data_PartSwitch</c> (the variants the VAB applies by default) and from the import's tallies.
        /// Called after the exception is described, so a failing module is known.</summary>
        /// <param name="present">The spawned part's module type names, or null when nothing was spawned.</param>
        private static IReadOnlyList<string> B9Items(PartCore core, AuditRecord rec, List<string> present)
        {
            Data_PartSwitch data = null;
            if (core?.modules != null) data = core.modules.OfType<Data_PartSwitch>().FirstOrDefault();
            if (data == null && core?.data?.serializedPartModules != null)
                data = core.data.serializedPartModules.Where(m => m.ModuleData != null).SelectMany(m => m.ModuleData)
                    .Select(d => d.DataObject).OfType<Data_PartSwitch>().FirstOrDefault();

            var sets = new List<string>();
            var edited = new List<string>();
            if (data?.VariantSets != null)
                for (var i = 0; i < data.VariantSets.Count; i++)
                {
                    var set = data.VariantSets[i];
                    if (set?.Variants == null || !set.Variants.Any(v => v?.Transformers != null && v.Transformers.Any(IsB9Override))) continue;
                    var active = data.ActiveVariants != null && i < data.ActiveVariants.Count ? data.ActiveVariants[i] : null;
                    if (active == null && data.DefaultActiveVariants != null && i < data.DefaultActiveVariants.Count)
                        active = data.DefaultActiveVariants[i];
                    var variant = set.Variants.FirstOrDefault(v => v != null && string.Equals(v.VariantId, active, StringComparison.Ordinal));
                    sets.Add(AuditB9.Set(set.VariantSetId, active, B9Kinds(variant?.Transformers, edited)));
                }

            var tally = B9Overrides.TallyOf(rec.Part);
            var moduleTally = B9Overrides.ModuleTallyOf(rec.Part);
            var notes = B9Overrides.NoteCountOf(rec.Part);
            var counts = tally == null && moduleTally == null && notes == 0
                ? null
                : AuditB9.Counts(tally?.MassVariants ?? 0, tally?.ResourceVariants ?? 0, tally?.ScalarVariants ?? 0,
                    moduleTally?.Transformers ?? 0, moduleTally?.OffForms ?? 0, notes);
            var failure = AuditB9.Failure(sets.Count > 0, present != null, present, rec.UninitializedModules, rec.FailingModule, edited);
            return AuditB9.Items(sets, counts, failure);
        }

        /// <summary>A transformer the B9 overrides add (Redux's KSP1 import builds none of these types).</summary>
        private static bool IsB9Override(ITransformer t) =>
            t is MassModifier || t is ResourceContainerRemover || t is ResourceContainerAdder || t is PartScalarTransformer
            || t is ModuleDefinitionTransformer;

        /// <summary>The kinds of a variant's B9 override transformers; the behaviours its module transformers edit are
        /// added to <paramref name="edited"/>.</summary>
        private static List<string> B9Kinds(List<ITransformer> transformers, List<string> edited)
        {
            var kinds = new List<string>();
            if (transformers == null) return kinds;
            var mass = transformers.OfType<MassModifier>().ToList();
            if (mass.Count > 0) kinds.Add(AuditB9.Mass(mass.Sum(m => (double)m.Modifier)));
            var adders = transformers.OfType<ResourceContainerAdder>().ToList();
            if (adders.Count > 0) kinds.Add(AuditB9.Resources(adders.SelectMany(a => a.Containers ?? new List<ContainedResourceDefinition>()).Select(c => c?.name)));
            else if (transformers.OfType<ResourceContainerRemover>().Any()) kinds.Add(AuditB9.Resources(null));
            foreach (var s in transformers.OfType<PartScalarTransformer>()) kinds.Add(AuditB9.Scalar(s.Key));
            foreach (var m in transformers.OfType<ModuleDefinitionTransformer>())
            {
                kinds.Add(AuditB9.Module(m.BehaviourType, m.Key));
                if (!string.IsNullOrEmpty(m.BehaviourType) && !edited.Contains(m.BehaviourType)) edited.Add(m.BehaviourType);
            }
            return kinds;
        }

        /// <summary>Command and engines from the part's definition, when no model could be spawned.</summary>
        private static void SanityFromDefinition(PartCore core, AuditRecord rec)
        {
            var modules = core.data.serializedPartModules;
            if (modules == null) return;
            foreach (var m in modules)
            {
                var type = m.BehaviourType;
                if (type == null) continue;
                if (typeof(Module_Command).IsAssignableFrom(type)) rec.HasCommand = true;
                if (typeof(Module_Engine).IsAssignableFrom(type)) rec.Engines++;
                if (typeof(Module_Fairing).IsAssignableFrom(type)) rec.ReduxFairing = true;
            }
        }

        private static void Describe(Exception e, AuditRecord rec, AuditObservations obs)
        {
            obs.Threw = true;
            rec.ExceptionType = e.GetType().Name;
            rec.ExceptionMessage = e.Message ?? "";
            var frames = new List<StackFrameInfo>();
            var lines = new List<string>();
            try
            {
                foreach (var frame in new StackTrace(e, false).GetFrames() ?? new StackFrame[0])
                {
                    MethodBase method = null;
                    try
                    {
                        method = Harmony.GetMethodFromStackframe(frame) ?? frame.GetMethod();
                    }
                    catch (Exception)
                    {
                        method = frame.GetMethod();
                    }
                    var type = method?.DeclaringType;
                    var typeName = type != null ? type.Name : "?";
                    frames.Add(new StackFrameInfo(typeName, IsPartModule(type), method?.Name ?? "?"));
                    lines.Add(typeName + "." + (method?.Name ?? "?"));
                }
            }
            catch (Exception)
            {
                // A stack we cannot walk: the type and message are still recorded.
            }
            rec.Stack = lines.ToArray();
            rec.FailingModule = StackModuleLocator.FailingModule(frames);
        }

        /// <summary>A part module type, but not the <c>PartBehaviourModule</c> base itself: its <c>Init</c> wraps every
        /// module's <c>OnInitialize</c>.</summary>
        internal static bool IsPartModule(Type type) =>
            type != null && type != typeof(PartBehaviourModule) && typeof(IPartModule).IsAssignableFrom(type);

        private static Exception Unwrap(Exception e)
        {
            while (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
            return e;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        // ---- disposal ----

        private void Dispose(string name, GameObject root, GameObject createdTemplate, GameObject template, GameObject rebuiltPrefab,
            Ksp1ImportedPartRegistry.Entry entry, AuditRecord rec)
        {
            var log = KSP1PartModPlugin.Log;
            try
            {
                // OnDestroy: DestroyEntity and the message unsubscriptions.
                if (root != null) Object.DestroyImmediate(root);
            }
            catch (Exception e)
            {
                log?.LogError($"[KSP1PartMod] audit: {name}: could not destroy the audited part: {e}");
            }

            // Only a template we created is ours; ApplyOnGameObject(…, false) keeps the reference, but a swapped one
            // would be ours too.
            var templateKept = false;
            foreach (var t in new[] { createdTemplate, createdTemplate != null ? template : null })
            {
                if (t == null) continue;
                try
                {
                    if (VabUses(name, t)) templateKept = true;
                    else Object.DestroyImmediate(t);
                }
                catch (Exception e)
                {
                    templateKept = true;
                    log?.LogError($"[KSP1PartMod] audit: {name}: could not destroy the audit's template: {e}");
                }
                if (ReferenceEquals(template, createdTemplate)) break;
            }

            if (rebuiltPrefab == null) return;
            if (templateKept)
            {
                // The template shares the prefab's materials: the prefab stays resident with it.
                log?.LogWarning($"[KSP1PartMod] audit: {name}: template kept, rebuilt prefab left resident");
                return;
            }
            try
            {
                // Shared textures may be used by live parts: kept, then left to UnloadUnusedAssets.
                _targets.DestroyGraph(rebuiltPrefab, !Ksp1Textures.SharingArmed);
                if (entry != null && (entry.Prefab == rebuiltPrefab || entry.Prefab == null)) entry.Prefab = null;
            }
            catch (Exception e)
            {
                log?.LogError($"[KSP1PartMod] audit: {name}: could not release the rebuilt prefab: {e}");
            }
        }

        /// <summary>The VAB's own available part of that name holds this object as its model.</summary>
        private bool VabUses(string name, GameObject template)
        {
            var known = _builder.Stats?.AllKnownParts;
            if (known == null) return false;
            foreach (var p in known)
                if (p != null && p.Name == name && p.ModelPrefab == template) return true;
            return false;
        }

        // ---- log capture ----

        /// <summary><c>Application.logMessageReceived</c> while one part is processed (main thread, synchronous).
        /// <c>[KSP1 Import]</c> lines are counted apart; our own lines are ignored.</summary>
        private sealed class LogCapture
        {
            public int ImportWarnings, ImportErrors, LoggedErrors;
            public readonly List<string> ImportSamples = new List<string>();
            public readonly List<string> ErrorSamples = new List<string>();

            public void Attach() => Application.logMessageReceived += OnLog;
            public void Detach() => Application.logMessageReceived -= OnLog;

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition == null || condition.Contains("[KSP1PartMod]")) return;
                var error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                if (condition.Contains("[KSP1 Import]"))
                {
                    if (type == LogType.Warning) ImportWarnings++;
                    else if (error) ImportErrors++;
                    else return;
                    if (ImportSamples.Count < Samples) ImportSamples.Add(Sample(condition));
                }
                else if (error)
                {
                    LoggedErrors++;
                    if (ErrorSamples.Count < Samples) ErrorSamples.Add(Sample(condition));
                }
            }

            private static string Sample(string text) => AuditSummary.FirstLine(text, SampleChars);
        }
    }
}
