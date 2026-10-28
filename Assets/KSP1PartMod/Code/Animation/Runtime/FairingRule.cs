using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>Why KSP1 engines with a jettison shroud break in the VAB (decompilation of <c>Ksp2Redux.dll</c> 26w42c,
    /// 2026-10-28). Redux turns a KSP1 <c>ModuleJettison</c> into a KSP2 <c>Module_Fairing</c> whose base model transform
    /// is named <c>"model"</c>; a KSP1 import has none. <c>Module_Fairing.OnInitialize</c> then reads the length of its
    /// private <c>_baseColliders</c> while it is still null: the part's finalization stops halfway (placeholder cylinder
    /// kept, the part cannot be spawned again in the same VAB visit). The array is read nowhere else, so an empty one lets
    /// Redux's own code run to the end. Second Redux bug in the same module: <c>OnMaterialLoaded</c> calls
    /// <c>_moduleColor.GetColors()</c> without a check, and KSP1 parts have no <c>Module_Color</c>. Skipping it after the
    /// two material fields are set leaves exactly the state the throw leaves (<c>_materialLoaded</c> still false, so the
    /// KSP2 procedural shroud is still never built), without the exception.</summary>
    public static class FairingRule
    {
        /// <summary>Before <c>OnInitialize</c>: give a KSP1 part's null <c>_baseColliders</c> an empty array.</summary>
        public static bool NeedsEmptyColliders(bool isKsp1Part, bool collidersNull) => isKsp1Part && collidersNull;

        /// <summary>Instead of <c>OnMaterialLoaded</c>: only for a KSP1 part without <c>Module_Color</c> and with a
        /// material (the null-material branch of Redux never touches <c>_moduleColor</c>, it is left to run).</summary>
        public static bool SkipMaterialLoaded(bool isKsp1Part, bool hasModuleColor, bool hasMaterial) =>
            isKsp1Part && !hasModuleColor && hasMaterial;

        /// <summary>True the first time a part name is seen in <paramref name="logged"/>.</summary>
        public static bool FirstTime(HashSet<string> logged, string part) => logged != null && logged.Add(part ?? "?");

        public static string CollidersLine(string part) =>
            $"[KSP1PartMod] fairing: {part} base colliders were null (Redux looks for transform 'model'), set empty";

        public static string MaterialLine(string part) =>
            $"[KSP1PartMod] fairing: {part} has no Module_Color, Redux's OnMaterialLoaded skipped (it would throw); " +
            "the KSP2 shroud stays unbuilt, as before";

        public static readonly StringComparer Names = StringComparer.Ordinal;
    }
}
