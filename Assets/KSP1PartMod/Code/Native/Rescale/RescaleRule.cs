using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Rescale
{
    /// <summary>KSP1's own scaling rules that Redux misses (0.16.0, plan (c), read in KSP1's <c>PartLoader</c>): a part
    /// without <c>rescaleFactor</c> is scaled by 1.25, and each attach node's position is multiplied by the last
    /// <c>scale</c> value met before it in the cfg. Pure.</summary>
    public static class RescaleRule
    {
        /// <summary><c>Part.rescaleFactor</c>'s field initializer in KSP1.</summary>
        public const float Ksp1Default = 1.25f;

        /// <summary>The <c>module</c> values of cfg entries that are not parts to rescale (kerbals, the flag).</summary>
        public static readonly IReadOnlyList<string> Excluded = new[] { "KerbalEVA", "FlagSite" };

        /// <summary>True when the cfg's <c>module</c> is one of <see cref="Excluded"/>.</summary>
        public static bool IsExcluded(IKsp1Node part)
        {
            var module = IsruCfg.Value(part, "module");
            if (module == null) return false;
            foreach (var e in Excluded)
                if (string.Equals(module, e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>1.25 when the part has no <c>rescaleFactor</c> and is not excluded; null otherwise.</summary>
        public static float? MissingDefault(IKsp1Node part)
        {
            if (part == null || part.GetValue("rescaleFactor") != null || IsExcluded(part)) return null;
            return Ksp1Default;
        }

        /// <summary>A node position value: <c>node_stack_*</c> or <c>node_attach</c>.</summary>
        public static bool IsNodeKey(string key)
        {
            var k = key?.Trim();
            return k != null && (k.StartsWith("node_stack_", StringComparison.Ordinal) || k == "node_attach");
        }

        /// <summary>For each node value, in cfg order: its key and the factor of the last <c>scale</c> value before it (1 when
        /// there is none, or when it is not a positive number).</summary>
        public static List<KeyValuePair<string, float>> NodeScale(IKsp1Node part)
        {
            var result = new List<KeyValuePair<string, float>>();
            if (part == null) return result;
            var factor = 1f;
            foreach (var v in part.AllValues)
            {
                var key = v.Key?.Trim();
                if (key == "scale")
                {
                    factor = IsruCfg.TryNumber(v.Value, out var s) && s > 0 ? (float)s : 1f;
                    continue;
                }
                if (IsNodeKey(key)) result.Add(new KeyValuePair<string, float>(key, factor));
            }
            return result;
        }

        /// <summary>True when some node of the part has a factor other than 1.</summary>
        public static bool HasNodeScale(IKsp1Node part)
        {
            foreach (var p in NodeScale(part))
                if (p.Value != 1f) return true;
            return false;
        }

        /// <summary>The factor of a <c>MODEL</c>'s <c>position</c>: the part's <c>rescaleFactor</c> (1 when not a positive
        /// finite number).</summary>
        public static float ModelPositionFactor(float rescaleFactor) =>
            rescaleFactor > 0 && !float.IsInfinity(rescaleFactor) && !float.IsNaN(rescaleFactor) ? rescaleFactor : 1f;
    }
}
