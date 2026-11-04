using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP1PartMod.Native;

namespace KSP1PartMod.Game
{
    /// <summary>0.16.0, saved crafts of 0.15.x: such a craft saved one <c>Module_Engine</c> per KSP1 engine module and the game kept
    /// the last one, so its saved <c>activeEngineMode</c> is the last module's id (<c>vernier</c>, <c>CenterThree</c>) while its
    /// saved <c>currentEngineModeIndex</c> is 0. On a part <see cref="EngineModes"/> ordered, the engine then runs mode 0 while the
    /// mode dropdown shows another mode. A postfix on <c>Module_Engine.OnInitialize</c> (VAB and flight, after the saved state is
    /// applied and after Redux subscribed the dropdown) makes the dropdown agree with the mode that runs:
    /// <c>activeEngineMode = engineModes[currentEngineModeIndex].engineID</c>. Redux's dropdown handler then finds the same index
    /// and changes nothing. Skipped while the engine is changing mode. Armed only when <see cref="EngineModes"/> is (it follows
    /// <c>sep_procedural_engines</c> and <c>ksp1_engine_modes_fallback</c>); its own Harmony id; one throw stops it for the
    /// session. No per-frame code.</summary>
    internal static class EngineModeSync
    {
        public const string HarmonyId = "KSP1PartMod.EngineModeSync";

        private static volatile bool _broken;
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        internal static void Reset()
        {
            _broken = false;
            lock (Logged) Logged.Clear();
        }

        internal static MethodInfo FindOnInitialize()
        {
            var m = AccessTools.DeclaredMethod(typeof(Module_Engine), "OnInitialize", Type.EmptyTypes);
            return m != null && !m.IsStatic && m.ReturnType == typeof(void) ? m : null;
        }

        public static bool TryApply(Harmony harmony)
        {
            if (!EngineModes.IsArmed) return false;
            var init = FindOnInitialize();
            if (init == null)
            {
                EngineModes.Warn(EngineSizeLines.ModeSyncMemberMissing("Module_Engine.OnInitialize"));
                return false;
            }
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(EngineModeSync), nameof(AfterInitialize)));
            return true;
        }

        internal static void AfterInitialize(Module_Engine __instance)
        {
            if (_broken || __instance == null) return;
            try
            {
                var data = __instance.DataEngine;
                if (data?.engineModes == null || data.engineModes.Length < 2) return;
                var part = IsruRuntime.PartNameOf(__instance);
                if (part == null || !EngineModes.WasOrdered(part)) return;
                if (Sync(data, out var from, out var to))
                {
                    bool first;
                    lock (Logged) first = Logged.Add(part);
                    if (first) EngineModes.Info(EngineSizeLines.ModeSynced(part, from, to));
                }
            }
            catch (Exception e)
            {
                _broken = true;
                EngineModes.Error(EngineSizeLines.EnginesStopped("saved engine mode repair", e.ToString()));
            }
        }

        /// <summary>Sets <c>activeEngineMode</c> to the id of the mode that runs, when it names another one. False when nothing
        /// was changed (agreement already, an index out of range, or a mode change under way).</summary>
        internal static bool Sync(Data_Engine data, out string from, out string to)
        {
            from = to = null;
            if (data?.engineModes == null || data.activeEngineMode == null) return false;
            var k = data.currentEngineModeIndex;
            if (k < 0 || k >= data.engineModes.Length || data.engineModes[k] == null) return false;
            if (data.State == EngineState.ChangingMode) return false;
            from = data.activeEngineMode.GetValue();
            to = data.engineModes[k].engineID;
            if (string.Equals(from, to, StringComparison.Ordinal)) return false;
            data.activeEngineMode.SetValue(to);
            return true;
        }
    }
}
