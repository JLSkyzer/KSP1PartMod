using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KSP.Game;
using KSP.IO;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Injection;
using KSP1PartMod.Animation.Tracks;
using Redux.Ksp1Import.Assets;
using SpaceWarp2.API.Loading;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>At each campaign load, right after Redux imports the KSP1 parts and before vessels are created,
    /// plans each imported part's animations and adds (or refreshes) <see cref="Module_Ksp1Animations"/> in its
    /// definition. Idempotent: with the same feature set Redux keeps the same <c>PartCore</c> objects across
    /// loads. Never blocks a load: every path ends with resolve().</summary>
    internal static class AnimationLoadStep
    {
        public const string StepName = "KSP1PartMod: KSP1 animation modules";
        public const string ReduxImportStep = "Redux: Importing KSP1 parts";
        public const string ModuleName = nameof(PartComponentModule_Ksp1Animations);

        private static readonly string[] ReduxDeployableBehaviours = { "Module_Deployable", "Ksp1Module_Deployable" };

        public static void Register()
        {
            var settings = KSP1PartModPlugin.Animations;
            if (!settings.Enabled)
            {
                KSP1PartModPlugin.Log.LogInfo("[KSP1PartMod] anim: disabled by " + KSP1PartModPlugin.AnimationSettingsFile);
                return;
            }
            SaveLoad.AddStepAfter(KSP1PartModPlugin.ModId, StepName, StockFlow.CampaignLoad, ReduxImportStep, Run);
        }

        private static void Run(Action resolve, Action<string> reject)
        {
            // A new generation at each load: tracks of the previous load are never bound to this one's parts.
            var generation = TrackCache.Begin();
            var tracksStarted = false;
            try
            {
                // Without an ECS type index every part carrying our module fails to load: add nothing then.
                if (!EcsTypeRegistration.TryRegister("campaign load"))
                {
                    KSP1PartModPlugin.Log.LogWarning(
                        $"[KSP1PartMod] anim: ECS types not registered ({EcsTypeRegistration.Status}), no animation module added");
                    return;
                }
                var planned = Apply(KSP1PartModPlugin.Animations.OwnFirstDeployable, KSP1PartModPlugin.Animations.FollowReduxDeployables);
                // Queues the .mu reads on a worker and returns at once: the load never waits for them.
                if (planned.Count > 0)
                {
                    tracksStarted = true;
                    TrackLoad.Start(planned);
                }
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError("[KSP1PartMod] anim: step failed: " + e);
            }
            finally
            {
                // No worker: the generation is ready with no tracks, and every part keeps its phase 1 behaviour.
                if (!tracksStarted) TrackCache.MarkReady(generation);
                resolve();
            }
        }

        /// <summary>The parts planned, with their plan, for the track worker.</summary>
        private static List<(Ksp1ImportedPartRegistry.Entry entry, PartAnimationPlan plan)> Apply(bool ownFirstDeployable, bool followRedux)
        {
            var planned = new List<(Ksp1ImportedPartRegistry.Entry entry, PartAnimationPlan plan)>();
            var log = KSP1PartModPlugin.Log;
            var clock = Stopwatch.StartNew();
            var complete = Ksp1ImportedPartRegistry.IsComplete;
            var entries = Ksp1ImportedPartRegistry.ImportedParts.ToList();
            log.LogInfo($"[KSP1PartMod] anim: step start, registry complete={complete}, {entries.Count} part(s)");
            if (!complete)
            {
                log.LogWarning("[KSP1PartMod] anim: Redux's KSP1 import is not complete, no module added");
                return planned;
            }

            var partJson = GameManager.Instance.Game.Parts.PartJson;
            int added = 0, refreshed = 0, untouched = 0;
            var plansByMod = new Dictionary<string, List<PartAnimationPlan>>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                // Set by ApplyToPart before it edits the core: if it throws after that, PartJson is still rewritten.
                var action = InjectionAction.None;
                try
                {
                    var plan = ApplyToPart(entry, ownFirstDeployable, followRedux, out action);
                    var mod = entry.ModName ?? "?";
                    if (!plansByMod.TryGetValue(mod, out var plans)) plansByMod[mod] = plans = new List<PartAnimationPlan>();
                    plans.Add(plan);
                    planned.Add((entry, plan));
                    if (action == InjectionAction.None) untouched++;
                    else if (action == InjectionAction.Add) added++;
                    else refreshed++;
                }
                catch (Exception e)
                {
                    log.LogError($"[KSP1PartMod] anim: {entry?.PartName} skipped: {e}");
                }
                finally
                {
                    if (action != InjectionAction.None) RewriteJson(partJson, entry);
                }
            }

            foreach (var mod in plansByMod.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                log.LogInfo(ModuleInjection.SummaryLine(mod.Key, mod.Value));
                if (followRedux) log.LogInfo(ModuleInjection.FollowerLine(mod.Key, mod.Value));
            }
            if (!followRedux) log.LogInfo("[KSP1PartMod] anim: Redux deployables not followed (follow_redux_deployables false)");
            log.LogInfo($"[KSP1PartMod] anim: added {added}, refreshed {refreshed}, untouched {untouched} in {clock.ElapsedMilliseconds} ms");
            return planned;
        }

        /// <summary>Keeps <c>PartJson</c> equal to the edited core, which the simulation merges from.</summary>
        private static void RewriteJson(Dictionary<string, string> partJson, Ksp1ImportedPartRegistry.Entry entry)
        {
            try
            {
                partJson[entry.PartName] = IOProvider.ToJson(entry.Core);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log.LogError($"[KSP1PartMod] anim: {entry.PartName} PartJson not rewritten: {e}");
            }
        }

        /// <summary>Plans one part and edits its definition (<c>Core.modules</c> and
        /// <c>Core.data.serializedPartModules</c> together).</summary>
        private static PartAnimationPlan ApplyToPart(Ksp1ImportedPartRegistry.Entry entry, bool ownFirstDeployable,
            bool followRedux, out InjectionAction action)
        {
            action = InjectionAction.None;
            var log = KSP1PartModPlugin.Log;
            var core = entry.Core;
            var modules = core.data.serializedPartModules ??= new List<SerializedPartModule>();
            core.modules ??= new List<ModuleData>();

            var behaviours = new HashSet<string>(modules.Where(m => m.BehaviourType != null).Select(m => m.BehaviourType.Name),
                StringComparer.Ordinal);
            var plan = OwnershipPlanner.Plan(Ksp1NodeAdapter.Modules(entry.PartNode), behaviours.Contains("Module_SolarPanel"),
                behaviours, ownFirstDeployable, followRedux);
            action = ModuleInjection.Decide(modules.Select(m => m.Name).ToList(), plan, ModuleName);
            // From here on the caller rewrites PartJson, even if an edit below throws. Notes once per session: on
            // the first load (Add), not on each reload that refreshes the same core.
            if (action == InjectionAction.Add)
                foreach (var note in plan.Notes) log.LogInfo($"[KSP1PartMod] anim: {entry.PartName} {note}");

            // The deployable module actually on the part: the plan only predicts it from the cfg.
            var deployableIndex = modules.FindIndex(m => m.BehaviourType != null && ReduxDeployableBehaviours.Contains(m.BehaviourType.Name));
            var present = deployableIndex < 0 ? null : modules[deployableIndex].BehaviourType.Name;
            // On a reload we may have removed it ourselves already.
            var removedEarlier = present == null && plan.RemoveReduxDeployable && action == InjectionAction.Refresh;
            if (present != plan.ReduxDeployableModule && !removedEarlier)
                log.LogInfo($"[KSP1PartMod] anim: {entry.PartName} Redux deployable module is {present ?? "none"}, " +
                            $"the plan expected {plan.ReduxDeployableModule ?? "none"}");

            if (action == InjectionAction.None) return plan;

            var data = new Data_Ksp1Animations
            {
                Entries = plan.Entries.ToList(),
                ReduxClips = plan.ReduxClips.ToList(),
            };
            if (action == InjectionAction.Add) Add(core, modules, data);
            else Refresh(core, modules, data);

            if (plan.RemoveReduxDeployable && deployableIndex >= 0)
            {
                RemoveModule(core, modules, deployableIndex);
                log.LogInfo($"[KSP1PartMod] anim: {entry.PartName} removed Redux's {present} (own_first_deployable)");
            }
            return plan;
        }

        private static void Add(PartCore core, List<SerializedPartModule> modules, Data_Ksp1Animations data)
        {
            core.modules.Add(data);
            modules.Add(new SerializedPartModule
            {
                Name = ModuleName,
                BehaviourType = typeof(Module_Ksp1Animations),
                ComponentType = typeof(PartComponentModule_Ksp1Animations),
                ModuleData = new List<SerializedModuleData> { new SerializedModuleData(data) },
            });
        }

        /// <summary>Replaces our data object, by reference, in the module's data list and in
        /// <c>Core.modules</c>.</summary>
        private static void Refresh(PartCore core, List<SerializedPartModule> modules, Data_Ksp1Animations data)
        {
            var index = modules.FindIndex(m => m.Name == ModuleName);
            var module = modules[index];
            if (module.ModuleData == null)
            {
                module.ModuleData = new List<SerializedModuleData>();
                modules[index] = module;
            }

            var dataIndex = module.ModuleData.FindIndex(d => d.DataType == typeof(Data_Ksp1Animations));
            ModuleData old = null;
            if (dataIndex < 0) module.ModuleData.Add(new SerializedModuleData(data));
            else
            {
                var serialized = module.ModuleData[dataIndex];
                old = serialized.DataObject;
                serialized.DataObject = data;
                module.ModuleData[dataIndex] = serialized;
            }

            var coreIndex = old == null ? -1 : core.modules.FindIndex(m => ReferenceEquals(m, old));
            if (coreIndex < 0) coreIndex = core.modules.FindIndex(m => m is Data_Ksp1Animations);
            if (coreIndex >= 0) core.modules[coreIndex] = data;
            else core.modules.Add(data);
        }

        /// <summary>Removes the module and, by reference, its data objects from <c>Core.modules</c> (a solar panel
        /// also has a <c>Data_Deployable</c>, so never by type).</summary>
        private static void RemoveModule(PartCore core, List<SerializedPartModule> modules, int index)
        {
            var removed = modules[index];
            modules.RemoveAt(index);
            if (removed.ModuleData == null) return;
            foreach (var serialized in removed.ModuleData)
            {
                var obj = serialized.DataObject;
                if (obj != null) core.modules.RemoveAll(m => ReferenceEquals(m, obj));
            }
        }
    }
}
