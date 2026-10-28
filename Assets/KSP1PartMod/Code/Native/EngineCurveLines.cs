using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native
{
    /// <summary>The <c>[KSP1PartMod] engines:</c> line of 0.13.1 (<c>fix_engine_emissive_curve</c>). Redux's KSP1 engine
    /// importer never sets <c>Data_Engine.EmissiveTemperatureCurve</c> (declared without an initializer); a vessel loaded
    /// from a save rebuilds its <c>Data_Engine</c> from the part JSON, where that curve is null, and
    /// <c>EngineConfigBlobBuilderExtensions.Construct</c> → <c>BlobFloatCurveBuilder.ResolveMaxTime</c> throws: the save
    /// manager skips the vessel as corrupted. The fix fills it with Redux's own zero curve; every other curve a KSP2 config
    /// builder reads is only counted when null. Pure.</summary>
    public static class EngineCurveLines
    {
        public const string Prefix = "[KSP1PartMod] engines: ";

        /// <summary>The <c>FloatCurve</c> fields Redux 26w42c's config blob builders read without a null test, by module data
        /// type: <c>EngineConfigBlobBuilderExtensions</c> (and <c>__ConfigBlobBuilders.Build_EngineModeBlob</c> for each mode),
        /// <c>RCSConfigBlobBuilderExtensions</c>, <c>LitPartConfigBlobBuilderExtensions</c>,
        /// <c>ConvectorConfigBlobBuilderExtensions</c>.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> CheckedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Data_Engine"] = new[] { "EmissiveTemperatureCurve" },
            ["Data_Engine.EngineMode"] = new[]
            {
                "atmosphereCurve", "thrustCurve", "HeatAtmosphereCurve", "atmCurve", "velCurve", "atmCurveIsp", "velCurveIsp",
                "throttleIspCurveAtmStrength", "throttleIspCurve",
            },
            ["Data_RCS"] = new[] { "atmosphereCurve", "thrustCurve" },
            ["Data_LitPart"] = new[] { "RedColorGradient", "GreenColorGradient", "BlueColorGradient", "AlphaColorGradient" },
            ["Data_Convector"] = new[] { "AtmosphereCurve" },
        };

        /// <summary>Counts of one import.</summary>
        public sealed class Tally
        {
            public int FilledModules, FilledParts;
            public readonly Dictionary<string, int> OtherNulls = new Dictionary<string, int>(StringComparer.Ordinal);
            public bool Any => FilledModules > 0 || OtherNulls.Count > 0 || Parts > 0;
            public int Parts;

            public void NoteNull(string owner, string field)
            {
                var key = owner + "." + field;
                OtherNulls.TryGetValue(key, out var n);
                OtherNulls[key] = n + 1;
            }

            public void Clear()
            {
                FilledModules = FilledParts = Parts = 0;
                OtherNulls.Clear();
            }
        }

        public static string Summary(Tally t)
        {
            var others = t.OtherNulls.Count == 0
                ? "none"
                : string.Join(", ", t.OtherNulls.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} x{p.Value}"));
            return $"{Prefix}emissive curve filled on {t.FilledModules} engine module(s) of {t.FilledParts} part(s); other null curves: {others}";
        }

        public static string Armed(bool on) => Prefix + (on ? "emissive curve fix armed (fix_engine_emissive_curve)" : "emissive curve fix off");

        public static string Stopped(string error) => Prefix + "emissive curve fix stopped for this session: " + error;
    }
}
