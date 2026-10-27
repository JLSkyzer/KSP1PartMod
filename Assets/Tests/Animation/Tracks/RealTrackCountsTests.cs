using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Tracks
{
    public class RealTrackCountsTests
    {
        private static bool IsKreOrBdb(string path)
        {
            var top = InstalledKsp1.TopFolder(path);
            return top.Equals("KerbalReusabilityExpansion", StringComparison.OrdinalIgnoreCase)
                   || top.Equals("Bluedog_DB", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<(MuAnimClip clip, string host)> Clips(Func<string, bool> pick)
        {
            foreach (var path in InstalledKsp1.AllMu().Where(pick))
            {
                MuFile file;
                try { file = MuReader.Read(path); } catch (MuFormatException) { continue; }
                var stack = new Stack<MuNode>();
                stack.Push(file.Root);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (n.Animation != null)
                        foreach (var c in n.Animation.Clips) yield return (c, n.Path());
                    foreach (var ch in n.Children) stack.Push(ch);
                }
            }
        }

        private static int CountClipsWithTracks(Func<string, bool> pick) =>
            Clips(pick).Count(c => TrackBuilder.FromClip(c.clip, c.host, 0, out _) != null);

        [Test] public void Tracks_StockClipsWithNonTransformCurves_Are83() => Assert.AreEqual(83, CountClipsWithTracks(InstalledKsp1.IsStock));
        [Test] public void Tracks_KreBdbClipsWithNonTransformCurves_Are49() => Assert.AreEqual(49, CountClipsWithTracks(IsKreOrBdb));
        [Test] public void Tracks_NoActivationCurveAnywhere() =>
            Assert.AreEqual(0, Clips(_ => true).SelectMany(c => c.clip.Curves).Count(c => c.Property.StartsWith("m_IsActive")));
    }
}
