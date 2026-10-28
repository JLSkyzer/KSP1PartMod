using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
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
    /// <summary>The Task 8 glue on real Unity objects (EditMode): <see cref="AdvancedLookAt"/> (its <c>Apply</c>, as
    /// <c>LateUpdate</c> does not run here) and the per-slot <see cref="MaterialWriter"/> of the RCS emissive.</summary>
    public class PluginGlueTests
    {
        private readonly List<Object> _made = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private Transform Node(string name, Vector3 position)
        {
            var go = new GameObject(name);
            _made.Add(go);
            go.transform.position = position;
            return go.transform;
        }

        private AdvancedLookAt LookAt(Transform rotator, Transform target, bool[] constrained = null, float[] scales = null)
        {
            var host = new GameObject("part");
            _made.Add(host);
            var c = host.AddComponent<AdvancedLookAt>();
            c.Init(new[] { rotator }, new[] { target }, constrained, scales);
            return c;
        }

        [Test] public void LookAt_Unconstrained_FacesTheTarget_WithItsUp()
        {
            var rotator = Node("rotator", Vector3.zero);
            var target = Node("target", new Vector3(5f, 0f, 0f));
            target.rotation = Quaternion.Euler(90f, 0f, 0f); // target.up = (0, 0, 1)
            LookAt(rotator, target).Apply();
            Assert.Greater(Vector3.Dot(rotator.forward, Vector3.right), 0.9999f);
            Assert.Greater(Vector3.Dot(rotator.up, target.up), 0.9999f);
        }

        [Test] public void LookAt_OneAxisConstrained_OtherAxesKeepTheOriginal()
        {
            var rotator = Node("rotator", Vector3.zero);
            var target = Node("target", new Vector3(5f, 5f, 0f)); // look: pitch -45, yaw 90
            LookAt(rotator, target, new[] { false, true, false }, new[] { 0f, 0f, 0f }).Apply();
            var e = rotator.localEulerAngles;
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, e.x), 1e-3f, "x not constrained: original kept");
            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, e.y), 1e-3f, "y constrained, scale 0: fully look-at");
        }

        [Test] public void LookAt_Scale_CompoundsFrameAfterFrame_AsKsp1()
        {
            var rotator = Node("rotator", Vector3.zero);
            var target = Node("target", new Vector3(5f, 0f, 0f)); // look yaw 90
            var c = LookAt(rotator, target, new[] { false, true, false }, new[] { 0f, 0.5f, 0f });
            c.Apply();
            Assert.AreEqual(0f, Mathf.DeltaAngle(45f, rotator.localEulerAngles.y), 1e-3f, "0.5 * 0 + 0.5 * 90");
            c.Apply();
            Assert.AreEqual(0f, Mathf.DeltaAngle(67.5f, rotator.localEulerAngles.y), 1e-3f, "the original is this frame's rotation");
        }

        [Test] public void LookAt_MissingTransforms_Skipped()
        {
            var rotator = Node("rotator", Vector3.zero);
            var c = LookAt(rotator, null);
            Assert.DoesNotThrow(() => c.Apply());
            Assert.AreEqual(Quaternion.identity, rotator.rotation);
            var same = Node("same", Vector3.zero);
            LookAt(rotator, same).Apply(); // zero direction: left alone
            Assert.AreEqual(Quaternion.identity, rotator.rotation);
        }

        [Test] public void LookAt_Apply_10000Frames_AllocatesZero()
        {
            var rotator = Node("rotator", Vector3.zero);
            var target = Node("target", new Vector3(3f, 1f, 2f));
            var plain = LookAt(rotator, target);
            var blended = LookAt(Node("r2", Vector3.one), target, new[] { true, true, false }, new[] { 0.3f, 0.6f, 1f });
            plain.Apply();
            blended.Apply();
            Assert.That(() =>
            {
                for (var k = 0; k < 10000; k++)
                {
                    plain.Apply();
                    blended.Apply();
                }
            }, Is.Not.AllocatingGCMemory());
        }

        private MeshRenderer MeshUnder(Transform parent, string name)
        {
            var go = new GameObject(name);
            _made.Add(go);
            go.transform.SetParent(parent, false);
            return go.AddComponent<MeshRenderer>();
        }

        [Test] public void FirstMeshRenderer_NeverRedux_RcsPlume()
        {
            var thruster = Node("RCSthruster", Vector3.zero);
            var plume = Node("[KSP1 RCS Plume] RCSthruster", Vector3.zero);
            plume.SetParent(thruster, false);
            var smoke = new GameObject("smoke");
            _made.Add(smoke);
            smoke.transform.SetParent(plume, false);
            smoke.AddComponent<SkinnedMeshRenderer>();
            MeshUnder(plume, "core");
            Assert.IsNull(Module_Ksp1Animations.FirstMeshRenderer(thruster), "only the plume's renderers: none taken");

            var glow = MeshUnder(thruster, "nozzleGlow"); // after the plume in hierarchy order
            Assert.AreSame(glow, Module_Ksp1Animations.FirstMeshRenderer(thruster));
        }

        [Test] public void FirstMeshRenderer_ThrusterInsideAPlume_None()
        {
            var plume = Node("[ksp1 rcs plume] x", Vector3.zero);
            var thruster = Node("rcs", Vector3.zero);
            thruster.SetParent(plume, false);
            MeshUnder(thruster, "m");
            Assert.IsNull(Module_Ksp1Animations.FirstMeshRenderer(thruster), "Redux's test walks every ancestor");
        }

        [Test] public void MaterialWriter_PerSlot_WritesOnlyItsSlot()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null || lit.FindPropertyIndex("_EmissionColor") < 0) Assert.Ignore("no 'Universal Render Pipeline/Lit' with _EmissionColor");
            var material = new Material(lit);
            _made.Add(material);
            var go = new GameObject("thruster");
            _made.Add(go);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;

            var def = new AnimEntryDef { Kind = EntryKind.RcsEmissive, Key = "rcsemissive::0", ShaderProperty = "_EmissiveColor" };
            var writer = new MaterialWriter(new Renderer[] { null, r }, def, AnimationSettings.Defaults);
            Assert.AreEqual(1, writer.RendererCount);
            StringAssert.Contains("_EmissionColor", writer.Note, "mapped by shader_property_map");

            // The sink copies the material with renderer.materials, which Unity reports as a leak in edit mode only.
            LogAssert.ignoreFailingMessages = true;
            var table = new ChannelTable();
            var sink = new RendererChannels(table);
            try
            {
                writer.Register(table, sink);
                writer.WriteAt(0, new Rgba(1f, 1f, 1f, 0.5f));
                Assert.AreEqual(0, table.SubmitCount, "slot 0 has no renderer");
                writer.WriteAt(1, new Rgba(1f, 1f, 1f, 0.5f));
                Assert.AreEqual(1, table.SubmitCount);
                writer.WriteAt(7, new Rgba(1f, 1f, 1f, 0.5f));
                Assert.AreEqual(1, table.SubmitCount, "out of range: nothing");
                Assert.That(() =>
                {
                    for (var k = 0; k < 10000; k++) writer.WriteAt(1, new Rgba(1f, 1f, 1f, k * 1e-4f));
                }, Is.Not.AllocatingGCMemory());
            }
            finally
            {
                sink.ReleaseAll();
            }
        }
    }
}
