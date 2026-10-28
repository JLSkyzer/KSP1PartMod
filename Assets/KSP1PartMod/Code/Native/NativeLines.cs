using System.Collections.Generic;

namespace KSP1PartMod.Native
{
    /// <summary>The <c>[KSP1PartMod] native:</c> log lines (exact text of the plan).</summary>
    public static class NativeLines
    {
        public const string Prefix = "[KSP1PartMod] native: ";

        public static string Pair(string ksp1Module, string nativeModule) => ksp1Module + " -> " + nativeModule;

        public static string Armed(IEnumerable<string> pairs) => Prefix + "armed: " + string.Join(", ", pairs);

        public static string Off(string pair, string switchKey) => Prefix + pair + " off (" + switchKey + " false)";

        public const string NoneArmed = Prefix + "no converter armed, Redux's import as in 0.7.0";

        public static string MemberNotFound(string member, string what) =>
            Prefix + member + " not found, " + what + " not added (Redux's import as in 0.7.0)";

        public static string AlreadyRedux(string ksp1Module) =>
            Prefix + ksp1Module + " already has a Redux importer, ours not added";

        public static string PartNote(string part, string note) => Prefix + part + ": " + note;

        public static string CrossfeedSummary(int parts, int startOn, int techRequired) =>
            Prefix + $"crossfeed: {parts} part(s) got {CrossfeedMapping.NativeModule} ({startOn} start with crossfeed on), techRequired ignored on {techRequired}";

        public static string PatchFailed(string error) =>
            Prefix + "import hook failed, no more native module added this session: " + error;

        public static string ImporterFailed(string part, string ksp1Module, string error) =>
            Prefix + part + ": " + ksp1Module + " not converted (Redux drops it, as in 0.7.0): " + error;
    }
}
