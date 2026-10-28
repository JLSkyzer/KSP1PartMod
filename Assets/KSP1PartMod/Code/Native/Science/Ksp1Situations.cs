using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Science
{
    /// <summary>KSP2's science situations, in the order and with the values of the game's own enum (26w42c).</summary>
    public enum Ksp2Situation : byte { None, HighOrbit, LowOrbit, Atmosphere, Splashed, Landed }

    /// <summary>One valid location of a KSP2 experiment: a situation on every body, and whether the vessel's region counts.</summary>
    public struct ScienceLocation
    {
        public readonly Ksp2Situation Situation;
        public readonly bool RequiresRegion;

        public ScienceLocation(Ksp2Situation situation, bool requiresRegion)
        {
            Situation = situation;
            RequiresRegion = requiresRegion;
        }

        /// <summary><c>Landed+R</c>, <c>HighOrbit</c>.</summary>
        public string Label => Situation + (RequiresRegion ? "+R" : "");
    }

    /// <summary>The KSP2 situations of a KSP1 mask, in KSP1's bit order, and what the mapping had to note.</summary>
    public sealed class SituationMap
    {
        public IReadOnlyList<ScienceLocation> Locations { get; }
        public IReadOnlyList<string> Notes { get; }

        public SituationMap(IReadOnlyList<ScienceLocation> locations, IReadOnlyList<string> notes)
        {
            Locations = locations;
            Notes = notes;
        }

        public bool Has(Ksp2Situation s) => Locations.Any(l => l.Situation == s);
        public bool RequiresRegion(Ksp2Situation s) => Locations.Any(l => l.Situation == s && l.RequiresRegion);
    }

    /// <summary>A <c>RESULTS</c> key read as a KSP2 location id, or the reason it is skipped.</summary>
    public struct FlavorKey
    {
        public readonly string LocationId;
        public readonly string SkipReason;

        public FlavorKey(string locationId, string skipReason)
        {
            LocationId = locationId;
            SkipReason = skipReason;
        }
    }

    /// <summary>The <c>RESULTS</c> of a definition: the entries kept (location id and text, the first one of a location wins) and
    /// the ones skipped.</summary>
    public sealed class FlavorSet
    {
        public IReadOnlyList<(string LocationId, string Text)> Kept { get; }
        public IReadOnlyList<(string Key, string Reason)> Skipped { get; }
        public int Total => Kept.Count + Skipped.Count;

        public FlavorSet(IReadOnlyList<(string, string)> kept, IReadOnlyList<(string, string)> skipped)
        {
            Kept = kept;
            Skipped = skipped;
        }
    }

    /// <summary>KSP1's experiment situations into KSP2's (science plan (e)). Pure.
    /// <list type="bullet">
    /// <item>KSP1 bits (S: KSP1's enum, consistent with the stock definitions): <c>SrfLanded</c> 1, <c>SrfSplashed</c> 2,
    /// <c>FlyingLow</c> 4, <c>FlyingHigh</c> 8, <c>InSpaceLow</c> 16, <c>InSpaceHigh</c> 32;</item>
    /// <item>1 → Landed, 2 → Splashed, 4 or 8 → Atmosphere, 16 → LowOrbit, 32 → HighOrbit;</item>
    /// <item>a situation requires a region when its bit is in the biome mask; Atmosphere does when 4 or 8 is. A split (one
    /// atmosphere bit with a biome, the other without) becomes "region required", noted;</item>
    /// <item>no KSP1 biome name is ever read: KSP2 uses the vessel's own region.</item>
    /// </list></summary>
    public static class Ksp1Situations
    {
        public const int SrfLanded = 1, SrfSplashed = 2, FlyingLow = 4, FlyingHigh = 8, InSpaceLow = 16, InSpaceHigh = 32, All = 63;

        public const string DefaultLocation = "Default";
        public const string SkipBiome = "biome";
        public const string SkipUnknown = "unknown situation";
        public const string SkipDuplicate = "duplicate location";

        /// <summary>The KSP1 situation names of the <c>RESULTS</c> keys, with their KSP2 situation.</summary>
        private static readonly (string Token, Ksp2Situation Situation)[] Tokens =
        {
            ("SrfLanded", Ksp2Situation.Landed), ("SrfSplashed", Ksp2Situation.Splashed), ("FlyingLow", Ksp2Situation.Atmosphere),
            ("FlyingHigh", Ksp2Situation.Atmosphere), ("InSpaceLow", Ksp2Situation.LowOrbit), ("InSpaceHigh", Ksp2Situation.HighOrbit),
        };

        public static SituationMap Map(int situationMask, int biomeMask)
        {
            var notes = new List<string>();
            if ((situationMask & ~All) != 0) notes.Add($"situationMask {situationMask}: bits above {InSpaceHigh} ignored");
            var sit = situationMask & All;
            var bio = biomeMask & sit;
            var list = new List<ScienceLocation>();
            if ((sit & SrfLanded) != 0) list.Add(new ScienceLocation(Ksp2Situation.Landed, (bio & SrfLanded) != 0));
            if ((sit & SrfSplashed) != 0) list.Add(new ScienceLocation(Ksp2Situation.Splashed, (bio & SrfSplashed) != 0));
            if ((sit & (FlyingLow | FlyingHigh)) != 0)
            {
                list.Add(new ScienceLocation(Ksp2Situation.Atmosphere, (bio & (FlyingLow | FlyingHigh)) != 0));
                if ((sit & (FlyingLow | FlyingHigh)) == (FlyingLow | FlyingHigh) && (bio & (FlyingLow | FlyingHigh)) != 0
                    && (bio & (FlyingLow | FlyingHigh)) != (FlyingLow | FlyingHigh))
                    notes.Add("situationMask: FlyingLow and FlyingHigh are one KSP2 situation (Atmosphere), region required " +
                              $"(biomeMask has {((bio & FlyingLow) != 0 ? "FlyingLow" : "FlyingHigh")} only)");
            }
            if ((sit & InSpaceLow) != 0) list.Add(new ScienceLocation(Ksp2Situation.LowOrbit, (bio & InSpaceLow) != 0));
            if ((sit & InSpaceHigh) != 0) list.Add(new ScienceLocation(Ksp2Situation.HighOrbit, (bio & InSpaceHigh) != 0));
            return new SituationMap(list, notes);
        }

        /// <summary>The KSP2 situations of a KSP1 mask (no regions), for the Jaccard rule.</summary>
        public static IReadOnlyCollection<Ksp2Situation> SituationsOf(int situationMask) =>
            Map(situationMask, 0).Locations.Select(l => l.Situation).ToList();

        /// <summary>English, for the requirements term: <c>Landed (region)</c>, <c>Low orbit</c>.</summary>
        public static string English(ScienceLocation l)
        {
            string name;
            switch (l.Situation)
            {
                case Ksp2Situation.HighOrbit: name = "High orbit"; break;
                case Ksp2Situation.LowOrbit: name = "Low orbit"; break;
                default: name = l.Situation.ToString(); break;
            }
            return l.RequiresRegion ? name + " (region)" : name;
        }

        public static string Requirements(IEnumerable<ScienceLocation> locations) =>
            string.Join(", ", (locations ?? Enumerable.Empty<ScienceLocation>()).Select(English));

        /// <summary><c>Landed+R, Splashed+R, Atmosphere, LowOrbit, HighOrbit</c>.</summary>
        public static string Labels(IEnumerable<ScienceLocation> locations) =>
            string.Join(", ", (locations ?? Enumerable.Empty<ScienceLocation>()).Select(l => l.Label));

        /// <summary><c>default</c> → <c>Default</c>; <c>&lt;Body&gt;&lt;Situation&gt;</c> → <c>&lt;Body&gt;_&lt;KSP2 situation&gt;</c>, with
        /// <c>Sun</c> → <c>Kerbol</c>; a biome suffix is skipped (KSP2's regions differ), as is a key without a body or a
        /// situation.</summary>
        public static FlavorKey ResultKey(string key)
        {
            key = key?.Trim() ?? "";
            if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase)) return new FlavorKey(DefaultLocation, null);
            var at = -1;
            var token = "";
            var situation = Ksp2Situation.None;
            foreach (var (t, s) in Tokens)
            {
                var i = key.IndexOf(t, StringComparison.Ordinal);
                if (i > 0 && (at < 0 || i < at))
                {
                    at = i;
                    token = t;
                    situation = s;
                }
            }
            if (at <= 0) return new FlavorKey(null, SkipUnknown);
            if (at + token.Length < key.Length) return new FlavorKey(null, SkipBiome);
            var body = key.Substring(0, at);
            if (body == "Sun") body = "Kerbol";
            return new FlavorKey(body + "_" + situation, null);
        }

        /// <summary>Every <c>key = text</c> of a <c>RESULTS</c> node, in order: the first entry of a location wins (KSP1 picks
        /// one at random among several; <c>FlyingLow</c> and <c>FlyingHigh</c> share a location).</summary>
        public static FlavorSet Results(IEnumerable<KeyValuePair<string, string>> values)
        {
            var kept = new List<(string, string)>();
            var skipped = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in values ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                var k = ResultKey(kv.Key);
                if (k.LocationId == null) skipped.Add((kv.Key, k.SkipReason));
                else if (!seen.Add(k.LocationId)) skipped.Add((kv.Key, SkipDuplicate));
                else kept.Add((k.LocationId, kv.Value?.Trim() ?? ""));
            }
            return new FlavorSet(kept, skipped);
        }
    }
}
