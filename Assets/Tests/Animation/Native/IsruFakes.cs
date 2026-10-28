using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary>A fake resource table for the ISRU rules, written for the tests (not Redux's): Ore → CarbonRegolith at
    /// 0.05 t per KSP1 unit, LiquidFuel → Methane, XenonGas → Xenon, every other name kept. Amounts convert by mass, KSP1
    /// density over the KSP2 unit mass, and stay unchanged when either is unknown or 0 (ElectricCharge), as the plan (g)
    /// describes Redux's <c>ConvertAmount</c>.</summary>
    internal static class IsruFakes
    {
        public static readonly IReadOnlyDictionary<string, double> Ksp1Density = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Ore"] = 0.05, ["LiquidFuel"] = 0.005, ["Oxidizer"] = 0.005, ["MonoPropellant"] = 0.004, ["ElectricCharge"] = 0,
            ["XenonGas"] = 0.0001,
        };

        public static readonly IReadOnlyDictionary<string, double> Ksp2Mass = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["CarbonRegolith"] = 1, ["Water"] = 1, ["Ammonia"] = 1, ["Uranium"] = 1, ["TestRocks"] = 0.05,
            ["Methane"] = 0.0025, ["Oxidizer"] = 0.005, ["MonoPropellant"] = 0.004, ["ElectricCharge"] = 0, ["Xenon"] = 0.0001,
            ["Hydrogen"] = 0.0001,
        };

        public static readonly string[] Minable = { "Water", "Ammonia", "CarbonRegolith", "Uranium" };

        /// <param name="ore">The KSP2 resource KSP1 Ore maps to (<c>ksp1_ore_resource</c>).</param>
        /// <param name="exists">Overrides the known names; null: the names of <see cref="Ksp2Mass"/>.</param>
        public static IsruResources Table(string ore = "CarbonRegolith", Func<string, bool> exists = null) =>
            new IsruResources(
                name => name == "Ore" ? ore : name == "LiquidFuel" ? "Methane" : name == "XenonGas" ? "Xenon" : name,
                Convert,
                exists ?? (name => Ksp2Mass.ContainsKey(name)),
                Minable,
                name => Ksp2Mass.TryGetValue(name, out var m) ? m : double.NaN);

        public static double Convert(string source, string target, double amount)
        {
            if (!Ksp1Density.TryGetValue(source, out var d) || !Ksp2Mass.TryGetValue(target, out var m) || d <= 0 || m <= 0)
                return amount;
            return amount * d / m;
        }

        /// <summary>The modules named <paramref name="name"/> of the first PART of <paramref name="cfg"/>.</summary>
        public static List<IKsp1Node> Modules(string cfg, string name) =>
            CfgText.Parse(cfg).GetNodes("PART").First().GetNodes("MODULE")
                .Where(m => string.Equals(m.GetValue("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
