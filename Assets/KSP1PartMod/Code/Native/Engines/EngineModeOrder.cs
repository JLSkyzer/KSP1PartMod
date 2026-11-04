using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Engines
{
    /// <summary>Which KSP1 engine modules of a part become the modes of one <c>Module_Engine</c>, and in which order, where
    /// Redux's own <c>GetOrderedSwitchedEngineModules</c> returns nothing (0.16.0, plan (a), (b), (d)). The results are
    /// MODULE indices (positions among the part's <c>MODULE</c> nodes, in cfg order), so the game layer can hand Redux its own
    /// node objects back. Pure.</summary>
    public static class EngineModeOrder
    {
        /// <summary>SEP's mode switch: repeated <c>EngineID</c> values, which Redux does not read.</summary>
        public const string SepSwitchModule = "ModuleSEPEngineSwitch";

        /// <summary>The engine switches Redux already reads (<c>primaryEngineID</c> … <c>quaternaryEngineID</c>).</summary>
        public static readonly IReadOnlyList<string> ReduxSwitchModules = new[] { "ModuleTundraEngineSwitch", "MultiModeEngine", "ModuleMultiModeEngine" };

        /// <summary>The KSP1 engine modules Redux imports (<c>Ksp1PartModuleImporterRegistry.IsEngineModuleName</c>).</summary>
        public static bool IsEngineModule(string name) =>
            name != null && (name.Trim().Equals("ModuleEngines", StringComparison.OrdinalIgnoreCase) ||
                             name.Trim().Equals("ModuleEnginesFX", StringComparison.OrdinalIgnoreCase));

        /// <summary>Redux's own default <c>engineID</c> (<c>EngineImporter.BuildMode</c>).</summary>
        public const string DefaultEngineId = "Engine";

        /// <summary>The part's MODULE nodes, in cfg order.</summary>
        public static List<IKsp1Node> Modules(IKsp1Node part) =>
            part == null ? new List<IKsp1Node>() : part.GetNodes("MODULE").ToList();

        private static string NameOf(IKsp1Node module) => IsruCfg.Value(module, "name");

        /// <summary>The <c>engineID</c> of an engine module, Redux's default when absent.</summary>
        public static string EngineIdOf(IKsp1Node module)
        {
            var id = IsruCfg.Value(module, "engineID");
            return string.IsNullOrWhiteSpace(id) ? DefaultEngineId : id;
        }

        private static bool Named(IKsp1Node module, string name) => string.Equals(NameOf(module), name, StringComparison.OrdinalIgnoreCase);

        private static bool HasReduxSwitch(List<IKsp1Node> modules) => modules.Any(m => ReduxSwitchModules.Any(s => Named(m, s)));

        private static List<int> EngineIndices(List<IKsp1Node> modules)
        {
            var result = new List<int>();
            for (var i = 0; i < modules.Count; i++)
                if (modules[i] != null && IsEngineModule(NameOf(modules[i]))) result.Add(i);
            return result;
        }

        /// <summary>SEP clusters: the engine modules in <c>ModuleSEPEngineSwitch</c>'s <c>EngineID</c> order (matched on
        /// <c>engineID</c>, any case; an id no engine has is skipped), then the other engines in cfg order. Empty when the part
        /// has fewer than two engine modules, no SEP switch, or a switch Redux already reads.</summary>
        public static int[] Sep(IKsp1Node part)
        {
            var modules = Modules(part);
            var engines = EngineIndices(modules);
            if (engines.Count < 2 || HasReduxSwitch(modules)) return Array.Empty<int>();
            var sw = modules.FirstOrDefault(m => m != null && Named(m, SepSwitchModule));
            if (sw == null) return Array.Empty<int>();
            var result = new List<int>();
            foreach (var v in sw.AllValues)
            {
                if (!string.Equals(v.Key?.Trim(), "EngineID", StringComparison.OrdinalIgnoreCase)) continue;
                var id = IsruCfg.Text(v.Value);
                if (id.Length == 0) continue;
                foreach (var i in engines)
                {
                    if (result.Contains(i) || !string.Equals(IsruCfg.Value(modules[i], "engineID"), id, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(i);
                    break;
                }
            }
            foreach (var i in engines)
                if (!result.Contains(i)) result.Add(i);
            return result.ToArray();
        }

        /// <summary>Parts with two or more engine modules and no switch at all (neither one Redux reads nor SEP's): the engine
        /// modules in cfg order, so mode 0 is the cfg's first (main) engine. Empty otherwise.</summary>
        public static int[] Fallback(IKsp1Node part)
        {
            var modules = Modules(part);
            var engines = EngineIndices(modules);
            if (engines.Count < 2 || HasReduxSwitch(modules) || modules.Any(m => m != null && Named(m, SepSwitchModule))) return Array.Empty<int>();
            return engines.ToArray();
        }

        /// <summary>Mode ids made unique (any case), in order: the first keeps its id, the second <c>"Engine"</c> becomes
        /// <c>"Engine_2"</c>, the third <c>"Engine_3"</c>, skipping a suffix another mode already uses. A null or blank id is
        /// Redux's <c>"Engine"</c>.</summary>
        public static string[] UniqueIds(IReadOnlyList<string> ids)
        {
            if (ids == null) return Array.Empty<string>();
            var given = ids.Select(i => string.IsNullOrWhiteSpace(i) ? DefaultEngineId : i).ToArray();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new string[given.Length];
            for (var k = 0; k < given.Length; k++)
            {
                var id = given[k];
                if (used.Add(id))
                {
                    result[k] = id;
                    continue;
                }
                var n = 2;
                string candidate;
                do candidate = id + "_" + n++;
                while (used.Contains(candidate) || given.Skip(k + 1).Contains(candidate, StringComparer.OrdinalIgnoreCase));
                used.Add(candidate);
                result[k] = candidate;
            }
            return result;
        }
    }
}
