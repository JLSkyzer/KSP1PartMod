using System;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>KSP1 <c>defaultActionGroup</c> values to the KSP2 action group names our module binds.</summary>
    public static class ActionGroupMap
    {
        /// <summary>"Lights", "Gear", "Brakes", "Abort", "Custom01".."Custom10", or null for anything else (None,
        /// REPLACEWITHDEFAULT, Stage, RCS, SAS, unknown). Case is ignored, as KSP1's enum parse does.</summary>
        public static string Map(string ksp1Value)
        {
            var v = ksp1Value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v.Equals("Light", StringComparison.OrdinalIgnoreCase)) return "Lights";
            if (v.Equals("Gear", StringComparison.OrdinalIgnoreCase)) return "Gear";
            if (v.Equals("Brakes", StringComparison.OrdinalIgnoreCase)) return "Brakes";
            if (v.Equals("Abort", StringComparison.OrdinalIgnoreCase)) return "Abort";
            for (var i = 1; i <= 10; i++)
            {
                var custom = "Custom" + i.ToString("00");
                if (v.Equals(custom, StringComparison.OrdinalIgnoreCase)) return custom;
            }
            return null;
        }
    }
}
