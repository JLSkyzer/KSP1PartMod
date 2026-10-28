using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    public class PlumeLinesTests
    {
        [TestCase("LiquidEngineRK-7(Clone)", "LiquidEngineRK-7")]
        [TestCase("liquidEngine2.prefab_bb26733d", "liquidEngine2")]
        [TestCase("liquidEngine2", "liquidEngine2")]
        [TestCase(null, null)]
        public void TypeOf(string name, string type) => Assert.AreEqual(type, PlumeLines.TypeOf(name));

        [Test] public void Budget_TwoLinesPerType()
        {
            var b = new PlumeLines.Budget();
            Assert.IsTrue(b.TakeBind("a"));
            Assert.IsFalse(b.TakeBind("a"), "one bind line per type");
            Assert.IsTrue(b.TakeWatch("a"));
            Assert.IsFalse(b.TakeWatch("a"), "one watcher at a time");
            b.ReleaseWatch("a");
            Assert.IsTrue(b.TakeWatch("a"), "a destroyed watcher frees the type");
            b.Done("a");
            Assert.IsFalse(b.TakeWatch("a"), "nothing after the running line");
            Assert.IsFalse(b.TakeBind("a"));
            Assert.IsFalse(b.TakeWatch("b"), "no watch before the bind line");
        }

        [Test] public void Line()
        {
            var s = new PlumeLines.Snapshot
            {
                Part = "LiquidEngineRK-7", Moment = "at bind", EngineState = "Off", HasManager = true, Events = 1, Effects = 3,
                Initialized = true, VfxActive = false, FxVisible = null, PlumeCount = 1,
            };
            var p = new PlumeLines.Plume { Name = "[KSP1 Plume] Main thrustTransform", ActiveInHierarchy = true, ScaleX = 1, ScaleY = 1, ScaleZ = 0.5f, RendererCount = 1 };
            p.Renderers.Add(new PlumeLines.Renderer { Kind = "SkinnedMeshRenderer", Enabled = false, HasMesh = true, Material = "m", Shader = "s" });
            s.Plumes.Add(p);
            Assert.AreEqual("[KSP1PartMod] plume: LiquidEngineRK-7: at bind, engine Off; ThrottleVFXManager: 1 event(s), 3 effect(s), " +
                            "initialized true, vfx active false, fx visible ?; 1 plume object(s) | '[KSP1 Plume] Main thrustTransform' active true, " +
                            "scale (1, 1, 0.5), 1 renderer(s): SkinnedMeshRenderer off mesh 'm'/'s'", PlumeLines.Line(s));
            Assert.AreEqual("[KSP1PartMod] plume: x: at bind, engine Off; no ThrottleVFXManager on the part; 0 plume object(s)",
                PlumeLines.Line(new PlumeLines.Snapshot { Part = "x", Moment = "at bind", EngineState = "Off" }));
        }
    }
}
