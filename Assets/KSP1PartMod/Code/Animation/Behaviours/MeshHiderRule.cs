namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>Bluedog_DB's <c>ModuleBDBDeployableMeshHider</c> (notes §3.11): flight only, the objects are
    /// hidden while the deployable is retracted.</summary>
    public static class MeshHiderRule
    {
        public static bool Hidden(bool inFlight, bool retracted) => inFlight && retracted;
    }

    /// <summary>The mesh hider's objects, as <see cref="MeshHiderState"/> sees them (the glue wraps its GameObjects).</summary>
    public interface IHiderObjects
    {
        int Count { get; }
        bool ActiveSelf(int i);
        void SetActive(int i, bool active);
    }

    /// <summary>What the mesh hider does with its objects' <c>SetActive</c>, per object: it is called only on a change of
    /// <see cref="MeshHiderRule.Hidden"/>. On hiding, each object's <c>activeSelf</c> is recorded and only the active ones
    /// are set inactive; on showing (extension, <see cref="Restore"/> at shutdown, on disable or when a B9 gate closes the
    /// entry), only the objects we set inactive are set active again. An object some other code hid (a B9 variant for
    /// instance) is never shown by us. No allocation after construction.</summary>
    public sealed class MeshHiderState
    {
        private readonly bool[] _hidByUs;

        public MeshHiderState(int count) => _hidByUs = new bool[count > 0 ? count : 0];

        /// <summary>We are hiding the objects (some of them may have been inactive already).</summary>
        public bool Hiding { get; private set; }

        /// <summary>Whether object <paramref name="i"/> was set inactive by us and not shown since.</summary>
        public bool HidByUs(int i) => i >= 0 && i < _hidByUs.Length && _hidByUs[i];

        /// <summary>One frame: hides or shows on a change of <see cref="MeshHiderRule.Hidden"/>. True when it did.</summary>
        public bool Step(bool inFlight, bool retracted, IHiderObjects objects)
        {
            var hidden = MeshHiderRule.Hidden(inFlight, retracted);
            if (hidden == Hiding) return false;
            if (hidden) Hide(objects);
            else Show(objects);
            return true;
        }

        /// <summary>Shows again what we hid. True when we were hiding.</summary>
        public bool Restore(IHiderObjects objects)
        {
            if (!Hiding) return false;
            Show(objects);
            return true;
        }

        private void Hide(IHiderObjects objects)
        {
            Hiding = true;
            var n = Count(objects);
            for (var i = 0; i < n; i++)
            {
                _hidByUs[i] = objects.ActiveSelf(i);
                if (_hidByUs[i]) objects.SetActive(i, false);
            }
        }

        private void Show(IHiderObjects objects)
        {
            Hiding = false;
            var n = Count(objects);
            for (var i = 0; i < n; i++)
            {
                if (!_hidByUs[i]) continue;
                _hidByUs[i] = false;
                objects.SetActive(i, true);
            }
        }

        private int Count(IHiderObjects objects)
        {
            if (objects == null) return 0;
            return objects.Count < _hidByUs.Length ? objects.Count : _hidByUs.Length;
        }
    }
}
