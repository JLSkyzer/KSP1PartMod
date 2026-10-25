using System.Collections.Generic;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    /// <summary>Builds a machine's saved values the way the codec hands them back.</summary>
    internal static class SavedValues
    {
        public static Dictionary<string, float> Of(string key, params (string suffix, float value)[] values)
        {
            var d = new Dictionary<string, float>();
            foreach (var (suffix, value) in values) d[key + "." + suffix] = value;
            return d;
        }

        public static float B(bool b) => b ? 1f : 0f;
    }
}
