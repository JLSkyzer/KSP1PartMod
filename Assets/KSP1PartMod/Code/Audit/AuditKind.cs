using System;
using System.Collections.Generic;

namespace KSP1PartMod.Audit
{
    /// <summary>What an audited part showed. A part can carry several kinds.</summary>
    [Flags]
    public enum AuditKind
    {
        None = 0,
        /// <summary>An exception escaped <c>FinalizeLoad</c> (or the step that was running).</summary>
        Threw = 1 << 0,
        /// <summary><c>IsFinalized</c> stayed false.</summary>
        NotFinalized = 1 << 1,
        /// <summary>The <c>model-placeholder</c> child is still there.</summary>
        PlaceholderLeft = 1 << 2,
        /// <summary>A part module has <c>IsInitialized</c> false after <c>FinalizeLoad</c>.</summary>
        ModuleNotInitialized = 1 << 3,
        /// <summary>No prefab could be built for the part.</summary>
        NoPrefab = 1 << 4,
        /// <summary>The template step threw.</summary>
        TemplateFailed = 1 << 5,
        /// <summary>A <c>[KSP1 Import]</c> warning was logged.</summary>
        ImportWarning = 1 << 6,
        /// <summary>A <c>[KSP1 Import]</c> error was logged.</summary>
        ImportError = 1 << 7,
        /// <summary>Another error or exception was logged while the part was processed.</summary>
        LoggedError = 1 << 8,
        /// <summary>A renderer has a null material.</summary>
        MaterialLess = 1 << 9,
        /// <summary>A renderer uses <c>Hidden/InternalErrorShader</c>.</summary>
        ErrorShader = 1 << 10,
        /// <summary>The previous run stopped during this part; it was skipped.</summary>
        CrashedPreviousRun = 1 << 11,
    }

    /// <summary>Names of the kinds, as written in logs and reports.</summary>
    public static class AuditKinds
    {
        /// <summary>Every single flag, in declaration order.</summary>
        public static readonly IReadOnlyList<AuditKind> All = new[]
        {
            AuditKind.Threw, AuditKind.NotFinalized, AuditKind.PlaceholderLeft, AuditKind.ModuleNotInitialized,
            AuditKind.NoPrefab, AuditKind.TemplateFailed, AuditKind.ImportWarning, AuditKind.ImportError,
            AuditKind.LoggedError, AuditKind.MaterialLess, AuditKind.ErrorShader, AuditKind.CrashedPreviousRun,
        };

        /// <summary>The snake_case name of one flag.</summary>
        public static string Name(AuditKind single)
        {
            switch (single)
            {
                case AuditKind.Threw: return "threw";
                case AuditKind.NotFinalized: return "not_finalized";
                case AuditKind.PlaceholderLeft: return "placeholder_left";
                case AuditKind.ModuleNotInitialized: return "module_not_initialized";
                case AuditKind.NoPrefab: return "no_prefab";
                case AuditKind.TemplateFailed: return "template_failed";
                case AuditKind.ImportWarning: return "import_warning";
                case AuditKind.ImportError: return "import_error";
                case AuditKind.LoggedError: return "logged_error";
                case AuditKind.MaterialLess: return "material_less";
                case AuditKind.ErrorShader: return "error_shader";
                case AuditKind.CrashedPreviousRun: return "crashed_previous_run";
                default: return single.ToString();
            }
        }

        /// <summary>The names of the set flags joined with '|' (empty for none).</summary>
        public static string Join(AuditKind kinds)
        {
            var parts = new List<string>();
            foreach (var k in All)
                if ((kinds & k) != 0) parts.Add(Name(k));
            return string.Join("|", parts);
        }
    }
}
