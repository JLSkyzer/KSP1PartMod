using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The exact log lines (and PAM labels) of the engine shroud feature, 0.7.0. Name lists are joined with a
    /// plain comma, as in <c>jettisonName</c>.</summary>
    public static class ShroudLines
    {
        // ---- import (Task 2) ----

        public const string Armed =
            "[KSP1PartMod] shroud: armed: Redux's Module_Fairing is not added to KSP1 parts with a ModuleJettison shroud; " +
            "the KSP1 meshes are kept, hidden until a part is attached below";

        public const string OffBySwitch = "[KSP1PartMod] shroud: off (ksp1_engine_shrouds)";

        public const string OffAnimationsDisabled =
            "[KSP1PartMod] shroud: off because animations are disabled (enabled false): Redux's Module_Fairing left as in 0.6.0";

        public static string MemberNotFound(string member) =>
            $"[KSP1PartMod] shroud: {member} not found, shrouds off (Redux's Module_Fairing left as in 0.6.0)";

        public const string StagingSignalArmed = "[KSP1PartMod] shroud: staging signal armed on PartBehavior.ActivateAllModules";

        public const string StagingSignalMissing =
            "[KSP1PartMod] shroud: PartBehavior.ActivateAllModules not found, non-fairing shrouds jettison at the first engine ignition instead";

        public static string ImportSummary(int parts, int withMissing, int rebuilds) =>
            $"[KSP1PartMod] shroud: import: {parts} part(s) kept their KSP1 shroud, {withMissing} with a transform missing, {rebuilds} prefab rebuild(s)";

        public static string ModuleNotAdded(string part, IEnumerable<string> names) =>
            $"[KSP1PartMod] shroud: {part}: Redux's Module_Fairing not added, KSP1 shroud '{string.Join(",", names)}' kept";

        public static string HiddenInPrefab(string part, int count) =>
            $"[KSP1PartMod] shroud: {part}: {count} shroud transform(s) hidden in the prefab";

        public static string NotFoundInModel(string part, IEnumerable<string> names) =>
            $"[KSP1PartMod] shroud: {part}: shroud transform(s) '{string.Join(",", names)}' not found in the model";

        public static string PatchFailed(string exception) =>
            $"[KSP1PartMod] shroud: patch failed, stopped for this session: {exception}";

        // ---- runtime (Task 3) ----

        /// <param name="found">Names found in the model.</param>
        public static string Resolved(string part, IReadOnlyList<string> names, int found, string node, bool nodeFound, bool isFairing, int gates) =>
            $"[KSP1PartMod] anim: {part} shroud '{string.Join(",", names)}': {found}/{names.Count} transform(s) found, node '{node}' " +
            $"{(nodeFound ? "found" : "missing")}, isFairing {(isFairing ? "True" : "False")}, {gates} variant gate(s)";

        public static string CollidersDisabled(string part, int count) =>
            $"[KSP1PartMod] anim: {part} shroud: {count} collider(s) disabled in flight";

        public static string Shown(string part, string name) =>
            $"[KSP1PartMod] anim: {part} shroud '{name}' shown (part attached below)";

        public static string Hidden(string part) => $"[KSP1PartMod] anim: {part} shroud hidden (nothing attached below)";

        public static string VariantChanged(string part, string set, string id, string name) =>
            $"[KSP1PartMod] anim: {part} shroud: variant '{set}' -> '{id}': shroud '{name}'";

        public static string Jettisoned(string part, string name, ShroudReason reason) =>
            $"[KSP1PartMod] anim: {part} shroud '{name}' jettisoned ({(reason == ShroudReason.Staged ? "staged" : reason == ShroudReason.Restored ? "restored from the saved state" : "bottom node detached")})";

        public static string HideOverride(string part, bool on) => $"[KSP1PartMod] anim: {part} shroud hide override {(on ? "on" : "off")}";

        /// <summary>The VAB toggle button: the label of the action that applies next.</summary>
        public const string HideLabel = "Hide shroud";
        public const string ShowLabel = "Show shroud";

        /// <summary>The audit summary line (no log prefix: the audit writes it into its report).</summary>
        public static string AuditSummary(int kept, int missing, int withoutNode, int stillFairing) =>
            $"audit: shrouds: {kept} part(s) with a KSP1 shroud kept, {missing} with a shroud transform missing, {withoutNode} without the node, " +
            $"{stillFairing} still carrying Redux's Module_Fairing";
    }
}
