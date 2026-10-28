using System.Collections.Generic;
using System.Text.RegularExpressions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary><see cref="RendererChannels.RebindAll"/> on real Unity objects (EditMode): what a variant change does to
    /// the material copies. Each <c>renderer.materials</c> instancing logs an edit-mode error, expected here.</summary>
    public class RendererChannelsRebindTests
    {
        private const string Emission = "_EmissionColor";
        private static readonly AnimationSettings Defaults = AnimationSettings.Defaults;

        private readonly List<Object> _made = new List<Object>();
        private Shader _lit;
        private ChannelTable Table;
        private RendererChannels Sink;

        [SetUp] public void SetUp()
        {
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null || !_lit.keywordSpace.FindKeyword("_EMISSION").isValid || _lit.FindPropertyIndex(Emission) < 0)
                Assert.Ignore("no 'Universal Render Pipeline/Lit' with _EmissionColor and an _EMISSION keyword");
            Table = new ChannelTable();
            Sink = new RendererChannels(Table);
        }

        [TearDown] public void TearDown()
        {
            Sink?.ReleaseAll();
            foreach (var o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private Material Mat(bool keywordOn, string name = null)
        {
            var m = new Material(_lit) { name = name ?? (keywordOn ? "lit-on" : "lit-off") };
            if (keywordOn) m.EnableKeyword("_EMISSION");
            else m.DisableKeyword("_EMISSION");
            _made.Add(m);
            return m;
        }

        private MeshRenderer Renderer(Material m)
        {
            var go = new GameObject("r");
            _made.Add(go);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            return r;
        }

        private static void ExpectInstancing(int materials)
        {
            for (var i = 0; i < materials; i++)
                LogAssert.Expect(LogType.Error, new Regex(@"^Instantiating material due to calling renderer\.material during edit mode"));
        }

        /// <summary>The slot no longer holds our copy (another material was put there): the copy is destroyed and the new
        /// material decided again, with a copy of its own.</summary>
        [Test] public void Sink_RebindAll_ReleasesAndRecreatesCopies()
        {
            var r = Renderer(Mat(keywordOn: false));
            ExpectInstancing(2);
            Sink.AddRenderer(r, Defaults, out _);
            var first = r.sharedMaterial;
            var other = Mat(keywordOn: false, "other");
            r.sharedMaterial = other;
            Assert.AreEqual(1, Sink.RebindAll());
            Assert.IsTrue(first == null, "our old copy destroyed");
            Assert.AreNotSame(other, r.sharedMaterial, "a copy of the new material");
            Assert.IsTrue(r.sharedMaterial.IsKeywordEnabled("_EMISSION"));
            Assert.IsFalse(other.IsKeywordEnabled("_EMISSION"), "the shared material is never written");
            Assert.AreEqual(1, Sink.CopiesOf(r));
        }

        /// <summary>The slot still holds our copy (the texture switch rewrote its properties in place, possibly keeping it
        /// as a reverter key if Redux's switch ran after our bind): the copy stays, same reference, and gets our keyword and
        /// black emission again.</summary>
        [Test] public void Sink_RebindAll_SameSlot_KeepsCopyAndRefreshesIt()
        {
            var shared = Mat(keywordOn: false);
            var r = Renderer(shared);
            ExpectInstancing(1);
            Sink.AddRenderer(r, Defaults, out _);
            var copy = r.sharedMaterial;
            copy.DisableKeyword("_EMISSION"); // CopyPropertiesFromMaterial from a copy taken before our bind
            copy.SetColor(Emission, Color.white);
            Assert.AreEqual(1, Sink.RebindAll());
            Assert.IsTrue(copy != null, "not destroyed");
            Assert.AreSame(copy, r.sharedMaterial);
            Assert.IsTrue(copy.IsKeywordEnabled("_EMISSION"));
            Assert.AreEqual(Color.black, copy.GetColor(Emission));
            Assert.AreEqual(1, Sink.CopiesOf(r));
            Sink.ReleaseAll();
            Assert.AreSame(shared, r.sharedMaterial, "still ours: released at shutdown");
        }

        /// <summary>The 2a deferral: under the OAB selection shader the rebind waits; the flush does it once the shader
        /// changes back.</summary>
        [Test] public void Sink_RebindAll_UnderSelectionShader_Deferred()
        {
            var selection = UnityEditor.ShaderUtil.CreateShaderAsset(
                "Shader \"KSP2/Scenery/Standard_OAB_Selection\" { SubShader { Pass { Color (1,1,1,1) } } }", false);
            if (selection == null || selection.name != "KSP2/Scenery/Standard_OAB_Selection") Assert.Ignore("no test selection shader");
            _made.Add(selection);
            var lit = Mat(keywordOn: true);
            var r = Renderer(lit);
            var t = Sink.AddRenderer(r, Defaults, out _);
            var c = Table.Register(t, Shader.PropertyToID(Emission), ChannelValueType.Color, WriterClass.Color, 0, new Value4(0, 0, 0, 1));
            lit.shader = selection;
            Assert.AreEqual(0, Sink.RebindAll(), "deferred");
            Assert.AreEqual(0, Sink.Rebinds);
            Table.Submit(c, new Value4(1, 0, 0, 1));
            ChannelFlush.Register(Table, Sink);
            ChannelFlush.FlushAll();
            Assert.AreEqual(0, Sink.Rebinds, "still under the selection shader");
            lit.shader = _lit; // the shader switch dropped the _EMISSION keyword: the rebind copies the material
            ExpectInstancing(1);
            Table.Submit(c, new Value4(0, 1, 0, 1));
            ChannelFlush.FlushAll();
            ChannelFlush.Unregister(Table);
            Assert.AreEqual(1, Sink.Rebinds, "rebound once the shader is back");
        }

        [Test] public void Sink_RebindAll_NoCopyNeeded_ReleasesOnly()
        {
            var r = Renderer(Mat(keywordOn: false));
            ExpectInstancing(1);
            Sink.AddRenderer(r, Defaults, out _);
            var first = r.sharedMaterial;
            var lit = Mat(keywordOn: true, "other-on");
            r.sharedMaterial = lit;
            Sink.RebindAll();
            Assert.IsTrue(first == null);
            Assert.AreSame(lit, r.sharedMaterial);
            Assert.AreEqual(0, Sink.CopiesOf(r));
        }

        /// <summary>Redux's texture switch instances its targets before our bind and keeps the instances as reverter keys:
        /// they are not ours (the <c>fresh</c> rule), and a rebind never destroys them.</summary>
        [Test] public void Sink_RebindAll_KeepsInstancesThatWereNotOurs()
        {
            var r = Renderer(Mat(keywordOn: false));
            LogAssert.ignoreFailingMessages = true; // how often Unity logs the edit-mode instancing here is not the point
            var redux = r.materials[0]; // Redux's instancing, before our bind
            _made.Add(redux);
            Sink.AddRenderer(r, Defaults, out _);
            Assert.AreSame(redux, r.sharedMaterial);
            Assert.AreEqual(0, Sink.CopiesOf(r));
            redux.DisableKeyword("_EMISSION"); // the reverter's CopyPropertiesFromMaterial, from a copy taken before our bind
            Sink.RebindAll();
            Assert.IsTrue(redux != null, "not destroyed");
            Assert.AreSame(redux, r.sharedMaterial, "the same instance stays in the slot");
            Assert.IsTrue(redux.IsKeywordEnabled("_EMISSION"), "decided again in place");
            Assert.AreEqual(0, Sink.CopiesOf(r));
        }

        [Test] public void Sink_RebindAll_DestroyedRenderer_Skipped()
        {
            var r = Renderer(Mat(keywordOn: true));
            Sink.AddRenderer(r, Defaults, out _);
            Object.DestroyImmediate(r.gameObject);
            Assert.AreEqual(0, Sink.RebindAll());
        }
    }
}
