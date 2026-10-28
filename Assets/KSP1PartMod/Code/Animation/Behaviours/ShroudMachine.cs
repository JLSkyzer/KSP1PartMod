using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>The state of one shroud entry on a part instance (engine shrouds, 0.7.0): its jettison latch and the VAB hide
    /// override, saved under the entry key (<c>&lt;key&gt;.jettisoned</c>, <c>&lt;key&gt;.hide_override</c>). Each frame it
    /// says which name is shown (<see cref="Step"/>). No allocation after construction.</summary>
    public sealed class ShroudMachine : IEntryMachine
    {
        /// <summary>Consecutive detached steps, after an attached one, that jettison a shroud in flight.</summary>
        public const int DetachDebounce = 3;

        private readonly string _kJettisoned, _kHide;
        private readonly bool _ignoreNodes;
        private bool _seenJettisoned, _jettisonEvent;

        public ShroudMachine(AnimEntryDef def)
        {
            _kJettisoned = def.Key + ".jettisoned";
            _kHide = def.Key + ".hide_override";
            _ignoreNodes = def.IgnoreNodes;
            Latch = new ShroudLatch(def.IsFairing, def.CheckBottomNode, def.DecoupleEnabled, def.ManualJettison, def.IgnoreNodes);
        }

        public ShroudLatch Latch { get; }

        /// <summary>The player hid the shroud from the VAB (KSP1 <c>shroudHideOverride</c>).</summary>
        public bool HideOverride { get; private set; }

        public bool Changed { get; private set; }

        /// <summary>A saved jettison hides the shroud from the first step, without a jettison event.</summary>
        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (Machine.TryRead(saved, _kJettisoned, out var j)) Latch.Restore(Machine.Bool(j));
            if (Machine.TryRead(saved, _kHide, out var h)) HideOverride = Machine.Bool(h);
            _seenJettisoned = Latch.Jettisoned;
            _jettisonEvent = false;
            Changed = false;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kJettisoned] = Machine.Of(Latch.Jettisoned);
            into[_kHide] = Machine.Of(HideOverride);
            Changed = false;
        }

        /// <summary>True when the override changed.</summary>
        public bool SetHideOverride(bool on)
        {
            if (HideOverride == on) return false;
            HideOverride = on;
            Changed = true;
            return true;
        }

        /// <summary>The part's stage was activated (flight only).</summary>
        public void OnStaged(bool attached)
        {
            Latch.OnStaged(attached);
            NoteLatch();
        }

        /// <summary>One frame. The latch runs in flight while the entry is active (an inactive KSP1 module does nothing).</summary>
        /// <param name="entryActive">The entry's B9 gate is open.</param>
        /// <param name="selected">The name the variants select (<see cref="ShroudSelection.Shown"/>), -1 for none.</param>
        /// <returns>The index of the name to show, or -1 when the shroud is hidden.</returns>
        public int Step(bool inEditor, bool attached, bool entryActive, int selected)
        {
            if (entryActive && !inEditor) Latch.Step(true, attached, DetachDebounce);
            NoteLatch();
            var shownAttached = inEditor ? attached : entryActive && Latch.Debounced;
            var visible = ShroudRule.Visible(inEditor, shownAttached, _ignoreNodes, Latch.Jettisoned, HideOverride, entryActive && selected >= 0);
            return visible ? selected : -1;
        }

        /// <summary>True once, after the step (or <see cref="OnStaged"/>) in which the latch closed in this session; never for
        /// a jettison restored from the saved state.</summary>
        public bool TakeJettisonEvent()
        {
            if (!_jettisonEvent) return false;
            _jettisonEvent = false;
            return true;
        }

        private void NoteLatch()
        {
            if (!Latch.Jettisoned || _seenJettisoned) return;
            _seenJettisoned = true;
            _jettisonEvent = true;
            Changed = true;
        }
    }
}
