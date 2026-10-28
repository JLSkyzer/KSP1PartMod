using System;
using System.Linq;
using KSP1PartMod.Animation.Textures;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Textures
{
    /// <summary>Examples from the VRAM note (bits: DXT1 4, DXT5 8, RGBA32 32).</summary>
    public class TextureCensusTests
    {
        private const long Mb = 1024 * 1024;

        [Test] public void Dxt5_4096_WithMips_About21_3Mb_InTheTopClass()
        {
            var c = new TextureCensus("VAB entry", 10);
            c.Add("BDB/Saturn_S1C", 4096, 4096, "DXT5", 8, true, 13, false);
            Assert.AreEqual(1, c.All.Count);
            Assert.AreEqual(21.3, c.All.Bytes / (double)Mb, 0.05);
            Assert.AreEqual(1, c.BySize[4].Count);
            Assert.AreEqual(0, c.WithoutMipmaps.Count + c.Uncompressed.Count + c.NotPowerOfTwo.Count + c.Readable.Count);
        }

        [Test] public void Rgba32_Npot_Readable_NoMips_CountsInFourClasses()
        {
            var c = new TextureCensus("flight entry", 10);
            c.Add("decal", 2000, 1000, "RGBA32", 32, false, 1, true);
            var bytes = 2000L * 1000 * 4;
            Assert.AreEqual(bytes, c.All.Bytes);
            Assert.AreEqual(bytes, c.WithoutMipmaps.Bytes);
            Assert.AreEqual(bytes, c.Uncompressed.Bytes);
            Assert.AreEqual(bytes, c.NotPowerOfTwo.Bytes);
            Assert.AreEqual(bytes, c.Readable.Bytes, "its RAM copy: the same bytes again");
            Assert.AreEqual(1, c.BySize[3].Count, "longest side 2000: the 2048 class");
        }

        [Test] public void PowerOfTwo_IsPerSide_NotSquare()
        {
            var c = new TextureCensus("x", 10);
            c.Add("wide", 2048, 1024, "RGBA32", 32, false, 12, false);
            Assert.AreEqual(0, c.NotPowerOfTwo.Count);
            Assert.AreEqual(1, c.Uncompressed.Count);
        }

        [Test] public void TinyTexture_IsNotWithoutMipmaps()
        {
            var c = new TextureCensus("x", 10);
            c.Add("tiny", 4, 4, "DXT1", 4, true, 1, false);
            Assert.AreEqual(0, c.WithoutMipmaps.Count);
            c.Add("small", 8, 8, "DXT1", 4, true, 1, false);
            Assert.AreEqual(1, c.WithoutMipmaps.Count);
            Assert.AreEqual(32, c.WithoutMipmaps.Bytes, "8x8 DXT1: four 8-byte blocks");
        }

        [Test] public void SizeClasses_ByTheLongestSide()
        {
            Assert.AreEqual(0, TextureCensus.SizeClass(256));
            Assert.AreEqual(1, TextureCensus.SizeClass(257));
            Assert.AreEqual(1, TextureCensus.SizeClass(512));
            Assert.AreEqual(2, TextureCensus.SizeClass(1024));
            Assert.AreEqual(3, TextureCensus.SizeClass(2048));
            Assert.AreEqual(4, TextureCensus.SizeClass(2049));
            Assert.AreEqual(4, TextureCensus.SizeClass(8192));
            Assert.AreEqual(5, TextureCensus.SizeLabels.Length);
        }

        [Test] public void ByFormat_LargestFirst()
        {
            var c = new TextureCensus("x", 10);
            c.Add("a", 256, 256, "DXT1", 4, true, 1, false);
            c.Add("b", 1024, 1024, "DXT5", 8, true, 1, false);
            c.Add("c", 256, 256, "DXT1", 4, true, 1, false);
            var formats = c.ByFormat.ToList();
            CollectionAssert.AreEqual(new[] { "DXT5", "DXT1" }, formats.Select(f => f.Key));
            Assert.AreEqual(2, formats[1].Value.Count);
            Assert.AreEqual(2 * 256 * 256 / 2, formats[1].Value.Bytes);
        }

        [Test] public void Largest_TopN_TiesFirstSeen()
        {
            var c = new TextureCensus("x", 2);
            c.Add("small", 256, 256, "DXT5", 8, true, 1, false);
            c.Add("big1", 1024, 1024, "DXT5", 8, true, 1, false);
            c.Add("big2", 1024, 1024, "DXT5", 8, true, 1, false);
            c.Add("mid", 512, 512, "DXT5", 8, true, 1, false);
            CollectionAssert.AreEqual(new[] { "big1", "big2" }, c.Largest.Select(t => t.Name));
            Assert.AreEqual(4, c.All.Count);
            var top = c.Largest.First();
            Assert.AreEqual(1024, top.Width);
            Assert.AreEqual("DXT5", top.Format);
            Assert.AreEqual(Mb, top.Bytes);
        }

        [Test] public void BadArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextureCensus("x", 0));
            var c = new TextureCensus(null, 1);
            Assert.AreEqual("?", c.When);
            c.Add(null, 16, 16, null, 32, false, 1, false);
            Assert.AreEqual("?", c.Largest.Single().Name);
            Assert.AreEqual("?", c.ByFormat.Single().Key);
        }
    }
}
