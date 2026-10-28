using System.Collections.Generic;
using KSP1PartMod.Ui;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Ui
{
    public class ToggleSyncTests
    {
        /// <summary>A toggle that, like Unity's with a notifying <c>isOn</c>, calls its listener on each change; the
        /// listener is the bar's: a switch to on is a click unless the sync is writing.</summary>
        private sealed class FakeToggle : ISyncedToggle
        {
            private readonly ToggleSync _sync;
            private readonly List<int> _clicks;
            private readonly int _index;
            private bool _on;
            public int Writes;

            public FakeToggle(ToggleSync sync, List<int> clicks, int index, bool on)
            {
                _sync = sync;
                _clicks = clicks;
                _index = index;
                _on = on;
            }

            public bool IsOn
            {
                get => _on;
                set
                {
                    Writes++;
                    if (_on == value) return;
                    _on = value;
                    if (value && !_sync.Syncing) _clicks.Add(_index);
                }
            }
        }

        private static List<FakeToggle> Make(ToggleSync sync, List<int> clicks, params bool[] on)
        {
            var list = new List<FakeToggle>();
            for (var i = 0; i < on.Length; i++) list.Add(new FakeToggle(sync, clicks, i, on[i]));
            return list;
        }

        [Test] public void Apply_WritesOnlyTheTogglesThatChange()
        {
            var sync = new ToggleSync();
            var clicks = new List<int>();
            var toggles = Make(sync, clicks, true, false, false, false);
            Assert.AreEqual(2, sync.Apply(toggles, 2));
            CollectionAssert.AreEqual(new[] { 1, 0, 1, 0 }, toggles.ConvertAll(t => t.Writes));
        }

        [Test] public void Apply_LeavesExactlyOneOn()
        {
            var sync = new ToggleSync();
            var clicks = new List<int>();
            // Every button clicked in turn stayed on in 0.13.0: start from several on.
            var toggles = Make(sync, clicks, true, true, false, true);
            sync.Apply(toggles, 2);
            CollectionAssert.AreEqual(new[] { false, false, true, false }, toggles.ConvertAll(t => t.IsOn));
        }

        [Test] public void Apply_NothingToDoWritesNothing()
        {
            var sync = new ToggleSync();
            var clicks = new List<int>();
            var toggles = Make(sync, clicks, false, true, false);
            Assert.AreEqual(0, sync.Apply(toggles, 1));
            Assert.AreEqual(0, sync.Apply(toggles, 1));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, toggles.ConvertAll(t => t.Writes));
        }

        [Test] public void Apply_GuardBlocksTheClick()
        {
            var sync = new ToggleSync();
            var clicks = new List<int>();
            var toggles = Make(sync, clicks, true, false, false);
            sync.Apply(toggles, 1);
            Assert.IsTrue(toggles[1].IsOn);
            CollectionAssert.IsEmpty(clicks);
            Assert.IsFalse(sync.Syncing);
            // Outside the sync, a switch to on is a click.
            toggles[2].IsOn = true;
            CollectionAssert.AreEqual(new[] { 2 }, clicks);
        }

        [Test] public void Apply_OutOfRangeSwitchesEverythingOff()
        {
            var sync = new ToggleSync();
            var clicks = new List<int>();
            var toggles = Make(sync, clicks, true, true);
            Assert.AreEqual(2, sync.Apply(toggles, -1));
            CollectionAssert.AreEqual(new[] { false, false }, toggles.ConvertAll(t => t.IsOn));
            Assert.AreEqual(0, sync.Apply<FakeToggle>(null, 0));
        }

        [Test] public void Apply_ClearsTheGuardWhenAWriteThrows()
        {
            var sync = new ToggleSync();
            var toggles = new List<ISyncedToggle> { new Throwing() };
            Assert.Throws<System.InvalidOperationException>(() => sync.Apply(toggles, 0));
            Assert.IsFalse(sync.Syncing);
        }

        private sealed class Throwing : ISyncedToggle
        {
            public bool IsOn
            {
                get => false;
                set => throw new System.InvalidOperationException();
            }
        }
    }
}
