using System;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Game.Audit;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Structural nodes (0.17.0): the KSP1 <c>ModuleStructuralNode</c> roots (interstage caps and trusses) are shown
    /// only while a part is attached to one of their nodes (VAB: <c>OABPart.FindNodeWithTag(node).ConnectedPart</c>; flight:
    /// <c>part.Model.TryGetAttachment(node).AttachedPart</c>), and never while the VAB mesh toggle hides them or their B9 gate is
    /// closed. Visual only: <c>SetActive</c> on the root, written only on a change. Redux has no importer for the module, so the
    /// roots are otherwise always visible.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>One structure entry's runtime state.</summary>
        private sealed class StructureRuntime
        {
            public readonly StructureMachine Machine;
            /// <summary>Null when the root object was not found: the entry does nothing.</summary>
            public GameObject Root;
            public IObjectAssemblyPartNode[] OabNodes;
            public AttachNodeData[] FlightNodes;
            public int NodesFound;
            /// <summary>The mesh toggle of the whole part, set at build: every structure entry follows it.</summary>
            public Action<bool> ApplyAll;

            public StructureRuntime(AnimEntryDef def) => Machine = new StructureMachine(def);
        }

        // ---------------------------------------------------------------- bind

        /// <summary>After <see cref="BindVariants"/>: the root object and the nodes of every structure entry. Never throws: a
        /// failure disables the entry.</summary>
        private void ResolveStructures()
        {
            foreach (var e in _entries)
            {
                if (e.StructureRt == null || e.Disabled) continue;
                try
                {
                    ResolveStructure(e);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        private void ResolveStructure(Entry e)
        {
            var def = e.Def;
            var rt = e.StructureRt;
            var inEditor = PartBackingMode == PartBackingModes.OAB;
            var root = def.TransformNames != null && def.TransformNames.Length > 0 ? def.TransformNames[0] : "";
            var nodes = def.TargetNames ?? new string[0];
            rt.OabNodes = new IObjectAssemblyPartNode[nodes.Length];
            rt.FlightNodes = new AttachNodeData[nodes.Length];
            rt.Root = FirstNamed(root)?.gameObject;
            rt.NodesFound = 0;
            for (var i = 0; i < nodes.Length; i++)
                if (FindStructureNode(rt, nodes, i, inEditor)) rt.NodesFound++;
            StructureLine(def, StructureLines.Resolved(_name, root, rt.NodesFound, nodes.Length, rt.Root != null), "resolved");
            if (rt.Root != null) AuditProbe.Note("structure_kept");
            else AuditProbe.Note("structure_missing");
        }

        /// <summary>The node's lookup, cached. True when found.</summary>
        private bool FindStructureNode(StructureRuntime rt, string[] names, int i, bool inEditor)
        {
            if (inEditor)
            {
                var node = rt.OabNodes[i];
                if (node == null || node is UnityEngine.Object o && o == null)
                    node = rt.OabNodes[i] = OABPart?.FindNodeWithTag(names[i]);
                return node != null;
            }
            if (rt.FlightNodes[i] != null) return true;
            var model = part != null ? part.Model : null;
            if (model != null && model.TryGetAttachment(names[i], out var data)) rt.FlightNodes[i] = data;
            return rt.FlightNodes[i] != null;
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>One frame of a structure entry, active or not (an entry inactive under its B9 gate hides its root).
        /// Allocation-free; <c>SetActive</c> is written only when the root's state differs.</summary>
        private void StepStructure(Entry e, bool inEditor)
        {
            var rt = e.StructureRt;
            if (rt?.Root == null || rt.OabNodes == null) return;
            var names = e.Def.TargetNames;
            var attached = false;
            for (var i = 0; i < names.Length && !attached; i++)
            {
                if (!FindStructureNode(rt, names, i, inEditor)) continue;
                attached = inEditor ? rt.OabNodes[i].ConnectedPart != null : rt.FlightNodes[i].AttachedPart != null;
            }
            var visible = rt.Machine.Step(attached, !Inactive(e));
            if (rt.Root.activeSelf != visible) rt.Root.SetActive(visible);
            var change = rt.Machine.LastChange;
            if (change == StructureChange.None) return;
            var root = e.Def.TransformNames[0];
            var line = change == StructureChange.Shown ? StructureLines.Shown(_name, root) : StructureLines.Hidden(_name, root);
            if (inEditor) StructureLine(e.Def, line, change.ToString());
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        /// <summary>The VAB mesh toggle: every structure entry of the part follows the one that carries the button
        /// (KSP1 <c>ModuleStructuralNodeToggle.UpdateNodes</c>).</summary>
        private void SetAllStructuresHidden(bool hidden)
        {
            foreach (var other in _entries)
                if (other.StructureRt != null && !other.Disabled) other.StructureRt.Machine.SetMeshHidden(hidden);
            StructureLine(null, StructureLines.MeshToggle(_name, hidden));
        }

        /// <summary>A structure line (it carries its own prefix), once per part type, entry, scene and <paramref name="what"/> per
        /// load; a null definition logs every time.</summary>
        private void StructureLine(AnimEntryDef def, string line, string what = null)
        {
            if (def != null && !LoggedPluginLine.First(_name + "|" + def.Key + "|" + PartBackingMode + "|structure" + (what == null ? "" : "|" + what), TrackCache.Generation))
                return;
            KSP1PartModPlugin.Log?.LogInfo(line);
        }
    }
}
