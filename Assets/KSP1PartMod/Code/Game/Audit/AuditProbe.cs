using System;
using System.Collections.Generic;

namespace KSP1PartMod.Game.Audit
{
    /// <summary>Our own fixes report here when they act (<c>AuditProbe.Note("fairing")</c>). A no-op unless a part is
    /// being audited; the notes of the audited part go into its <c>mod_fixes</c> column, which shows the parts that work
    /// only thanks to our fixes. Main thread only, like the spawn.</summary>
    internal static class AuditProbe
    {
        private static readonly List<string> Notes = new List<string>();
        private static bool _active;

        public static bool Active => _active;

        public static void Note(string fix)
        {
            if (!_active || string.IsNullOrEmpty(fix) || Notes.Contains(fix)) return;
            Notes.Add(fix);
        }

        internal static void Begin()
        {
            Notes.Clear();
            _active = true;
        }

        /// <summary>Stops listening and returns the notes, in the order they came.</summary>
        internal static string[] End()
        {
            _active = false;
            var notes = Notes.ToArray();
            Notes.Clear();
            return notes;
        }
    }
}
