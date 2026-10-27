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
        /// <summary>KSP1 module names never taken (<c>skip_modules</c>), trimmed, distinct, ordinal.</summary>
        public IReadOnlyCollection<string> SkipModules => _switches2a.SkipModules;

        /// <summary>The phase 2a keys, read together.</summary>
        private sealed class Switches2a
        {
            public bool MaterialCurves = true, LightCurves = true, EmissionSetup = true, ThrottleEffects = true, HeatGlow = true;
            public IReadOnlyCollection<string> SkipModules = NoModules;
        }

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
                new Switches2a());

        private AnimationSettings(bool enabled, bool fixMuReader, bool ownFirstDeployable,
            IReadOnlyDictionary<string, string[]> shaderPropertyMap, bool diagnostics, bool followRedux,
            bool hideMaterialLess, bool fixIcons, bool subcategoryBar, float editorAnimationSpeed, Switches2a switches2a)
        {
            _switches2a = switches2a;
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
                        case "skip_modules": s2a.SkipModules = ReadNames(p, problems) ?? s2a.SkipModules; break;
                        case "shader_property_map": map = ReadMap(p, problems) ?? map; break;
                        default:
                            if (!p.Name.StartsWith("_")) problems.Add($"unknown key \"{p.Name}\" ignored");
                            break;
                    }
                }
                if (problems.Count > 0) error = string.Join("; ", problems);
                return new AnimationSettings(enabled, fix, own, map ?? DefaultShaderPropertyMap(), diagnostics, follow, hide, icons, subcategories, editorSpeed, s2a);
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
