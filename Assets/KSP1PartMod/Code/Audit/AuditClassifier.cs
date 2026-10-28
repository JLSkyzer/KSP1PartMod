using System.Collections.Generic;

namespace KSP1PartMod.Audit
{
    /// <summary>What the spawner saw of one part, before it is turned into kinds.</summary>
    public sealed class AuditObservations
    {
        /// <summary>An exception was caught.</summary>
        public bool Threw;
        public bool IsFinalized;
        public bool PlaceholderPresent;
        public bool NoPrefab;
        public bool TemplateFailed;
        public bool CrashedPreviousRun;
        public IReadOnlyList<string> UninitializedModules;
        public int ImportWarnings;
        public int ImportErrors;
        public int LoggedErrors;
        public int Materialless;
        public int ErrorShader;
    }

    public static class AuditClassifier
    {
        /// <summary>Observations to kinds. A part skipped after a crash gives only <see cref="AuditKind.CrashedPreviousRun"/>.
        /// With no prefab, or a failed template step, nothing was finalized, so the finalize kinds are left out. An exception with <c>IsFinalized</c>
        /// gives <c>Threw</c> only.</summary>
        public static AuditKind Kinds(AuditObservations o)
        {
            if (o == null) return AuditKind.None;
            if (o.CrashedPreviousRun) return AuditKind.CrashedPreviousRun;
            var k = AuditKind.None;
            if (o.Threw) k |= AuditKind.Threw;
            if (o.NoPrefab) k |= AuditKind.NoPrefab;
            if (o.TemplateFailed) k |= AuditKind.TemplateFailed;
            if (!o.NoPrefab && !o.TemplateFailed)
            {
                if (!o.IsFinalized) k |= AuditKind.NotFinalized;
                if (o.PlaceholderPresent) k |= AuditKind.PlaceholderLeft;
                if (o.UninitializedModules != null && o.UninitializedModules.Count > 0) k |= AuditKind.ModuleNotInitialized;
            }
            if (o.ImportWarnings > 0) k |= AuditKind.ImportWarning;
            if (o.ImportErrors > 0) k |= AuditKind.ImportError;
            if (o.LoggedErrors > 0) k |= AuditKind.LoggedError;
            if (o.Materialless > 0) k |= AuditKind.MaterialLess;
            if (o.ErrorShader > 0) k |= AuditKind.ErrorShader;
            return k;
        }

        /// <summary>A part with any kind other than <see cref="AuditKind.ImportWarning"/> is a problem; with only that
        /// kind it is "ok with warnings".</summary>
        public static bool IsProblem(AuditKind kinds) => (kinds & ~AuditKind.ImportWarning) != 0;
    }
}
