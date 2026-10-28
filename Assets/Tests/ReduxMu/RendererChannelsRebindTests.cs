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

        /// <summary>The slot still holds our copy (the texture switch rewrote properties in place): released, the material
        /// of the bind back in the slot, then a fresh copy of it.</summary>
        [Test] public void Sink_RebindAll_SameSlot_NewCopyOfBindMaterial()
        {
            var shared = Mat(keywordOn: false);
            var r = Renderer(shared);
            ExpectInstancing(2);
            Sink.AddRenderer(r, Defaults, out _);
            var first = r.sharedMaterial;
            Sink.RebindAll();
            Assert.IsTrue(first == null);
            Assert.AreNotSame(shared, r.sharedMaterial);
            Assert.IsTrue(r.sharedMaterial.IsKeywordEnabled("_EMISSION"));
            Assert.AreEqual(1, Sink.CopiesOf(r));
            Sink.ReleaseAll();
            Assert.AreSame(shared, r.sharedMaterial, "the copy made by the rebind is ours too");
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
