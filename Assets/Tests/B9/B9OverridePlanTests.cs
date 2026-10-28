using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.B9;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9OverridePlanTests
    {
        private static readonly B9TankTypes Types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox + B9Text.BdbMono));

        private static B9OverridePlan Plan(string part, B9Switches settings = null) =>
            B9OverridePlan.For(B9Text.Modules(part), Types, settings ?? B9Switches.AllOn);

        /// <summary>A stock set whose id is the one an appended B9 set would take first.</summary>
        private const string MassOnlyAfterStock = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		SUBTYPE
		{
			name = Light
		}
		SUBTYPE
		{
			name = Heavy
			addedMass = 0.25
			addedCost = 100
		}
	}
	MODULE
	{
		name = ModulePartVariants
		moduleID = B9Switch0
		VARIANT
		{
			name = White
			GAMEOBJECTS
			{
				meshWhite = true
			}
		}
	}
}";

        [Test] public void Plan_MassOnlyNonVisual_AppendedIdAfterTheStockSet()
        {
            var plan = Plan(MassOnlyAfterStock);
            Assert.AreEqual(1, plan.Switches.Count);
            var sw = plan.Switches[0];
            Assert.IsTrue(sw.Appended);
            Assert.AreEqual("B9Switch0_2", sw.SetId, "unique against the stock set's id");
            Assert.AreEqual(0, sw.ModuleIndex);
            CollectionAssert.AreEqual(new[] { "Light", "Heavy" }, sw.VariantIds);
            Assert.AreEqual("Light", sw.DefaultVariant);
            Assert.AreEqual(0, sw.Subtypes[0].MassDelta);
            Assert.AreEqual(0.25, sw.Subtypes[1].MassDelta, 1e-12);
            Assert.AreEqual(100, sw.Subtypes[1].Cost, 1e-9);
            Assert.IsNull(sw.Subtypes[1].Resources);
            Assert.IsFalse(plan.GainsPartSwitch, "the stock set already gives the part a Module_PartSwitch");
        }

        [Test] public void Plan_VariantSwitches_OverrideSetId_OnlyWhenAsked()
        {
            var modules = B9Text.Modules(MassOnlyAfterStock);
            Assert.IsNull(VariantSwitches.All(modules)[0].OverrideSetId, "0.8.0 callers see no appended id");
            var all = VariantSwitches.All(modules, sw => true);
            Assert.AreEqual("B9Switch0_2", all[0].OverrideSetId);
            Assert.IsNull(all[0].SetId);
            Assert.AreEqual("B9Switch0", all[1].SetId, "the stock set keeps its id");
            Assert.IsNull(all[1].OverrideSetId);
        }

        private const string MassOnly = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		SUBTYPE
		{
			name = Light
		}
		SUBTYPE
		{
			name = Heavy
			addedMass = 0.25
		}
	}
}";

        [Test] public void Plan_NoReduxSet_GainsPartSwitch()
        {
            var plan = Plan(MassOnly);
            Assert.AreEqual("B9Switch0", plan.Switches.Single().SetId);
            Assert.IsTrue(plan.GainsPartSwitch);
        }

        private const string Visual = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = meshSwitch
		SUBTYPE
		{
			name = A
			transform = meshA
		}
		SUBTYPE
		{
			name = B
			transform = meshB
			addedMass = 0.1
			defaultSubtypePriority = 5
		}
	}
}";

        [Test] public void Plan_VisualSwitch_KeepsReduxId()
        {
            var plan = Plan(Visual);
            var sw = plan.Switches.Single();
            Assert.IsFalse(sw.Appended);
            Assert.AreEqual("meshSwitch", sw.SetId);
            Assert.AreEqual("B", sw.DefaultVariant);
            Assert.AreEqual(0.1, sw.Subtypes[1].MassDelta, 1e-12);
            Assert.IsFalse(plan.GainsPartSwitch);
        }

        private const string VisualAndNothing = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		SUBTYPE
		{
			name = Nothing
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = meshSwitch
		SUBTYPE
		{
			name = A
			transform = meshA
		}
	}
}";

        [Test] public void Plan_VisualSwitchWithoutOverrides_Listed_NonVisualWithout_Left()
        {
            var plan = Plan(VisualAndNothing);
            Assert.AreEqual(1, plan.Switches.Count);
            Assert.AreEqual("meshSwitch", plan.Switches[0].SetId);
            Assert.AreEqual(1, plan.Switches[0].ModuleIndex);
            Assert.AreEqual(1, plan.Switches[0].B9Index);
            Assert.IsFalse(plan.Switches[0].HasOverrides);
        }

        private const string TwoTanks = @"
