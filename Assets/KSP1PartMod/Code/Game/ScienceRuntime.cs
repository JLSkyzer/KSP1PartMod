using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Game.Science;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native.Science;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>The runtime of converted KSP1 experiments (science plan, Task 5), armed at pre-initialization when
    /// <c>ksp1_science</c> is on. Only for the parts of our record (<see cref="ScienceImporter.PartOf"/>, by part name); KSP2's own
    /// experiment parts are never touched.
    /// <list type="bullet">
    /// <item><b>animator gate</b>: a postfix on <c>Module_ScienceExperiment.OnInitialize</c> (VAB and flight). KSP2 marks the part
    /// animated whenever it holds any <c>Animator</c> (Redux's Mecanim copy of a single KSP1 deployable, or of a parachute, gives
    /// one), then counts a run down only once its native deploy state was entered, which no KSP1 clip has; and it sends the empty
    /// animator parameter of our configurations to that <c>Animator</c> at each start and stop. The postfix sets the module's
    /// <c>Animator</c> to null (every later trigger, reset, speed change and state callback of the module tests it) and
    /// <c>IsPartAnimated</c> to false; <see cref="Module_Ksp1Animations"/> sets it back to true only for a configuration that waits
    /// for its clip and has one (<c>science_wait_for_animation</c>, <c>enabled</c>). The trigger KSP2 sends inside its own
    /// <c>OnInitialize</c> for a part loaded running cannot be stopped by a postfix: logged once;</item>
    /// <item><b>clips</b> (flight): the part is handed to its <see cref="Module_Ksp1Animations"/> (<c>AttachScienceBridge</c>);
    /// without one, no clip (logged);</item>
    /// <item><b>report line</b> (flight): a prefix and postfix on <c>PartComponentModule_ScienceExperiment.CreateScienceReports</c>
    /// mark the part whose reports are being stored; a postfix on <c>ScienceStorageComponent.StoreResearchReport</c> logs the first
    /// report of ours stored per part instance: KSP2 id, location id, value.</item>
    /// </list>
    /// Writes only <c>Animator</c>, <c>IsPartAnimated</c> and (through the bridge) <c>PartIsDeployed</c>. Every target is resolved by
    /// name; a missing one turns its feature off with one line. A patch that throws stops every patch here for the session (one
    /// line) and Redux's code runs as is.</summary>
    internal static class ScienceRuntime
    {
        public const string HarmonyId = "KSP1PartMod.ScienceRuntime";
        public const string RuntimeFeature = "science runtime";
        public const string ReportFeature = "report line";
        public const string ComponentTypeName = "KSP.Sim.impl.PartComponentModule_ScienceExperiment";
        public const string StorageTypeName = "KSP.Sim.impl.ScienceStorageComponent";

        /// <summary>One converted experiment part instance, for its animation module.</summary>
        internal sealed class Handle
        {
            public string Part;
            public Data_ScienceExperiment Data;
            public SciencePart Record;
            public ScienceBridge Bridge;
        }

        private static FieldInfo _animator, _data;
        private static bool _on, _reportOn, _broken, _deployAnimations, _waitForAnimation, _animationsEnabled = true;
        private static readonly object Lock = new object();
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<object, object> ReportLogged = new ConditionalWeakTable<object, object>();
        private static readonly object Mark = new object();
        [ThreadStatic] private static PartComponentModule _reporting;
        [ThreadStatic] private static string _reportingPart;

        /// <summary>Tests: the part name of a component (its <c>Part</c> is set by the game only).</summary>
        internal static Func<PartComponentModule, string> ComponentPartForTests;

        internal static bool On => _on;
        internal static bool ReportOn => _reportOn;
        internal static bool Broken => _broken;
        /// <summary><c>enabled</c> and <c>science_deploy_animations</c>.</summary>
        internal static bool AnimationsOn => _animationsEnabled && _deployAnimations;
        /// <summary><see cref="AnimationsOn"/> and <c>science_wait_for_animation</c>.</summary>
        internal static bool WaitOn => AnimationsOn && _waitForAnimation;

        /// <summary>Arms what the settings and the game's members allow. Returns true when a patch was applied.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            if (!settings.Ksp1Science) return false;
            _animationsEnabled = settings.Enabled;
            _deployAnimations = settings.ScienceDeployAnimations;
            _waitForAnimation = settings.ScienceWaitForAnimation;
            try
            {
                var access = ScienceAccess.Get();
                var module = access.Module;
                var init = module == null ? null : AccessTools.DeclaredMethod(module, "OnInitialize", Type.EmptyTypes);
                var animator = module == null ? null : AccessTools.Field(module, "Animator");
                var data = module == null ? null : AccessTools.Field(module, "dataScienceExperiment");
                var standings = access.Data == null ? null : AccessTools.Field(access.Data, "ExperimentStandings");
                var element = standings?.FieldType.IsGenericType == true && standings.FieldType.GetGenericTypeDefinition() == typeof(List<>)
                    ? standings.FieldType.GetGenericArguments()[0] : null;
                var state = element == null ? null : AccessTools.Field(element, "CurrentExperimentState");
                var id = element == null ? null : AccessTools.Field(element, "ExperimentID");
                string missing = null;
                if (access.MissingExperiment.Count > 0) missing = access.MissingExperiment[0];
                else if (access.MissingRuntime.Count > 0) missing = access.MissingRuntime[0];
                else if (init == null || init.IsStatic) missing = "Module_ScienceExperiment.OnInitialize";
                else if (animator == null || animator.IsStatic || animator.FieldType != typeof(Animator)) missing = "Module_ScienceExperiment.Animator";
                else if (data == null || data.IsStatic || data.FieldType != access.Data) missing = "Module_ScienceExperiment.dataScienceExperiment";
                else if (standings == null || standings.IsStatic || element != typeof(ExperimentStanding)) missing = "Data_ScienceExperiment.ExperimentStandings";
                else if (state == null || state.FieldType != typeof(ExperimentState)) missing = "ExperimentStanding.CurrentExperimentState";
                else if (id == null || id.FieldType != typeof(string)) missing = "ExperimentStanding.ExperimentID";
                if (missing != null)
                {
                    NativeModules.Warn(ScienceLines.MemberNotFound(missing, RuntimeFeature));
                    return false;
                }
                _animator = animator;
                _data = data;
                harmony.Patch(init, postfix: new HarmonyMethod(typeof(ScienceRuntime), nameof(AfterInit)));
                _on = true;
                ArmReportLine(harmony);
            }
            catch (Exception e)
            {
                Break(e);
                harmony.UnpatchAll(HarmonyId);
                _on = _reportOn = false;
                return false;
            }
            NativeModules.Info(ScienceLines.Armed(AnimationsOn, WaitOn));
            return true;
        }

        private static void ArmReportLine(Harmony harmony)
        {
            var component = AccessTools.TypeByName(ComponentTypeName);
            var storage = AccessTools.TypeByName(StorageTypeName);
            var create = component == null ? null : AccessTools.DeclaredMethod(component, "CreateScienceReports", new[] { typeof(ExperimentDefinition), typeof(int) });
            var store = storage == null ? null : AccessTools.DeclaredMethod(storage, "StoreResearchReport", new[] { typeof(ResearchReport) });
            string missing = null;
            if (component == null) missing = ComponentTypeName;
            else if (create == null || create.IsStatic) missing = "PartComponentModule_ScienceExperiment.CreateScienceReports";
            else if (storage == null) missing = StorageTypeName;
            else if (store == null || store.IsStatic || store.ReturnType != typeof(bool)) missing = "ScienceStorageComponent.StoreResearchReport";
            else if (AccessTools.Field(typeof(ResearchReport), "ResearchLocationID")?.FieldType != typeof(string)) missing = "ResearchReport.ResearchLocationID";
            else if (AccessTools.Field(typeof(ResearchReport), "InitialScienceValue")?.FieldType != typeof(float)) missing = "ResearchReport.InitialScienceValue";
            else if (AccessTools.Field(typeof(ResearchReport), "ExperimentID")?.FieldType != typeof(string)) missing = "ResearchReport.ExperimentID";
            if (missing != null)
            {
                NativeModules.Warn(ScienceLines.MemberNotFound(missing, ReportFeature));
                return;
            }
            harmony.Patch(create, prefix: new HarmonyMethod(typeof(ScienceRuntime), nameof(BeforeCreateReports)),
                postfix: new HarmonyMethod(typeof(ScienceRuntime), nameof(AfterCreateReports)));
            harmony.Patch(store, postfix: new HarmonyMethod(typeof(ScienceRuntime), nameof(AfterStoreReport)));
            _reportOn = true;
        }

        // ---------------------------------------------------------------- patches

        /// <summary>After KSP2's own initialization of the module: ours lose the <c>Animator</c> and the animated flag.</summary>
        internal static void AfterInit(PartBehaviourModule __instance)
        {
            // After a break the gate still applies (null and clear): a part loaded later must not get KSP2's native wait back.
            // Only the bridge and its lines are skipped.
            if (!_on || __instance == null) return;
            try
            {
                var part = IsruRuntime.PartNameOf(__instance);
                var record = ScienceImporter.PartOf(part);
                if (record == null || !record.HasModule) return;
                if (!(_data.GetValue(__instance) is Data_ScienceExperiment data)) return;
                var flight = __instance.PartBackingMode == PartBehaviourModule.PartBackingModes.Flight;
                var nativeAnimated = data.IsPartAnimated;
                _animator.SetValue(__instance, null);
                data.IsPartAnimated = false;
                if (!flight || _broken) return;
                if (nativeAnimated) LogLoadedRunning(part, data);
                var handle = new Handle { Part = part, Data = data, Record = record };
                var anim = __instance.GetComponent<Module_Ksp1Animations>();
                if (anim != null)
                {
                    anim.AttachScienceBridge(handle);
                    return;
                }
                foreach (var c in record.Configurations)
                    LogOnce("clip|" + part + "|" + c.ExperimentDefinitionId,
                        ScienceLines.NoClip(part, c.ExperimentDefinitionId, !c.Clip.HasSource ? ScienceNoClip.NoFxModules
                            : AnimationsOn ? ScienceNoClip.NoEntry : ScienceNoClip.AnimationsOff));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>KSP2 sent the configuration's (empty) animator parameter in its own <c>OnInitialize</c>, before this postfix.</summary>
        private static void LogLoadedRunning(string part, Data_ScienceExperiment data)
        {
            var standings = data.ExperimentStandings;
            if (standings == null) return;
            // As IndexOfRunningExperiment, which scans from the end: the highest running index (one runs at a time).
            for (var i = standings.Count - 1; i >= 0; i--)
                if (standings[i] != null && standings[i].CurrentExperimentState == ExperimentState.RUNNING)
                {
                    LogOnce("loaded|" + part, ScienceLines.LoadedRunning(part, standings[i].ExperimentID));
                    return;
                }
        }

        internal static void BeforeCreateReports(PartComponentModule __instance)
        {
            _reporting = null;
            _reportingPart = null;
            if (_broken || !_reportOn || __instance == null) return;
            try
            {
                var part = ComponentPartForTests != null ? ComponentPartForTests(__instance) : __instance.Part?.PartName;
                var record = ScienceImporter.PartOf(part);
                if (record == null || !record.HasModule || ReportLogged.TryGetValue(__instance, out _)) return;
                _reporting = __instance;
                _reportingPart = part;
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        internal static void AfterCreateReports()
        {
            _reporting = null;
            _reportingPart = null;
        }

        /// <summary>The first report of ours stored per part instance, while its experiment creates it.</summary>
        internal static void AfterStoreReport(ResearchReport researchReport, bool __result)
        {
            var owner = _reporting;
            if (_broken || owner == null || !__result || researchReport == null) return;
            try
            {
                var record = ScienceImporter.PartOf(_reportingPart);
                if (record == null || !Uses(record, researchReport.ExperimentID)) return;
                lock (Lock)
                {
                    if (ReportLogged.TryGetValue(owner, out _)) return;
                    ReportLogged.Add(owner, Mark);
                }
                NativeModules.Info(ScienceLines.ReportStored(_reportingPart, researchReport.ExperimentID, researchReport.ResearchLocationID,
                    researchReport.InitialScienceValue));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static bool Uses(SciencePart record, string id)
        {
            foreach (var c in record.Configurations)
                if (string.Equals(c.ExperimentDefinitionId, id, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ---------------------------------------------------------------- the bridge

        /// <summary>Per flight frame, one part's bridge. A throw stops every bridge for the session (<see cref="Break"/>); each part
        /// then drops its own at its next step and is no longer animated for KSP2, so no run waits for a gate nobody writes: the
        /// game is never left worse than with no bridge.</summary>
        internal static void Step(Handle h)
        {
            var bridge = h?.Bridge;
            if (bridge == null) return;
            if (_broken)
            {
                Stop(h);
                return;
            }
            try
            {
                bridge.Step();
            }
            catch (Exception e)
            {
                Break(e);
                Stop(h);
            }
        }

        /// <summary>Drops the part's bridge and clears <c>IsPartAnimated</c> (once: the bridge is gone after).</summary>
        internal static void Stop(Handle h)
        {
            if (h == null) return;
            h.Bridge = null;
            if (h.Data != null) h.Data.IsPartAnimated = false;
        }

        // ---------------------------------------------------------------- helpers

        internal static void LogOnce(string key, string line)
        {
            lock (Lock)
                if (!Logged.Add(key))
                    return;
            NativeModules.Info(line);
        }

        internal static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            NativeModules.Error(ScienceLines.PatchFailed(e.ToString()));
        }

        internal static void Reset()
        {
            _on = _reportOn = _broken = _deployAnimations = _waitForAnimation = false;
            _animationsEnabled = true;
            _animator = _data = null;
            _reporting = null;
            _reportingPart = null;
            ComponentPartForTests = null;
            lock (Lock) Logged.Clear();
        }
    }
}
