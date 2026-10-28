using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Tests.Animation;

namespace KSP1PartMod.Tests.B9
{
    /// <summary>Hand-written cfg text for the B9 tests.</summary>
    internal static class B9Text
    {
        /// <summary>The top-level nodes of a cfg text.</summary>
        public static List<IKsp1Node> Roots(string text) => CfgText.Parse(text).AllNodes.ToList();

        /// <summary>The MODULE nodes of the first PART of a cfg text.</summary>
        public static List<IKsp1Node> Modules(string partText) =>
            CfgText.Parse(partText).GetNodes("PART").First().GetNodes("MODULE").ToList();

        /// <summary>The first SUBTYPE of a MODULE body.</summary>
        public static IKsp1Node Subtype(string body) => CfgText.Module(body).GetNodes("SUBTYPE").First();

        /// <summary>BDB's LFOX tank type, as written in Bluedog_DB's B9 compatibility file (numbers only).</summary>
        public const string BdbLfox = @"
B9_TANK_TYPE
{
	name = bdbLFOX
	tankMass = 0.000625
	tankCost = 0.7
	RESOURCE
	{
		name = LiquidFuel
		unitsPerVolume = 0.45
	}
	RESOURCE
	{
		name = Oxidizer
		unitsPerVolume = 0.55
	}
}
";

        public const string BdbMono = @"
B9_TANK_TYPE
{
	name = bdbMono
	tankMass = 0.0005
	tankCost = 0.3
	percentFilled = 50
	RESOURCE
	{
		name = MonoPropellant
		unitsPerVolume = 1
	}
}
";
    }
}
