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

        /// <summary>Starting guesses, not verified in game. Each settings object gets its own copy.</summary>
        private static IReadOnlyDictionary<string, string[]> DefaultShaderPropertyMap() =>
            ReadOnly(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["_EmissiveColor"] = new[] { "_EmissiveColor", "_EmissionColor" },
                ["_BurnColor"] = new[] { "_BurnColor" },
            });

        public static readonly AnimationSettings Defaults =
            new AnimationSettings(true, true, false, DefaultShaderPropertyMap(), true, true, true, true);

        private AnimationSettings(bool enabled, bool fixMuReader, bool ownFirstDeployable,
            IReadOnlyDictionary<string, string[]> shaderPropertyMap, bool diagnostics, bool followRedux,
            bool hideMaterialLess, bool fixIcons)
        {
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
                    hide = Defaults.HideMaterialLessRenderers, icons = Defaults.FixPartIcons;
                IReadOnlyDictionary<string, string[]> map = null;
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
                        case "shader_property_map": map = ReadMap(p, problems) ?? map; break;
                        default:
                            if (!p.Name.StartsWith("_")) problems.Add($"unknown key \"{p.Name}\" ignored");
                            break;
                    }
                }
                if (problems.Count > 0) error = string.Join("; ", problems);
                return new AnimationSettings(enabled, fix, own, map ?? DefaultShaderPropertyMap(), diagnostics, follow, hide, icons);
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
