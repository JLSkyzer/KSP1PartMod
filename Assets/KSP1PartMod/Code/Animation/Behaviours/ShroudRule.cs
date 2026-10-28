namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Which of an entry's shroud names is shown: the first one its variants enable (KSP1 uses the first
    /// <c>jettisonName</c> whose GameObject is active).</summary>
    public static class ShroudSelection
    {
        /// <summary>The index of the first true value, or -1 when none (or no array).</summary>
        public static int Shown(bool[] namesEnabled)
        {
            if (namesEnabled == null) return -1;
            for (var i = 0; i < namesEnabled.Length; i++)
                if (namesEnabled[i]) return i;
            return -1;
        }
    }

    /// <summary>Why a shroud was jettisoned. <see cref="Restored"/>: it was already jettisoned in the saved state.</summary>
    public enum ShroudReason { None, BottomDetached, Staged, Restored }

    /// <summary>When a shroud is visible, after KSP1's <c>ModuleJettison</c> (engine shrouds, 0.7.0). All inputs are plain
    /// booleans the glue reads; nothing allocates.</summary>
    public static class ShroudRule
    {
        /// <summary>Shown while a part is attached to the bottom node (or <c>ignoreNodes</c>), the entry is active (its B9
        /// gate is open and a variant enables a name) and the player has not hidden it. In flight a jettisoned shroud is gone
        /// too; in the editor nothing is ever jettisoned, so a saved jettison does not hide it.</summary>
        public static bool Visible(bool inEditor, bool attached, bool ignoreNodes, bool jettisoned, bool hideOverride, bool entryActive)
        {
            if (!entryActive || hideOverride) return false;
            if (!inEditor && jettisoned) return false;
            return attached || ignoreNodes;
        }
    }

    /// <summary>The one-way jettison of a shroud in flight. It latches in two ways, as KSP1 does:
    /// <list type="bullet">
    /// <item>the bottom node goes from attached to detached (the part below is gone) when <c>isFairing</c> or
    /// <c>decoupleEnabled</c>, never for <c>ignoreNodes</c> (KSP1 <c>DecoupleAction</c> returns first); it needs an attached state seen in flight and
    /// then a number of consecutive detached steps (a transient clear, or a load that starts detached, never latches);</item>
    /// <item>the part's stage activates (<see cref="OnStaged"/>) when <c>!isFairing</c>, unless <c>manualJettison</c> or
    /// <c>checkBottomNode</c> with a part attached.</item>
    /// </list>The editor never latches. No allocation.</summary>
    public sealed class ShroudLatch
    {
        private readonly bool _isFairing, _checkBottomNode, _decoupleEnabled, _manualJettison, _ignoreNodes;
        private bool _seenAttached;
        private int _detachedSteps;

        public ShroudLatch(bool isFairing, bool checkBottomNode, bool decoupleEnabled, bool manualJettison, bool ignoreNodes)
        {
            _isFairing = isFairing;
            _checkBottomNode = checkBottomNode;
            _decoupleEnabled = decoupleEnabled;
            _manualJettison = manualJettison;
            _ignoreNodes = ignoreNodes;
        }

        /// <summary>The shroud is jettisoned.</summary>
        public bool Jettisoned { get; private set; }

        /// <summary>Why, the first time. <see cref="ShroudReason.None"/> until then.</summary>
        public ShroudReason Reason { get; private set; }

        /// <summary>A jettison read from the saved state: the shroud stays gone from the first step.</summary>
        public void Restore(bool jettisoned)
        {
            if (!jettisoned || Jettisoned) return;
            Jettisoned = true;
            Reason = ShroudReason.Restored;
        }

        /// <summary>One frame. Returns <see cref="Jettisoned"/> after it. In the editor nothing is tracked.</summary>
        /// <param name="framesDetachedThreshold">Consecutive detached steps, after an attached one, that latch (3).</param>
        public bool Step(bool inFlight, bool attached, int framesDetachedThreshold)
        {
            if (Jettisoned || !inFlight) return Jettisoned;
            if (attached)
            {
                _seenAttached = true;
                _detachedSteps = 0;
                return false;
            }
            if (!_seenAttached) return false;
            if (++_detachedSteps >= framesDetachedThreshold && LatchesOnDetach()) Latch(ShroudReason.BottomDetached);
            return Jettisoned;
        }

        /// <summary>The part's stage was activated (flight only). Returns <see cref="Jettisoned"/> after it.</summary>
        public bool OnStaged(bool attached)
        {
            if (Jettisoned) return true;
            if (!_isFairing && !_manualJettison && !(_checkBottomNode && attached)) Latch(ShroudReason.Staged);
            return Jettisoned;
        }

        // KSP1's DecoupleAction returns on ignoreNodes before it jettisons: such a shroud goes only on staging.
        private bool LatchesOnDetach() => !_ignoreNodes && (_isFairing || _decoupleEnabled);

        private void Latch(ShroudReason reason)
        {
            Jettisoned = true;
            Reason = reason;
        }
    }
}
