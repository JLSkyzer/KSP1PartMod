using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KSP1PartMod.Animation.Settings
{
    /// <summary>The player's <c>animations.json</c>. Keys starting with '_' at the top level, and <c>_note</c>
    /// inside <c>shader_property_map</c>, are comments.</summary>
    public sealed class AnimationSettings
    {
        /// <summary>Master switch for the KSP1 animations.</summary>
        public bool Enabled { get; }
        /// <summary>Patch Redux's <c>.mu</c> reader so models of versions 0 to 2 load.</summary>
        public bool FixMuReader { get; }
        /// <summary>Take the first deployable away from Redux's module and play it ourselves.</summary>
        public bool OwnFirstDeployable { get; }
        /// <summary>KSP1 material property -> KSP2 property names to try, in order (ordinal keys). A map in the
        /// file replaces the default one.</summary>
        public IReadOnlyDictionary<string, string[]> ShaderPropertyMap { get; }
        /// <summary>One-shot material dump of a few KSP1 parts in the VAB (<c>diag:</c> lines), independent of
        /// <see cref="Enabled"/>.</summary>
        public bool Diagnostics { get; }
        /// <summary>Draw the clip of each Redux deployable (and parachute) bound to Redux's Mecanim copy, which does not
        /// move (<c>follow_redux_deployables</c>).</summary>
        public bool FollowReduxDeployables { get; }
        /// <summary>Hide renderers of KSP1 parts whose materials are all null (<c>hide_material_less_renderers</c>).</summary>
        public bool HideMaterialLessRenderers { get; }
        /// <summary>Render KSP1 part icons with the KSP2 shader swap (<c>fix_part_icons</c>).</summary>
        public bool FixPartIcons { get; }
        /// <summary>Second vertical bar of KSP1 part types in the VAB when a KSP1 mod category is selected
        /// (<c>subcategory_bar</c>).</summary>
        public bool SubcategoryBar { get; }
        /// <summary>VAB speed of our animations (<c>editor_animation_speed</c>, see
        /// <see cref="Behaviours.EditorSpeed"/>): 1 real time (default), 0 KSP1's editor values.</summary>
        public float EditorAnimationSpeed { get; }
        /// <summary>Material tracks of clips (<c>material_curves</c>); off: phase 1 behaviour.</summary>
        public bool MaterialCurves => _switches2a.MaterialCurves;
        /// <summary>Light tracks of clips (<c>light_curves</c>).</summary>
        public bool LightCurves => _switches2a.LightCurves;
        /// <summary>Keyword <c>_EMISSION</c> and white <c>_EmissionMap</c> when missing (<c>emission_setup</c>).</summary>
        public bool EmissionSetup => _switches2a.EmissionSetup;
        /// <summary><c>FXModuleThrottleEffects</c> drives its colour changers (<c>throttle_effects</c>).</summary>
        public bool ThrottleEffects => _switches2a.ThrottleEffects;
        /// <summary>Heat entries and their tracks (<c>heat_glow</c>).</summary>
        public bool HeatGlow => _switches2a.HeatGlow;
        /// <summary>Opt-in cost counter (<c>perf_counter</c>, default false): times every <c>Drive</c> and the channel flush
        /// and logs <c>anim: perf: …</c> every 60 s.</summary>
        public bool PerfCounter => _switches2a.PerfCounter;
        /// <summary>KSP1 module names never taken (<c>skip_modules</c>), trimmed, distinct, ordinal.</summary>
        public IReadOnlyCollection<string> SkipModules => _switches2a.SkipModules;
        /// <summary>Let Redux's <c>Module_Fairing</c> finish its initialization on KSP1 parts (<c>fix_fairing_init</c>):
        /// empty base colliders instead of null, and no <c>OnMaterialLoaded</c> on a part without <c>Module_Color</c>.</summary>
        public bool FixFairingInit => _switches041.FixFairingInit;
        /// <summary>Max thrust, Isp and propellant in the VAB tooltip of KSP1 engines (<c>engine_tooltip_stats</c>).</summary>
        public bool EngineTooltipStats => _switches041.EngineTooltipStats;
        /// <summary>The picker panel grows by the sub-category bar's width so the grid keeps whole columns
        /// (<c>subcategory_bar_keeps_grid</c>).</summary>
        public bool SubcategoryBarKeepsGrid => _switches041.SubcategoryBarKeepsGrid;
        /// <summary>One copy of each KSP1 texture file for every rebuilt part, instead of one per part
        /// (<c>share_ksp1_textures</c>).</summary>
        public bool ShareKsp1Textures => _switches041.ShareKsp1Textures;
        /// <summary>KSP1 textures that stay readable follow the game's texture quality (<c>ksp1_textures_follow_quality</c>).</summary>
        public bool Ksp1TexturesFollowQuality => _switches041.Ksp1TexturesFollowQuality;
        /// <summary>Opt-in (default false): raise the texture streaming budget by the KSP1 texture memory, capped by the card's
        /// memory (<c>raise_streaming_budget</c>).</summary>
        public bool RaiseStreamingBudget => _switches041.RaiseStreamingBudget;

        /// <summary>Keep the KSP1 mesh of engine shrouds (<c>ModuleJettison</c>) and show it by the KSP1 rules, instead of
        /// Redux's <c>Module_Fairing</c> (<c>ksp1_engine_shrouds</c>, default true). It acts only with <see cref="Enabled"/>
        /// (<see cref="ShroudSwitch.Arm"/>).</summary>
        public bool Ksp1EngineShrouds => _switches041.Ksp1EngineShrouds;

        /// <summary>Opt-in (default false): audit every KSP1 part once at the first VAB entry (<c>audit_on_vab_entry</c>).</summary>
        public bool AuditOnVabEntry => _audit.OnVabEntry;
        /// <summary>Milliseconds per frame the audit may use (<c>audit_frame_budget_ms</c>, default 8, 1 to 50).</summary>
        public float AuditFrameBudgetMs => _audit.FrameBudgetMs;
        /// <summary>Parts between two unloads of the audit's textures (<c>audit_unload_every</c>, default 100, 10 to 1000).</summary>
        public int AuditUnloadEvery => _audit.UnloadEvery;

        /// <summary>Entries for the KSP1 plugin modules of phase 2b (DeployableEngines, Bluedog_DB, DMagic science,
        /// FXModuleAnimateRCS) (<c>plugin_modules</c>).</summary>
        public bool PluginModules => _switches2a.PluginModules;
        /// <summary>B9PartSwitch <c>moduleActive = false</c> overrides gate our entries with the part's variant
        /// (<c>b9_module_gates</c>).</summary>
        public bool B9ModuleGates => _switches2a.B9ModuleGates;

        /// <summary>ModuleManager safety net: names blocked by the plugin rule (<c>mm_plugin_safety_net</c>, default true).</summary>
        public bool MmPluginSafetyNet => _mm.SafetyNet;
        /// <summary>Plugin names always blocked (<c>mm_plugin_folders</c>), trimmed, distinct, case-insensitive.</summary>
        public IReadOnlyCollection<string> MmPluginFolders => _mm.PluginFolders;
        /// <summary>Folders with a DLL and no PART are blocked too (<c>mm_plugin_folders_auto</c>, default true).</summary>
        public bool MmPluginFoldersAuto => _mm.PluginFoldersAuto;
        /// <summary>Names never blocked (<c>mm_needs_keep</c>), trimmed, distinct, case-insensitive.</summary>
        public IReadOnlyCollection<string> MmNeedsKeep => _mm.NeedsKeep;
        /// <summary>A root patch whose BEFORE/AFTER names an absent mod is dropped, as MM does
        /// (<c>mm_before_after_need_mod</c>, default true).</summary>
        public bool MmBeforeAfterNeedMod => _mm.BeforeAfterNeedMod;
        /// <summary>MM's <c>*=</c>, <c>+=</c>, <c>-=</c>, <c>/=</c> (<c>mm_arithmetic</c>, default true).</summary>
        public bool MmArithmetic => _mm.Arithmetic;
        /// <summary>MM's <c>,n</c>, <c>,-n</c>, <c>,*</c> on values and child nodes (<c>mm_index_selectors</c>, default true).</summary>
        public bool MmIndexSelectors => _mm.IndexSelectors;
        /// <summary>Unindexed edits hit the first match only, as MM does (<c>mm_edit_first_match</c>, default false: every
        /// match, as Redux does).</summary>
        public bool MmEditFirstMatch => _mm.EditFirstMatch;
        /// <summary>KSP1 <c>ModuleToggleCrossfeed</c> imported as KSP2's <c>Module_ToggleCrossfeed</c>
        /// (<c>ksp1_crossfeed_toggle</c>, default true).</summary>
        public bool Ksp1CrossfeedToggle => _mm.CrossfeedToggle;
        /// <summary>KSP1 <c>ModuleAblator</c> imported as KSP2's <c>Module_Heatshield</c> (<c>ksp1_heatshields</c>, default true).</summary>
        public bool Ksp1Heatshields => _mm.Heatshields;
        /// <summary>Multiplies the computed heat shield <c>Rate</c> (<c>ablator_rate_scale</c>, default 1, 0.01 to 100).</summary>
        public double AblatorRateScale => _mm.AblatorRateScale;
        /// <summary>The <c>shieldChar</c> colour changer follows the part's ablator (<c>ablator_char</c>, default true; needs
        /// <c>ksp1_heatshields</c> and, being an animation entry, <c>enabled</c>).</summary>
        public bool AblatorChar => _mm.AblatorChar;

        /// <summary>B9PartSwitch <c>addedMass</c> and tank dry mass become a V-SwiFT <c>MassModifier</c> (<c>b9_mass</c>).</summary>
        public bool B9Mass => _b9.Mass;
        /// <summary>B9 tank types set the part's resources per subtype (<c>b9_resources</c>).</summary>
        public bool B9Resources => _b9.Resources;
        /// <summary>B9 <c>MODULE</c>/<c>DATA</c> edits reach the native module (<c>b9_module_data</c>).</summary>
        public bool B9ModuleData => _b9.ModuleData;
        /// <summary>B9 <c>moduleActive = false</c> gives the native off form (<c>b9_module_active</c>; needs
        /// <see cref="B9ModuleData"/>).</summary>
        public bool B9ModuleActive => _b9.ModuleActive;
        /// <summary>B9 <c>maxTemp</c>, <c>skinMaxTemp</c>, <c>crashTolerance</c>, <c>CoMOffset</c> in flight
        /// (<c>b9_part_scalars</c>).</summary>
        public bool B9PartScalars => _b9.PartScalars;
        /// <summary>Sets (and a <c>Module_PartSwitch</c>) for the B9 modules Redux skips (<c>b9_new_part_switches</c>).</summary>
        public bool B9NewPartSwitches => _b9.NewPartSwitches;

        /// <summary>KSP1 <c>ModuleResourceConverter</c>s imported as one native <c>Module_ResourceConverter</c>, one mode each
        /// (<c>ksp1_converters</c>, default true).</summary>
        public bool Ksp1Converters => _isru.Converters;
        /// <summary>Planetary KSP1 <c>ModuleResourceHarvester</c>s imported as Redux's <c>Module_Mine</c> (<c>ksp1_drills</c>).</summary>
        public bool Ksp1Drills => _isru.Drills;
        /// <summary>KSP1 <c>ModuleOrbitalSurveyor</c> imported as Redux's <c>Module_ResourceScanner</c> (<c>ksp1_survey_scanners</c>).</summary>
        public bool Ksp1SurveyScanners => _isru.SurveyScanners;
        /// <summary>KSP1 <c>ModuleResourceScanner</c> with <c>RequiresUnlock = false</c> imported as Redux's
        /// <c>Module_ResourceScanner</c> (<c>ksp1_resource_scanners</c>).</summary>
        public bool Ksp1ResourceScanners => _isru.ResourceScanners;
        /// <summary>The native resource KSP1 Ore becomes (<c>ksp1_ore_resource</c>, default <c>CarbonRegolith</c>), trimmed.</summary>
        public string Ksp1OreResource => _isru.OreResource;
        /// <summary>The resource the Ore remap sets, or null when <see cref="Ksp1OreResource"/> is empty or <c>TestRocks</c>
        /// (Redux's own mapping kept).</summary>
        public string OreRemapTarget =>
            _isru.OreResource.Length == 0 || string.Equals(_isru.OreResource, ReduxOreResource, StringComparison.OrdinalIgnoreCase)
                ? null
                : _isru.OreResource;
        /// <summary>Multiplies the yield of KSP1 drills, which is derived from the KSP2 drill per EC/s (<c>harvest_multiplier</c>, default
        /// 1, 0.01 to 100).</summary>
        public double HarvestMultiplier => _isru.HarvestMultiplier;
        /// <summary>Multiplies the heat of converters and drills (<c>isru_heat_scale</c>, default 1, 0 to 10; 0 = no heat).</summary>
        public double IsruHeatScale => _isru.HeatScale;
        /// <summary>A <c>ModuleAnimationGroup</c> active clip plays while the part's native module runs
        /// (<c>isru_active_animation</c>, default true; needs <see cref="Enabled"/>).</summary>
        public bool IsruActiveAnimation => _isru.ActiveAnimation;
        /// <summary>KSP1 converter modes keep their KSP1 rates (<c>isru_ksp1_rates</c>, default false: their rates are scaled to the
        /// KSP2 converter's consumption per EC/s, as the drills follow the KSP2 drill's yield per EC/s).</summary>
        public bool IsruKsp1Rates => _isru.Ksp1Rates;

        /// <summary>Redux's own mapping of KSP1 Ore.</summary>
        public const string ReduxOreResource = "TestRocks";

        /// <summary>The ISRU keys of 0.10.0, read together.</summary>
        private sealed class SwitchesIsru
        {
            public bool Converters = true, Drills = true, SurveyScanners = true, ResourceScanners = true, ActiveAnimation = true;
            public bool Ksp1Rates;
            public string OreResource = "CarbonRegolith";
            public double HarvestMultiplier = 1.0, HeatScale = 1.0;
        }

        private readonly SwitchesIsru _isru;

        /// <summary>The <c>b9_*</c> keys of 0.9.0, read together.</summary>
        private sealed class SwitchesB9
        {
            public bool Mass = true, Resources = true, ModuleData = true, ModuleActive = true, PartScalars = true, NewPartSwitches = true;
        }

        private readonly SwitchesB9 _b9;

        /// <summary>The keys of 0.8.0 (ModuleManager and native modules), read together.</summary>
        private sealed class SwitchesMm
        {
            public bool SafetyNet = true, PluginFoldersAuto = true, BeforeAfterNeedMod = true, Arithmetic = true, IndexSelectors = true;
            public bool CrossfeedToggle = true, Heatshields = true, AblatorChar = true;
            public float AblatorRateScale = 1f;
            public bool EditFirstMatch;
            public IReadOnlyCollection<string> PluginFolders = ReadOnlyNames(ModuleManager.MmPluginRule.DefaultCurated);
            public IReadOnlyCollection<string> NeedsKeep = ReadOnlyNames(ModuleManager.MmPluginRule.DefaultKeep);
        }

        private static IReadOnlyCollection<string> ReadOnlyNames(IEnumerable<string> names) =>
            new ReadOnlyCollection<string>(names.ToList());

        /// <summary>The phase 2a and 2b keys, read together.</summary>
        private sealed class Switches2a
        {
            public bool MaterialCurves = true, LightCurves = true, EmissionSetup = true, ThrottleEffects = true, HeatGlow = true;
            public bool PerfCounter;
            public IReadOnlyCollection<string> SkipModules = NoModules;
            public bool PluginModules = true, B9ModuleGates = true;
        }

        /// <summary>The 0.4.1 fix switches and the later per-feature switches, read together.</summary>
        private sealed class Switches041
        {
            public bool FixFairingInit = true, EngineTooltipStats = true, SubcategoryBarKeepsGrid = true;
            public bool ShareKsp1Textures = true, Ksp1TexturesFollowQuality = true, Ksp1EngineShrouds = true;
            public bool RaiseStreamingBudget;
        }

        /// <summary>The audit-mode keys, read together.</summary>
        private sealed class SwitchesAudit
        {
            public bool OnVabEntry;
            public float FrameBudgetMs = 8f;
            public int UnloadEvery = 100;
        }

        private readonly Switches041 _switches041;
        private readonly SwitchesMm _mm;
        private readonly SwitchesAudit _audit;

        private static readonly IReadOnlyCollection<string> NoModules = new ReadOnlyCollection<string>(new string[0]);
        private readonly Switches2a _switches2a;

        /// <summary><c>_EmissionColor</c> first: the property KSP1 parts' KSP2 shaders were seen with in game (spec 2a.6).
        /// Each settings object gets its own copy.</summary>
        private static IReadOnlyDictionary<string, string[]> DefaultShaderPropertyMap() =>
            ReadOnly(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["_EmissiveColor"] = new[] { "_EmissionColor", "_EmissiveColor" },
                ["_TintColor"] = new[] { "_TintColor" },
                ["_Color"] = new[] { "_Color" },
                ["_BurnColor"] = new[] { "_BurnColor" },
            });

        public static readonly AnimationSettings Defaults =
            new AnimationSettings(true, true, false, DefaultShaderPropertyMap(), true, true, true, true, true, Behaviours.EditorSpeed.RealTime,
                new Switches2a(), new Switches041(), new SwitchesAudit(), new SwitchesMm(), new SwitchesB9(), new SwitchesIsru());

        private AnimationSettings(bool enabled, bool fixMuReader, bool ownFirstDeployable,
            IReadOnlyDictionary<string, string[]> shaderPropertyMap, bool diagnostics, bool followRedux,
            bool hideMaterialLess, bool fixIcons, bool subcategoryBar, float editorAnimationSpeed, Switches2a switches2a,
            Switches041 switches041, SwitchesAudit audit, SwitchesMm mm, SwitchesB9 b9, SwitchesIsru isru)
        {
            _isru = isru;
            _b9 = b9;
            _mm = mm;
            _audit = audit;
            _switches2a = switches2a;
            _switches041 = switches041;
            EditorAnimationSpeed = editorAnimationSpeed;
            SubcategoryBar = subcategoryBar;
            FollowReduxDeployables = followRedux;
            HideMaterialLessRenderers = hideMaterialLess;
            FixPartIcons = fixIcons;
            Diagnostics = diagnostics;
            Enabled = enabled;
            FixMuReader = fixMuReader;
            OwnFirstDeployable = ownFirstDeployable;
            ShaderPropertyMap = shaderPropertyMap;
        }

        /// <summary>Never throws. Empty text gives <see cref="Defaults"/> with no error; bad JSON gives
        /// <see cref="Defaults"/> and an error; a key of the wrong type keeps its default and is reported, as is an
        /// unknown key. error is null when everything was read.</summary>
        public static AnimationSettings Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) return Defaults;
            try
            {
                if (!(JToken.Parse(json) is JObject root))
                {
                    error = "animations.json ignored, settings root is not a JSON object";
                    return Defaults;
                }
                var problems = new List<string>();
                bool enabled = Defaults.Enabled, fix = Defaults.FixMuReader, own = Defaults.OwnFirstDeployable,
                    diagnostics = Defaults.Diagnostics, follow = Defaults.FollowReduxDeployables,
                    hide = Defaults.HideMaterialLessRenderers, icons = Defaults.FixPartIcons,
                    subcategories = Defaults.SubcategoryBar;
                var editorSpeed = Defaults.EditorAnimationSpeed;
                IReadOnlyDictionary<string, string[]> map = null;
                var s2a = new Switches2a();
                var s041 = new Switches041();
                var audit = new SwitchesAudit();
                var mm = new SwitchesMm();
                var b9 = new SwitchesB9();
                var isru = new SwitchesIsru();
                foreach (var p in root.Properties())
                {
                    switch (p.Name)
                    {
                        case "enabled": ReadBool(p, ref enabled, problems); break;
                        case "fix_mu_reader": ReadBool(p, ref fix, problems); break;
                        case "own_first_deployable": ReadBool(p, ref own, problems); break;
                        case "diagnostics": ReadBool(p, ref diagnostics, problems); break;
                        case "follow_redux_deployables": ReadBool(p, ref follow, problems); break;
                        case "hide_material_less_renderers": ReadBool(p, ref hide, problems); break;
                        case "fix_part_icons": ReadBool(p, ref icons, problems); break;
                        case "subcategory_bar": ReadBool(p, ref subcategories, problems); break;
                        case "editor_animation_speed": ReadSpeed(p, ref editorSpeed, problems); break;
                        case "material_curves": ReadBool(p, ref s2a.MaterialCurves, problems); break;
                        case "light_curves": ReadBool(p, ref s2a.LightCurves, problems); break;
                        case "emission_setup": ReadBool(p, ref s2a.EmissionSetup, problems); break;
                        case "throttle_effects": ReadBool(p, ref s2a.ThrottleEffects, problems); break;
                        case "heat_glow": ReadBool(p, ref s2a.HeatGlow, problems); break;
                        case "perf_counter": ReadBool(p, ref s2a.PerfCounter, problems); break;
                        case "plugin_modules": ReadBool(p, ref s2a.PluginModules, problems); break;
                        case "b9_module_gates": ReadBool(p, ref s2a.B9ModuleGates, problems); break;
                        case "fix_fairing_init": ReadBool(p, ref s041.FixFairingInit, problems); break;
                        case "engine_tooltip_stats": ReadBool(p, ref s041.EngineTooltipStats, problems); break;
                        case "subcategory_bar_keeps_grid": ReadBool(p, ref s041.SubcategoryBarKeepsGrid, problems); break;
                        case "share_ksp1_textures": ReadBool(p, ref s041.ShareKsp1Textures, problems); break;
                        case "ksp1_textures_follow_quality": ReadBool(p, ref s041.Ksp1TexturesFollowQuality, problems); break;
                        case "ksp1_engine_shrouds": ReadBool(p, ref s041.Ksp1EngineShrouds, problems); break;
                        case "raise_streaming_budget": ReadBool(p, ref s041.RaiseStreamingBudget, problems); break;
                        case "audit_on_vab_entry": ReadBool(p, ref audit.OnVabEntry, problems); break;
                        case "audit_frame_budget_ms": ReadClampedFloat(p, 1, 50, ref audit.FrameBudgetMs, problems); break;
                        case "audit_unload_every": ReadClampedInt(p, 10, 1000, ref audit.UnloadEvery, problems); break;
                        case "mm_plugin_safety_net": ReadBool(p, ref mm.SafetyNet, problems); break;
                        case "mm_plugin_folders_auto": ReadBool(p, ref mm.PluginFoldersAuto, problems); break;
                        case "mm_before_after_need_mod": ReadBool(p, ref mm.BeforeAfterNeedMod, problems); break;
                        case "mm_arithmetic": ReadBool(p, ref mm.Arithmetic, problems); break;
                        case "mm_index_selectors": ReadBool(p, ref mm.IndexSelectors, problems); break;
                        case "mm_edit_first_match": ReadBool(p, ref mm.EditFirstMatch, problems); break;
                        case "ksp1_crossfeed_toggle": ReadBool(p, ref mm.CrossfeedToggle, problems); break;
                        case "ksp1_heatshields": ReadBool(p, ref mm.Heatshields, problems); break;
                        case "ablator_rate_scale": ReadClampedFloat(p, 0.01, 100, ref mm.AblatorRateScale, problems); break;
                        case "ablator_char": ReadBool(p, ref mm.AblatorChar, problems); break;
                        case "b9_mass": ReadBool(p, ref b9.Mass, problems); break;
                        case "b9_resources": ReadBool(p, ref b9.Resources, problems); break;
                        case "b9_module_data": ReadBool(p, ref b9.ModuleData, problems); break;
                        case "b9_module_active": ReadBool(p, ref b9.ModuleActive, problems); break;
                        case "b9_part_scalars": ReadBool(p, ref b9.PartScalars, problems); break;
                        case "b9_new_part_switches": ReadBool(p, ref b9.NewPartSwitches, problems); break;
                        case "ksp1_converters": ReadBool(p, ref isru.Converters, problems); break;
                        case "ksp1_drills": ReadBool(p, ref isru.Drills, problems); break;
                        case "ksp1_survey_scanners": ReadBool(p, ref isru.SurveyScanners, problems); break;
                        case "ksp1_resource_scanners": ReadBool(p, ref isru.ResourceScanners, problems); break;
                        case "ksp1_ore_resource": ReadString(p, ref isru.OreResource, problems); break;
                        case "harvest_multiplier": ReadClampedDouble(p, 0.01, 100, ref isru.HarvestMultiplier, problems); break;
                        case "isru_heat_scale": ReadClampedDouble(p, 0, 10, ref isru.HeatScale, problems); break;
                        case "isru_active_animation": ReadBool(p, ref isru.ActiveAnimation, problems); break;
                        case "isru_ksp1_rates": ReadBool(p, ref isru.Ksp1Rates, problems); break;
                        case "mm_plugin_folders": mm.PluginFolders = ReadNames(p, problems, StringComparer.OrdinalIgnoreCase) ?? mm.PluginFolders; break;
                        case "mm_needs_keep": mm.NeedsKeep = ReadNames(p, problems, StringComparer.OrdinalIgnoreCase) ?? mm.NeedsKeep; break;
                        case "skip_modules": s2a.SkipModules = ReadNames(p, problems) ?? s2a.SkipModules; break;
                        case "shader_property_map": map = ReadMap(p, problems) ?? map; break;
                        default:
                            if (!p.Name.StartsWith("_")) problems.Add($"unknown key \"{p.Name}\" ignored");
                            break;
                    }
                }
                if (problems.Count > 0) error = string.Join("; ", problems);
                return new AnimationSettings(enabled, fix, own, map ?? DefaultShaderPropertyMap(), diagnostics, follow, hide, icons, subcategories, editorSpeed, s2a, s041, audit, mm, b9, isru);
            }
            catch (Exception e)
            {
                error = $"animations.json ignored, invalid JSON: {e.Message}";
                return Defaults;
            }
        }

        private static void ReadBool(JProperty p, ref bool value, List<string> problems)
        {
            if (p.Value.Type == JTokenType.Boolean) value = (bool)p.Value;
            else problems.Add($"\"{p.Name}\" is not true or false, default kept");
        }

        /// <summary>A finite number, 0 or more.</summary>
        private static void ReadSpeed(JProperty p, ref float value, List<string> problems)
        {
            if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
            {
                var v = (double)p.Value;
                if (v >= 0 && !double.IsInfinity(v) && !double.IsNaN(v))
                {
                    value = (float)v;
                    return;
                }
            }
            problems.Add($"\"{p.Name}\" is not a number of 0 or more, default kept");
        }

        /// <summary>A finite number, clamped to min..max (reported when it was outside).</summary>
        private static void ReadClampedFloat(JProperty p, double min, double max, ref float value, List<string> problems)
        {
            if (TryReadClamped(p, min, max, out var v, problems)) value = (float)v;
        }

        /// <summary>As <see cref="ReadClampedFloat"/>, kept as a double.</summary>
        private static void ReadClampedDouble(JProperty p, double min, double max, ref double value, List<string> problems)
        {
            if (TryReadClamped(p, min, max, out var v, problems)) value = v;
        }

        private static bool TryReadClamped(JProperty p, double min, double max, out double value, List<string> problems)
        {
            value = 0;
            if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
            {
                var v = (double)p.Value;
                if (!double.IsNaN(v) && !double.IsInfinity(v))
                {
                    var c = Math.Min(max, Math.Max(min, v));
                    if (c != v) problems.Add($"\"{p.Name}\" is outside {Inv(min)} to {Inv(max)}, clamped to {Inv(c)}");
                    value = c;
                    return true;
                }
            }
            problems.Add($"\"{p.Name}\" is not a number of {Inv(min)} to {Inv(max)}, default kept");
            return false;
        }

        /// <summary>A string, trimmed (empty allowed).</summary>
        private static void ReadString(JProperty p, ref string value, List<string> problems)
        {
            if (p.Value.Type == JTokenType.String) value = ((string)p.Value).Trim();
            else problems.Add($"\"{p.Name}\" is not a string, default kept");
        }

        /// <summary>A whole number, clamped to min..max (reported when it was outside). 0 is refused and the default kept.</summary>
        private static void ReadClampedInt(JProperty p, int min, int max, ref int value, List<string> problems)
        {
            if (p.Value.Type == JTokenType.Integer)
            {
                var v = (long)p.Value;
                if (v == 0)
                {
                    problems.Add($"\"{p.Name}\" is 0, which is refused, default kept");
                    return;
                }
                var c = Math.Min(max, Math.Max(min, v));
                if (c != v) problems.Add($"\"{p.Name}\" is outside {Inv(min)} to {Inv(max)}, clamped to {Inv(c)}");
                value = (int)c;
                return;
            }
            problems.Add($"\"{p.Name}\" is not a whole number of {Inv(min)} to {Inv(max)}, default kept");
        }

        private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        private static string Inv(long v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Trimmed, distinct names (ordinal unless a comparer is given; the first spelling is kept); null (default
        /// kept) when the value is not a list. Items that are not non-empty strings are skipped and reported.</summary>
        private static IReadOnlyCollection<string> ReadNames(JProperty p, List<string> problems, StringComparer comparer = null)
        {
            if (!(p.Value is JArray array))
            {
                problems.Add($"\"{p.Name}\" is not a list of names, default kept");
                return null;
            }
            var names = new List<string>();
            var seen = new HashSet<string>(comparer ?? StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (!IsName(item))
                {
                    problems.Add($"{p.Name}: item {item.ToString(Newtonsoft.Json.Formatting.None)} is not a name, ignored");
                    continue;
                }
                var name = ((string)item).Trim();
                if (seen.Add(name)) names.Add(name);
            }
            return new ReadOnlyCollection<string>(names);
        }

        /// <summary>The map, or null (default kept) when the value is not an object.</summary>
        private static IReadOnlyDictionary<string, string[]> ReadMap(JProperty p, List<string> problems)
        {
            if (!(p.Value is JObject obj))
            {
                problems.Add($"\"{p.Name}\" is not an object, default kept");
                return null;
            }
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var entry in obj.Properties())
            {
                if (entry.Name == "_note") continue;
                var names = entry.Value is JArray array && array.Count > 0 && array.All(IsName)
                    ? array.Select(t => ((string)t).Trim()).ToArray()
                    : null;
                if (names != null) map[entry.Name] = names;
                else problems.Add($"{p.Name}.{entry.Name} is not a list of property names, ignored");
            }
            return ReadOnly(map);
        }

        private static IReadOnlyDictionary<string, string[]> ReadOnly(Dictionary<string, string[]> map) =>
            new ReadOnlyDictionary<string, string[]>(map);

        private static bool IsName(JToken t) => t.Type == JTokenType.String && ((string)t).Trim().Length > 0;
    }
}
