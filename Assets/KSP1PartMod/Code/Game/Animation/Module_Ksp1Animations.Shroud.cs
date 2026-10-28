using System;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Audit;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Engine shrouds (0.7.0): the KSP1 <c>ModuleJettison</c> meshes kept by <see cref="ShroudImport"/>. The shroud
    /// is shown while a part is attached to its bottom node (VAB: <c>OABPart.FindNodeWithTag(node).ConnectedPart</c>;
    /// flight: <c>part.Model.TryGetAttachment(node).AttachedPart</c>), only the name its variants select, never while its B9
    /// gate is closed, the VAB hide override is on, or (flight) after its jettison. Visual only: <c>SetActive</c> on the
    /// shroud roots, <c>Renderer.enabled</c> on the renderers the prefab disabled, and in flight <c>Collider.enabled = false</c>
    /// under the roots. Nothing is restored at shutdown: the prefab baseline is hidden and instances die with the part.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>One shroud entry's runtime state.</summary>
        private sealed class ShroudRuntime
        {
            public readonly ShroudMachine Machine;
            /// <summary>Null when no transform was found: the entry does nothing.</summary>
            public ShroudObjects Objects;
            /// <summary>Per name, its variant gates; null for a name always enabled.</summary>
            public EntryGate[] NameGates;
            public bool[] Enabled;
            /// <summary>The name the variants select (<see cref="ShroudSelection.Shown"/>), -1 for none.</summary>
            public int Selected = -1;
            public IObjectAssemblyPartNode OabNode;
            public AttachNodeData FlightNode;
            /// <summary>The first-ignition fallback when the staging signal is not armed (flight, <c>!isFairing</c>).</summary>
            public Func<bool> Ignited;
            public bool IgnitionSeen;
            public bool? LastAttached;
            public bool LastOverride;

            public ShroudRuntime(AnimEntryDef def) => Machine = new ShroudMachine(def);
        }

        // ---------------------------------------------------------------- bind

        /// <summary>After <see cref="BindVariants"/>: the transforms, the node, the name gates, the flight colliders, the bind
        /// line and the audit notes of every shroud entry. Never throws: a failure disables the entry.</summary>
        private void ResolveShrouds()
        {
            foreach (var e in _entries)
            {
                if (e.ShroudRt == null || e.Disabled) continue;
                try
                {
                    ResolveShroud(e);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        private void ResolveShroud(Entry e)
        {
            var def = e.Def;
            var rt = e.ShroudRt;
            var inEditor = PartBackingMode == PartBackingModes.OAB;
            var names = def.TransformNames ?? new string[0];

            var owned = ShroudImport.OwnedRendererPaths(_name);
            // An ignoreNodes shroud is not convertible: Redux keeps it and the prefab postfix leaves it as it is.
            if (owned == null && names.Length > 0 && !def.IgnoreNodes)
                ShroudLine(def, $"[KSP1PartMod] anim: {_name} shroud: no prefab renderer record, its renderers are left as they are", "norecord", warn: true);
            var objects = ShroudObjects.Find(gameObject.transform, names, owned);

            var nodeFound = FindShroudNode(rt, def, inEditor);
            var gates = InitShroudGates(e);
            ShroudLine(def, ShroudLines.Resolved(_name, names, objects.Found, def.NodeName, nodeFound, def.IsFairing, gates));

            ShroudRule.AuditNotes(objects.Found, names.Length, nodeFound, def.IgnoreNodes, out var kept, out var missing, out var noNode);
            if (kept) AuditProbe.Note("shroud_kept");
            if (missing) AuditProbe.Note("shroud_missing");
            if (noNode) AuditProbe.Note("shroud_no_node");

            rt.LastOverride = rt.Machine.HideOverride;
            if (objects.Found == 0)
            {
                // The entry does nothing: its button stays hidden.
                ShroudLine(def, "[KSP1PartMod] anim: " + PluginRules.MissingLine(_name, def, "transform(s) '" + string.Join("', '", names) + "'"), "missing");
                return;
            }
            rt.Objects = objects;
            if (inEditor) return;

            ShroudLine(def, ShroudLines.CollidersDisabled(_name, objects.DisableColliders()), "colliders");
            if (!ShroudImport.StagingSignalArmed && !def.IsFairing && PluginInputs.TryAnyEngineIgnited(this, out var ignited, out _))
                rt.Ignited = ignited;
        }

        /// <summary>The node's lookup, cached. True when found.</summary>
        private bool FindShroudNode(ShroudRuntime rt, AnimEntryDef def, bool inEditor)
        {
            if (inEditor)
            {
                var node = rt.OabNode;
                if (node == null || node is UnityEngine.Object o && o == null)
                    node = rt.OabNode = OABPart?.FindNodeWithTag(def.NodeName);
                return node != null;
            }
            if (rt.FlightNode != null) return true;
            var model = part != null ? part.Model : null;
            if (model != null && model.TryGetAttachment(def.NodeName, out var data)) rt.FlightNode = data;
            return rt.FlightNode != null;
        }

        /// <summary>Each name's variant gates on the watched sets, set to the selection; then the selected name. Returns the
        /// number of gates.</summary>
        private int InitShroudGates(Entry e)
        {
            var rt = e.ShroudRt;
            var names = e.Def.TransformNames ?? new string[0];
            rt.NameGates = new EntryGate[names.Length];
            rt.Enabled = new bool[names.Length];
            var count = 0;
            for (var i = 0; i < names.Length; i++)
            {
                var gates = e.Def.NameGates != null && i < e.Def.NameGates.Length ? e.Def.NameGates[i] : null;
                if (gates != null && gates.Length > 0)
                {
                    var sets = new int[gates.Length];
                    for (var k = 0; k < gates.Length; k++) sets[k] = _variants?.IndexOf(gates[k]?.SetId) ?? -1;
                    var gate = new EntryGate(gates, sets);
                    for (var k = 0; k < gates.Length; k++)
                        if (sets[k] >= 0) gate.OnVariant(sets[k], _variants.ActiveOf(sets[k]));
                    rt.NameGates[i] = gate;
                    count += gates.Length;
                }
                rt.Enabled[i] = rt.NameGates[i] == null || rt.NameGates[i].Active;
            }
            rt.Selected = ShroudSelection.Shown(rt.Enabled);
            return count;
        }

        // ---------------------------------------------------------------- variants

        /// <summary>A watched set changed: the name gates that follow it, and the variant line when the selected name
        /// changes.</summary>
        private void NoteShroudVariant(int set, string id, string label)
        {
            foreach (var e in _entries)
            {
                var rt = e.ShroudRt;
                if (rt?.NameGates == null || e.Disabled) continue;
                var moved = false;
                for (var i = 0; i < rt.NameGates.Length; i++)
                {
                    var g = rt.NameGates[i];
                    if (g == null || !g.Follows(set)) continue;
                    g.OnVariant(set, id);
                    rt.Enabled[i] = g.Active;
                    moved = true;
                }
                if (!moved) continue;
                var selected = ShroudSelection.Shown(rt.Enabled);
                if (selected == rt.Selected) continue;
                rt.Selected = selected;
                ShroudLine(e.Def, ShroudLines.VariantChanged(_name, label, id, NameOf(e.Def, selected)), "variant|" + label + "|" + id);
            }
        }

        /// <summary><see cref="RetryVariants"/> found the sets: every name gate again.</summary>
        private void ReinitShroudGates()
        {
            foreach (var e in _entries)
                if (e.ShroudRt != null && !e.Disabled) InitShroudGates(e);
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>One frame of a shroud entry, active or not (an entry inactive under its B9 gate hides its shroud).
        /// Allocation-free; <c>SetActive</c> and <c>enabled</c> are written only on a change of the shown name.</summary>
        private void StepShroud(Entry e, bool inEditor)
        {
            var rt = e.ShroudRt;
            if (rt?.Objects == null) return;
            var def = e.Def;
            var attached = ShroudAttached(rt, def, inEditor);
            var active = !Inactive(e);
            if (!inEditor && active && rt.Ignited != null && !rt.IgnitionSeen && rt.Ignited())
            {
                rt.IgnitionSeen = true;
                rt.Machine.OnStaged(attached);
            }
            var index = rt.Machine.Step(inEditor, attached, active, rt.Selected);
            if (rt.Machine.TakeJettisonEvent())
                KSP1PartModPlugin.Log?.LogInfo(ShroudLines.Jettisoned(_name, NameOf(def, rt.Selected), rt.Machine.Latch.Reason));
            if (rt.Machine.HideOverride != rt.LastOverride)
            {
                rt.LastOverride = rt.Machine.HideOverride;
                KSP1PartModPlugin.Log?.LogInfo(ShroudLines.HideOverride(_name, rt.LastOverride));
            }
            rt.Objects.Apply(index);
            if (rt.LastAttached.HasValue && rt.LastAttached.Value != attached)
            {
                if (attached && index >= 0) ShroudChangeLine(def, inEditor, ShroudLines.Shown(_name, NameOf(def, index)), "shown|" + index);
                else if (!attached && index < 0) ShroudChangeLine(def, inEditor, ShroudLines.Hidden(_name), "hidden");
            }
            rt.LastAttached = attached;
        }

        /// <summary>A part is attached to the shroud's node. The node is looked up again while missing or destroyed (a dictionary
        /// or a short list: no allocation).</summary>
        private bool ShroudAttached(ShroudRuntime rt, AnimEntryDef def, bool inEditor)
        {
            if (!FindShroudNode(rt, def, inEditor)) return false;
            return inEditor ? rt.OabNode.ConnectedPart != null : rt.FlightNode.AttachedPart != null;
        }

        /// <summary>The part's stage was activated (<c>PartBehavior.ActivateAllModules</c>, through
        /// <see cref="ShroudImport"/>). Flight only; the jettison line comes with the next frame.</summary>
        internal void OnPartStaged()
        {
            if (!_ready || PartBackingMode != PartBackingModes.Flight) return;
            foreach (var e in _entries)
            {
                var rt = e.ShroudRt;
                if (rt?.Objects == null || e.Disabled || Inactive(e)) continue;
                try
                {
                    rt.Machine.OnStaged(ShroudAttached(rt, e.Def, false));
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static string NameOf(AnimEntryDef def, int index)
        {
            var names = def.TransformNames;
            if (names == null || names.Length == 0) return "";
            return index >= 0 && index < names.Length ? names[index] : names[0];
        }

        /// <summary>A shroud line (it carries its own prefix), once per part type, entry, scene and <paramref name="what"/> per
        /// load.</summary>
        private void ShroudLine(AnimEntryDef def, string line, string what = null, bool warn = false)
        {
            if (!LoggedPluginLine.First(_name + "|" + def.Key + "|" + PartBackingMode + "|shroud" + (what == null ? "" : "|" + what), TrackCache.Generation))
                return;
            if (warn) KSP1PartModPlugin.Log?.LogWarning(line);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        /// <summary>The shown / hidden lines: once per part type and state in the VAB, each time in flight.</summary>
        private void ShroudChangeLine(AnimEntryDef def, bool inEditor, string line, string what)
        {
            if (inEditor) ShroudLine(def, line, what);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }
    }
}
