using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Native.Sizes
{
    /// <summary>Where a part's size label came from.</summary>
    public enum SizeSource
    {
        /// <summary>Redux's label kept.</summary>
        Redux,
        /// <summary>The largest stack node's size (measured by Redux from the model, else the node's legacy size).</summary>
        Nodes,
        /// <summary>A <c>bulkheadProfiles</c> token Redux does not know (<c>sizeNpM</c>, <c>size000</c>).</summary>
        Token,
    }

    public struct SizeDecision
    {
        public string Key;
        public SizeSource Source;
        /// <summary>Registry steps between Redux's label and ours (0: unchanged).</summary>
        public int Steps;
    }

    /// <summary>Size labels of KSP1 parts (0.16.0, plan (e)). The table is KSP2's <c>PartSizeRegistry</c> (public keys and
    /// diameters, in its order) with each key's <c>MetaAssemblySizeFilterType</c> name; a test checks it against the game.
    /// Pure.</summary>
    public static class PartSizeRule
    {
        public struct Entry
        {
            public string Key, Category;
            public float Diameter;
            public Entry(string key, float diameter, string category)
            {
                Key = key;
                Diameter = diameter;
                Category = category;
            }
        }

        /// <summary>KSP2's registry, in its order (26w42c).</summary>
        public static readonly IReadOnlyList<Entry> Registry = new[]
        {
            new Entry("XXS", 0.3125f, "XXS"), new Entry("XS", 0.625f, "XS"), new Entry("XS+", 0.9375f, "XSPLUS"),
            new Entry("SM", 1.25f, "S"), new Entry("SM+", 1.875f, "SPLUS"), new Entry("MD", 2.5f, "M"),
            new Entry("MD+", 3.125f, "MPLUS"), new Entry("LG", 3.75f, "L"), new Entry("LG+", 4.375f, "LPLUS"),
            new Entry("XL", 5f, "XL"), new Entry("XL+", 7.5f, "XLPLUS"), new Entry("2XL", 10f, "XXL"),
            new Entry("3XL", 20f, "XXXL"), new Entry("4XL", 40f, "XXXXL"), new Entry("5XL", 60f, "XXXXXL"),
            new Entry("6XL", 80f, "XXXXXXL"),
        };

        /// <summary>KSP1's whole sizes (<c>size0</c> 0.625 m … <c>size5</c> 7.5 m), then the sizes Redux maps beyond
        /// (<c>size6</c> … <c>size10</c>: 2XL … 6XL).</summary>
        private static readonly float[] Whole = { 0.625f, 1.25f, 2.5f, 3.75f, 5f, 7.5f, 10f, 20f, 40f, 60f, 80f };

        public static int IndexOf(string key)
        {
            for (var i = 0; i < Registry.Count; i++)
                if (string.Equals(Registry[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The diameter (m) of one <c>bulkheadProfiles</c> token, KSP1's convention: <c>size00</c> 0.3125,
        /// <c>size000</c> 0.15625, <c>sizeN</c>, and <c>sizeNpM</c> M tenths of the way from N to N+1. Null for any other
        /// token (<c>srf</c>, <c>mk2</c>…).</summary>
        public static float? TokenDiameter(string token)
        {
            var t = token?.Trim().ToLowerInvariant();
            if (t == null || !t.StartsWith("size", StringComparison.Ordinal) || t.Length == 4) return null;
            var rest = t.Substring(4);
            if (rest == "00") return 0.3125f;
            if (rest == "000") return 0.15625f;
            var p = rest.IndexOf('p');
            var whole = p < 0 ? rest : rest.Substring(0, p);
            if (whole.Length == 0 || whole.Any(c => c < '0' || c > '9') || (whole.Length > 1 && whole[0] == '0')) return null;
            if (!int.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n >= Whole.Length) return null;
            if (p < 0) return Whole[n];
            var frac = rest.Substring(p + 1);
            if (frac.Length == 0 || frac.Any(c => c < '0' || c > '9') || n + 1 >= Whole.Length) return null;
            var f = float.Parse("0." + frac, CultureInfo.InvariantCulture);
            return Whole[n] + f * (Whole[n + 1] - Whole[n]);
        }

        /// <summary>The largest diameter among the tokens of a <c>bulkheadProfiles</c> value (split as Redux does), or null.</summary>
        public static float? FromTokens(string bulkheadProfiles)
        {
            if (string.IsNullOrWhiteSpace(bulkheadProfiles)) return null;
            float? best = null;
            foreach (var token in bulkheadProfiles.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = TokenDiameter(token);
                if (d.HasValue && (!best.HasValue || d.Value > best.Value)) best = d;
            }
            return best;
        }

        /// <summary>The registry key closest to <paramref name="diameter"/> in absolute value; on a tie the first (Redux's
        /// <c>GetClosestPartSize</c>).</summary>
        public static string Closest(float diameter)
        {
            var best = "SM";
            var bestDiff = float.PositiveInfinity;
            foreach (var e in Registry)
            {
                var diff = Math.Abs(e.Diameter - diameter);
                if (diff >= bestDiff) continue;
                best = e.Key;
                bestDiff = diff;
            }
            return best;
        }

        /// <summary>The largest key among the stack nodes (by registry order); null when the part has no stack node with a
        /// known key.</summary>
        public static string FromStackNodes(IEnumerable<(bool stack, string sizeKey)> nodes)
        {
            string best = null;
            var bestIndex = -1;
            if (nodes == null) return null;
            foreach (var (stack, key) in nodes)
            {
                if (!stack) continue;
                var i = IndexOf(key);
                if (i <= bestIndex) continue;
                bestIndex = i;
                best = Registry[i].Key;
            }
            return best;
        }

        /// <summary>A stack part takes its largest stack node's key; a part without one keeps Redux's label, except when
        /// Redux fell back (no token it knows) and a token parses.</summary>
        public static SizeDecision Decide(string reduxKey, bool reduxWasAuto, string bulkheadProfiles, IEnumerable<(bool stack, string sizeKey)> stackKeys)
        {
            var d = new SizeDecision { Key = reduxKey, Source = SizeSource.Redux };
            var fromNodes = FromStackNodes(stackKeys);
            if (fromNodes != null)
            {
                d.Key = fromNodes;
                d.Source = SizeSource.Nodes;
            }
            else if (reduxWasAuto)
            {
                var token = FromTokens(bulkheadProfiles);
                if (token.HasValue)
                {
                    d.Key = Closest(token.Value);
                    d.Source = SizeSource.Token;
                }
            }
            var a = IndexOf(reduxKey);
            var b = IndexOf(d.Key);
            d.Steps = a < 0 || b < 0 ? 0 : Math.Abs(a - b);
            return d;
        }

        /// <summary>The <c>MetaAssemblySizeFilterType</c> name of a key; null when the key is unknown.</summary>
        public static string CategoryOf(string key)
        {
            var i = IndexOf(key);
            return i < 0 ? null : Registry[i].Category;
        }
    }
}
