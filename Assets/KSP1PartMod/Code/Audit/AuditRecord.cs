using System.Collections.Generic;

namespace KSP1PartMod.Audit
{
    /// <summary>The result for one audited part, one CSV row.</summary>
    public sealed class AuditRecord
    {
        private static readonly string[] None = new string[0];

        // identity
        public string Part = "";
        public string Mod = "";
        /// <summary>The part is available (unlocked) in the current campaign.</summary>
        public bool Available;
        /// <summary>One of the KSP2 control parts.</summary>
        public bool Control;
        /// <summary>We caused the prefab to be rebuilt.</summary>
        public bool Rebuilt;

        // timings
        public double PrefabMs;
        public double FinalizeMs;
        public double TotalMs => PrefabMs + FinalizeMs;

        public AuditKind Kinds;

        // exception
        public string ExceptionType = "";
        public string ExceptionMessage = "";
        /// <summary>The part module in whose frame the exception came from, or null.</summary>
        public string FailingModule;
        public IReadOnlyList<string> Stack = None;

        // modules and logs
        public IReadOnlyList<string> UninitializedModules = None;
        public int ImportWarnings;
        public IReadOnlyList<string> ImportWarningSamples = None;
        public int ImportErrors;
        public int LoggedErrors;
        public IReadOnlyList<string> LoggedErrorSamples = None;

        // renderers
        public int Materialless;
        public int MateriallessHidden;
        public int ErrorShader;

        // sanity and fixes
        public int Crew;
        public bool HasCommand;
        public bool CommandInitialized;
        public int Engines;
        public IReadOnlyList<string> ModFixes = None;
    }
}
