using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The visual-only plugin entries without a clip machine (spec 2b.4, 2b.6, 2b.7): the RCS glow
    /// (<c>FXModuleAnimateRCS</c>, <c>ModuleBdbRCSEmissive</c>), the mesh hider (<c>ModuleBDBDeployableMeshHider</c>) and the
    /// advanced look-at (<c>ModuleAdvancedLookAtConstraint</c>). They only read <c>Data_RCS</c>, <c>Module_RCS</c> and
    /// <c>Data_Deployable</c>; they write our samplers, our channels, <c>SetActive</c> of the mesh hider's objects and the
    /// rotators' rotation.</summary>
    public partial class Module_Ksp1Animations
    {
        // ---------------------------------------------------------------- bind

        /// <summary>The part's RCS (<see cref="PluginInputs.TryRcs"/>) into the entry. A missing RCS disables the entry in
        /// flight with its line (once per part type); in the editor the entry is kept at rest.</summary>
        private bool RcsInput(Entry e, ref string note)
        {
            if (PluginInputs.TryRcs(this, out e.Thrusters, out e.RcsForce, out e.RcsMax, out var field)) return true;
            e.Thrusters = null;
            e.RcsForce = null;
            if (!PluginRules.Requires(e.Def.Kind, PartBackingMode == PartBackingModes.OAB)) return true;
            LogPluginLine(e.Def, PluginRules.MissingLine(_name, e.Def, field), "missing");
            note = InputMissingNote;
            return false;
        }

        /// <summary><c>ModuleBdbRCSEmissive</c>: the first mesh renderer under the thruster (itself included), in hierarchy
        /// order. Particle, trail and line renderers (Redux's plumes) are passed over.</summary>
        private static Renderer FirstMeshRenderer(Transform thruster)
        {
            if (thruster == null) return null;
            foreach (var r in thruster.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) return r;
            return null;
        }

        /// <summary>Every object of the part whose name is one of <paramref name="names"/> (KSP1 <c>FindModelTransforms</c>:
        /// all matches), in hierarchy order, each once.</summary>
        private GameObject[] ObjectsNamed(string[] names)
        {
            var found = new List<GameObject>();
            if (names == null) return found.ToArray();
            if (_transforms == null) _transforms = new PartTransforms(gameObject.transform);
            foreach (var t in _transforms.Transforms)
            {
                if (t == null || Array.IndexOf(names, t.name) < 0) continue;
                if (!found.Contains(t.gameObject)) found.Add(t.gameObject);
            }
            return found.ToArray();
        }

        /// <summary>The first object of the part with this name (KSP1 <c>FindModelTransform</c>), or null.</summary>
        private Transform FirstNamed(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_transforms == null) _transforms = new PartTransforms(gameObject.transform);
            foreach (var t in _transforms.Transforms)
                if (t != null && string.Equals(t.name, name, StringComparison.Ordinal)) return t;
            return null;
        }

        /// <summary>The look-at constraints of the entry on one <see cref="AdvancedLookAt"/> of ours. A rotator that Redux's
        /// own look-at runtime drives is left to it (<see cref="PluginRules.LeftToRedux"/>).</summary>
        private void BuildLookAt(Entry e)
        {
            var def = e.Def;
            var redux = PluginInputs.ReduxLookAtRotators(this);
            var rotators = new List<Transform>();
            var targets = new List<Transform>();
            var missing = 0;
            var n = Math.Min(def.RotatorNames?.Length ?? 0, def.TargetNames?.Length ?? 0);
            for (var i = 0; i < n; i++)
            {
                var rotatorName = def.RotatorNames[i];
                if (PluginRules.LeftToRedux(rotatorName, redux))
                {
                    LogPluginLine(def, PluginRules.LookAtLeftToReduxLine(_name, rotatorName), "redux|" + rotatorName);
                    continue;
                }
                var rotator = FirstNamed(rotatorName);
                var target = FirstNamed(def.TargetNames[i]);
                if (rotator == null) missing++;
                if (target == null) missing++;
                if (rotator == null || target == null) continue;
                rotators.Add(rotator);
                targets.Add(target);
            }
            if (rotators.Count > 0)
            {
                e.LookAt = gameObject.AddComponent<AdvancedLookAt>();
                e.LookAt.Init(rotators.ToArray(), targets.ToArray(), def.AxisConstrained, def.AxisScales);
            }
            LogPluginLine(def, PluginRules.LookAtLine(_name, rotators.Count, missing));
        }

        /// <summary>Each mesh hider follows our first Deploy entry of the part (the ModuleDeployablePart family), else Redux's
        /// <c>Data_Deployable.CurrentDeployState</c>; with neither its objects are never hidden (line logged).</summary>
        private void ResolveMeshHiders()
        {
            foreach (var e in _entries)
            {
                if (e.Hider == null || e.Disabled) continue;
                try
                {
                    ResolveMeshHider(e);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        private void ResolveMeshHider(Entry e)
        {
            Entry ours = null;
            foreach (var o in _entries)
                if (!o.Disabled && o.Def.Kind == EntryKind.Deploy && o.Deploy != null) { ours = o; break; }
            if (ours != null)
            {
                var deploy = ours.Deploy;
                e.Retracted = () => deploy.State == DeployState.Retracted;
                LogPluginLine(e.Def, PluginRules.MeshHiderLine(_name, e.HiddenObjects.Length, ours.Def.Key));
                return;
            }
            if (ReduxFollowerBinder.TryDeployableClock(this, e.Def, out var read, out _, out var note))
            {
                e.Retracted = () => read() == ReduxDeployState.Retracted;
                LogPluginLine(e.Def, PluginRules.MeshHiderLine(_name, e.HiddenObjects.Length, null));
                return;
            }
            LogPluginLine(e.Def, PluginRules.MissingLine(_name, e.Def, "our Deploy entry or Module_Deployable with Data_Deployable"), "missing");
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Flight: each sampler at the time of its thruster, <see cref="RcsScalar"/> of its force; the first non-zero
        /// sample is logged once per part type. Editor, a count mismatch or no RCS: at rest (weight 0, the material's own
        /// value). Allocation-free.</summary>
        private void StepRcsGlow(Entry e, bool inEditor)
        {
            var samplers = e.Tracks;
            var map = e.RcsThrusterOf;
            if (inEditor || map == null || e.RcsForce == null)
            {
                TrackSampler.SampleAll(samplers, in RestTrackPose);
                return;
            }
            var n = samplers.Length < map.Length ? samplers.Length : map.Length;
            for (var k = 0; k < n; k++)
            {
                var force = e.RcsForce(map[k]);
                var scalar = RcsScalar.Of(force, e.RcsMax, e.Def.ThrustForceMult);
                if (force > 0f && !e.RcsLogged)
                {
                    e.RcsLogged = true;
                    LogPluginLine(e.Def, PluginRules.RcsFirstValuesLine(_name, force, e.RcsMax, scalar), "first");
                }
                samplers[k].Sample(new ClipPose(scalar, 0f, 1f, true));
            }
        }

        /// <summary>Flight: each thruster's renderer gets <c>(1, 1, 1, alphaCurve(scalar))</c> on its Color-class channel.
        /// Editor or no RCS: nothing is submitted, so the material's own colour shows. Allocation-free.</summary>
        private static void StepRcsEmissive(Entry e, bool inEditor)
        {
            if (inEditor || e.RcsForce == null || e.Writer == null || e.Thrusters == null) return;
            for (var i = 0; i < e.Thrusters.Length; i++)
            {
                var scalar = RcsScalar.Of(e.RcsForce(i), e.RcsMax, e.Def.ThrustForceMult);
                e.Writer.WriteAt(i, new Rgba(1f, 1f, 1f, e.RcsAlpha.Evaluate(scalar)));
            }
        }

        private static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null) return;
            for (var i = 0; i < objects.Length; i++)
                if (objects[i] != null) objects[i].SetActive(active);
        }

        /// <summary>A plugin entry gives back what it changed on the part: the mesh hider shows again the objects it hid;
        /// the look-at is disabled, or destroyed at shutdown. Never throws.</summary>
        private void ReleasePluginObjects(Entry e, bool destroy)
        {
            try
            {
                if (e.Hider != null && e.Hider.Restore()) SetActive(e.HiddenObjects, true);
                if (e.LookAt != null)
                {
                    if (destroy)
                    {
                        Destroy(e.LookAt);
                        e.LookAt = null;
                    }
                    else e.LookAt.enabled = false;
                }
            }
            catch (Exception ex)
            {
                Error($"{SafePartName()} {e.Def.Kind} '{Subject(e.Def)}' release failed: {ex}");
            }
        }
    }
}
