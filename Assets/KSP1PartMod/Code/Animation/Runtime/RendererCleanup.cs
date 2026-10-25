using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>One renderer of a KSP1 part instance, as the cleanup sees it.</summary>
    public readonly struct PartRendererInfo
    {
        public readonly string Path;
        public readonly int MaterialSlots;
        public readonly int NullMaterials;
        /// <summary>Under Redux's generated reentry hull (a <c>ReentryMesh</c> / <c>GeneratedReentryMeshRoot</c>).</summary>
        public readonly bool InReentryHull;
        public readonly bool ActiveInHierarchy;
        public readonly bool Enabled;

        public PartRendererInfo(string path, int materialSlots, int nullMaterials, bool inReentryHull, bool activeInHierarchy, bool enabled)
        {
            Path = path;
            MaterialSlots = materialSlots;
            NullMaterials = nullMaterials;
            InReentryHull = inReentryHull;
            ActiveInHierarchy = activeInHierarchy;
            Enabled = enabled;
        }

        public bool AllMaterialsNull => MaterialSlots > 0 && NullMaterials == MaterialSlots;
    }

    public enum CleanupAction { Keep, DisableRenderer, DeactivateHull }

    /// <summary>A renderer whose material slots are all null draws Unity's error shader. Redux 26w42c's reentry hull
    /// (<c>ReentryMeshGenerator.CreateGeneratedGroup</c>, run on every KSP1 part by
    /// <c>RuntimeReentryMeshFallback.EnsureGenerated</c> from <c>PartModuleLoadPatcher.ApplyOnGameObject</c>) is such a
    /// renderer by design: it is created inactive and <c>ReentryMesh.OnEnable</c> gives it
    /// <c>ContextualFxSystem.ReentryMaterial</c>. Its renderers are never disabled (the reentry effect would be lost
    /// for good); an active hull that still has no material is deactivated, as <c>ReentryMesh.SetReentryActive(false)</c>
    /// does, and the FX manager turns it back on (with its material) at reentry.</summary>
    public static class RendererCleanup
    {
        private const int MaxPaths = 8;

        public static CleanupAction Decide(PartRendererInfo r)
        {
            if (!r.AllMaterialsNull) return CleanupAction.Keep;
            if (r.InReentryHull) return r.ActiveInHierarchy ? CleanupAction.DeactivateHull : CleanupAction.Keep;
            return r.Enabled ? CleanupAction.DisableRenderer : CleanupAction.Keep;
        }

        /// <summary>Null when nothing was hidden.</summary>
        public static string Line(string part, IReadOnlyList<string> renderers, IReadOnlyList<string> hulls)
        {
            renderers = renderers ?? new string[0];
            hulls = hulls ?? new string[0];
            if (renderers.Count == 0 && hulls.Count == 0) return null;
            var parts = new List<string>();
            if (renderers.Count > 0) parts.Add($"hid {renderers.Count} renderer(s) without material ({Paths(renderers)})");
            if (hulls.Count > 0) parts.Add($"deactivated {hulls.Count} reentry hull(s) without material ({Paths(hulls)})");
            return $"[KSP1PartMod] render: {part} " + string.Join("; ", parts);
        }

        /// <summary>The reentry hull renderers left alone, for the log: they are expected to have no material.</summary>
        public static string HullLine(string part, int withoutMaterial, int active) =>
            $"[KSP1PartMod] render: {part} reentry hull(s): {withoutMaterial} renderer(s) without material, {active} active (left to ReentryMesh)";

        private static string Paths(IReadOnlyList<string> paths) =>
            paths.Count <= MaxPaths
                ? string.Join(", ", paths)
                : string.Join(", ", paths.Take(MaxPaths)) + $", … {paths.Count - MaxPaths} more";
    }
}
