using System.Globalization;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The game value a phase 2b plugin entry follows (read by the glue's <c>PluginInputs</c>).</summary>
    public enum PluginInput { None, EngineIgnited, EngineMode, Decoupled, Acceleration, RcsForce }

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
                case EntryKind.RcsGlow:
                case EntryKind.RcsEmissive: return PluginInput.RcsForce;
                default: return PluginInput.None;
            }
        }

        /// <summary>Whether a missing input disables the entry. The editor previews of the engine nozzle and the
        /// decoupler do not read their input, nor does the RCS glow (flight only, at rest in the editor); the engine mode
        /// does in both scenes (the editor shows the selected mode). The spring never needs one: without it the clip holds
        /// its anchor.</summary>
        public static bool Requires(EntryKind kind, bool inEditor)
        {
            switch (InputOf(kind))
            {
                case PluginInput.EngineIgnited:
                case PluginInput.Decoupled:
                case PluginInput.RcsForce: return !inEditor;
                case PluginInput.EngineMode: return true;
                default: return false;
            }
        }

        /// <summary>The spring's acceleration along the host's up axis: 1 g in the editor (KSP1 runs the spring there under
        /// gravity), else the reading, else NaN, which holds the anchor (<see cref="AccelSpring"/>).</summary>
        public static float SpringAcceleration(bool inEditor, bool hasReader, float reading) =>
            inEditor ? AccelSpring.OneG : hasReader ? reading : float.NaN;

        /// <summary>Whether the engine deploy or mode machine still waits for its start pose (<c>Start</c>), taken from the
        /// engine once, on the first tick of the module (not at bind: the saved <c>Data_Engine</c> may not be loaded
        /// yet). A waiting machine is neither ticked nor shown, so a vessel loaded with a running engine shows its nozzle
        /// out with no motion. The nozzle starts in flight only (the editor shows its preview); the engine mode in both
        /// scenes, so a part placed in another mode than the time-zero one shows its pose at once.</summary>
        public static bool WaitsForStart(EntryKind kind, bool inEditor, bool started)
        {
            if (started) return false;
            switch (kind)
            {
                case EntryKind.EngineDeploy: return !inEditor;
                case EntryKind.EngineMode: return true;
                default: return false;
            }
        }

        /// <summary>The machine takes its start pose now: it waits for it (<see cref="WaitsForStart"/>) and this is a
        /// tick of the module (<paramref name="ticking"/>), not the step run at bind.</summary>
        public static bool StartsNow(EntryKind kind, bool inEditor, bool started, bool ticking) =>
            ticking && WaitsForStart(kind, inEditor, started);

        /// <summary>The force a thruster's <c>RelativeForce</c> is divided by: <c>Data_RCS.maxThrust</c> (kN) when the
        /// thruster uses fuel; 1 otherwise, as <c>Module_RCS.CalculateThrust</c> then writes the raw value 1, not
        /// kN (spec "Plugin inputs").</summary>
        public static float RcsReference(float maxThrust, bool requiresFuel) => requiresFuel ? maxThrust : 1f;

        /// <summary>A rotator of <c>ModuleAdvancedLookAtConstraint</c> that Redux's own look-at runtime also drives
        /// (<c>Ksp1LookAtConstraintRuntime.Constraints[].RotatorName</c>, matched as Redux does: ignoring case) is left to
        /// Redux.</summary>
        public static bool LeftToRedux(string rotator, System.Collections.Generic.IReadOnlyList<string> reduxRotators)
        {
            if (string.IsNullOrEmpty(rotator) || reduxRotators == null) return false;
            for (var i = 0; i < reduxRotators.Count; i++)
                if (string.Equals(rotator, reduxRotators[i], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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

        /// <summary>The clip, else (clip-less kinds: look-at, mesh hider, RCS emissive) the entry key.</summary>
        public static string MissingLine(string part, AnimEntryDef def, string input) =>
            $"{part} {def.Kind} '{def.Clip ?? def.Key}' disabled: {input} not found";

        public static string RcsEmissiveLine(string part, string writerNote) => $"{part} rcs emissive {writerNote}";

        /// <summary>A count mismatch disables the entry (spec 2b.6).</summary>
        public static string RcsGlowLine(string part, int thrusters, int hosts) =>
            $"{part} rcs glow: {thrusters} thruster(s), {hosts} host(s) {(thrusters == hosts ? "matched" : "count mismatch, disabled")}";

        /// <summary>Final review I1: the RCS emissive colour, <c>alphaCurve(scalar)</c> premultiplied into every component.
        /// KSP1 writes <c>(1, 1, 1, a)</c> and its shaders read the alpha as the intensity; the KSP2 <c>_EmissionColor</c>
        /// that <c>shader_property_map</c> picks first reads only the RGB, so an idle thruster (scalar 0, alpha 0) must be
        /// black, not white. NaN gives black; the alpha is clamped to 0..1.</summary>
        public static Rgba RcsEmissiveColor(Behaviours.FloatCurve alphaCurve, float scalar)
        {
            var a = alphaCurve != null ? alphaCurve.Evaluate(scalar) : scalar;
            a = a > 0f ? (a < 1f ? a : 1f) : 0f; // NaN fails "a > 0": black
            return new Rgba(a, a, a, a);
        }

        /// <summary>The name prefix of Redux's RCS plume objects (<c>Ksp1RcsPlumeBuilder</c>).</summary>
        public const string RcsPlumePrefix = "[KSP1 RCS Plume]";

        /// <summary>An object inside Redux's RCS plume: it or an ancestor is named <see cref="RcsPlumePrefix"/>…, ignoring
        /// case, the test of Redux's <c>Ksp1PartModuleRuntimeSetup.IsInsideKsp1RcsPlume</c>. Our emission never goes
        /// there.</summary>
        /// <param name="namesUpward">The object's name, then each ancestor's.</param>
        public static bool InsideRcsPlume(System.Collections.Generic.IEnumerable<string> namesUpward)
        {
            if (namesUpward == null) return false;
            foreach (var n in namesUpward)
                if (n != null && n.StartsWith(RcsPlumePrefix, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string RcsFirstValuesLine(string part, float force, float maxThrust, float scalar) =>
            $"{part} rcs glow first values: force {G(force)}, maxThrust {G(maxThrust)}, scalar {G(scalar)}";

        /// <param name="ourEntryKey">The key of our Deploy entry the objects follow, or null for Redux's deployable.</param>
        public static string MeshHiderLine(string part, int objects, string ourEntryKey) =>
            $"{part} mesh hider: {objects} object(s) follow {(ourEntryKey != null ? "our entry '" + ourEntryKey + "'" : "Redux Data_Deployable")}";

        public static string LookAtLine(string part, int constraints, int missing) =>
            $"{part} advanced look-at: {constraints} constraint(s), {missing} missing transform(s)";

        public static string LookAtLeftToReduxLine(string part, string rotator) => $"{part} look-at '{rotator}' left to Redux";

        /// <summary>Six significant digits: a force in kN may be far below 0.001.</summary>
        private static string G(float v) => v.ToString("G6", CultureInfo.InvariantCulture);

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
