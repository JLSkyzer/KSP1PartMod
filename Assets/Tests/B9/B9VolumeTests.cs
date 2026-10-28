using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.B9;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9VolumeTests
    {
        private static double? Volume(string partText, int b9, int subtype)
        {
            var modules = B9Text.Modules(partText);
            var all = modules.Where(m => m.GetValue("name") == "ModuleB9PartSwitch").ToList();
            return B9Volume.Of(all[b9], all[b9].GetNodes("SUBTYPE").ElementAt(subtype), all);
        }

        private const string Plain = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fuelSwitch
		baseVolume = 400
		SUBTYPE
		{
			name = A
		}
		SUBTYPE
		{
			name = B
			volumeAdded = 50
		}
		SUBTYPE
		{
			name = C
			volumeMultiplier = 0.5
			volumeAdded = 10
		}
	}
}";

        [Test] public void Volume_BaseVolumeAlone() => Assert.AreEqual(400, Volume(Plain, 0, 0).Value, 1e-9);

        [Test] public void Volume_VolumeAdded() => Assert.AreEqual(450, Volume(Plain, 0, 1).Value, 1e-9);

        [Test] public void Volume_VolumeMultiplier_ThenAdded() => Assert.AreEqual(210, Volume(Plain, 0, 2).Value, 1e-9);

        [Test] public void Volume_NoBaseVolume_Zero() =>
            Assert.AreEqual(0, Volume(Plain.Replace("baseVolume = 400", ""), 0, 0).Value, 1e-9);

        [Test] public void Volume_VariableBase_Null()
        {
            Assert.IsNull(Volume(Plain.Replace("400", "#$../tank_volume$"), 0, 0));
            Assert.IsNull(Volume(Plain.Replace("volumeAdded = 50", "volumeAdded = #$x$"), 0, 1));
        }

        private const string ParentChild = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fuelSwitch
		baseVolume = 400
		SUBTYPE
		{
			name = LFO
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = mountSwitch
		parentID = fuelSwitch
		SUBTYPE
		{
			name = Long
			volumeAddedToParent = 100
		}
		SUBTYPE
		{
			name = Short
			volumeAddedToParent = 65
			defaultSubtypePriority = 2
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = other
		parentID = elsewhere
		SUBTYPE
		{
			name = X
			volumeAddedToParent = 1000
		}
	}
}";

        [Test] public void Volume_Child_AddsItsDefaultOnly() => Assert.AreEqual(465, Volume(ParentChild, 0, 0).Value, 1e-9);

        [Test] public void Volume_ParentWithZeroBase_TakesTheChild() =>
            Assert.AreEqual(65, Volume(ParentChild.Replace("baseVolume = 400", "baseVolume = 0"), 0, 0).Value, 1e-9);

        [Test] public void Volume_ChildItself_HasNoTank() => Assert.AreEqual(0, Volume(ParentChild, 1, 0).Value, 1e-9);

        [Test] public void Volume_Children_ListsThePairs()
        {
            var modules = B9Text.Modules(ParentChild);
            var children = B9Volume.Children(modules[0], modules);
            Assert.AreEqual(1, children.Count);
            Assert.AreEqual("mountSwitch", children[0].GetValue("moduleID"));
            Assert.AreEqual(65, B9Volume.AddedToParent(children[0]).Value, 1e-9);
        }

        [Test] public void Volume_NullInputs_Null()
        {
            Assert.IsNull(B9Volume.Of(null, null, null));
        }
    }
}
