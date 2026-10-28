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

        /// <summary>The phase 2a and 2b keys, read together.</summary>
        private sealed class Switches2a
        {
            public bool MaterialCurves = true, LightCurves = true, EmissionSetup = true, ThrottleEffects = true, HeatGlow = true;
            public bool PerfCounter;
            public IReadOnlyCollection<string> SkipModules = NoModules;
            public bool PluginModules = true, B9ModuleGates = true;
        }

        /// <summary>The 0.4.1 fix switches, read together.</summary>
        private sealed class Switches041
        {
            public bool FixFairingInit = true, EngineTooltipStats = true, SubcategoryBarKeepsGrid = true;
            public bool ShareKsp1Textures = true, Ksp1TexturesFollowQuality = true;
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
                new Switches2a(), new Switches041(), new SwitchesAudit());

        private AnimationSettings(bool enabled, bool fixMuReader, bool ownFirstDeployable,
            IReadOnlyDictionary<string, string[]> shaderPropertyMap, bool diagnostics, bool followRedux,
            bool hideMaterialLess, bool fixIcons, bool subcategoryBar, float editorAnimationSpeed, Switches2a switches2a,
            Switches041 switches041, SwitchesAudit audit)
        {
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
                        case "raise_streaming_budget": ReadBool(p, ref s041.RaiseStreamingBudget, problems); break;
                        case "audit_on_vab_entry": ReadBool(p, ref audit.OnVabEntry, problems); break;
                        case "audit_frame_budget_ms": ReadClampedFloat(p, 1, 50, ref audit.FrameBudgetMs, problems); break;
                        case "audit_unload_every": ReadClampedInt(p, 10, 1000, ref audit.UnloadEvery, problems); break;
                        case "skip_modules": s2a.SkipModules = ReadNames(p, problems) ?? s2a.SkipModules; break;
                        case "shader_property_map": map = ReadMap(p, problems) ?? map; break;
                        default:
                            if (!p.Name.StartsWith("_")) problems.Add($"unknown key \"{p.Name}\" ignored");
                            break;
                    }
                }
                if (problems.Count > 0) error = string.Join("; ", problems);
                return new AnimationSettings(enabled, fix, own, map ?? DefaultShaderPropertyMap(), diagnostics, follow, hide, icons, subcategories, editorSpeed, s2a, s041, audit);
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
            if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
            {
                var v = (double)p.Value;
                if (!double.IsNaN(v) && !double.IsInfinity(v))
                {
                    var c = Math.Min(max, Math.Max(min, v));
                    if (c != v) problems.Add($"\"{p.Name}\" is outside {Inv(min)} to {Inv(max)}, clamped to {Inv(c)}");
                    value = (float)c;
                    return;
                }
            }
            problems.Add($"\"{p.Name}\" is not a number of {Inv(min)} to {Inv(max)}, default kept");
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

        /// <summary>Trimmed, distinct names (ordinal); null (default kept) when the value is not a list. Items that are not
        /// non-empty strings are skipped and reported.</summary>
        private static IReadOnlyCollection<string> ReadNames(JProperty p, List<string> problems)
        {
            if (!(p.Value is JArray array))
            {
                problems.Add($"\"{p.Name}\" is not a list of names, default kept");
                return null;
            }
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
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
