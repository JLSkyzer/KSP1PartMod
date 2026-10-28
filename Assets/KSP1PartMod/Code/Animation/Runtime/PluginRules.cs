using System.Globalization;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The game value a phase 2b plugin entry follows (read by the glue's <c>PluginInputs</c>).</summary>
    public enum PluginInput { None, EngineIgnited, EngineMode, Decoupled, Acceleration }

    /// <summary>The decisions of the plugin entries' glue (engine deploy and mode, decoupler clip, science toggle, landing
    /// bag, acceleration spring) that need no game type: which input each kind follows, when a missing input disables
    /// the entry, the spring's acceleration, and the exact log lines. Visual only: nothing here delays ignition or
    /// separation.</summary>
    public static class PluginRules
    {
        public static PluginInput InputOf(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.EngineDeploy: return PluginInput.EngineIgnited;
                case EntryKind.EngineMode: return PluginInput.EngineMode;
                case EntryKind.DecouplerClip: return PluginInput.Decoupled;
                case EntryKind.AccelSpring: return PluginInput.Acceleration;
                default: return PluginInput.None;
            }
        }

        /// <summary>Whether a missing input disables the entry. The editor previews of the engine nozzle and the
        /// decoupler do not read their input; the engine mode does in both scenes (the editor shows the selected mode). The
        /// spring never needs one: without it the clip holds its anchor.</summary>
        public static bool Requires(EntryKind kind, bool inEditor)
        {
            switch (InputOf(kind))
            {
                case PluginInput.EngineIgnited:
                case PluginInput.Decoupled: return !inEditor;
                case PluginInput.EngineMode: return true;
                default: return false;
            }
        }

        /// <summary>The spring's acceleration along the host's up axis: 1 g in the editor (KSP1 runs the spring there under
        /// gravity), else the reading, else NaN, which holds the anchor (<see cref="AccelSpring"/>).</summary>
        public static float SpringAcceleration(bool inEditor, bool hasReader, float reading) =>
            inEditor ? AccelSpring.OneG : hasReader ? reading : float.NaN;

        /// <summary>The engine deploy and mode machines take their pose from the engine once, on their first flight
        /// step (<c>Start</c>), not in the editor.</summary>
        public static bool StartsNow(bool inEditor, bool started) => !inEditor && !started;

        // ---------------------------------------------------------------- log lines (after "anim: ")

        public static string EngineDeployLine(string part, AnimEntryDef def) =>
            $"{part} engine deploy '{def.Clip}' follows Module_Engine.EngineIgnited; ignition not delayed (KSP1 waits until {Num(def.WaitForAnimation)})";

        public static string EngineModeLine(string part, AnimEntryDef def, string field) =>
            $"{part} engine mode '{def.Clip}' follows {field}, time-zero mode '{def.ModeAtTimeZero}'";

        public static string DecouplerLine(string part, AnimEntryDef def) =>
            $"{part} decoupler '{def.Clip}' plays on Data_Decouple.isDecoupled; separation not delayed (KSP1 waits until {Num(def.WaitForAnimation)})";

        public static string ScienceLine(string part, AnimEntryDef def) => $"{part} science toggle '{def.Clip}' (visual only)";

        /// <param name="field">The field read, or null when none was found (the clip then holds its anchor).</param>
        public static string SpringLine(string part, string field) => $"{part} accel spring from {field ?? "none, held"}";

        public static string MissingLine(string part, AnimEntryDef def, string input) =>
            $"{part} {def.Kind} '{def.Clip}' disabled: {input} not found";

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
