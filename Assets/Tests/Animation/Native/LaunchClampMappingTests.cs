using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>Launch clamps (0.19.0): the pure mapping and a census of the installed cfg files.</summary>
    public class LaunchClampMappingTests
    {
        private static IKsp1Node Clamp(string body) =>
            CfgText.Parse("MODULE\n{\nname = LaunchClamp\n" + body + "\n}\n").GetNodes("MODULE").First();

        [Test] public void Node_SrfAttachWhenPresent()
        {
            var m = LaunchClampMapping.From(Clamp(""), new[] { "bottom", "srfAttach" });
            Assert.AreEqual("srfAttach", m.NodeId);
            Assert.IsEmpty(m.Notes);
        }

        [Test] public void Node_FirstWhenNoSurfaceNode_Noted()
        {
            var m = LaunchClampMapping.From(Clamp(""), new[] { "top", "bottom" });
            Assert.AreEqual("top", m.NodeId);
            CollectionAssert.Contains(m.Notes, LaunchClampMapping.NodeNotFoundNote("srfAttach", "top"));
        }

        [Test] public void Node_NoNodesAtAll_KeepsTheSurfaceDefault() =>
            Assert.AreEqual("srfAttach", LaunchClampMapping.From(Clamp(""), new string[0]).NodeId);

        [Test] public void Notes_EveryKsp1KeyIsNotReproduced()
        {
            var m = LaunchClampMapping.From(Clamp("trf_towerPivot_name = a\ntrf_towerStretch_name = b\nanim_decouple_name = launchclamp\nanim_decouple_name = x"), new[] { "srfAttach" });
            CollectionAssert.AreEqual(new[]
            {
                "LaunchClamp: trf_towerPivot_name not reproduced",
                "LaunchClamp: trf_towerStretch_name not reproduced",
                "LaunchClamp: anim_decouple_name not reproduced",
            }, m.Notes);
        }

        [Test] public void Census_InstalledCfg_EveryClampMapsToTheSurfaceNode()
        {
            var files = InstalledKsp1.AllCfg().Where(f => File.ReadAllText(f).Contains("LaunchClamp")).ToArray();
            if (files.Length == 0) Assert.Ignore("no KSP1 cfg files installed");
            var clamps = 0;
            foreach (var f in files)
                foreach (var part in CfgText.Parse(File.ReadAllText(f)).GetNodes("PART"))
                    foreach (var mod in part.GetNodes("MODULE").Where(x => x.GetValue("name")?.Trim() == "LaunchClamp"))
                    {
                        clamps++;
                        // KSP1's node_attach becomes Redux's srfAttach node.
                        var ids = part.AllValues.Any(v => v.Key.Trim() == "node_attach") ? new[] { "srfAttach" } : new string[0];
                        Assert.AreEqual("srfAttach", LaunchClampMapping.From(mod, ids).NodeId, part.GetValue("name"));
                    }
            TestContext.WriteLine($"launch clamp census: {files.Length} cfg file(s), {clamps} clamp(s)");
            Assert.AreEqual(7, clamps, "the stock TT-18 and six Bluedog_DB clamps and towers (2026-11-20)");
        }
    }
}
