using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KSP1PartMod.Native.Science
{
    /// <summary>A generated definition as KSP2's experiment asset (science plan (b)): <c>{ "version": 0.1, "data": { … } }</c> with the
    /// field names of <c>ExperimentDefinition</c>, <c>FlavorDescription</c> and <c>ResearchLocation</c>, and the enums as strings
    /// (<c>"Both"</c>, <c>"HighOrbit"</c>). Every location has an empty body and region (every body, the vessel's own region). Also the
    /// CSV of the terms for <c>ReduxLocalizer.CreateSourceFromCsv</c>. Pure.</summary>
    public static class ScienceDefinitionJson
    {
        /// <summary><c>ExperimentCore.EXPERIMENT_SERIALIZATION_VERSION</c>.</summary>
        public const float Version = 0.1f;

        /// <summary>KSP2's <c>ScienceExperimentType</c> names.</summary>
        public static string TypeName(ScienceExperimentKind k) =>
            k == ScienceExperimentKind.Both ? "Both" : k == ScienceExperimentKind.Sample ? "SampleType" : "DataType";

        private static JArray Flavor(IEnumerable<ScienceFlavor> flavor) =>
            new JArray((flavor ?? Enumerable.Empty<ScienceFlavor>()).Select(f => new JObject
            {
                ["ResearchLocationID"] = f.LocationId,
                ["LocalizationTag"] = f.Term,
            }));

        /// <summary>The <c>data</c> object of a built definition.</summary>
        public static JObject Data(ScienceDefinitionModel d) => new JObject
        {
            ["ExperimentID"] = d.ExperimentId,
            ["DisplayName"] = d.NameTerm,
            ["DisplayRequirements"] = d.RequirementsTerm,
            ["ExperimentType"] = TypeName(d.Type),
            ["DataFlavorDescriptions"] = Flavor(d.DataFlavor),
            ["SampleFlavorDescriptions"] = Flavor(d.SampleFlavor),
            ["DataReportDisplayName"] = d.DataReportTerm,
            ["SampleReportDisplayName"] = d.SampleReportTerm,
            ["ValidLocations"] = new JArray(d.Locations.Select(l => new JObject
            {
                ["RequiresRegion"] = l.RequiresRegion,
                ["BodyName"] = "",
                ["ScienceSituation"] = l.Situation.ToString(),
                ["ScienceRegion"] = "",
            })),
            ["DataValue"] = (float)d.DataValue,
            ["SampleValue"] = (float)d.SampleValue,
            ["TransmissionSize"] = (float)d.TransmissionSize,
            ["RequiresEVA"] = d.RequiresEva,
        };

        /// <summary>The whole asset text (<c>ExperimentCore</c>).</summary>
        public static string Core(ScienceDefinitionModel d) =>
            new JObject { ["version"] = Version, ["data"] = Data(d) }.ToString(Formatting.None);

        /// <summary>The terms of every built definition as one CSV (<c>Key,Type,Desc,English</c>), each term once, and the counts:
        /// all terms, names and flavor texts.</summary>
        public static string Csv(IEnumerable<ScienceDefinitionModel> definitions, out int terms, out int names, out int flavors)
        {
            var sb = new StringBuilder();
            sb.Append("Key,Type,Desc,English\n");
            var seen = new HashSet<string>();
            terms = names = flavors = 0;
            foreach (var d in (definitions ?? Enumerable.Empty<ScienceDefinitionModel>()).Where(x => x.IsBuilt))
            {
                var flavorTerms = new HashSet<string>(d.DataFlavor.Select(f => f.Term));
                foreach (var t in d.Terms)
                {
                    if (t?.Term == null || !seen.Add(t.Term)) continue;
                    sb.Append(Escape(t.Term)).Append(",Text,,").Append(Escape(t.English)).Append('\n');
                    terms++;
                    if (t.Term == d.NameTerm) names++;
                    else if (flavorTerms.Contains(t.Term)) flavors++;
                }
            }
            return sb.ToString();
        }

        /// <summary>A CSV field: quoted, quotes doubled, line breaks as spaces.</summary>
        public static string Escape(string value) =>
            "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}
