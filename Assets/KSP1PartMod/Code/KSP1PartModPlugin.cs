using System.IO;
using System.Reflection;
using KSP1PartMod.Game;
using Redux.ExtraModTypes;
using ILogger = SpaceWarp2.API.Logging.ILogger;

namespace KSP1PartMod
{
    /// <summary>Entry point: places the KSP1 parts imported by Redux into the tech tree.</summary>
    public class KSP1PartModPlugin : KerbalMod
    {
        public const string ModId = "KSP1PartMod";
        public const string Version = "0.1.0";

        private static string _modFolder;

        /// <summary>Folder of KSP1PartMod.dll (the installed mod folder).</summary>
        internal static string ModFolder =>
            _modFolder ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        internal static ILogger Log { get; private set; }

        public override void OnPreInitialized()
        {
            Log = SWLogger;
            TechTreeLoadStep.Register(this);
        }

        public override void OnInitialized()
        {
            Log = SWLogger;
            Log.LogInfo("[KSP1PartMod] loaded " + Version);
        }
    }
}
