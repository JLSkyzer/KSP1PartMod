using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>FXModuleAnimateThrottle's engine link on a Redux part. Redux merges a part's KSP1 engines into one
    /// KSP2 engine with one mode per KSP1 <c>engineID</c> (turbojet: Dry, Wet), so KSP1's <c>engineName</c> names a
    /// mode: the clip follows the engine only while that mode is active, and reads 0, not operational, otherwise.</summary>
    public static class EngineInput
    {
        /// <param name="engineName">The entry's <c>engineName</c>; blank follows the engine whatever its mode.</param>
        /// <param name="activeModeId">The engine's current mode <c>engineID</c>.</param>
        public static (float throttle, bool operational) Read(string engineName, string activeModeId, float throttle, bool operational)
        {
            if (float.IsNaN(throttle) || float.IsInfinity(throttle)) throttle = 0f;
            if (!string.IsNullOrWhiteSpace(engineName)
                && !string.Equals(engineName.Trim(), activeModeId?.Trim(), StringComparison.OrdinalIgnoreCase))
                return (0f, false);
            return (throttle, operational);
        }

        /// <summary>A blank name matches any engine.</summary>
        public static bool HasMode(string engineName, IEnumerable<string> modeIds)
        {
            if (modeIds == null) return false;
            if (string.IsNullOrWhiteSpace(engineName)) return true;
            foreach (var id in modeIds)
                if (string.Equals(engineName.Trim(), id?.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
