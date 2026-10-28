using System.Collections.Generic;
using System.Linq;

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

        /// <summary>The heat shield reference, once, before the first KSP1 shield.</summary>
        public static string HeatshieldReference(NativeShieldRef r) =>
            r == null || r.IsFallback
                ? Prefix + "heat shield reference: no KSP2 shield found, fallback Rate " + AblatorMapping.Inv(NativeShieldRef.FallbackRate)
                : Prefix + $"heat shield reference: {r.Count} KSP2 shield(s), median Rate {AblatorMapping.Inv(r.Rate)}, capacity {AblatorMapping.Inv(r.Capacity)}";

        /// <summary>Every KSP2 shield read, as a (Rate × PyrolysisLossFactor, capacity) pair, to settle the rule in game.</summary>
        public static string HeatshieldReferenceDetail(NativeShieldRef r) =>
            Prefix + "heat shield reference: (Rate x PLF, capacity) of each: " +
            string.Join(", ", r.Shields.Select(s => "(" + AblatorMapping.Inv(s.Rate) + ", " + AblatorMapping.Inv(s.Capacity) + ")"));

        /// <summary>The <c>native: heat shields:</c> line: <paramref name="others"/> are the other fields not reproduced, each
        /// with its number of parts.</summary>
        public static string HeatshieldSummary(int parts, int withoutResource, IEnumerable<(string Field, int Parts)> others) =>
            Prefix + $"heat shields: {parts} part(s) got {AblatorMapping.NativeModule} ({withoutResource} without the resource, skipped); " +
            "not reproduced: lossExp/lossConst shape, reentryConductivity" +
            string.Concat((others ?? Enumerable.Empty<(string, int)>()).Select(o => $", {o.Item1} on {o.Item2}"));

        public static string PatchFailed(string error) =>
            Prefix + "import hook failed, no native importer is added to new registries this session: " + error;

        public static string ImporterFailed(string part, string ksp1Module, string error) =>
            Prefix + part + ": " + ksp1Module + " not converted (Redux drops it, as in 0.7.0): " + error;
    }
}
