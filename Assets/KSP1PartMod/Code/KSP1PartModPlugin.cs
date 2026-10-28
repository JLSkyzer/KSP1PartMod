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

namespace KSP1PartMod
{
    /// <summary>Entry point: places the KSP1 parts imported by Redux into the tech tree.</summary>
    public class KSP1PartModPlugin : KerbalMod
    {
        public const string ModId = "KSP1PartMod";
        public const string Version = "0.4.0";
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
                EngineTooltipStats.TryApply(new Harmony(EngineTooltipStats.HarmonyId), Animations.EngineTooltipStats);
            }
            catch (Exception e)
            {
                Log.LogError("[KSP1PartMod] tooltip: engine stats not applied: " + e);
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
        }

        public override void OnInitialized()
        {
            Log = SWLogger;
            Log.LogInfo("[KSP1PartMod] loaded " + Version);
            if (Animations.Enabled) EcsTypeRegistration.TryRegister("initialization");
        }
    }
}
