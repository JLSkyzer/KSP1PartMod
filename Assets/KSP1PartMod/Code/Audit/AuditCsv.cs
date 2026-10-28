using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KSP1PartMod.Audit
{
    /// <summary>The audit report as RFC 4180 CSV: invariant culture, rows separated by CRLF, a cell capped at
    /// <see cref="MaxCell"/> characters.</summary>
    public static class AuditCsv
    {
        public const int MaxCell = 2000;
        private const int KeepWhenCapped = 1990;

        public static readonly IReadOnlyList<string> Columns = new[]
        {
            "part", "mod", "available", "control", "rebuilt", "prefab_ms", "finalize_ms", "kinds",
            "exception_type", "exception_message", "failing_module", "stack",
            "uninitialized_modules", "import_warnings", "import_warning_samples", "import_errors",
            "logged_errors", "logged_error_samples", "materialless", "materialless_hidden", "error_shader",
            "crew", "has_command", "command_initialized", "engines", "mod_fixes",
        };

        public static string Header() => string.Join(",", Columns);

        public static string Line(AuditRecord r)
        {
            var cells = new[]
            {
                r.Part, r.Mod, Bool(r.Available), Bool(r.Control), Bool(r.Rebuilt), Num(r.PrefabMs), Num(r.FinalizeMs),
                AuditKinds.Join(r.Kinds),
                r.ExceptionType, r.ExceptionMessage, r.FailingModule, Join(r.Stack, " <- "),
                Join(r.UninitializedModules, ";"), Int(r.ImportWarnings), Join(r.ImportWarningSamples, " || "), Int(r.ImportErrors),
                Int(r.LoggedErrors), Join(r.LoggedErrorSamples, " || "), Int(r.Materialless), Int(r.MateriallessHidden), Int(r.ErrorShader),
                Int(r.Crew), Bool(r.HasCommand), Bool(r.CommandInitialized), Int(r.Engines), Join(r.ModFixes, ";"),
            };
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(Cap(cells[i])));
            }
            return sb.ToString();
        }

        /// <summary>The header and one row per record, each ended by CRLF.</summary>
        public static string Document(IEnumerable<AuditRecord> records)
        {
            var sb = new StringBuilder(Header()).Append("\r\n");
            if (records != null)
                foreach (var r in records) sb.Append(Line(r)).Append("\r\n");
            return sb.ToString();
        }

        internal static string Cap(string cell)
        {
            if (cell == null) return "";
            if (cell.Length <= MaxCell) return cell;
            return cell.Substring(0, KeepWhenCapped) + "…(+" + (cell.Length - KeepWhenCapped).ToString(CultureInfo.InvariantCulture) + ")";
        }

        internal static string Escape(string cell)
        {
            if (cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return cell;
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        private static string Bool(bool b) => b ? "true" : "false";
        private static string Int(int i) => i.ToString(CultureInfo.InvariantCulture);
        private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Join(IReadOnlyList<string> items, string sep) => items == null ? "" : string.Join(sep, items);
    }
}
