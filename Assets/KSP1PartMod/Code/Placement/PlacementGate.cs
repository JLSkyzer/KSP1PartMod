using System;

namespace KSP1PartMod.Placement
{
    /// <summary>Calls its action at most once. Unity's <c>StartCoroutine</c> runs a coroutine up to its first
    /// <c>yield</c> at once and returns null when it ended there: a placement that was ready at once already called
    /// <c>done()</c>, and the caller must not take the null for a failure and call it again (log of 2026-10-25: the step
    /// "completed" twice and "could not start the placement coroutine").</summary>
    public sealed class OnceAction
    {
        private readonly Action _inner;

        public OnceAction(Action inner) => _inner = inner;

        public bool Called { get; private set; }

        public void Invoke()
        {
            if (Called) return;
            Called = true;
            _inner?.Invoke();
        }
    }

    /// <summary>The placement's session state. It is registered once per session, and it runs early in the campaign
    /// load (right after the game's "Loading Science Data", before the VAB builds its part list from the tech tree);
    /// the later <c>Loaded</c> action then only places when the early step could not.</summary>
    public sealed class PlacementGate
    {
        private bool _registered;
        private bool _early;

        /// <summary>True the first time only.</summary>
        public bool TryRegister()
        {
            if (_registered) return false;
            _registered = true;
            return true;
        }

        public void PlacedEarly() => _early = true;

        /// <summary>True once after <see cref="PlacedEarly"/>.</summary>
        public bool ConsumeEarly()
        {
            var e = _early;
            _early = false;
            return e;
        }

        public void Reset() => _early = false;
    }
}
