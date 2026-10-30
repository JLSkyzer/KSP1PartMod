using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Fairings
{
    /// <summary>Every log line of the fairings plan (0.15.0). Pure.</summary>
    public static class FairingLines
    {
        public const string Prefix = "[KSP1PartMod] fairing: ";

        public static string ModuleOf(FairingKind kind) =>
            kind == FairingKind.Saf ? FairingMapping.SafModule : FairingMapping.ProceduralModule;

        // ---- arming

        public static string GuardsArmed => Prefix + "guards armed (_modelTransform, _materialLoaded)";

        public static string GuardMemberMissing(string member) =>
            Prefix + $"Module_Fairing.{member} not found, guards off: KSP1 fairing bases are not converted";

        /// <summary>The converter's missing "member" when the guards did not arm.</summary>
        public const string GuardsNotArmed = "runtime guards on Module_Fairing (fix_fairing_init, or a member missing)";

        /// <summary>The SAF converter's missing "member" when the 0.7.0 prefab patch is not armed.</summary>
        public const string PrefabPatchNotArmed = "the 0.7.0 prefab patch (ksp1_engine_shrouds and enabled, or a member missing)";

        public static string ConverterOff(FairingKind kind, string why) =>
            NativeLines.Prefix + NativeLines.Pair(ModuleOf(kind), FairingMapping.NativeModule) + " off: " + why + " not armed";

        // ---- import

        public static string Part(string part, FairingKind kind, FairingValues v) =>
            NativeLines.Prefix + $"{part}: {ModuleOf(kind)} -> {FairingMapping.NativeModule}: base r {IsruCfg.Inv(v.BaseRadius)}, " +
            $"max r {IsruCfg.Inv(v.MaxRadius)}, cap r {IsruCfg.Inv(v.CapRadius)}, {v.DefaultDeployType}, ejection {IsruCfg.Inv(v.EjectionForce)}, " +
            $"sides {v.FairingSideCount}, t/m2 {IsruCfg.Inv(v.MassAreaRatio)}, pivot {v.Pivot}, axis {v.LocalUpAxis}, " +
            $"base transform '{v.BaseModelTransformName}'";

        /// <summary>The part note of a refusal (logged as <c>native: &lt;part&gt;: …</c>).</summary>
        public static string RefusedNote(FairingKind kind, FairingRefusal reason, string detail = null) =>
            $"{ModuleOf(kind)} not converted ({FairingPartRule.Label(reason)}" + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + ")";

        public static string Summary(FairingKind kind, int parts, IEnumerable<(string Reason, int Count)> refused,
            IEnumerable<(string Field, int Count)> notReproduced)
        {
            var r = (refused ?? Enumerable.Empty<(string, int)>()).ToList();
            var n = (notReproduced ?? Enumerable.Empty<(string, int)>()).ToList();
            return NativeLines.Prefix + NativeLines.Pair(ModuleOf(kind), FairingMapping.NativeModule) + $": {parts} part(s)" +
                   (r.Count == 0 ? "" : "; refused " + string.Join(", ", r.Select(x => x.Reason + " " + x.Count))) +
                   (n.Count == 0 ? "" : "; not reproduced " + string.Join(", ", n.Select(x => x.Field + " " + x.Count)));
        }

        // ---- runtime

        public static string ModelFallback(string part, string transform) =>
            Prefix + $"{part}: model fallback to {transform}";

        public static string MaterialAllowed(string part) =>
            Prefix + $"{part}: material without Module_Color, panels allowed";

        public static string PatchFailed(string error) =>
            Prefix + "patch failed, guards stopped for this session (Redux's code runs as is): " + error;

        // ---- SAF meshes

        public static string SafHidden(string part, int hidden, IReadOnlyCollection<string> missing) =>
            Prefix + $"{part}: SAF mesh hidden {hidden} transform(s), {missing?.Count ?? 0} not found" +
            (missing == null || missing.Count == 0 ? "" : " (" + string.Join(", ", missing) + ")");

        public static string SafHideFailed(string error) =>
            Prefix + "SAF mesh hiding failed, stopped for this session: " + error;

        // ---- B9

        public static string B9Diffs(int diffs) => $"; {diffs} fairing definition diff(s)";
    }
}