PART
{
	mass = 1
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fuelSwitch
		baseVolume = 100
		SUBTYPE
		{
			name = LFO
			tankType = bdbLFOX
		}
		SUBTYPE
		{
			name = Mono
			tankType = bdbMono
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = monoSwitch
		baseVolume = 10
		SUBTYPE
		{
			name = Mono
			tankType = bdbMono
			addedMass = 0.01
		}
		SUBTYPE
		{
			name = None
			tankType = Structural
		}
	}
}";

        [Test] public void Plan_TwoTankSwitches_SecondNoted()
        {
            var plan = Plan(TwoTanks);
            Assert.AreEqual(2, plan.Switches.Count);
            var first = plan.Switches[0];
            Assert.IsTrue(first.IsTank);
            Assert.AreEqual(2, first.Subtypes[0].Resources.Count);
            Assert.AreEqual(45, first.Subtypes[0].Resources[0].MaxAmount, 1e-9);
            Assert.AreEqual(0.0625, first.Subtypes[0].MassDelta, 1e-12, "dry mass");
            Assert.AreEqual(100, first.Subtypes[1].Resources[0].MaxAmount, 1e-9);
            Assert.AreEqual(50, first.Subtypes[1].Resources[0].Amount, 1e-9);
            var second = plan.Switches[1];
            Assert.IsFalse(second.IsTank);
            Assert.IsTrue(second.Subtypes.All(s => s.Resources == null));
            Assert.AreEqual(0.01, second.Subtypes[0].MassDelta, 1e-12, "addedMass only: no dry mass for a tank that is not applied");
            Assert.AreEqual(0, second.Subtypes[1].MassDelta);
            Assert.IsTrue(second.Appended);
            Assert.IsFalse(plan.Switches.Contains(null));
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.SecondTankSwitch("monoSwitch", 0, 0.005),
                "Structural 0 t, Mono 0.0005 x 10 = 0.005 t");
            Assert.IsTrue(plan.HasSecondTankSwitch);
        }

        [Test] public void Plan_NewPartSwitchesOff_DropsNonVisualSwitches()
        {
            var off = new B9Switches { Mass = true, Resources = true, ModuleData = true, ModuleActive = true, PartScalars = true, NewPartSwitches = false };
            Assert.IsEmpty(Plan(MassOnlyAfterStock, off).Switches);
            Assert.IsFalse(Plan(MassOnlyAfterStock, off).GainsPartSwitch);
            Assert.AreEqual(1, Plan(Visual, off).Switches.Count, "Redux's sets keep their overrides");
        }

        [Test] public void Plan_Switches_FromSettings()
        {
            var s = B9Switches.From(AnimationSettings.Parse("{\"b9_mass\": false, \"b9_module_data\": false}", out _));
            Assert.IsFalse(s.Mass);
            Assert.IsTrue(s.Resources);
            Assert.IsFalse(s.ModuleData);
            Assert.IsFalse(s.ModuleActive, "module off needs module data");
            Assert.IsTrue(s.PartScalars);
            Assert.IsTrue(s.NewPartSwitches);
            Assert.AreEqual(0, Plan(Visual, s).Switches[0].Subtypes[1].MassDelta);
            Assert.IsTrue(B9Switches.From(AnimationSettings.Defaults).ModuleActive);
        }

        [Test] public void Plan_ResourcesOff_DryMassStays()
        {
            var s = new B9Switches { Mass = true, Resources = false, ModuleData = true, ModuleActive = true, PartScalars = true, NewPartSwitches = true };
            var sw = Plan(TwoTanks, s).Switches[0];
            Assert.IsNull(sw.Subtypes[0].Resources);
            Assert.AreEqual(0.0625, sw.Subtypes[0].MassDelta, 1e-12);
        }

        private const string Modules = @"
