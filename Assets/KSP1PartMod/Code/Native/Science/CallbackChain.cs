using System;

namespace KSP1PartMod.Native.Science
{
    /// <summary>Puts our work in front of a game callback without ever starving it: the inner callback runs once, whatever our
    /// work does. An exception of ours goes to <c>onError</c> (or propagates, after the inner callback, when there is none).
    /// Pure.</summary>
    public static class CallbackChain
    {
        public static Action Wrap(Action before, Action inner, Action<Exception> onError = null) => () =>
        {
            try
            {
                before?.Invoke();
            }
            catch (Exception e) when (onError != null)
            {
                onError(e);
            }
            finally
            {
                inner?.Invoke();
            }
        };
    }
}
