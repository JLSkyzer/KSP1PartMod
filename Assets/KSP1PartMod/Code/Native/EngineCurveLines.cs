using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

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
            /// <summary>0.15.1: intakes whose null mach curve was filled, the parts that had one, and where the curve came from.</summary>
            public int IntakesFilled, IntakeParts, IntakesFromCfg, IntakesDefault;
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
                IntakesFilled = IntakeParts = IntakesFromCfg = IntakesDefault = 0;
                OtherNulls.Clear();
            }
        }

        public static string Summary(Tally t)
        {
            var others = t.OtherNulls.Count == 0
                ? "none"
                : string.Join(", ", t.OtherNulls.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} x{p.Value}"));
            return $"{Prefix}emissive curve filled on {t.FilledModules} engine module(s) of {t.FilledParts} part(s); other null curves: {others}; " +
                   $"intake mach curve filled on {t.IntakesFilled} intake(s) of {t.IntakeParts} part(s) ({t.IntakesFromCfg} from the cfg, {t.IntakesDefault} default)";
        }

        /// <summary>The KSP1 module whose <c>machCurve</c> Redux's intake importer drops.</summary>
        public const string IntakeModule = "ModuleResourceIntake";

        /// <summary>KSP2's own flight default for a missing intake mach curve (<c>Module_ResourceIntake.OnInitialize</c>):
        /// one smooth key (0, 1), flattened as <see cref="IntakeMachKeys"/> gives keys.</summary>
        public static float[] DefaultIntakeMachKeys() => new[] { 0f, 1f, float.NaN, float.NaN };

        /// <summary>The <c>ModuleResourceIntake</c> nodes of a KSP1 part, in cfg order (Redux imports each, in that order).</summary>
        public static List<IKsp1Node> IntakeModules(IKsp1Node part) =>
            part == null
                ? new List<IKsp1Node>()
                : part.GetNodes("MODULE").Where(m => m != null && string.Equals(m.GetValue("name")?.Trim(), IntakeModule, StringComparison.Ordinal)).ToList();

        /// <summary>The cfg's own <c>machCurve</c> of one intake module, four floats per key (time, value, in, out; NaN tangents
        /// for a two-value key, added with <c>Add(t, v)</c>); null when the module has none or no readable key.</summary>
        public static float[] IntakeMachKeys(IKsp1Node intakeModule)
        {
            var curve = intakeModule?.GetNodes("machCurve").FirstOrDefault();
            if (curve == null) return null;
            var keys = EntryParser.Keys(curve);
            return keys.Length >= 4 ? keys : null;
        }

        /// <summary>The keys of the <paramref name="index"/>-th null intake of a part: that intake's cfg curve when the part's
        /// cfg intakes line up with its imported ones (<paramref name="importedIntakes"/>), else KSP2's default.</summary>
        public static float[] IntakeKeysFor(IReadOnlyList<IKsp1Node> cfgIntakes, int importedIntakes, int index, out bool fromCfg)
        {
            var keys = cfgIntakes != null && cfgIntakes.Count == importedIntakes && index >= 0 && index < cfgIntakes.Count
                ? IntakeMachKeys(cfgIntakes[index])
                : null;
            fromCfg = keys != null;
            return keys ?? DefaultIntakeMachKeys();
        }

        public static string Armed(bool on) => Armed(on, false);

        /// <summary>The arming line for the emissive (0.13.1) and intake (0.15.1) halves.</summary>
        public static string Armed(bool engines, bool intakes) =>
            Prefix + (engines ? "emissive curve fix armed (fix_engine_emissive_curve)" : "emissive curve fix off") + ", " +
            (intakes ? "intake mach curve fix armed (fix_intake_mach_curve)" : "intake mach curve fix off");

        public static string Stopped(string error) => Prefix + "emissive curve fix stopped for this session: " + error;
    }
}
