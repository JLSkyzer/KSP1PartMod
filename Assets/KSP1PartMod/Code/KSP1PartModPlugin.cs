using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using Redux.ExtraModTypes;
using ILogger = SpaceWarp2.API.Logging.ILogger;

[assembly: InternalsVisibleTo("KSP1PartMod.Tests.ReduxMu")]

namespace KSP1PartMod
{
    /// <summary>Entry point: places the KSP1 parts imported by Redux into the tech tree.</summary>
    public class KSP1PartModPlugin : KerbalMod
    {
        public const string ModId = "KSP1PartMod";
        public const string Version = "0.1.0";
        public const string AnimationSettingsFile = "animations.json";

        private static string _modFolder;

        /// <summary>Folder of KSP1PartMod.dll (the installed mod folder).</summary>
        internal static string ModFolder =>
            _modFolder ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        internal static ILogger Log { get; private set; }

        /// <summary>animations.json as read at startup.</summary>
        internal static AnimationSettings Animations { get; private set; } = AnimationSettings.Defaults;

        public override void OnPreInitialized()
        {
            Log = SWLogger;
            Animations = ReadAnimationSettings();
            ApplyMuReaderFix(Animations);
            TechTreeLoadStep.Register(this);
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
        }
    }
}
