using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Animation.Textures
{
    /// <summary>A count and its estimated bytes.</summary>
    public struct CensusBin
    {
        public int Count;
        public long Bytes;

        public void Add(long bytes)
        {
            Count++;
            Bytes += bytes;
        }
    }

    /// <summary>One texture of the largest list.</summary>
    public struct CensusTexture
    {
        public string Name;
        public int Width, Height;
        public string Format;
        public long Bytes;
    }

    /// <summary>A census of the live KSP1 textures at one memory moment (performance plan Task 2): counts and estimated bytes
    /// (<see cref="TextureMemory.Estimate"/>, full mip chain) by format and by size class, and the textures without mipmaps,
    /// uncompressed, not a power of two, or readable, plus the N largest. It only reads what it is given. Pure.</summary>
    public sealed class TextureCensus
    {
        /// <summary>The size classes by the longest side: up to 256, 512, 1024, 2048, then above 2048 (printed ≥4096).</summary>
        public static readonly string[] SizeLabels = { "≤256", "512", "1024", "2048", "≥4096" };

        private readonly Dictionary<string, CensusBin> _byFormat = new Dictionary<string, CensusBin>(StringComparer.Ordinal);
        private readonly List<string> _formatOrder = new List<string>();
        private readonly CensusBin[] _bySize = new CensusBin[5];
        private readonly CensusTexture[] _largest;
        private CensusBin _all, _withoutMips, _uncompressed, _npot, _readable;

        public TextureCensus(string when, int topN)
        {
            if (topN < 1) throw new ArgumentOutOfRangeException(nameof(topN));
            When = when ?? "?";
            _largest = new CensusTexture[topN];
        }

        public string When { get; }
        /// <summary>The time the census took (set by the caller).</summary>
        public double CensusMs { get; set; }

        public CensusBin All => _all;
        public CensusBin WithoutMipmaps => _withoutMips;
        public CensusBin Uncompressed => _uncompressed;
        public CensusBin NotPowerOfTwo => _npot;
        /// <summary>Readable textures keep a RAM copy of the same bytes.</summary>
        public CensusBin Readable => _readable;
        public IReadOnlyList<CensusBin> BySize => _bySize;
        public int LargestCount { get; private set; }

        /// <summary>The formats, largest bytes first (ties in first-seen order).</summary>
        public IEnumerable<KeyValuePair<string, CensusBin>> ByFormat =>
            _formatOrder.Select((f, i) => (f, i)).OrderByDescending(x => _byFormat[x.f].Bytes).ThenBy(x => x.i)
                .Select(x => new KeyValuePair<string, CensusBin>(x.f, _byFormat[x.f]));

        public IEnumerable<CensusTexture> Largest => _largest.Take(LargestCount);

        public void Add(string name, int width, int height, string formatName, int bits, bool block, int mipCount, bool readable)
        {
            var bytes = TextureMemory.Estimate(width, height, mipCount, bits, block);
            var format = string.IsNullOrEmpty(formatName) ? "?" : formatName;
            var longest = Math.Max(width, height);

            _all.Add(bytes);
            if (!_byFormat.TryGetValue(format, out var bin)) _formatOrder.Add(format);
            bin.Add(bytes);
            _byFormat[format] = bin;
            _bySize[SizeClass(longest)].Add(bytes);
            if (mipCount <= 1 && longest > 4) _withoutMips.Add(bytes);
            if (!block) _uncompressed.Add(bytes);
            if (!IsPowerOfTwo(width) || !IsPowerOfTwo(height)) _npot.Add(bytes);
            if (readable) _readable.Add(bytes);
            Offer(new CensusTexture { Name = name ?? "?", Width = width, Height = height, Format = format, Bytes = bytes });
        }

        public static int SizeClass(int longestSide) =>
            longestSide <= 256 ? 0 : longestSide <= 512 ? 1 : longestSide <= 1024 ? 2 : longestSide <= 2048 ? 3 : 4;

        public static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

        /// <summary>Largest first; an equal size keeps the texture seen first.</summary>
        private void Offer(CensusTexture t)
        {
            if (LargestCount == _largest.Length && t.Bytes <= _largest[LargestCount - 1].Bytes) return;
            var at = 0;
            while (at < LargestCount && _largest[at].Bytes >= t.Bytes) at++;
            var last = LargestCount < _largest.Length ? LargestCount : _largest.Length - 1;
            for (var i = last; i > at; i--) _largest[i] = _largest[i - 1];
            _largest[at] = t;
            if (LargestCount < _largest.Length) LargestCount++;
        }
    }
}
