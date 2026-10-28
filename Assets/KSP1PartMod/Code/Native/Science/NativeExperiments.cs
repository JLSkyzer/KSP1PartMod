using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Science
{
    /// <summary>KSP2's <c>ScienceExperimentType</c>, same values: a data report (transmittable), a sample (recovered), or both.</summary>
    public enum ScienceExperimentKind : byte { Data = 1, Sample = 2, Both = 3 }

    /// <summary>One KSP2 experiment definition, as the mapping needs it.</summary>
    public sealed class NativeExperiment
    {
        public string Id { get; }
        public IReadOnlyList<ScienceLocation> Locations { get; }
        public ScienceExperimentKind Type { get; }
        public bool RequiresEva { get; }

        public NativeExperiment(string id, IEnumerable<ScienceLocation> locations, ScienceExperimentKind type, bool requiresEva)
        {
            Id = id;
            Locations = (locations ?? Enumerable.Empty<ScienceLocation>()).ToList();
            Type = type;
            RequiresEva = requiresEva;
        }

        public bool Has(Ksp2Situation s) => Locations.Any(l => l.Situation == s);
    }

    /// <summary>The KSP2 experiments a KSP1 experiment can point to (science plan (b) and (h)). <see cref="Redux26w42c"/> is the
    /// list read in the 26w42c game data (stock and Redux bundles); Task 3 replaces it with the live store's list. Pure.</summary>
    public sealed class NativeExperiments
    {
        private static ScienceLocation L(Ksp2Situation s, bool r = false) => new ScienceLocation(s, r);

        private static readonly ScienceLocation[] Everywhere =
        {
            L(Ksp2Situation.HighOrbit), L(Ksp2Situation.LowOrbit), L(Ksp2Situation.Atmosphere), L(Ksp2Situation.Landed, true),
            L(Ksp2Situation.Splashed, true),
        };

        /// <summary>The 26w42c experiments, in the table order of plan (b) (the order breaks the last ties).</summary>
        public static readonly NativeExperiments Redux26w42c = new NativeExperiments(new[]
        {
            new NativeExperiment("CrewObservation", Everywhere, ScienceExperimentKind.Data, false),
            new NativeExperiment("EnvironmentSurvey", Everywhere, ScienceExperimentKind.Both, false),
            new NativeExperiment("RadiationReport", Everywhere, ScienceExperimentKind.Data, false),
            new NativeExperiment("AtmosphereSurvey", new[] { L(Ksp2Situation.Atmosphere, true) }, ScienceExperimentKind.Both, false),
            new NativeExperiment("OrbitalSurvey", new[] { L(Ksp2Situation.HighOrbit), L(Ksp2Situation.LowOrbit, true) }, ScienceExperimentKind.Both, false),
            new NativeExperiment("MarineSurvey", new[] { L(Ksp2Situation.Landed, true), L(Ksp2Situation.Splashed, true) }, ScienceExperimentKind.Both, false),
            new NativeExperiment("SurfaceSurvey", new[] { L(Ksp2Situation.Landed, true), L(Ksp2Situation.Splashed, true) }, ScienceExperimentKind.Both, true),
            new NativeExperiment("CrewReport", new[] { L(Ksp2Situation.Landed, true) }, ScienceExperimentKind.Data, false),
            new NativeExperiment("ResourceScan", new[] { L(Ksp2Situation.HighOrbit), L(Ksp2Situation.LowOrbit) }, ScienceExperimentKind.Data, false),
        });

        /// <summary>Never a closest match: <c>CrewReport</c> is a leftover stock definition whose name is plain text (it shows empty).</summary>
        public static readonly IReadOnlyCollection<string> ClosestExcluded = Array.AsReadOnly(new[] { "CrewReport" });

        private readonly Dictionary<string, NativeExperiment> _byId = new Dictionary<string, NativeExperiment>(StringComparer.Ordinal);

        public IReadOnlyList<NativeExperiment> All { get; }

        /// <param name="experiments">In table order; a repeated id keeps its first entry.</param>
        public NativeExperiments(IEnumerable<NativeExperiment> experiments)
        {
            var list = new List<NativeExperiment>();
            foreach (var e in experiments ?? Enumerable.Empty<NativeExperiment>())
            {
                if (e?.Id == null || _byId.ContainsKey(e.Id)) continue;
                _byId[e.Id] = e;
                list.Add(e);
            }
            All = list;
        }

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);

        public NativeExperiment Find(string id) => id != null && _byId.TryGetValue(id, out var e) ? e : null;

        /// <summary>Jaccard index between the situations of a KSP1 mask and those of a KSP2 experiment (regions ignored).</summary>
        public static double Jaccard(int ksp1SituationMask, NativeExperiment native)
        {
            if (native == null) return 0;
            var a = new HashSet<Ksp2Situation>(Ksp1Situations.SituationsOf(ksp1SituationMask));
            var b = new HashSet<Ksp2Situation>(native.Locations.Select(l => l.Situation));
            var union = a.Union(b).Count();
            return union == 0 ? 0 : (double)a.Intersect(b).Count() / union;
        }

        /// <summary>How well a native type serves a KSP1 kind: 2 the same type, 1 a shared report type (one of them is Both), 0 none.</summary>
        private static int KindTier(ScienceExperimentKind native, ScienceExperimentKind wanted) =>
            native == wanted ? 2 : ((byte)native & (byte)wanted) != 0 ? 1 : 0;

        /// <summary>Rule 4 of plan (h): the experiment, not EVA-only and not in <see cref="ClosestExcluded"/>, whose situations best match the mask (Jaccard above 0); ties
        /// go to the type that serves <paramref name="kind"/> best, then to the table order. Null when nothing matches.</summary>
        public NativeExperiment Closest(int ksp1SituationMask, ScienceExperimentKind kind)
        {
            NativeExperiment best = null;
            double bestScore = 0;
            var bestTier = -1;
            foreach (var e in All)
            {
                if (e.RequiresEva || ClosestExcluded.Contains(e.Id)) continue;
                var score = Jaccard(ksp1SituationMask, e);
                if (score <= 0) continue;
                var tier = KindTier(e.Type, kind);
                if (best == null || score > bestScore + 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && tier > bestTier))
                {
                    best = e;
                    bestScore = score;
                    bestTier = tier;
                }
            }
            return best;
        }
    }

    /// <summary>The default tables of the science keys (science plan, Global Constraints and (h)). Read-only; each settings object
    /// takes its own copy.</summary>
    public static class ScienceDefaults
    {
        public static readonly IReadOnlyDictionary<string, string> NativeIds =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["crewReport"] = "CrewObservation",
            });

        /// <summary>The KSP2 experiment of a stock KSP1 instrument when no definition is on disk (plan (h), rule 4).</summary>
        public static readonly IReadOnlyDictionary<string, string> FallbackIds =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mysteryGoo"] = "EnvironmentSurvey",
                ["mobileMaterialsLab"] = "EnvironmentSurvey",
                ["temperatureScan"] = "EnvironmentSurvey",
                ["barometerScan"] = "AtmosphereSurvey",
                ["atmosphereAnalysis"] = "AtmosphereSurvey",
                ["seismicScan"] = "MarineSurvey",
                ["gravityScan"] = "OrbitalSurvey",
                ["infraredTelescope"] = "OrbitalSurvey",
                ["magnetometer"] = "RadiationReport",
            });

        /// <summary>The kerbal experiments (KSP2 kerbals run their own) and the ROC arm scanners.</summary>
        public static readonly IReadOnlyList<string> SkipIds = Array.AsReadOnly(new[] { "evaReport", "evaScience", "surfaceSample", "ROCScience" });

        /// <summary>The skip ids that are KSP1 kerbal experiments (their skip reason).</summary>
        public static readonly IReadOnlyList<string> EvaKerbalIds = Array.AsReadOnly(new[] { "evaReport", "evaScience", "surfaceSample" });
    }
}
