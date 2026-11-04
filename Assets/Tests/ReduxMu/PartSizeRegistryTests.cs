using System;
using System.Linq;
using KSP.OAB;
using KSP1PartMod.Native.Sizes;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>0.16.0: <see cref="PartSizeRule"/>'s table is KSP2's <c>PartSizeRegistry</c> (keys, diameters, order), and every
    /// key round-trips through <see cref="PartSizeRule.CategoryOf"/> and <c>PartSizeRegistry.GetPartSizeKey(null, category)</c>.</summary>
    public class PartSizeRegistryTests
    {
        [Test] public void Table_IsTheGamesRegistry()
        {
            var game = PartSizeRegistry.Definitions;
            Assert.AreEqual(game.Count, PartSizeRule.Registry.Count);
            for (var i = 0; i < game.Count; i++)
            {
                Assert.AreEqual(game[i].Key, PartSizeRule.Registry[i].Key, "order");
                Assert.AreEqual(game[i].Diameter, PartSizeRule.Registry[i].Diameter, 1e-6f, game[i].Key);
            }
        }

        [Test] public void EveryKey_RoundTripsThroughItsCategory()
        {
            foreach (var key in PartSizeRegistry.Definitions.Select(d => d.Key))
            {
                var name = PartSizeRule.CategoryOf(key);
                Assert.IsNotNull(name, key);
                var category = (MetaAssemblySizeFilterType)Enum.Parse(typeof(MetaAssemblySizeFilterType), name);
                Assert.AreEqual(key, PartSizeRegistry.GetPartSizeKey(null, category), key);
            }
        }

        [Test] public void Closest_IsTheGamesSizeForDiameter_OnTheRegistryDiameters()
        {
            foreach (var d in PartSizeRegistry.Definitions)
                Assert.AreEqual(d.Key, PartSizeRule.Closest(d.Diameter));
        }
    }
}
