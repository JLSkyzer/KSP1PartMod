using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.Native.Science
{
    /// <summary>How a KSP1 experiment id found its KSP2 experiment (science plan (h)).</summary>
    public enum ScienceResolutionKind { Generated, Identical, Closest, Skipped }

    /// <summary>Why a KSP1 experiment gets no KSP2 one.</summary>
    public enum ScienceSkipReason { None, EvaKerbal, NoCounterpart, NoDefinitionNoCounterpart }

    /// <summary>The resolution of one KSP1 experiment id of a part (unique per part).</summary>
    public sealed class ScienceResolution
    {
        public string Ksp1Id { get; }
        /// <summary>Null when skipped.</summary>
        public string Ksp2Id { get; }
        public ScienceResolutionKind Kind { get; }
        public ScienceSkipReason SkipReason { get; }
        /// <summary>The first module of the part that uses the id.</summary>
        public int ModuleIndex { get; }
        /// <summary>The KSP2 id was already taken by an earlier id of the part: no second configuration.</summary>
        public bool DuplicateKsp2 { get; }

        public ScienceResolution(string ksp1Id, string ksp2Id, ScienceResolutionKind kind, ScienceSkipReason skipReason, int moduleIndex, bool duplicateKsp2)
        {
            Ksp1Id = ksp1Id;
            Ksp2Id = ksp2Id;
            Kind = kind;
            SkipReason = skipReason;
            ModuleIndex = moduleIndex;
            DuplicateKsp2 = duplicateKsp2;
        }
    }

    /// <summary>The clip KSP1 played for an experiment: the stock module's <c>FxModules</c> (indices of the part's <c>MODULE</c>s), or
    /// the DMagic Generic module itself (its 2b <c>ScienceToggle</c> entry) with its animation fields.</summary>
    public sealed class ScienceClip
    {
        public IReadOnlyList<int> FxModules { get; }
        /// <summary>The DMagic Generic module index; -1 for none.</summary>
        public int ToggleModuleIndex { get; }
        /// <summary>DMagic <c>experimentAnimation</c> (S: true when absent).</summary>
        public bool ExperimentAnimation { get; }
        /// <summary>DMagic <c>experimentWaitForAnimation</c> (false when absent).</summary>
        public bool WaitForAnimation { get; }
        /// <summary>DMagic <c>waitForAnimationTime</c>, s (0 when absent).</summary>
        public double WaitForAnimationTime { get; }

        public bool HasSource => FxModules.Count > 0 || ToggleModuleIndex >= 0;

        public ScienceClip(IEnumerable<int> fxModules, int toggleModuleIndex, bool experimentAnimation, bool waitForAnimation, double waitForAnimationTime)
        {
            FxModules = (fxModules ?? Enumerable.Empty<int>()).ToList();
            ToggleModuleIndex = toggleModuleIndex;
            ExperimentAnimation = experimentAnimation;
            WaitForAnimation = waitForAnimation;
            WaitForAnimationTime = waitForAnimationTime;
        }

        public static readonly ScienceClip None = new ScienceClip(null, -1, false, false, 0);
    }

    /// <summary>One <c>ExperimentConfiguration</c> of the part's <c>Data_ScienceExperiment</c>, as plain values.</summary>
    public sealed class ScienceConfiguration
    {
        public string ExperimentDefinitionId { get; internal set; }
        public string Ksp1Id { get; internal set; }
        public ScienceResolutionKind Kind { get; internal set; }
        public int ModuleIndex { get; internal set; } = -1;
        /// <summary>The KSP1 module name.</summary>
        public string Module { get; internal set; }
        public int CrewRequired { get; internal set; }
        /// <summary>s; 0 completes at once.</summary>
        public double TimeToComplete { get; internal set; }
        /// <summary>The experiment completes once its clip is open (KSP2's deploy gate).</summary>
        public bool Waits { get; internal set; }
        public bool ContextualExperiment => false;
        public bool DenyInterruption => false;
        public string DeployedBoolAnimatorParamName => "";
        public string CFXName => "";
        public string SFXEvent => "";
        public ScienceClip Clip { get; internal set; } = ScienceClip.None;

        /// <summary>Tests and runtime glue: a configuration with only an id and a clip.</summary>
        public static ScienceConfiguration ForTests(string ksp2Id, ScienceClip clip) =>
            new ScienceConfiguration { ExperimentDefinitionId = ksp2Id, Clip = clip ?? ScienceClip.None };
    }

    /// <summary>The science of one part: its configurations, the resolution of each KSP1 id, report storage, and the notes.</summary>
    public sealed class SciencePart
    {
        public IReadOnlyList<ScienceConfiguration> Configurations => _configurations;
        public IReadOnlyList<ScienceResolution> Resolutions => _resolutions;
        public bool HasModule => _configurations.Count > 0;
        /// <summary><c>PartData.HasReportStorage</c> should be set.</summary>
        public bool ReportStorage { get; internal set; }
        public bool HasContainer { get; internal set; }
        /// <summary>The experiment modules read.</summary>
        public int ExperimentModules { get; internal set; }
        /// <summary>Modules whose KSP1 id an earlier module of the part already used.</summary>
        public int MergedModules { get; internal set; }
        /// <summary>KSP1 ids whose KSP2 id was already taken on the part.</summary>
        public int DuplicateKsp2 { get; internal set; }
        /// <summary>As KSP2's own pod (S, checked in game).</summary>
        public bool NotifyOnCompletion => true;
        public int CrewRequired => _configurations.Count == 0 ? 0 : _configurations.Max(c => c.CrewRequired);
        public IReadOnlyList<string> Notes => _notes;
        public IReadOnlyList<string> NotReproduced => _notReproduced;

        internal readonly List<ScienceConfiguration> _configurations = new List<ScienceConfiguration>();
        internal readonly List<ScienceResolution> _resolutions = new List<ScienceResolution>();
        internal readonly List<string> _notes = new List<string>();
        internal readonly List<string> _notReproduced = new List<string>();

        internal void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }
    }

    /// <summary>The KSP1 experiment and container modules of a part into one <c>Module_ScienceExperiment</c> (science plan (h)).
    /// For each experiment module, in cfg order, the first rule that applies wins:
    /// <list type="number">
    /// <item>skipped when the id is in <c>science_skip_ids</c>;</item>
    /// <item>identical when <c>science_native_ids</c> maps it to a KSP2 id the game has;</item>
    /// <item>generated <c>KSP1_&lt;id&gt;</c> with <c>science_generate_definitions</c> and a built definition;</item>
    /// <item>closest: from the definition's mask when there is one (<see cref="NativeExperiments.Closest"/>), else the table
    /// <c>science_fallback_ids</c>;</item>
    /// <item>otherwise skipped.</item>
    /// </list>
    /// A KSP1 id repeated on the part is read once (merged); a KSP2 id repeated is kept once. Report storage: a container or a
    /// resolved experiment (<c>science_report_storage</c>). <c>ksp1_science</c> off: nothing. Pure.</summary>
    public static class ScienceMapping
    {
        public const string StockModule = "ModuleScienceExperiment";
        public const string DMagicModule = "DMModuleScienceAnimate";
        public const string DMagicGenericModule = "DMModuleScienceAnimateGeneric";
        public const string ContainerModule = "ModuleScienceContainer";
        public const string NativeModule = "Module_ScienceExperiment";
        /// <summary>The shortest wait for a clip, s.</summary>
        public const double MinWait = 0.1;

        public static readonly IReadOnlyList<string> ExperimentModuleNames = Array.AsReadOnly(new[] { StockModule, DMagicModule, DMagicGenericModule });

        /// <summary>Keys handled, native, or about UI text only: no note.</summary>
        private static readonly string[] QuietKeys =
        {
            "name", "experimentID", "xmitDataScalar", "FxModules", "usageReqMaskInternal", "usageReqMaskExternal", "useStaging",
            "useActionGroups", "hideUIwhenUnavailable", "experimentActionName", "resetActionName", "reviewActionName",
            "collectActionName", "transmitWarningText", "dataIsCollectable", "interactionRange", "rerunnable", "resettable",
            "resettableOnEVA", "animationName", "experimentAnimation", "experimentWaitForAnimation", "waitForAnimationTime",
            "oneWayAnimation", "animSpeed", "showEditorEvents", "sitMask", "bioMask", "resourceExpCost", "customFailMessage",
            "deployingMessage", "startEventGUIName", "endEventGUIName", "toggleEventGUIName", "showStartEvent", "showEndEvent",
            "showToggleEvent", "endEventsDescription", "collectWarningText", "includeExperimentInAG", "experimentActionName",
            "tag", "moduleIsEnabled", "isEnabled",
        };

        /// <summary>The DMagic fields whose behaviour is not readable from the cfg (plan (f)), noted when present.</summary>
        private static readonly string[] DMagicNotReproduced =
        {
            "keepDeployedMode", "planetaryMask", "excludeAtmosphere", "excludeAtmosphereMessage", "asteroidReports", "asteroidTypeDependent",
            "requiredParts", "requiredPartsMessage", "experimentsLimit", "scienceValueRatio", "totalScienceLevel", "resourceToUse",
        };

        private static readonly string[] ContainerQuietKeys =
        {
            "name", "reviewActionName", "storeActionName", "collectActionName", "evaOnlyStorage", "storageRange", "canTransferInVessel",
            "allowRepeatedSubjects", "capacity", "canBeTransferredToInVessel", "tag",
        };

        private static readonly string[] SpecialisedDMagic =
        {
            "DMSIGINT", "DMSoilMoisture", "DMSeismicSensor", "DMSeismicHammer", "DMAsteroidScanner", "DMAnomalyScanner", "DMBathymetry",
            "DMBioDrill", "DMReconScope", "DMXRayDiffract", "DMRoverGooMat", "DMSolarCollector", "DMMagBoomModule", "DMEnviroSensor",
        };

        private static readonly string[] GroundScience = { "ModuleGroundExperiment", "ModuleGroundSciencePart", "ModuleGroundExpControl" };

        public static bool IsExperimentModule(IKsp1Node module) =>
            module != null && ExperimentModuleNames.Any(n => IsruCfg.IsModule(module, n));

        public static bool IsDMagic(IKsp1Node module) =>
            IsruCfg.IsModule(module, DMagicModule) || IsruCfg.IsModule(module, DMagicGenericModule);

        public static bool IsContainer(IKsp1Node module) => IsruCfg.IsModule(module, ContainerModule);

        /// <summary>The out-of-scope category of a KSP1 module (counted, never imported): <c>ModuleScienceLab</c>,
        /// <c>ModuleScienceConverter</c>, <c>ModuleTestSubject</c>, <c>ground science</c>, <c>specialised DMagic</c>; null otherwise.</summary>
        public static string OutOfScope(string moduleName)
        {
            var n = moduleName?.Trim() ?? "";
            if (n.Length == 0) return null;
            foreach (var x in new[] { "ModuleScienceLab", "ModuleScienceConverter", "ModuleTestSubject" })
                if (string.Equals(n, x, StringComparison.OrdinalIgnoreCase)) return x;
            if (GroundScience.Any(g => string.Equals(n, g, StringComparison.OrdinalIgnoreCase))) return "ground science";
            if (SpecialisedDMagic.Any(d => string.Equals(n, d, StringComparison.OrdinalIgnoreCase))
                || n.StartsWith("DMUniversalStorage", StringComparison.OrdinalIgnoreCase))
                return "specialised DMagic";
            return null;
        }

        /// <param name="modules">Every <c>MODULE</c> of the part, in cfg order (the indices of <c>FxModules</c>).</param>
        /// <param name="definitions">The KSP1 definitions read, by KSP1 id (built or not).</param>
        /// <param name="natives">The KSP2 experiments the game has.</param>
        public static SciencePart Resolve(IReadOnlyList<IKsp1Node> modules, IReadOnlyDictionary<string, ScienceDefinitionModel> definitions,
            NativeExperiments natives, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            natives = natives ?? NativeExperiments.Redux26w42c;
            definitions = definitions ?? new Dictionary<string, ScienceDefinitionModel>();
            modules = modules ?? Array.Empty<IKsp1Node>();
            var p = new SciencePart();
            if (!settings.Ksp1Science)
            {
                p._notes.Add("ksp1_science off: no experiment and no report storage");
                return p;
            }
            var seenKsp1 = new HashSet<string>(StringComparer.Ordinal);
            var takenKsp2 = new Dictionary<string, string>(StringComparer.Ordinal); // KSP2 id -> first KSP1 id
            for (var i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (IsContainer(m))
                {
                    p.HasContainer = true;
                    ReadContainer(m, p);
                    continue;
                }
                if (!IsExperimentModule(m)) continue;
                p.ExperimentModules++;
                var moduleName = IsruCfg.Value(m, "name");
                var id = IsruCfg.Value(m, "experimentID");
                if (string.IsNullOrEmpty(id))
                {
                    p._notes.Add($"{moduleName} (module {i}): no experimentID, ignored");
                    continue;
                }
                ReadKeys(m, moduleName, p);
                if (!seenKsp1.Add(id))
                {
                    p.MergedModules++;
                    p._notes.Add($"{moduleName} (module {i}): {id} already read from an earlier module, merged");
                    continue;
                }
                var (kind, ksp2, reason) = ResolveId(id, m, definitions, natives, settings, p);
                if (kind == ScienceResolutionKind.Skipped)
                {
                    p._resolutions.Add(new ScienceResolution(id, null, kind, reason, i, false));
                    continue;
                }
                if (takenKsp2.TryGetValue(ksp2, out var earlier))
                {
                    p.DuplicateKsp2++;
                    p._resolutions.Add(new ScienceResolution(id, ksp2, kind, ScienceSkipReason.None, i, true));
                    p._notes.Add($"{earlier} and {id} share {ksp2}: kept once, they score once per location together");
                    continue;
                }
                takenKsp2[ksp2] = id;
                p._resolutions.Add(new ScienceResolution(id, ksp2, kind, ScienceSkipReason.None, i, false));
                p._configurations.Add(Configuration(m, i, moduleName, id, ksp2, kind, settings, p));
            }
            p.ReportStorage = settings.ScienceReportStorage && (p.HasContainer || p.HasModule);
            return p;
        }

        private static (ScienceResolutionKind, string, ScienceSkipReason) ResolveId(string id, IKsp1Node module,
            IReadOnlyDictionary<string, ScienceDefinitionModel> definitions, NativeExperiments natives, AnimationSettings settings, SciencePart p)
        {
            // 1. skip list
            if (settings.ScienceSkipIds.Contains(id))
            {
                var eva = ScienceDefaults.EvaKerbalIds.Contains(id);
                p._notes.Add($"{id}: in science_skip_ids, no experiment ({(eva ? "KSP2 kerbals run their own" : "no KSP2 counterpart")})");
                return (ScienceResolutionKind.Skipped, null, eva ? ScienceSkipReason.EvaKerbal : ScienceSkipReason.NoCounterpart);
            }
            // 2. identical native
            if (settings.ScienceNativeIds.TryGetValue(id, out var native))
            {
                if (natives.Contains(native)) return (ScienceResolutionKind.Identical, native, ScienceSkipReason.None);
                p._notes.Add($"{id}: science_native_ids gives {native}, which the game does not have");
            }
            definitions.TryGetValue(id, out var def);
            // 3. generated
            if (settings.ScienceGenerateDefinitions && def != null && def.IsBuilt)
                return (ScienceResolutionKind.Generated, def.ExperimentId, ScienceSkipReason.None);
            // 4. closest
            if (def != null)
            {
                var mask = IsDMagic(module) ? DefinitionOverrides.Mask(module, "sitMask") ?? def.SituationMask : def.SituationMask;
                if (def.IsBuilt || def.SituationMask != 0 || mask != 0)
                {
                    var closest = natives.Closest(mask, KindOf(module, settings));
                    if (closest != null)
                    {
                        p._notes.Add($"{id}: the closest KSP2 experiment to situationMask {mask} is {closest.Id}" +
                                     (def.IsBuilt ? " (science_generate_definitions off)" : $" (definition not generated: {def.SkipReason})"));
                        return (ScienceResolutionKind.Closest, closest.Id, ScienceSkipReason.None);
                    }
                }
                p._notes.Add($"{id}: no KSP2 experiment matches its situations, skipped");
                return (ScienceResolutionKind.Skipped, null, ScienceSkipReason.NoCounterpart);
            }
            if (settings.ScienceFallbackIds.TryGetValue(id, out var fallback) && natives.Contains(fallback))
            {
                p._notes.Add($"{id}: no definition on disk, science_fallback_ids gives {fallback}");
                return (ScienceResolutionKind.Closest, fallback, ScienceSkipReason.None);
            }
            // 5. nothing
            p._notes.Add($"{id}: no definition on disk and no KSP2 counterpart, skipped");
            return (ScienceResolutionKind.Skipped, null, ScienceSkipReason.NoDefinitionNoCounterpart);
        }

        /// <summary>The report kind a module's transmit share asks for (as the definition builder reads it).</summary>
        public static ScienceExperimentKind KindOf(IKsp1Node module, AnimationSettings settings)
        {
            if (settings != null && !settings.ScienceTransmitSplit) return ScienceExperimentKind.Data;
            var x = IsruCfg.Number(module, "xmitDataScalar");
            if (!x.HasValue || x.Value >= ScienceDefinitionBuilder.FullTransmit) return ScienceExperimentKind.Data;
            return x.Value <= 0 ? ScienceExperimentKind.Sample : ScienceExperimentKind.Both;
        }

        private static ScienceConfiguration Configuration(IKsp1Node m, int index, string moduleName, string id, string ksp2,
            ScienceResolutionKind kind, AnimationSettings settings, SciencePart p)
        {
            var c = new ScienceConfiguration
            {
                ExperimentDefinitionId = ksp2,
                Ksp1Id = id,
                Kind = kind,
                ModuleIndex = index,
                Module = moduleName,
            };
            var usage = IsruCfg.Number(m, "usageReqMaskInternal");
            var mask = usage.HasValue && usage.Value > 0 ? (long)usage.Value : 0;
            if ((mask & (4 | 8)) != 0) c.CrewRequired = 1;
            if ((mask & 8) != 0) p._notes.Add($"{id}: usageReqMaskInternal {mask} asks for a scientist; KSP2 counts crew, not traits (1 kerbal required)");

            if (IsruCfg.IsModule(m, DMagicGenericModule))
            {
                var anim = IsruCfg.Bool(m, "experimentAnimation") ?? true;
                var wait = IsruCfg.Bool(m, "experimentWaitForAnimation") ?? false;
                var time = IsruCfg.Number(m, "waitForAnimationTime") ?? 0;
                c.Clip = new ScienceClip(null, index, anim, wait, time);
                c.Waits = anim && wait && settings.Enabled && settings.ScienceDeployAnimations && settings.ScienceWaitForAnimation;
                if (c.Waits) c.TimeToComplete = Math.Max(time, MinWait);
            }
            else if (IsruCfg.IsModule(m, DMagicModule))
            {
                p._notes.Add($"{DMagicModule} (module {index}): no clip entry for this module (a follow-up), {id} runs without a clip");
            }
            else
            {
                var fx = IsruCfg.Value(m, "FxModules");
                if (!string.IsNullOrEmpty(fx))
                {
                    var list = new List<int>();
                    foreach (var token in fx.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 0)
                            list.Add(n);
                        else p._notes.Add($"{id}: FxModules '{fx}': '{token}' is not a module index, ignored");
                    }
                    c.Clip = new ScienceClip(list, -1, false, false, 0);
                }
            }
            return c;
        }

        private static void ReadKeys(IKsp1Node m, string moduleName, SciencePart p)
        {
            foreach (var kv in m.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                var value = IsruCfg.Text(kv.Value);
                switch (key)
                {
                    case "rerunnable":
                        if (IsruCfg.Bool(m, key) == false)
                            p.NotReproducedField(key, $"{moduleName}: rerunnable = False not reproduced (a KSP2 experiment runs again; it scores once per location)");
                        break;
                    case "resettable":
                    case "resettableOnEVA":
                        if (IsruCfg.Bool(m, key) == true)
                            p.NotReproducedField(key, $"{moduleName}: {key} = {value} not reproduced (KSP2 has no reset)");
                        break;
                    case "resourceExpCost":
                        if (IsruCfg.Number(m, key) > 0)
                            p.NotReproducedField(key, $"{moduleName}: resourceExpCost = {value} not reproduced (the KSP2 experiment costs no resource)");
                        break;
                    default:
                        if (DMagicNotReproduced.Contains(key)) p.NotReproducedField(key, $"{moduleName}: {key} = {value} not reproduced");
                        else if (!QuietKeys.Contains(key)) p.NotReproducedField(key, $"{moduleName}: {key} not reproduced");
                        break;
                }
            }
            foreach (var child in m.AllNodes)
            {
                var kind = child.Name?.Trim() ?? "";
                p.NotReproducedField("node " + kind, $"{moduleName}: node {kind} not reproduced");
            }
        }

        private static void ReadContainer(IKsp1Node m, SciencePart p)
        {
            foreach (var kv in m.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                if (key == "name" || key == "tag" || key.EndsWith("ActionName", StringComparison.Ordinal)) continue;
                if (ContainerQuietKeys.Contains(key))
                    p.NotReproducedField(key, $"{ContainerModule}: {key} not reproduced (KSP2 keeps one report pool per vessel)");
                else p.NotReproducedField(key, $"{ContainerModule}: {key} not reproduced");
            }
        }
    }
}
