using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.Native.Science
{
    /// <summary>A localization term of a generated definition and its English text.</summary>
    public sealed class ScienceTerm
    {
        public string Term { get; }
        public string English { get; }

        public ScienceTerm(string term, string english)
        {
            Term = term;
            English = english;
        }
    }

    /// <summary>One flavor entry of a generated definition: the KSP2 location id (<c>Default</c>, <c>Mun_LowOrbit</c>) and its term.</summary>
    public sealed class ScienceFlavor
    {
        public string LocationId { get; }
        public string Term { get; }

        public ScienceFlavor(string locationId, string term)
        {
            LocationId = locationId;
            Term = term;
        }
    }

    /// <summary>What the first module that uses a KSP1 id brings to its definition: its <c>xmitDataScalar</c> and, for a DMagic
    /// module, its <c>sitMask</c>/<c>bioMask</c> (cfg fields only). Null fields: absent.</summary>
    public sealed class DefinitionOverrides
    {
        public double? XmitDataScalar { get; }
        public int? SitMask { get; }
        public int? BioMask { get; }

        public DefinitionOverrides(double? xmitDataScalar, int? sitMask, int? bioMask)
        {
            XmitDataScalar = xmitDataScalar;
            SitMask = sitMask;
            BioMask = bioMask;
        }

        public static DefinitionOverrides From(IKsp1Node module)
        {
            if (module == null) return new DefinitionOverrides(null, null, null);
            var dmagic = ScienceMapping.IsDMagic(module);
            return new DefinitionOverrides(IsruCfg.Number(module, "xmitDataScalar"),
                dmagic ? Mask(module, "sitMask") : null, dmagic ? Mask(module, "bioMask") : null);
        }

        internal static int? Mask(IKsp1Node node, string key)
        {
            var n = IsruCfg.Number(node, key);
            return n.HasValue && n.Value >= 0 && n.Value <= int.MaxValue ? (int)n.Value : (int?)null;
        }

        /// <summary>For each KSP1 id, the overrides of the first experiment module (parts and modules in order) that uses it.</summary>
        public static IReadOnlyDictionary<string, DefinitionOverrides> FirstUses(IEnumerable<IReadOnlyList<IKsp1Node>> parts)
        {
            var first = new Dictionary<string, DefinitionOverrides>(StringComparer.Ordinal);
            foreach (var modules in parts ?? Enumerable.Empty<IReadOnlyList<IKsp1Node>>())
            foreach (var m in modules ?? (IReadOnlyList<IKsp1Node>)Array.Empty<IKsp1Node>())
            {
                if (!ScienceMapping.IsExperimentModule(m)) continue;
                var id = IsruCfg.Value(m, "experimentID");
                if (string.IsNullOrEmpty(id) || first.ContainsKey(id)) continue;
                first[id] = From(m);
            }
            return first;
        }
    }

    /// <summary>The plain model of a generated KSP2 definition (science plan (h)), or why the KSP1 definition is not generated.</summary>
    public sealed class ScienceDefinitionModel
    {
        public string Ksp1Id { get; internal set; }
        /// <summary><c>KSP1_&lt;id&gt;</c>.</summary>
        public string ExperimentId { get; internal set; }
        /// <summary>The resolved title (the id when there is none).</summary>
        public string Title { get; internal set; }
        public bool IsBuilt { get; internal set; }
        /// <summary>Why it is not built; null when it is.</summary>
        public string SkipReason { get; internal set; }

        /// <summary>The masks used: the definition's, or a DMagic module's override.</summary>
        public int SituationMask { get; internal set; }
        public int BiomeMask { get; internal set; }
        public double BaseValue { get; internal set; }
        public double DataScale { get; internal set; } = 1;
        public double ValueScale { get; internal set; } = 1;
        /// <summary>The <c>xmitDataScalar</c> used, clamped to 0..1.</summary>
        public double XmitDataScalar { get; internal set; } = 1;

        public ScienceExperimentKind Type { get; internal set; } = ScienceExperimentKind.Data;
        public double DataValue { get; internal set; }
        public double SampleValue { get; internal set; }
        /// <summary>kbit (S: KSP1's Mits taken as kbit).</summary>
        public double TransmissionSize { get; internal set; }
        public bool RequiresEva => false;
        public IReadOnlyList<ScienceLocation> Locations { get; internal set; } = Array.Empty<ScienceLocation>();

        public string NameTerm { get; internal set; }
        public string RequirementsTerm { get; internal set; }
        public string DataReportTerm { get; internal set; }
        public string SampleReportTerm { get; internal set; }
        /// <summary>Every term of the definition, once each, with its English text.</summary>
        public IReadOnlyList<ScienceTerm> Terms => _terms;
        public IReadOnlyList<ScienceFlavor> DataFlavor => _flavor;
        /// <summary>The sample flavor copies the data flavor (the same terms).</summary>
        public IReadOnlyList<ScienceFlavor> SampleFlavor => _flavor;
        /// <summary><c>RESULTS</c> entries kept, of <see cref="FlavorTotal"/>.</summary>
        public int FlavorKept { get; internal set; }
        public int FlavorTotal { get; internal set; }
        public IReadOnlyList<(string Key, string Reason)> FlavorSkipped { get; internal set; } = Array.Empty<(string, string)>();

        public IReadOnlyList<string> Notes => _notes;
        /// <summary>The KSP1 fields not reproduced (once each).</summary>
        public IReadOnlyList<string> NotReproduced => _notReproduced;

        internal readonly List<ScienceTerm> _terms = new List<ScienceTerm>();
        internal readonly List<ScienceFlavor> _flavor = new List<ScienceFlavor>();
        internal readonly List<string> _notes = new List<string>();
        internal readonly List<string> _notReproduced = new List<string>();

        /// <summary>The English text of a term of this definition; null when it is not one.</summary>
        public string English(string term) => _terms.FirstOrDefault(t => t.Term == term)?.English;

        internal string AddTerm(string suffix, string english)
        {
            var term = ScienceDefinitionBuilder.TermPrefix + Ksp1Id + "/" + suffix;
            _terms.Add(new ScienceTerm(term, english));
            return term;
        }

        internal void NotReproducedField(string field, string note)
        {
            IsruCfg.AddOnce(_notReproduced, field);
            IsruCfg.AddOnce(_notes, note);
        }
    }

    /// <summary>A KSP1 <c>EXPERIMENT_DEFINITION</c> into the plain model of a KSP2 definition (science plan (h)). Pure.
    /// <list type="bullet">
    /// <item>id <c>KSP1_&lt;id&gt;</c>; the name, requirements, report names and flavor texts are terms
    /// <c>KSP1PartMod/Science/&lt;id&gt;/…</c> whose English text is given (the title resolved by <c>titleResolver</c> when it is a
    /// <c>#</c> tag);</item>
    /// <item>with <c>x</c> = <c>xmitDataScalar</c> (clamped 0..1, 1 when absent) and <c>v = baseValue × science_value_scale</c>:
    /// <c>x ≥ 0.999</c> Data <c>v</c>; <c>0 &lt; x &lt; 0.999</c> Both, data <c>v × x</c> and sample <c>v × (1 − x)</c>; <c>x = 0</c>
    /// Sample <c>v</c>; <c>science_transmit_split</c> off: Data <c>v</c>;</item>
    /// <item>size <c>baseValue × dataScale</c> kbit; one location per mapped situation (<see cref="Ksp1Situations"/>);</item>
    /// <item>flavor from <c>RESULTS</c>, with a data <c>Default</c> always present (the KSP1 <c>default</c>, else the title);</item>
    /// <item>skipped (noted): no id, no <c>baseValue</c> above 0, no mapped situation.</item>
    /// </list></summary>
    public static class ScienceDefinitionBuilder
    {
        public const string IdPrefix = "KSP1_";
        public const string TermPrefix = "KSP1PartMod/Science/";
        /// <summary>From this transmit share up, the experiment is a data report only.</summary>
        public const double FullTransmit = 0.999;

        public const string SkipNoId = "no id";
        public const string SkipNoBaseValue = "no baseValue";
        public const string SkipNoSituation = "no mapped situation";

        private static readonly string[] QuietKeys = { "id", "title", "baseValue", "dataScale", "situationMask", "biomeMask" };

        public static ScienceDefinitionModel From(IKsp1Node def, DefinitionOverrides overrides, AnimationSettings settings,
            Func<string, string> titleResolver)
        {
            settings = settings ?? AnimationSettings.Defaults;
            overrides = overrides ?? new DefinitionOverrides(null, null, null);
            var d = new ScienceDefinitionModel();
            var id = IsruCfg.Value(def, "id");
            d.Ksp1Id = id ?? "";
            d.ExperimentId = IdPrefix + d.Ksp1Id;
            if (string.IsNullOrEmpty(id)) return Skip(d, SkipNoId, "EXPERIMENT_DEFINITION without an id, skipped");

            d.Title = Title(def, id, titleResolver, d._notes);
            ReadFields(def, d);

            var baseValue = IsruCfg.Number(def, "baseValue");
            if (!baseValue.HasValue || !(baseValue.Value > 0))
                return Skip(d, SkipNoBaseValue, $"{id}: baseValue '{IsruCfg.Value(def, "baseValue") ?? "(none)"}' is not a number above 0, definition skipped");
            d.BaseValue = baseValue.Value;
            var dataScale = IsruCfg.Number(def, "dataScale");
            if (dataScale.HasValue && dataScale.Value > 0) d.DataScale = dataScale.Value;
            else d._notes.Add($"{id}: dataScale '{IsruCfg.Value(def, "dataScale") ?? "(none)"}' is not a number above 0, 1 used");

            var sit = DefinitionOverrides.Mask(def, "situationMask") ?? 0;
            var bio = DefinitionOverrides.Mask(def, "biomeMask") ?? 0;
            if (overrides.SitMask.HasValue || overrides.BioMask.HasValue)
            {
                d._notes.Add($"{id}: the DMagic module's sitMask {overrides.SitMask?.ToString() ?? "-"} / bioMask {overrides.BioMask?.ToString() ?? "-"} " +
                             $"replace situationMask {sit} / biomeMask {bio}");
                sit = overrides.SitMask ?? sit;
                bio = overrides.BioMask ?? bio;
            }
            d.SituationMask = sit;
            d.BiomeMask = bio;
            var map = Ksp1Situations.Map(sit, bio);
            foreach (var n in map.Notes) d._notes.Add(id + ": " + n);
            d.Locations = map.Locations;
            if (map.Locations.Count == 0) return Skip(d, SkipNoSituation, $"{id}: situationMask {sit} maps to no KSP2 situation, definition skipped");

            // Type and values.
            double x;
            if (overrides.XmitDataScalar.HasValue) x = Math.Min(1, Math.Max(0, overrides.XmitDataScalar.Value));
            else
            {
                x = 1;
                d._notes.Add($"{id}: no xmitDataScalar on the module, 1 assumed (a data report)");
            }
            d.XmitDataScalar = x;
            d.ValueScale = settings.ScienceValueScale;
            var v = d.BaseValue * d.ValueScale;
            if (!settings.ScienceTransmitSplit || x >= FullTransmit)
            {
                d.Type = ScienceExperimentKind.Data;
                d.DataValue = v;
            }
            else if (x <= 0)
            {
                d.Type = ScienceExperimentKind.Sample;
                d.SampleValue = v;
            }
            else
            {
                d.Type = ScienceExperimentKind.Both;
                d.DataValue = v * x;
                d.SampleValue = v * (1 - x);
            }
            d.TransmissionSize = d.BaseValue * d.DataScale;

            // Terms.
            d.NameTerm = d.AddTerm("Name", d.Title);
            d.RequirementsTerm = d.AddTerm("Requirements", Ksp1Situations.Requirements(d.Locations));
            d.DataReportTerm = d.AddTerm("DataReport", d.Title);
            d.SampleReportTerm = d.AddTerm("SampleReport", d.Title + " (sample)");

            // Flavor.
            var results = def.GetNodes("RESULTS").FirstOrDefault();
            var set = Ksp1Situations.Results(results?.AllValues);
            d.FlavorTotal = set.Total;
            d.FlavorKept = set.Kept.Count;
            d.FlavorSkipped = set.Skipped;
            if (set.Kept.All(k => k.LocationId != Ksp1Situations.DefaultLocation))
            {
                d._notes.Add($"{id}: no default result, the title is the Default flavor text");
                d._flavor.Add(new ScienceFlavor(Ksp1Situations.DefaultLocation, d.AddTerm("Flavor/" + Ksp1Situations.DefaultLocation, d.Title)));
            }
            foreach (var (location, text) in set.Kept)
                d._flavor.Add(new ScienceFlavor(location, d.AddTerm("Flavor/" + location, text.Length == 0 ? d.Title : text)));
            var biomes = set.Skipped.Count(s => s.Reason == Ksp1Situations.SkipBiome);
            if (biomes > 0) d._notes.Add($"{id}: {biomes} biome result(s) skipped (KSP2's regions differ)");

            d.IsBuilt = true;
            return d;
        }

        private static string Title(IKsp1Node def, string id, Func<string, string> resolver, List<string> notes)
        {
            var title = IsruCfg.Value(def, "title");
            if (string.IsNullOrEmpty(title))
            {
                notes.Add($"{id}: no title, the id is the name");
                return id;
            }
            if (!title.StartsWith("#", StringComparison.Ordinal)) return title;
            string resolved = null;
            try { resolved = resolver?.Invoke(title); }
            catch (Exception) { /* a resolver failure keeps the tag */ }
            if (!string.IsNullOrEmpty(resolved)) return resolved;
            notes.Add($"{id}: title {title} not resolved, kept as is");
            return title;
        }

        private static void ReadFields(IKsp1Node def, ScienceDefinitionModel d)
        {
            foreach (var kv in def.AllValues)
            {
                var key = kv.Key?.Trim() ?? "";
                var value = IsruCfg.Text(kv.Value);
                switch (key)
                {
                    case "scienceCap":
                        d.NotReproducedField(key, $"scienceCap = {value} not reproduced (a KSP2 report scores once per experiment, location and type)");
                        break;
                    case "requireAtmosphere":
                    case "requireNoAtmosphere":
                        if (IsruCfg.Bool(def, key) == true)
                            d.NotReproducedField(key, $"{key} = {value} not reproduced (KSP2 locations have no atmosphere condition)");
                        break;
                    default:
                        if (!QuietKeys.Contains(key)) d.NotReproducedField(key, $"EXPERIMENT_DEFINITION: {key} not reproduced");
                        break;
                }
            }
            foreach (var child in def.AllNodes)
            {
                var kind = child.Name?.Trim() ?? "";
                if (kind != "RESULTS") d.NotReproducedField("node " + kind, $"EXPERIMENT_DEFINITION: node {kind} not reproduced");
            }
        }

        private static ScienceDefinitionModel Skip(ScienceDefinitionModel d, string reason, string note)
        {
            d.IsBuilt = false;
            d.SkipReason = reason;
            d._notes.Add(note);
            if (d.Title == null) d.Title = d.Ksp1Id;
            return d;
        }
    }
}