PART
{
	MODULE
	{
		name = ModuleEnginesFX
		engineID = Main
		maxThrust = 100
	}
	MODULE
	{
		name = ModuleGimbal
		gimbalRange = 5
	}
	MODULE
	{
		name = ModuleResourceConverter
		ConverterName = A
	}
	MODULE
	{
		name = ModuleResourceConverter
		ConverterName = B
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = engineSwitch
		SUBTYPE
		{
			name = Vac
			maxTemp = 2500
			CoMOffset = 0, -0.5, 0
			upgradeRequired = SomeUpgrade
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleEnginesFX
					engineID = Main
				}
				DATA
				{
					maxThrust = 120
				}
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleGimbal
				}
				moduleActive = false
			}
		}
		SUBTYPE
		{
			name = Sea
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleResourceConverter
					ConverterName = A
				}
				moduleActive = false
			}
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleResourceConverter
				}
				DATA
				{
					x = 1
				}
			}
		}
	}
}";

        [Test] public void Plan_ModuleEdits_DataOffAndNone()
        {
            var plan = Plan(Modules);
            var sw = plan.Switches.Single();
            Assert.IsTrue(sw.Appended);
            Assert.AreEqual("engineSwitch", sw.SetId);
            var vac = sw.Subtypes[0];
            Assert.AreEqual(2, vac.ModuleEdits.Count);
            var engine = vac.ModuleEdits[0];
            Assert.AreEqual(0, engine.TargetIndex);
            Assert.AreEqual("ModuleEnginesFX", engine.TargetModule);
            Assert.IsFalse(engine.Off);
            Assert.AreEqual("120", engine.Edited.GetValue("maxThrust"));
            CollectionAssert.AreEqual(new[] { "maxThrust" }, engine.DataKeys);
            var gimbal = vac.ModuleEdits[1];
            Assert.AreEqual(1, gimbal.TargetIndex);
            Assert.IsTrue(gimbal.Off);
            Assert.IsFalse(gimbal.NoOffForm);
            Assert.AreEqual("0", gimbal.Edited.GetValue("gimbalRange"));

            var sea = sw.Subtypes[1];
            var converter = sea.ModuleEdits.Single();
            Assert.AreEqual(2, converter.TargetIndex);
            Assert.IsTrue(converter.Off);
            Assert.IsTrue(converter.NoOffForm);
            Assert.IsNull(converter.Edited);
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.IdentifierMatches("engineSwitch", "Sea", "ModuleResourceConverter", 2));
        }

        [Test] public void Plan_ModuleActiveOff_DataKept()
        {
            var s = new B9Switches { Mass = true, Resources = true, ModuleData = true, ModuleActive = false, PartScalars = true, NewPartSwitches = true };
            var vac = Plan(Modules, s).Switches[0].Subtypes[0];
            Assert.AreEqual(1, vac.ModuleEdits.Count, "the gimbal off form is not planned");
            Assert.AreEqual("ModuleEnginesFX", vac.ModuleEdits[0].TargetModule);
        }

        [Test] public void Plan_PartScalars_AndNotes()
        {
            var plan = Plan(Modules);
            var vac = plan.Switches[0].Subtypes[0];
            Assert.AreEqual(2, vac.Scalars.Count);
            Assert.AreEqual("maxTemp", vac.Scalars[0].Key);
            CollectionAssert.AreEqual(new[] { 2500.0 }, vac.Scalars[0].Values);
            Assert.AreEqual("CoMOffset", vac.Scalars[1].Key);
            CollectionAssert.AreEqual(new[] { 0.0, -0.5, 0.0 }, vac.Scalars[1].Values);
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.UpgradeRequiredIgnored);
            var off = new B9Switches { Mass = true, Resources = true, ModuleData = true, ModuleActive = true, PartScalars = false, NewPartSwitches = true };
            Assert.IsEmpty(Plan(Modules, off).Switches[0].Subtypes[0].Scalars);
        }

        [Test] public void Plan_UnparseableVolume_NoTankOverride_Noted()
        {
            var plan = Plan(TwoTanks.Replace("baseVolume = 100", "baseVolume = #$../tank_volume$"));
            Assert.IsFalse(plan.Switches.Any(sw => sw.SetId == "fuelSwitch"), "no override left: no set");
            Assert.IsFalse(plan.Switches.Any(sw => sw.IsTank), "the second tank switch is not promoted");
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.UnparseableVolume("fuelSwitch"));
            Assert.IsTrue(plan.HasUnparseableVolume);
        }

        [Test] public void Plan_UnknownTankType_Noted()
        {
            var plan = Plan(TwoTanks.Replace("tankType = bdbLFOX", "tankType = nope"));
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.UnknownTankType("nope"));
            Assert.IsEmpty(plan.Switches[0].Subtypes[0].Resources, "no resources from an unknown type");
        }

        [Test] public void Plan_ChildVolume_Noted()
        {
            const string part = @"
PART
{
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fuelSwitch
		baseVolume = 100
		SUBTYPE
		{
			name = LFO
			tankType = bdbLFOX
		}
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = mount
		parentID = fuelSwitch
		SUBTYPE
		{
			name = Short
			volumeAddedToParent = 20
		}
		SUBTYPE
		{
			name = Long
			volumeAddedToParent = 50
		}
	}
}";
            var plan = Plan(part);
            Assert.AreEqual(54, plan.Switches[0].Subtypes[0].Resources[0].MaxAmount, 1e-9);
            CollectionAssert.Contains(plan.Notes, B9Lines.Notes.ChildVolumeAtDefault("mount", "fuelSwitch", "Short"));
            Assert.AreEqual(1, plan.ParentChildPairs);
        }

        [Test] public void Plan_NoB9_Empty_AndNullSafe()
        {
            Assert.IsEmpty(Plan("PART\n{\nMODULE\n{\nname = ModuleGimbal\n}\n}").Switches);
            Assert.IsEmpty(B9OverridePlan.For(null, null, null).Switches);
        }

        // ---- 0.8.0 safety net: B9PartSwitch is never blocked ----

        [Test] public void SafetyNet_B9PartSwitch_NeverBlocked()
        {
            Assert.IsFalse(MmPluginRule.Blocked("B9PartSwitch", MmPluginRule.DefaultCurated, MmPluginRule.DefaultKeep, new[] { "B9PartSwitch" }),
                "kept even as an automatic DLL folder");
            Assert.IsFalse(MmPluginRule.Blocked("b9partswitch/Plugins", MmPluginRule.DefaultCurated, MmPluginRule.DefaultKeep, new[] { "B9PartSwitch" }));
            Assert.IsFalse(MmPluginRule.Blocked("B9PartSwitch", new[] { "B9PartSwitch" }, MmPluginRule.DefaultKeep, null),
                "kept even if a player lists it as a plugin folder");
            CollectionAssert.Contains(AnimationSettings.Defaults.MmNeedsKeep, "B9PartSwitch");
            Assert.IsTrue(MmPluginRule.NeedsMet("B9PartSwitch",
                n => !MmPluginRule.Blocked(n, MmPluginRule.DefaultCurated, MmPluginRule.DefaultKeep, new[] { "B9PartSwitch" })));
        }
    }
}
