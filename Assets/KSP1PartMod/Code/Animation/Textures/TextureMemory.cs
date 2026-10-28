using System;
using System.Globalization;

namespace KSP1PartMod.Animation.Textures
{
    /// <summary>Texture size estimates and the opt-in streaming budget raise (<c>raise_streaming_budget</c>).</summary>
    public static class TextureMemory
    {
        public const double BytesPerMb = 1024.0 * 1024.0;
        /// <summary>The VRAM kept free under the raised budget: a quarter of the card, at least 1 GB.</summary>
        public const int MinMarginMb = 1024;

        /// <summary>Bytes of a 2D texture with <paramref name="mipCount"/> levels (at least 1). A block-compressed format
        /// stores 4x4 blocks: a level is rounded up to whole blocks.</summary>
        public static long Estimate(int width, int height, int mipCount, int bitsPerPixel, bool blockCompressed)
        {
            if (width <= 0 || height <= 0 || bitsPerPixel <= 0) return 0;
            long total = 0;
            var levels = Math.Max(1, mipCount);
            for (var i = 0; i < levels; i++)
            {
                long w = Math.Max(1, width >> i), h = Math.Max(1, height >> i);
                if (blockCompressed)
                {
                    w = (w + 3) / 4 * 4;
                    h = (h + 3) / 4 * 4;
                }
                total += w * h * bitsPerPixel / 8;
            }
            return total;
        }

        public static int MarginMb(int graphicsMemoryMb) => Math.Max(MinMarginMb, graphicsMemoryMb / 4);

        /// <summary>The budget with KSP1's non-streaming bytes added back: base + KSP1, capped at the card's memory less
        /// <see cref="MarginMb"/>, never below base. An unknown card size (0 or less) gives base.</summary>
        public static float RaisedBudgetMb(float baseMb, long ksp1Bytes, int graphicsMemoryMb, out bool capped)
        {
            capped = false;
            if (graphicsMemoryMb <= 0 || ksp1Bytes <= 0 || baseMb < 0 || float.IsNaN(baseMb)) return baseMb;
            var cap = (double)graphicsMemoryMb - MarginMb(graphicsMemoryMb);
            var target = baseMb + ksp1Bytes / BytesPerMb;
            if (target > cap)
            {
                capped = true;
                target = cap;
            }
            return (float)Math.Max(baseMb, target);
        }

        /// <summary>The budget to raise from: the current value, unless it is still the one we set last (then the base we
        /// raised from). Someone else changing the budget makes their value the new base.</summary>
        public static float BaseBudgetMb(float currentMb, float? lastSetMb, float knownBaseMb) =>
            lastSetMb.HasValue && Math.Abs(currentMb - lastSetMb.Value) < 0.5f ? knownBaseMb : currentMb;

        public static string Mb(double bytes) => (bytes / BytesPerMb).ToString("0", CultureInfo.InvariantCulture);
    }
}
