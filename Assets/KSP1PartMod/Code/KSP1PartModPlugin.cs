using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Game.Ui;
using Redux.ExtraModTypes;
using ILogger = SpaceWarp2.API.Logging.ILogger;

[assembly: InternalsVisibleTo("KSP1PartMod.Tests.ReduxMu")]
[assembly: InternalsVisibleTo("KSP1PartMod.Tests.Animation")]
[assembly: InternalsVisibleTo("KSP1PartMod.Tests.Audit")]
[assembly: InternalsVisibleTo("KSP1PartMod.Tests.B9")]

namespace KSP1PartMod
{
    /// <summary>Entry point: places the KSP1 parts imported by Redux into the tech tree.</summary>
    public class KSP1PartModPlugin : KerbalMod
    {
        public const string ModId = "KSP1PartMod";
        public const string Version = "0.12.0";
        public const string AnimationSettingsFile = "animations.json";

        private static string _modFolder;

        /// <summary>Folder of KSP1PartMod.dll (the installed mod folder).</summary>
        internal static string ModFolder =>
            _modFolder ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        internal static ILogger Log { get; private set; }

        /// <summary>The loaded plugin, for its campaign feature switch (<c>Enabled</c>).</summary>
        internal static KSP1PartModPlugin Instance { get; private set; }

        /// <summary>animations.json as read at startup.</summary>
        internal static AnimationSettings Animations { get; private set; } = AnimationSettings.Defaults;

