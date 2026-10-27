using System.Collections.Generic;
using System.Text.RegularExpressions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>The renderer sink and the flush on real Unity objects (EditMode). Material copies are made with
    /// <c>renderer.materials</c>, which Unity reports as a leak in edit mode: those messages are ignored here.</summary>
    public class RendererChannelsTests
    {
        private const string Emission = "_EmissionColor";
        private static readonly AnimationSettings Defaults = AnimationSettings.Defaults;

        private readonly List<Object> _made = new List<Object>();
        private Shader _lit, _plain;
        private ChannelTable Table;
        private RendererChannels Sink;

        [SetUp] public void SetUp()
        {
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null || !_lit.keywordSpace.FindKeyword("_EMISSION").isValid || _lit.FindPropertyIndex(Emission) < 0)
                Assert.Ignore("no 'Universal Render Pipeline/Lit' with _EmissionColor and an _EMISSION keyword");
            _plain = Shader.Find("Unlit/Color");
            Table = new ChannelTable();
            Sink = new RendererChannels(Table);
        }

        [TearDown] public void TearDown()
        {
            if (Table != null) ChannelFlush.Unregister(Table);
            Sink?.ReleaseAll();
            foreach (var o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private Material Mat(bool keywordOn)
        {
            var m = new Material(_lit) { name = keywordOn ? "lit-on" : "lit-off" };
            if (keywordOn) m.EnableKeyword("_EMISSION");
            else m.DisableKeyword("_EMISSION");
            _made.Add(m);
            return m;
        }

        private Material Plain()
        {
            if (_plain == null) Assert.Ignore("no 'Unlit/Color' shader");
            var m = new Material(_plain) { name = "plain" };
            if (m.HasProperty(Emission)) Assert.Ignore("'Unlit/Color' has _EmissionColor");
            _made.Add(m);
            return m;
        }

        /// <summary>Unity's edit-mode error for each material that <c>renderer.materials</c> instances (in play mode there
        /// is no such message).</summary>
        private static void ExpectInstancing(int materials)
        {
            for (var i = 0; i < materials; i++)
                LogAssert.Expect(LogType.Error, new Regex(@"^Instantiating material due to calling renderer\.material during edit mode"));
        }

        private MeshRenderer Renderer(bool keywordOn = true, params Material[] materials)
        {
            var go = new GameObject("r");
            _made.Add(go);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials.Length > 0 ? materials : new[] { Mat(keywordOn) };
            return r;
        }

        private static Value4 V(Color c) => new Value4(c.r, c.g, c.b, c.a);

        private int Channel(Renderer r, string property = Emission)
        {
            var t = Sink.AddRenderer(r, Defaults, out _, property);
            return Table.Register(t, Shader.PropertyToID(property), ChannelValueType.Color, WriterClass.Color, 0, V(Color.black));
        }

        private void Flush()
        {
            ChannelFlush.Register(Table, Sink);
            ChannelFlush.FlushAll();
        }

        private void Write(Renderer r, string property, Color c)
        {
            Table.Submit(Channel(r, property), V(c));
            Flush();
        }

        [Test] public void Sink_WritesThroughBlock_KeepsOtherValues()
        {
            var r = Renderer();
            var b = new MaterialPropertyBlock();
            b.SetFloat("_Other", 3);
            r.SetPropertyBlock(b);
            Write(r, Emission, Color.red);
            r.GetPropertyBlock(b);
            Assert.AreEqual((3f, Color.red), (b.GetFloat("_Other"), b.GetColor(Emission)));
        }

        [Test] public void Sink_NothingWrittenBeforeFlush()
        {
            var r = Renderer();
            Table.Submit(Channel(r), V(Color.red));
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            Assert.AreNotEqual(Color.red, b.GetColor(Emission));
        }

        [Test] public void Sink_KeywordOff_CopiesOnceAtBind()
        {
            var r = Renderer(keywordOn: false);
            var shared = r.sharedMaterial;
            ExpectInstancing(1);
            Sink.AddRenderer(r, Defaults, out _);
            Assert.AreNotSame(shared, r.sharedMaterial);
            Assert.IsTrue(r.sharedMaterial.IsKeywordEnabled("_EMISSION"));
            Assert.AreEqual(Color.black, r.sharedMaterial.GetColor(Emission));
            Assert.IsFalse(shared.IsKeywordEnabled("_EMISSION"), "the shared material is never written");
            var copy = r.sharedMaterial;
            Sink.AddRenderer(r, Defaults, out _);
            Assert.AreSame(copy, r.sharedMaterial);
            Assert.AreEqual(1, Sink.CopiesOf(r));
        }

        [Test] public void Sink_KeywordOn_NoCopy()
        {
            var r = Renderer(keywordOn: true);
            var shared = r.sharedMaterial;
            Sink.AddRenderer(r, Defaults, out _);
            Assert.AreSame(shared, r.sharedMaterial);
            Assert.AreEqual(0, Sink.CopiesOf(r));
        }

        /// <summary>A renderer written through another property (here <c>_Color</c>) needs no emission copy.</summary>
        [Test] public void Sink_OtherProperty_NoCopy()
        {
            var r = Renderer(keywordOn: false);
            var shared = r.sharedMaterial;
            Sink.AddRenderer(r, Defaults, out _, "_BaseColor");
            Assert.AreSame(shared, r.sharedMaterial);
            ExpectInstancing(1);
            Sink.AddRenderer(r, Defaults, out _, Emission);
            Assert.AreEqual(1, Sink.CopiesOf(r), "an emission channel added later still decides at bind");
        }

        [Test] public void Sink_EmissionSetupOff_NoCopy()
        {
            var off = AnimationSettings.Parse("{\"emission_setup\": false}", out var error);
            Assert.IsNull(error);
            var r = Renderer(keywordOn: false);
            var shared = r.sharedMaterial;
            Sink.AddRenderer(r, off, out _);
            Assert.AreSame(shared, r.sharedMaterial);
        }

        [Test] public void Sink_ReleaseAll_DestroysCopiesOnly()
        {
            var r = Renderer(keywordOn: false);
            var shared = r.sharedMaterial;
            ExpectInstancing(1);
            Sink.AddRenderer(r, Defaults, out _);
            var copy = r.sharedMaterial;
            Sink.ReleaseAll();
            Assert.IsTrue(copy == null);
            Assert.IsTrue(shared != null);
            Assert.AreSame(shared, r.sharedMaterial, "the renderer gets its material back");
        }

        [Test] public void Sink_MaterialSwapped_RebindsAndReleasesOldCopy()
        {
            var r = Renderer(keywordOn: false);
            ExpectInstancing(1);
            var c = Channel(r);
            var oldCopy = r.sharedMaterial;
            var other = Mat(keywordOn: true);
            r.sharedMaterial = other;
            Table.Submit(c, V(Color.red));
            Flush();
            Assert.IsTrue(oldCopy == null);
            Assert.AreEqual(1, Sink.Rebinds);
            Assert.AreSame(other, r.sharedMaterial);
        }

        /// <summary>The OAB grab swaps the shader on the same material instance: the shader alone triggers the rebind.</summary>
        [Test] public void Sink_ShaderChangedOnSameMaterial_Rebinds()
        {
            var r = Renderer(keywordOn: true);
            var c = Channel(r);
            r.sharedMaterial.shader = Shader.Find("Universal Render Pipeline/Unlit") ?? _plain;
            Table.Submit(c, V(Color.red));
            Flush();
            Assert.AreEqual(1, Sink.Rebinds);
            Table.Submit(c, V(Color.blue));
            Flush();
            Assert.AreEqual(1, Sink.Rebinds, "once");
        }

        [Test] public void Sink_TwoMaterials_DecidedPerMaterial()
        {
            var r = Renderer(true, Mat(keywordOn: false), Plain());
            ExpectInstancing(2);
            Sink.AddRenderer(r, Defaults, out var note);
            Assert.AreEqual(1, Sink.CopiesOf(r));
            StringAssert.Contains("2 material(s)", note);
            Assert.AreEqual("plain", r.sharedMaterials[1].name, "the second slot keeps its shared material");
        }

        [Test] public void Sink_UnsetEmissionMap_WhiteMapInBlock()
        {
            var m = Mat(keywordOn: true);
            if (!m.HasProperty("_EmissionMap")) Assert.Ignore("no _EmissionMap");
            m.SetTexture("_EmissionMap", null);
            var r = Renderer(true, m);
            Write(r, Emission, Color.red);
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            Assert.AreSame(Texture2D.whiteTexture, b.GetTexture("_EmissionMap"));
        }

        [Test] public void Sink_Light_WritesFields()
        {
            var go = new GameObject("l");
            _made.Add(go);
            var l = go.AddComponent<Light>();
            l.intensity = 1f;
            var t = Sink.AddLight(l);
            var c = Table.Register(t, (int)ChannelValueType.LightIntensity, ChannelValueType.LightIntensity, WriterClass.Track, 0,
                new Value4(1f, 0, 0, 0));
            var e = Table.Register(t, (int)ChannelValueType.LightEnabled, ChannelValueType.LightEnabled, WriterClass.Track, 0,
                new Value4(1f, 0, 0, 0));
            Table.Submit(c, new Value4(4f, 0, 0, 0));
            Table.Submit(e, new Value4(0f, 0, 0, 0));
            Flush();
            Assert.AreEqual((4f, false), (l.intensity, l.enabled));
        }

        /// <summary>A watched ReentryMesh hull: every flush rewrites the renderer while it is active, once more when it
        /// goes inactive, then only on change.</summary>
        [Test] public void Sink_ActiveHull_RewritesEveryFrame()
        {
            var r = Renderer();
            var c = Channel(r);
            var hull = new GameObject("hull");
            _made.Add(hull);
            hull.SetActive(false);
            Sink.WatchActive(Sink.AddRenderer(r, Defaults, out _), hull);
            Table.Submit(c, V(Color.red));
            Flush();
            Flush();
            Assert.AreEqual(0, Table.WritesLastFlush);
            hull.SetActive(true);
            Flush();
            Flush();
            Assert.AreEqual(1, Table.WritesLastFlush);
            hull.SetActive(false);
            Flush();
            Assert.AreEqual(1, Table.WritesLastFlush, "one more rewrite");
            Flush();
            Assert.AreEqual(0, Table.WritesLastFlush);
        }

        /// <summary>A rebind of the part (new TrackCache generation) replaces its table: only the new one is flushed.</summary>
        [Test] public void Flush_Reregister_NoLeakNoDoubleFlush()
        {
            var r = Renderer();
            var c = Channel(r);
            ChannelFlush.Register(Table, Sink);
            ChannelFlush.Register(Table, Sink);
            var before = ChannelFlush.Count;
            var table2 = new ChannelTable();
            var sink2 = new RendererChannels(table2);
            try
            {
                ChannelFlush.Unregister(Table);
                ChannelFlush.Register(table2, sink2);
                Assert.AreEqual(before, ChannelFlush.Count);
                Table.Submit(c, V(Color.red));
                ChannelFlush.FlushAll();
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                Assert.AreNotEqual(Color.red, b.GetColor(Emission), "the old table is no longer flushed");
            }
            finally
            {
                ChannelFlush.Unregister(table2);
                sink2.ReleaseAll();
            }
        }

        /// <summary>After a failed mpb check another writer replaced our block after the LateUpdate flush: the render-time
        /// flush writes every written target again, not only those rewritten every frame, and the checked one becomes
        /// an every-frame target.</summary>
        [Test] public void Flush_FailedCheck_RenderTimeRewritesEveryWrittenTarget()
        {
            var checkedR = Renderer();
            var other = Renderer();
            var a = Channel(checkedR);
            var b = Channel(other);
            Table.Submit(a, V(Color.red));
            Table.Submit(b, V(Color.green));
            Flush();
            Sink.TryGetTarget(checkedR, out var checkedTarget);
            try
            {
                ChannelFlush.EnableRenderTimeFlush("test", Table, checkedTarget);
                var empty = new MaterialPropertyBlock();
                checkedR.SetPropertyBlock(empty); // the other writer replaces the blocks after our LateUpdate
                other.SetPropertyBlock(empty);
                ChannelFlush.FlushAtRender();
                var block = new MaterialPropertyBlock();
                checkedR.GetPropertyBlock(block);
                Assert.AreEqual(Color.red, block.GetColor(Emission));
                other.GetPropertyBlock(block);
                Assert.AreEqual(Color.green, block.GetColor(Emission), "a target not rewritten every frame");
                ChannelFlush.FlushAll();
                Assert.AreEqual(1, Table.WritesLastFlush, "the checked target is now rewritten every frame");
            }
            finally
            {
                ChannelFlush.DisableRenderTimeFlush();
            }
        }

        [Test] public void Flush_10000Frames_NoGcAlloc()
        {
            var r = Renderer(keywordOn: false);
            ExpectInstancing(1);
            var c = Channel(r);
            var go = new GameObject("l");
            _made.Add(go);
            var l = go.AddComponent<Light>();
            var lt = Sink.AddLight(l);
            var li = Table.Register(lt, (int)ChannelValueType.LightIntensity, ChannelValueType.LightIntensity, WriterClass.Track, 0,
                new Value4(1f, 0, 0, 0));
            var hull = new GameObject("hull");
            _made.Add(hull);
            Sink.WatchActive(Sink.AddRenderer(r, Defaults, out _), hull);
            Table.Submit(c, new Value4(0f, 0f, 0f, 1f));
            Flush();
            Assert.That(() =>
            {
                for (var i = 1; i <= 10000; i++)
                {
                    var x = i / 10000f;
                    Table.Submit(c, new Value4(x, x, x, 1f));
                    if (i % 2 == 0) Table.Submit(li, new Value4(x, 0f, 0f, 0f));
                    if (i % 1000 == 0) hull.SetActive(!hull.activeSelf);
                    ChannelFlush.FlushAll();
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, Sink.Rebinds);
        }
    }
}
