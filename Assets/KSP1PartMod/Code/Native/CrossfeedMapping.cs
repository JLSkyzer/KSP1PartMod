using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native
{
    /// <summary>KSP1 <c>ModuleToggleCrossfeed</c> read as KSP2's native <c>Module_ToggleCrossfeed</c> (plan (e)):
    /// <list type="bullet">
    /// <item><c>toggleEditor</c> → <c>ToggleInEditor</c>, <c>toggleFlight</c> → <c>ToggleInFlight</c>; missing or not a bool:
    /// KSP2's default, true;</item>
    /// <item><c>crossfeedStatus</c> sets the part's start state (<c>PartData.fuelCrossFeed</c>), over the PART's own
    /// <c>fuelCrossFeed</c>; missing: the PART value stays;</item>
    /// <item><c>techRequired</c> is not reproduced (KSP2's toggle has no tech gate): noted.</item>
    /// </list>
    /// Pure: no Unity, game or Redux type.</summary>
    public sealed class CrossfeedMapping
    {
        public const string Ksp1Module = "ModuleToggleCrossfeed";
        public const string NativeModule = "Module_ToggleCrossfeed";

        /// <summary>The keys this mapping reads; any other value of the module is noted as not reproduced.</summary>
        public static readonly IReadOnlyList<string> KnownKeys = new[] { "name", "toggleEditor", "toggleFlight", "crossfeedStatus", "techRequired" };

        /// <summary>The note of a second <c>ModuleToggleCrossfeed</c> on one part (KSP2's module is unique per part).</summary>
        public const string ExtraModuleNote = Ksp1Module + ": a second one on the part is not imported (" + NativeModule + " is one per part)";

        public bool ToggleInEditor { get; private set; } = true;
        public bool ToggleInFlight { get; private set; } = true;

        /// <summary>The part's start state: <c>crossfeedStatus</c> when it is a bool, else the PART value, else KSP2's
        /// default (true).</summary>
        public bool StartCrossFeed { get; private set; }

        /// <summary><c>crossfeedStatus</c> was read: the importer writes <see cref="StartCrossFeed"/> to the part.</summary>
        public bool OverridesPart { get; private set; }

        /// <summary>The <c>techRequired</c> value, ignored; null when absent.</summary>
        public string TechRequired { get; private set; }

        /// <summary>What is not reproduced, one line each, in the module's order.</summary>
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<string> _notes = new List<string>();

        public static string TechRequiredNote(string tech) =>
            $"{Ksp1Module}: techRequired '{tech}' ignored, the toggle is available from the start (KSP2's toggle has no tech gate)";

        public static string MissingToggleNote(string key) =>
            $"{Ksp1Module}: {key} missing, KSP2's default true used (KSP1's default not read)";

        public static CrossfeedMapping From(IKsp1Node module, bool? partCrossFeed)
        {
            var m = new CrossfeedMapping { StartCrossFeed = partCrossFeed ?? true };
            if (module == null) return m;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in module.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                var value = kv.Value?.Trim() ?? "";
                // KSP1 reads the first value of a key; a repeat is noted, never applied.
                if (!seen.Add(key))
                {
                    if (key != "name") m._notes.Add($"{Ksp1Module}: {key} repeated, the first value kept");
                    continue;
                }
                switch (key)
                {
                    case "name":
                        break;
                    case "toggleEditor":
                        m.ToggleInEditor = m.ReadBool(key, value, true);
                        break;
                    case "toggleFlight":
                        m.ToggleInFlight = m.ReadBool(key, value, true);
                        break;
                    case "crossfeedStatus":
                        if (TryBool(value, out var status))
                        {
                            m.StartCrossFeed = status;
                            m.OverridesPart = true;
                        }
                        else m._notes.Add($"{Ksp1Module}: crossfeedStatus '{value}' is not true or false, the part's fuelCrossFeed kept");
                        break;
                    case "techRequired":
                        if (value.Length == 0) break;
                        m.TechRequired = value;
                        m._notes.Add(TechRequiredNote(value));
                        break;
                    default:
                        m._notes.Add($"{Ksp1Module}: {key} not reproduced");
                        break;
                }
            }
            foreach (var child in module.AllNodes)
                m._notes.Add($"{Ksp1Module}: node {child.Name} not reproduced");
            // KSP1's default for a missing toggle was not read (S): KSP2's true is used, and said.
            foreach (var key in new[] { "toggleEditor", "toggleFlight" })
                if (!seen.Contains(key)) m._notes.Add(MissingToggleNote(key));
            return m;
        }

        private bool ReadBool(string key, string value, bool fallback)
        {
            if (TryBool(value, out var b)) return b;
            _notes.Add($"{Ksp1Module}: {key} '{value}' is not true or false, {(fallback ? "true" : "false")} kept");
            return fallback;
        }

        /// <summary>KSP1's bool: <c>true</c> or <c>false</c>, any case, around spaces.</summary>
        public static bool TryBool(string value, out bool result) => bool.TryParse(value?.Trim() ?? "", out result);
    }
}
