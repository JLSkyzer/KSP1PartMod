using System.Collections.Generic;

namespace KSP1PartMod.Ui
{
    /// <summary>One toggle of the sub-category bar, as <see cref="ToggleSync"/> sees it.</summary>
    public interface ISyncedToggle
    {
        bool IsOn { get; set; }
    }

    /// <summary>Puts the sub-category buttons in line with the selection. The stock visualizer of a toggle
    /// (<c>ToggleExtendedEventsVisualizer</c>) only redraws on <c>onValueChanged</c>, so a button switched off silently
    /// (<c>SetIsOnWithoutNotify</c>) stayed blue (0.13.0, in game). The bar now writes a notifying <c>isOn</c>, only to the
    /// toggles whose value changes, while <see cref="Syncing"/> is true: its click listener tests it and does not treat
    /// the write as a click.</summary>
    public sealed class ToggleSync
    {
        private bool _syncing;

        /// <summary>True while <see cref="Apply{T}"/> writes: a value change seen then is not a click.</summary>
        public bool Syncing => _syncing;

        /// <summary>Switches on the toggle at <paramref name="selected"/> and off every other one (none on when it is out
        /// of range), writing only those that change. Returns how many were written. No allocation.</summary>
        public int Apply<T>(IReadOnlyList<T> toggles, int selected) where T : ISyncedToggle
        {
            if (toggles == null) return 0;
            var written = 0;
            _syncing = true;
            try
            {
                // Off first, so no listener ever sees two buttons on.
                for (var i = 0; i < toggles.Count; i++)
                {
                    var t = toggles[i];
                    if (t == null || i == selected || !t.IsOn) continue;
                    t.IsOn = false;
                    written++;
                }
                if (selected >= 0 && selected < toggles.Count)
                {
                    var t = toggles[selected];
                    if (t != null && !t.IsOn)
                    {
                        t.IsOn = true;
                        written++;
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
            return written;
        }
    }
}