        public override void OnPreInitialized()
        {
            Log = SWLogger;
            Instance = this;
            // First: nothing about animations may cost the tech-tree placement.
            TechTreeLoadStep.Register();
            Animations = ReadAnimationSettings();
            global::KSP1PartMod.Game.Perf.PerfReportWriter.Begin(DateTime.Now);
            try
            {
                // Right after the settings and before every other patch: each later patch's cost falls inside a timed phase.
                global::KSP1PartMod.Game.Perf.LoadTimeline.Arm(new Harmony(global::KSP1PartMod.Game.Perf.LoadTimeline.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Perf.PerfLines.LoadStopped(e.ToString()));
            }
            try
            {
                // Before Redux's import (which finalizes the cfgs) and before every other import patch.
                MmPatches.TryApply(new Harmony(MmPatches.HarmonyId), MmPatches.Switches.From(Animations));
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.ModuleManager.MmLines.PatchFailed(e.ToString()));
            }
            Log.LogInfo("[KSP1PartMod] anim: VAB animation speed: " + EditorSpeed.Describe(Animations.EditorAnimationSpeed) +
                $" (editor_animation_speed {Animations.EditorAnimationSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            ApplyMuReaderFix(Animations);
            try
            {
                RenderFixes.Apply(new Harmony(RenderFixes.HarmonyId), Animations.HideMaterialLessRenderers, Animations.FixPartIcons);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] render: fixes not applied: " + e);
            }
            try
            {
                OabSubcategories.TryApply(new Harmony(OabSubcategories.HarmonyId), Animations.SubcategoryBar);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] ui: sub-category bar not armed: " + e);
            }
            try
            {
                FairingFixes.TryApply(new Harmony(FairingFixes.HarmonyId), Animations.FixFairingInit);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] fairing: fix not applied: " + e);
            }
            try
            {
                // Before Redux's import, which runs after pre-initialization; the settings are read above.
                ShroudImport.TryApply(new Harmony(ShroudImport.HarmonyId), ShroudSwitch.Arm(Animations.Enabled, Animations.Ksp1EngineShrouds));
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] shroud: import patches not applied, Redux's Module_Fairing left as in 0.6.0: " + e);
            }
            try
            {
                // Before Redux's import (tanks, B9 tank types, converters) and before NativeModules; independent of enabled.
                OreRemap.Arm(new Harmony(OreRemap.HarmonyId), Animations.OreRemapTarget);
                // Old saves: their Ore was stored as TestRocks; moved to the target on load (KSP1 parts only).
                OreMigration.Arm(new Harmony(OreMigration.HarmonyId));
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.NativeLines.Prefix + "ore: remap not applied, Redux's mapping kept: " + e);
            }
            try
            {
                // Before NativeModules (the science importer reads the definitions) and before Patch Manager runs its patch
                // flow (route A registers duplicates); independent of enabled.
                ScienceDefinitions.TryApply(new Harmony(ScienceDefinitions.HarmonyId), Animations, SWMetadata?.Guid ?? ModId);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.Science.ScienceLines.Failed("definitions", e.ToString()));
            }
            try
            {
                // Before Redux's import, which builds its module importer registries; independent of enabled.
                NativeModules.TryApply(new Harmony(NativeModules.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.NativeLines.PatchFailed(e.ToString()));
            }
            try
            {
                // With the importer's switch, not enabled: it protects the campaigns where the mod is off.
                if (Animations.Ksp1Science) ScienceStoreGuardPatch.TryApply(new Harmony(ScienceStoreGuardPatch.HarmonyId));
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.Science.ScienceLines.PatchFailed(e.ToString()));
            }
            try
            {
                // The runtime of converted experiments: animator gate, clips, report line (with ksp1_science).
                ScienceRuntime.TryApply(new Harmony(ScienceRuntime.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.Science.ScienceLines.PatchFailed(e.ToString()));
            }
            try
            {
                // The runtime of converted drills and scanners: ground probe, deploy bridge (whatever enabled is).
                IsruRuntime.TryApply(new Harmony(IsruRuntime.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.IsruLines.PatchFailed(e.ToString()));
            }
            try
            {
                // The runtime of converted radiators: deploy bridge, auto-on, start-state repair (whatever enabled is).
                RadiatorRuntime.TryApply(new Harmony(RadiatorRuntime.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.Native.RadiatorLines.PatchFailed(e.ToString()));
            }
            try
            {
                // After NativeModules, before Redux's import (which builds the tank catalog and the variant sets).
                B9Overrides.TryApply(new Harmony(B9Overrides.HarmonyId), Animations);
            }
            catch (Exception e)
            {
                Log.LogError(KSP1PartMod.B9.B9Lines.PartFailed("(arming)", e.ToString()));
            }
            try
            {
                EngineTooltipStats.TryApply(new Harmony(EngineTooltipStats.HarmonyId), Animations.EngineTooltipStats);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] tooltip: engine stats not applied: " + e);
            }
            try
            {
                // Before Redux's import, which runs after pre-initialization (see ApplyMuReaderFix).
                Ksp1Textures.TryApply(Animations.ShareKsp1Textures, Animations.Ksp1TexturesFollowQuality, Animations.RaiseStreamingBudget);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] textures: not armed, Redux's texture loading left as it is: " + e);
            }
            if (Animations.SubcategoryBar)
                Log.LogInfo("[KSP1PartMod] ui: grid width " + (Animations.SubcategoryBarKeepsGrid
                    ? "kept: the parts panel widens by the sub-category bar's width while it is shown"
                    : "not kept: the sub-category bar narrows the grid (subcategory_bar_keeps_grid false)"));
            if (Animations.Diagnostics) MaterialDiagnostics.Apply(new Harmony(MaterialDiagnostics.HarmonyId));
            else Log.LogInfo("[KSP1PartMod] diag: material dump disabled by " + AnimationSettingsFile);
            try
            {
                AnimationLoadStep.Register();
                if (Animations.Enabled) EcsTypeRegistration.TryRegister("pre-initialization");
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] anim: step registration failed, no animation module added: " + e);
            }
        }

        /// <summary>Never throws: a missing or broken file logs one line and gives the defaults.</summary>
        internal static AnimationSettings ReadAnimationSettings()
        {
            var path = Path.Combine(ModFolder, AnimationSettingsFile);
            try
            {
                if (!File.Exists(path))
                {
                    Log.LogWarning($"[KSP1PartMod] anim: {path} not found, defaults used");
                    return AnimationSettings.Defaults;
                }
                var settings = AnimationSettings.Parse(File.ReadAllText(path), out var error);
                if (error != null) Log.LogWarning($"[KSP1PartMod] anim: {AnimationSettingsFile}: {error}");
                return settings;
            }
            catch (Exception e)
            {
                Log.LogError($"[KSP1PartMod] anim: could not read {path}, defaults used: {e}");
                return AnimationSettings.Defaults;
            }
        }

        /// <summary>Before the first campaign load, so Redux's KSP1 import already reads the old models.</summary>
        private static void ApplyMuReaderFix(AnimationSettings settings)
        {
            if (!settings.FixMuReader)
            {
                Log.LogInfo("[KSP1PartMod] mu: fix disabled by " + AnimationSettingsFile);
                return;
            }
            var timed = KSP1PartMod.Perf.PerfClock.Start();
            try
            {
                var applied = MuReaderFix.TryApply(new Harmony(MuReaderFix.HarmonyId), out var report);
                if (applied) Log.LogInfo(report);
                else Log.LogWarning(report);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] mu: fix failed, Redux's reader left as it was: " + e);
            }
            finally
            {
                KSP1PartMod.Perf.PerfClock.Stop(KSP1PartMod.Perf.PerfPhase.MuFix, timed);
            }
        }

        public override void OnInitialized()
        {
            Log = SWLogger;
            Log.LogInfo("[KSP1PartMod] loaded " + Version);
            // The timeline first: its campaign load and ours lines are then in the report written at the same VAB or flight entry.
            global::KSP1PartMod.Game.Perf.LoadTimeline.SubscribeToGameStates(Game);
            Ksp1Textures.SubscribeToGameStates(Game);
            ChannelFlush.SubscribeToGameStates(Game); // perf_counter only: the scene of the frame lines
            try
            {
                global::KSP1PartMod.Game.Audit.AuditRunner.Arm(Game, Animations);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] audit: not armed, audit off: " + e);
            }
            if (Animations.Enabled) EcsTypeRegistration.TryRegister("initialization");
        }
    }
}
