using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>The deploy gate a wheel runs with, as the setup line names it (wheels plan, Task 4).</summary>
    public enum WheelGate
    {
        /// <summary>A native <c>Module_Deployable</c>: the wheel follows the deploy state.</summary>
        Native,
        /// <summary>No <c>ModuleWheelDeployment</c> in the cfg.</summary>
        FixedWheel,
        /// <summary>Redux kept its legacy <c>Ksp1Module_Deployable</c>: the wheel is always active.</summary>
        LegacyKept,
        /// <summary><c>own_first_deployable</c> removed Redux's deployable: the wheel is always active.</summary>
        OwnFirst,
    }

    /// <summary>The log lines of the wheels plan, Tasks 3 and 4 (exact text). Numbers: G6, invariant; an unknown median reads
    /// <c>none</c>.</summary>
    public static class WheelLines
    {
        public const string Prefix = NativeLines.Prefix;
        public const string WheelPrefix = "[KSP1PartMod] wheel: ";

        /// <summary>The animation note of a gear bridge that stands down (Task 4).</summary>
        public const string GearBridgeStandsDown = "gear bridge: Module_WheelBase already on Gear";

        private static string Inv(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "none" : IsruCfg.Inv(v);

        private static string List(IEnumerable<(string Name, int Count)> items)
        {
            var list = (items ?? Enumerable.Empty<(string, int)>()).Select(i => i.Item1 + " " + i.Item2).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // ---- Task 3: the importers ----

        public static string Reference(int parts, int legs, int free, int motorized, double radius, double brake, double lockTorque,
            double motorPeak, double motorEc, double ratedLoad) =>
            Prefix + $"wheel reference: {parts} KSP2 wheel part(s) (LEG {legs}, FREE {free}, MOTORIZED {motorized}), " +
            $"median radius {Inv(radius)} m, brake {Inv(brake)} kN·m, lock {Inv(lockTorque)} kN·m, " +
            $"motor peak {Inv(motorPeak)} kN·m at {Inv(motorEc)} EC/s, rated load {Inv(ratedLoad)} t";

        public static string ReferenceNone(string reason) => Prefix + $"wheel reference: none read ({reason})";

        public static string Part(string part, WheelBaseValues b, string kinds) =>
            Prefix + $"{part}: wheel {b.WheelType} r {Inv(b.Radius)} m on '{b.WheelColliderTransformName}': {kinds} " +
            $"(deploy on at {Inv(b.DeploymentSubsystemNormalized)}, off at {Inv(b.RetractionSubsystemNormalized)}; " +
            $"friction {Inv(b.FrictionPeak)}/{Inv(b.FrictionLimit)}, {(b.AutoFriction ? "auto" : "manual")})";

        public static string Summary(WheelTally t) =>
            Prefix + $"wheels: {t.Converted(WheelKind.Base)} part(s) got Module_WheelBase ({t.Bases(WheelTypeName.LEG)} leg(s), " +
            $"{t.Bases(WheelTypeName.FREE)} free, {t.Bases(WheelTypeName.MOTORIZED)} motorized); " +
            $"suspension {t.Converted(WheelKind.Suspension)}, steering {t.Converted(WheelKind.Steering)}, motor {t.Converted(WheelKind.Motor)}, " +
            $"motor-steering {t.Converted(WheelKind.MotorSteering)}, brakes {t.Converted(WheelKind.Brakes)}, damage {t.Converted(WheelKind.Damage)}, " +
            $"lock {t.Converted(WheelKind.Lock)}, bogey {t.Converted(WheelKind.Bogey)}; refused: {List(t.RefusedByReason())}; " +
            $"not reproduced: {List(WheelTally.ByCount(t.NotReproduced))}; " +
            $"unused by Redux's solver: {List(WheelTally.ByCount(t.UnusedBySolver))}";

        public static string MemberNotFound(string member, string kind) =>
            Prefix + $"wheels: {member} not found, {kind} off (Redux drops it, as in 0.13.1)";

        public static string B9(int edits, IReadOnlyCollection<(string Part, string Field)> stateEdits)
        {
            var list = (stateEdits ?? new (string, string)[0]).Select(e => e.Part + ": " + e.Field).ToList();
            return Prefix + $"wheels: B9: {edits} subtype edit(s) of wheel modules, {list.Count} on state fields not carried " +
                   $"({(list.Count == 0 ? "none" : string.Join(", ", list))})";
        }

        // ---- Task 4: the runtime ----

        public static string Armed(IEnumerable<string> kindsOn)
        {
            var list = (kindsOn ?? Enumerable.Empty<string>()).ToList();
            return WheelPrefix + "armed: " + (list.Count == 0 ? "none" : string.Join(", ", list));
        }

        /// <summary>The gate of the setup line, from what the part has.</summary>
        public static WheelGate Gate(bool hasNative, bool hasLegacy, bool hadDeployment, bool ownFirst)
        {
            if (hasNative) return WheelGate.Native;
            if (!hadDeployment) return WheelGate.FixedWheel;
            if (ownFirst && !hasLegacy) return WheelGate.OwnFirst;
            return WheelGate.LegacyKept;
        }

        public static string GateText(WheelGate gate)
        {
            switch (gate)
            {
                case WheelGate.Native: return "native Module_Deployable";
                case WheelGate.FixedWheel: return "none (fixed wheel)";
                case WheelGate.OwnFirst: return "none: always active (own_first_deployable)";
                default: return "none: always active (Redux kept its legacy deployable)";
            }
        }

        /// <param name="wheelTransform">The cfg's <c>wheelTransformName</c>; null or empty: <c>none</c>.</param>
        public static string Setup(string part, WheelTypeName type, string host, bool hostFound, string wheelTransform,
            bool wheelTransformFound, WheelGate gate) =>
            WheelPrefix + $"{part}: {type}, collider host '{host}' {(hostFound ? "found" : "MISSING")}, " +
            $"wheel transform '{wheelTransform ?? ""}' {(string.IsNullOrEmpty(wheelTransform) ? "none" : wheelTransformFound ? "found" : "MISSING")}, " +
            $"deploy gate {GateText(gate)}";

        public static string RuntimeMemberNotFound(string member) =>
            WheelPrefix + $"{member} not found, the wheel line is off (Redux's module left as is)";

        public static string PatchFailed(string exception) => WheelPrefix + $"patch failed, stopped for this session: {exception}";
    }
}
