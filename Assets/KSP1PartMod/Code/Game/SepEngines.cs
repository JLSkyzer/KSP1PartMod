using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Engines;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.0: a postfix on Redux's private <c>Ksp1PartTranslator.BuildConfiguredPrefab</c> (before the part JSON is
    /// written; the prefab rebuild runs it again) for the parts whose engine modes <see cref="EngineModes"/> ordered. SEP
    /// clusters: each mode's <c>maxThrust</c> and the part's mass (cfg mass + engine mass) from <see cref="SepEngineSets"/>,
    /// counting the engines in the built prefab; a refusal keeps Redux's values. Every multi-mode engine we ordered: duplicate
    /// <c>engineID</c>s made unique (<see cref="EngineModeOrder.UniqueIds"/>), so each mode can be selected; the display name
    /// is kept. Every write is absolute. Its own Harmony id; one throw stops it for the session.</summary>
    internal static class SepEngines
    {
        public const string HarmonyId = "KSP1PartMod.SepEngines";

        private static volatile bool _broken;

        internal static void Reset() => _broken = false;

        public static bool TryApply(Harmony harmony)
        {
            if (!EngineModes.IsArmed) return false; // nothing to complete; EngineModes said why
            var build = EngineCurveFix.FindBuildConfiguredPrefab();
            if (build == null)
            {
                EngineModes.Warn(EngineSizeLines.SepValuesMemberMissing("Ksp1PartTranslator.BuildConfiguredPrefab"));
                return false;
            }
            harmony.Patch(build, postfix: new HarmonyMethod(typeof(SepEngines), nameof(AfterBuildConfiguredPrefab)));
            return true;
        }

        internal static void AfterBuildConfiguredPrefab(GameObject __result, Ksp1ConfigNode __1, PartCore __2)
        {
            if (_broken) return;
            try
            {
                Apply(__1, __2, __result);
            }
            catch (Exception e)
            {
                _broken = true;
                EngineModes.Error(EngineSizeLines.EnginesStopped("SEP values and engine ids", e.ToString()));
            }
        }

        /// <summary>The engine data of <paramref name="core"/> whose mode count is <paramref name="modes"/> (distinct objects:
        /// the module list and the serialized modules share them on a fresh import).</summary>
        internal static List<Data_Engine> Engines(PartCore core, int modes)
        {
            var result = new List<Data_Engine>();
            void Take(ModuleData d)
            {
                if (d is Data_Engine e && e.engineModes != null && e.engineModes.Length == modes && !result.Contains(e)) result.Add(e);
            }
            if (core.modules != null)
                foreach (var m in core.modules) Take(m);
            var serialized = core.data?.serializedPartModules;
            if (serialized != null)
                foreach (var module in serialized)
                {
                    if (module.ModuleData == null) continue;
                    foreach (var d in module.ModuleData) Take(d.DataObject);
                }
            return result;
        }

        internal static List<string> TransformNames(GameObject prefab) =>
            prefab == null ? new List<string>() : prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();

        internal static void Apply(Ksp1ConfigNode partNode, PartCore core, GameObject prefab)
        {
            if (partNode == null || core == null) return;
            var part = new Ksp1NodeAdapter(partNode);
            var kind = EngineModes.KindOf(part, out var order);
            if (kind == EngineModes.Kind.None) return;
            var engines = Engines(core, order.Length);
            if (engines.Count == 0) return; // Redux did not import them as one engine
            var counting = !Ksp1ImportedPartRegistry.IsComplete;
            var name = partNode.GetValue("name") ?? "?";

            if (kind == EngineModes.Kind.Sep)
            {
                var c = SepEngineSets.Compute(part, TransformNames(prefab));
                if (c.Converted && engines.Any(e => !SameIds(c.ModeIds, e.engineModes)))
                {
                    c.Converted = false;
                    c.Reason = "Redux's engine modes are not in the SEP order";
                }
                if (!c.Converted)
                {
                    if (counting) EngineModes.NoteRefused(name, EngineSizeLines.SepRefused(name, c.Reason));
                }
                else
                {
                    foreach (var e in engines)
                        for (var k = 0; k < c.ModeThrust.Length; k++)
                            e.engineModes[k].maxThrust = (float)c.ModeThrust[k];
                    if (core.data != null) core.data.mass = (IsruCfg.Number(part, "mass") ?? new PartData().mass) + c.EngineMass;
                    if (counting) EngineModes.LogOnce(name + "\nsep values", EngineSizeLines.SepValues(name, c));
                }
            }

            foreach (var e in engines)
            {
                var ids = e.engineModes.Select(m => m?.engineID).ToArray();
                var unique = EngineModeOrder.UniqueIds(ids);
                for (var k = 0; k < ids.Length; k++)
                {
                    if (e.engineModes[k] == null || string.Equals(ids[k], unique[k], StringComparison.Ordinal)) continue;
                    e.engineModes[k].engineID = unique[k];
                    if (counting) EngineModes.LogOnce(name + "\nid " + k, EngineSizeLines.DuplicateId(name, ids[k], unique[k]));
                }
            }
        }

        private static bool SameIds(string[] ids, Data_Engine.EngineMode[] modes)
        {
            if (modes.Length != ids.Length) return false;
            var expected = EngineModeOrder.UniqueIds(ids);
            var unique = EngineModeOrder.UniqueIds(modes.Select(m => m?.engineID).ToArray());
            for (var k = 0; k < ids.Length; k++)
                if (!string.Equals(expected[k], unique[k], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
