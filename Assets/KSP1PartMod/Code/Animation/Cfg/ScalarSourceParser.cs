using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>KSP1's FX setters that follow the throttle (<c>FXModuleThrottleEffects</c>, spec 2a.7): each names, in
    /// its comma list <c>fxModuleNames</c>, the <c>moduleID</c>s of the part's scalar modules it drives. A name that is
    /// one of our Color entries links that entry to the setter's fields; the setter itself is never an entry.
    /// KSP1 1.12 defaults: <c>responseSpeed</c> 0.5, the three <c>dependOn…</c> false.</summary>
    public static class ScalarSourceParser
    {
        /// <summary>Links the Color entries of <paramref name="entries"/> (by <see cref="AnimEntryDef.ModuleIndex"/> into
        /// <paramref name="modules"/>) and returns one note per name that matches none, and one per name that an earlier
        /// setter already drives (the later setter's fields win, as KSP1's last write each frame). Setters whose module
        /// name is in <paramref name="skipModules"/> are ignored.</summary>
        public static IReadOnlyList<string> Apply(IReadOnlyList<IKsp1Node> modules, IReadOnlyList<AnimEntryDef> entries,
            ISet<string> skipModules = null)
        {
            var notes = new List<string>();
            if (modules == null || entries == null) return notes;
            var driver = new Dictionary<string, int>(StringComparer.Ordinal); // moduleID -> setter module index
            for (var i = 0; i < modules.Count; i++)
            {
                var setter = modules[i];
                var name = setter?.GetValue("name")?.Trim();
                if (!IsThrottleSetter(setter, name) || skipModules != null && skipModules.Contains(name)) continue;
                foreach (var id in setter.GetValue("fxModuleNames").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
                {
                    var linked = false;
                    foreach (var e in entries)
                    {
                        if (e.Kind != EntryKind.Color || !id.Equals(ModuleIdOf(modules, e), StringComparison.Ordinal)) continue;
                        Link(e, setter, id);
                        linked = true;
                    }
                    if (!linked)
                    {
                        notes.Add($"MODULE[{i}] {name} names '{id}', no colour changer of ours");
                        continue;
                    }
                    if (driver.TryGetValue(id, out var earlier) && earlier != i)
                        notes.Add($"MODULE[{i}] {name} names '{id}', already driven by MODULE[{earlier}]: the later setter wins");
                    driver[id] = i;
                }
            }
            return notes;
        }

        /// <summary><c>FXModuleThrottleEffects</c>, or another module whose name ends in <c>ThrottleEffects</c> and
        /// carries <c>fxModuleNames</c>.</summary>
        private static bool IsThrottleSetter(IKsp1Node module, string name) =>
            module != null && name != null && name.EndsWith("ThrottleEffects", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(module.GetValue("fxModuleNames"));

        /// <summary>KSP1 resolves a name with <c>Modules.GetScalarModule(id)</c>: <c>ScalarModuleID</c>, which is
        /// ModuleColorChanger's <c>moduleID</c>.</summary>
        private static string ModuleIdOf(IReadOnlyList<IKsp1Node> modules, AnimEntryDef e) =>
            e.ModuleIndex >= 0 && e.ModuleIndex < modules.Count ? modules[e.ModuleIndex]?.GetValue("moduleID")?.Trim() : null;

        private static void Link(AnimEntryDef e, IKsp1Node setter, string id)
        {
            e.ModuleId = id;
            e.ScalarLinked = true;
            e.ScalarResponse = Float(setter, "responseSpeed", 0.5f);
            e.DependOnEngineState = Bool(setter, "dependOnEngineState");
            e.DependOnOutput = Bool(setter, "dependOnOutput");
            e.DependOnThrottle = Bool(setter, "dependOnThrottle");
            var engine = setter.GetValue("engineName");
            e.EngineName = string.IsNullOrWhiteSpace(engine) ? null : engine.Trim();
        }

        private static float Float(IKsp1Node m, string key, float fallback) =>
            float.TryParse(m.GetValue(key)?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static bool Bool(IKsp1Node m, string key) => bool.TryParse(m.GetValue(key)?.Trim(), out var v) && v;
    }
}
