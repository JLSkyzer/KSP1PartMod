using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>When a structure root is visible, after KSP1's <c>ModuleStructuralNode.CheckDisplay</c> (0.17.0).</summary>
    public static class StructureRule
    {
        /// <summary>Shown while a part is attached to at least one of its nodes, the entry is active (its B9 gate is open) and the
        /// player has not hidden the structure from the VAB toggle.</summary>
        public static bool Visible(bool attached, bool meshHidden, bool entryActive) => entryActive && !meshHidden && attached;
    }

    /// <summary>What the last <see cref="StructureMachine.Step"/> did to the visibility, for the log lines.</summary>
    public enum StructureChange { None, Shown, Hidden }

    /// <summary>The state of one structure entry: the VAB mesh toggle (KSP1 <c>showMesh</c>), saved under
    /// <c>&lt;key&gt;.mesh_hidden</c>. No allocation after construction.</summary>
    public sealed class StructureMachine : IEntryMachine
    {
        private readonly string _kHidden;
        private bool? _last;

        public StructureMachine(AnimEntryDef def) => _kHidden = def.Key + ".mesh_hidden";

        /// <summary>The player hid the structure from the VAB toggle.</summary>
        public bool MeshHidden { get; private set; }

        public bool Changed { get; private set; }

        /// <summary>What the last <see cref="Step"/> changed.</summary>
        public StructureChange LastChange { get; private set; }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (Machine.TryRead(saved, _kHidden, out var h)) MeshHidden = Machine.Bool(h);
            Changed = false;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kHidden] = Machine.Of(MeshHidden);
            Changed = false;
        }

        /// <summary>True when the toggle changed.</summary>
        public bool SetMeshHidden(bool hidden)
        {
            if (MeshHidden == hidden) return false;
            MeshHidden = hidden;
            Changed = true;
            return true;
        }

        /// <summary>One frame: whether the root is shown.</summary>
        public bool Step(bool attached, bool entryActive)
        {
            var visible = StructureRule.Visible(attached, MeshHidden, entryActive);
            LastChange = !_last.HasValue || _last.Value == visible ? StructureChange.None
                : visible ? StructureChange.Shown : StructureChange.Hidden;
            _last = visible;
            return visible;
        }
    }
}
