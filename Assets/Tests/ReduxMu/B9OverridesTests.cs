using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.IO;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.B9;
using KSP1PartMod.Game;
using KSP1PartMod.Tests.Animation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using VSwift.Modules.Data;
using VSwift.Modules.InformationLoaders;
using VSwift.Modules.Transformers;
using VSwift.Modules.Variants;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c): the B9 override hook's targets and V-SwiFT types resolve,
    /// our transformers survive Redux's JSON and its flight loaders, and Redux's real
    /// <c>Ksp1B9PartSwitchVariantBuilder.TryBuild</c>, with our postfix armed, gives an in-memory BDB-like tank part its
    /// mass and tank transformers.</summary>
    public class B9OverridesTests
    {
        private readonly List<string> _lines = new List<string>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            B9Overrides.Sink = _lines.Add;
        }

        [TearDown] public void TearDown()
        {
            Disarm();
            B9Overrides.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(B9Overrides.HarmonyId).UnpatchAll(B9Overrides.HarmonyId);
            B9Overrides.Reset();
        }

        private bool Arm(string json = "{}") =>
            B9Overrides.TryApply(new Harmony(B9Overrides.HarmonyId), AnimationSettings.Parse(json, out _));

        /// <summary>IOProvider's settings are built by a RuntimeInitializeOnLoadMethod, which EditMode does not run:
        /// built here, and the global Newtonsoft default it sets is put back afterwards.</summary>
        private static void WithGameSettings(Action test)
        {
            var previous = JsonConvert.DefaultSettings;
            try
            {
                try { IOProvider.Init(); }
                catch (Exception e) { Assert.Ignore("IOProvider cannot initialise in EditMode: " + e.Message); }
                test();
            }
            finally { JsonConvert.DefaultSettings = previous; }
        }

        private const string TankTypes = @"
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
B9_TANK_TYPE
{
	name = bdbMono
	tankMass = 0.0005
	RESOURCE
	{
		name = MonoPropellant
		unitsPerVolume = 1
	}
}
";

        /// <summary>A BDB-like tank: a visual switch (Redux builds its set) that also carries the tank.</summary>
        private const string TankPart = @"
PART
{
	name = testTank
	rescaleFactor = 1.25
	mass = 0.5
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = fuelSwitch
		baseVolume = 100
		SUBTYPE
		{
			name = LFO
			tankType = bdbLFOX
			transform = lfoMesh
		}
		SUBTYPE
		{
			name = Mono
			title = Monopropellant
			tankType = bdbMono
			transform = monoMesh
			addedMass = 0.01
			maxTemp = 1500
			CoMOffset = 0, 0.5, 0
		}
	}
}
";

        /// <summary>A part whose only B9 switch has no visual data: Redux builds nothing.</summary>
        private const string MassOnlyPart = @"
PART
{
	name = testMass
	mass = 1
	MODULE
	{
		name = ModuleB9PartSwitch
		switcherDescription = Shielding
		SUBTYPE
		{
			name = Light
			title = Light shield
		}
		SUBTYPE
		{
			name = Heavy
			addedMass = 0.25
		}
	}
}
";

        /// <summary>A tank switch on a part whose base definition holds no resource (no tank type Redux knows).</summary>
        private const string EmptyBasePart = @"
PART
{
	name = testEmpty
	mass = 1
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = tank
		baseVolume = 10
		SUBTYPE
		{
			name = Mono
			tankType = bdbMono
		}
	}
}
";

        private sealed class Imported
        {
            public bool Result;
            public Data_PartSwitch Data;
            public List<string> Baseline;
            public Ksp1PartImportContext Context;
            public Ksp1ConfigNode Part;
        }

        /// <summary>Redux's own flow for one part, as far as the variant builder: the tank catalog from the files (our
        /// postfix reads it), the base resources as <c>BuildPartData</c> maps them, then <c>TryBuild</c>.</summary>
        private static Imported Import(string partText, string tankText = TankTypes, bool emptyBase = false)
        {
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(tankText + partText);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/test.cfg", "Test", node);
            var catalog = Ksp1B9TankCatalog.FromConfigFiles(new[] { file });
            var part = node.GetNodes("PART").First();
            var name = part.GetValue("name");
            var context = new Ksp1PartImportContext(name, new Ksp1ImportReport()) { PartNode = part };
            var resources = emptyBase ? new List<Ksp1ConfigNode>() : part.GetNodes("RESOURCE").Concat(catalog.GetResourceNodes(part, name)).ToList();
            context.PartData = new PartData
            {
                partName = name,
                resourceContainers = Ksp1ResourceMapper.MapPartResources(resources, null, context.Report, name, null),
            };
            float.TryParse(part.GetValue("rescaleFactor") ?? "1", System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var rescale);
            var result = Ksp1B9PartSwitchVariantBuilder.TryBuild(file, part, rescale, context, new string[0], out var data, out var baseline);
            return new Imported { Result = result, Data = data, Baseline = baseline, Context = context, Part = part };
        }

        private static List<string> Kinds(Variant v) => v.Transformers.Select(t => t.GetType().Name).ToList();

        // ------------------------------------------------------------------ resolution

        [Test] public void Resolve_FindsEveryHookTarget()
        {
            var t = B9Overrides.Resolve();
            CollectionAssert.IsEmpty(t.Missing);
            Assert.IsNotNull(t.TryBuild);
            Assert.IsNotNull(t.AddLocalizationTerms);
            Assert.IsNotNull(t.FromConfigFiles);
            Assert.IsNotNull(t.CatalogTypes, "Redux's catalog dictionary (built-in tank types)");
            var ps = t.TryBuild.GetParameters();
            CollectionAssert.AreEqual(new[] { "file", "partNode", "rescaleFactor", "context", "ignoredTransforms", "data", "baselineInactiveTransforms" },
                ps.Select(p => p.Name).ToArray(), "the postfix takes them by index");
            Assert.IsTrue(ps[5].IsOut && ps[6].IsOut);
            CollectionAssert.AreEqual(new[] { "partNode", "partName", "resolve", "add" },
                t.AddLocalizationTerms.GetParameters().Select(p => p.Name).ToArray());
        }

        [Test] public void Resolve_TransformerFields()
        {
            Assert.AreEqual(typeof(float), typeof(MassModifier).GetField("Modifier").FieldType);
            Assert.AreEqual(typeof(List<string>), typeof(ResourceContainerRemover).GetField("Containers").FieldType);
            Assert.AreEqual(typeof(List<ContainedResourceDefinition>), typeof(ResourceContainerAdder).GetField("Containers").FieldType);
            Assert.AreEqual(typeof(string), typeof(PartScalarTransformer).GetField("Key").FieldType);
            Assert.AreEqual(typeof(JToken), typeof(PartScalarTransformer).GetField("Value").FieldType);
            foreach (var f in new[] { "BehaviourType", "DataType", "Key", "Value" })
                Assert.IsNotNull(typeof(ModuleDefinitionTransformer).GetField(f), "ModuleDefinitionTransformer." + f + " (Task 3)");
            foreach (var f in new[] { "maxTemp", "skinMaxTemp", "crashTolerance" })
                Assert.AreEqual(typeof(double), typeof(PartData).GetField(f).FieldType, "PartData." + f);
            Assert.AreEqual(typeof(Vector3), typeof(PartData).GetField("coMassOffset").FieldType, "B9 CoMOffset -> PartData.coMassOffset");
            Assert.IsNotNull(typeof(Ksp1ResourceMapper).GetMethod("MapPartResources", new[]
            {
                typeof(IEnumerable<Ksp1ConfigNode>), typeof(ResourceDefinitionDatabase), typeof(Ksp1ImportReport), typeof(string),
                typeof(Ksp1ResourceMapper.Ksp2ResourceMassLookup),
            }));
        }

        [Test] public void LocalizationKeys_MatchReduxsPrivateMethods()
        {
            var t = typeof(Ksp1B9PartSwitchVariantBuilder);
            var setKey = AccessTools.Method(t, "GetVariantSetLocalizationKey", new[] { typeof(string), typeof(string) });
            var variantKey = AccessTools.Method(t, "GetVariantLocalizationKey", new[] { typeof(string), typeof(string), typeof(string) });
            Assert.IsNotNull(setKey);
            Assert.IsNotNull(variantKey);
            Assert.AreEqual(setKey.Invoke(null, new object[] { "p.a", "s 1" }), B9Overrides.SetKey("p.a", "s 1"));
            Assert.AreEqual(variantKey.Invoke(null, new object[] { "p.a", "s 1", "v_2" }), B9Overrides.VariantKey("p.a", "s 1", "v_2"));
        }

        // ------------------------------------------------------------------ JSON and flight loaders

        private static Data_PartSwitch OneOfEach() => new Data_PartSwitch
        {
            VariantSets =
            {
                new VariantSet
                {
                    VariantSetId = "fuelSwitch",
                    Variants =
                    {
                        new Variant
                        {
                            VariantId = "Mono",
                            Transformers =
                            {
                                new MassModifier { Modifier = 0.06f },
                                new ResourceContainerRemover { Containers = new List<string> { "Methalox" } },
                                new ResourceContainerAdder { Containers = new List<ContainedResourceDefinition> { new ContainedResourceDefinition(100, "MonoPropellant") { initialUnits = 50 } } },
                                B9Overrides.ScalarTransformer(new B9PartScalar { Key = "maxTemp", Values = new[] { 1500.0 } }, 1f),
                                B9Overrides.ScalarTransformer(new B9PartScalar { Key = "CoMOffset", Values = new[] { 0, 0.5, -1.0 } }, 1.25f),
                            },
                        },
                    },
                },
            },
            DefaultActiveVariants = { "Mono" },
            ActiveVariants = { "Mono" },
        };

        [Test] public void Json_RoundTrip_ThroughIOProvider_KeepsTypesAndValues() => WithGameSettings(() =>
        {
            var back = IOProvider.FromJson<Data_PartSwitch>(IOProvider.ToJson(OneOfEach()));
            var ts = back.VariantSets.Single().Variants.Single().Transformers;
            CollectionAssert.AreEqual(new[] { "MassModifier", "ResourceContainerRemover", "ResourceContainerAdder", "PartScalarTransformer", "PartScalarTransformer" },
                ts.Select(t => t.GetType().Name).ToArray());
            Assert.AreEqual(0.06f, ((MassModifier)ts[0]).Modifier);
            CollectionAssert.AreEqual(new[] { "Methalox" }, ((ResourceContainerRemover)ts[1]).Containers);
            var c = ((ResourceContainerAdder)ts[2]).Containers.Single();
            Assert.AreEqual(("MonoPropellant", 100.0, 50.0), (c.name, c.capacityUnits, c.initialUnits));
            var maxTemp = (PartScalarTransformer)ts[3];
            Assert.AreEqual("maxTemp", maxTemp.Key);
            Assert.AreEqual(1500.0, maxTemp.Value.Value<double>());
            var com = (PartScalarTransformer)ts[4];
            Assert.AreEqual("coMassOffset", com.Key);
            Assert.AreEqual(0.625, com.Value["y"].Value<double>(), 1e-6, "CoMOffset x the part's rescale factor");
        });

        /// <summary>What <c>LoadVariantPartData</c> does in flight: each saved value through its loader into a copy of
        /// the part definition.</summary>
        [Test] public void FlightLoaders_ApplyOurSavedInformation() => WithGameSettings(() =>
        {
            var partData = new PartData
            {
                maxTemp = 2000,
                resourceContainers = new List<ContainedResourceDefinition> { new ContainedResourceDefinition(0.5, "Methalox") },
            };
            var info = OneOfEach().GetStoredVariantInformation();
            var saved = info["0"].OrderBy(kv => int.Parse(kv.Key)).Select(kv => kv.Value).ToList();
            Assert.AreEqual(4, saved.Count, "MassModifier saves nothing (its value is the module's state)");
            foreach (var (type, value) in saved)
            {
                var loader = (IInformationLoader)Activator.CreateInstance(Type.GetType(type, true));
                loader.LoadInformationInto(partData, value);
            }
            Assert.AreEqual(1500.0, partData.maxTemp);
            Assert.AreEqual(new Vector3(0, 0.625f, -1.25f), partData.coMassOffset);
            Assert.AreEqual("MonoPropellant", partData.resourceContainers.Single().name);
            Assert.AreEqual(100, partData.resourceContainers.Single().capacityUnits);
        });

        [Test] public void Validate_AcceptsOurs_RejectsBadOnes() => WithGameSettings(() =>
        {
            foreach (var t in OneOfEach().VariantSets[0].Variants[0].Transformers) Assert.DoesNotThrow(() => B9Overrides.Validate(t), t.GetType().Name);
            Assert.Throws<InvalidOperationException>(() => B9Overrides.Validate(new PartScalarTransformer { Key = "noSuchField", Value = 1.0 }));
            Assert.Throws<InvalidOperationException>(() => B9Overrides.Validate(new ResourceContainerRemover()));
            Assert.Throws<InvalidOperationException>(() => B9Overrides.Validate(new ResourceContainerAdder()));
            Assert.Throws<InvalidOperationException>(() => B9Overrides.Validate(new ResourceContainerAdder
            {
                Containers = new List<ContainedResourceDefinition> { new ContainedResourceDefinition(0, "MonoPropellant") },
            }));
            Assert.Throws<InvalidOperationException>(() => B9Overrides.Validate(new MassModifier { Modifier = float.NaN }));
        });

        // ------------------------------------------------------------------ arming

        [Test] public void Arm_LogsTheArmedLine_ModuleDataOffUntilTask3()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.Contains(_lines, B9Lines.Armed(true, true, false, false, true, true));
            Assert.IsTrue(B9Overrides.IsArmed);
        }

        [Test] public void Arm_EveryAppliedSwitchOff_NothingArmed()
        {
            Assert.IsFalse(Arm("{\"b9_mass\": false, \"b9_resources\": false, \"b9_part_scalars\": false}"));
            CollectionAssert.AreEqual(new[] { B9Lines.AllOff }, _lines);
            var plain = Import(MassOnlyPart);
            Assert.IsFalse(plain.Result, "Redux as in 0.8.0");
            Assert.IsNull(plain.Data);
        }

        [Test] public void TankTypes_ReadFromTheFiles_WithReduxBuiltIns()
        {
            Assert.IsTrue(Arm());
            Import(TankPart);
            var types = B9Overrides.TankTypes;
            Assert.IsNotNull(types);
            Assert.AreEqual(0.000625, types.Find("bdbLFOX").TankMass);
            var lfo = types.Find("LFO");
            Assert.IsNotNull(lfo, "Redux's built-in");
            Assert.IsTrue(lfo.BuiltIn);
            Assert.IsNull(lfo.TankMass);
            Assert.AreEqual(100, lfo.Resources.First().PercentFilled, "Redux's fraction 1 read as 100 %");
        }

        // ------------------------------------------------------------------ Redux's real builder

        [Test] public void TryBuild_BdbLikeTank_ReduxSetGetsMassAndTankTransformers_AfterRedux()
        {
            Assert.IsTrue(Arm());
            var r = Import(TankPart);
            Assert.IsTrue(r.Result);
            var set = r.Data.VariantSets.Single();
            Assert.AreEqual("fuelSwitch", set.VariantSetId);
            var lfo = set.Variants[0];
            var mono = set.Variants[1];
            CollectionAssert.AreEqual(new[] { "TransformActivator", "MassModifier", "ResourceContainerRemover", "ResourceContainerAdder" }, Kinds(lfo));
            CollectionAssert.AreEqual(new[] { "TransformActivator", "MassModifier", "ResourceContainerRemover", "ResourceContainerAdder",
                "PartScalarTransformer", "PartScalarTransformer" }, Kinds(mono));

            Assert.AreEqual(0.0625f, lfo.Transformers.OfType<MassModifier>().Single().Modifier, 1e-6f, "0.000625 t x 100");
            Assert.AreEqual(0.06f, mono.Transformers.OfType<MassModifier>().Single().Modifier, 1e-6f, "0.01 + 0.0005 x 100");
            var baseNames = r.Context.PartData.resourceContainers.Select(c => c.name).ToList();
            CollectionAssert.AreEqual(new[] { "Methalox" }, baseNames, "Redux's catalog base: LiquidFuel + Oxidizer as Methalox");
            CollectionAssert.AreEqual(baseNames, lfo.Transformers.OfType<ResourceContainerRemover>().Single().Containers);
            var lfoAdd = lfo.Transformers.OfType<ResourceContainerAdder>().Single().Containers.Single();
            var reduxBase = r.Context.PartData.resourceContainers.Single();
            Assert.AreEqual(("Methalox", reduxBase.capacityUnits, reduxBase.initialUnits), (lfoAdd.name, lfoAdd.capacityUnits, lfoAdd.initialUnits),
                "the default subtype gives back exactly Redux's base");
            var monoAdd = mono.Transformers.OfType<ResourceContainerAdder>().Single().Containers.Single();
            Assert.AreEqual(("MonoPropellant", 100.0, 100.0), (monoAdd.name, monoAdd.capacityUnits, monoAdd.initialUnits));
            var scalars = mono.Transformers.OfType<PartScalarTransformer>().ToList();
            Assert.AreEqual(("maxTemp", 1500.0), (scalars[0].Key, scalars[0].Value.Value<double>()));
            Assert.AreEqual("coMassOffset", scalars[1].Key);
            Assert.AreEqual(0.625, scalars[1].Value["y"].Value<double>(), 1e-6, "0.5 x rescaleFactor 1.25");

            CollectionAssert.Contains(_lines, B9Lines.SetSummary("testTank", "fuelSwitch", 2, 0.06f, 0.0625f, new[] { "Methalox", "MonoPropellant" },
                new[] { "maxTemp", "CoMOffset" }));
            Assert.IsFalse(_lines.Any(l => l.Contains("failed") || l.Contains("dropped")), string.Join("\n", _lines));
            var tally = B9Overrides.TallyOf("testTank");
            Assert.AreEqual((1, 0, 2, 2, 1), (tally.Sets, tally.NewSets, tally.MassVariants, tally.ResourceVariants, tally.ScalarVariants));
        }

        [Test] public void TryBuild_NonVisualPart_GetsItsOwnSet_ResultTrue_Labels()
        {
            Assert.IsTrue(Arm());
            var r = Import(MassOnlyPart);
            Assert.IsTrue(r.Result, "Redux's CreateSerialized then adds Module_PartSwitch");
            Assert.IsNotNull(r.Baseline);
            var set = r.Data.VariantSets.Single();
            Assert.AreEqual("Shielding", set.VariantSetId);
            Assert.IsFalse(set.IsPopout);
            Assert.AreEqual(B9Overrides.SetKey("testMass", "Shielding"), set.VariantSetLocalizationKey);
            CollectionAssert.AreEqual(new[] { "Light", "Heavy" }, set.Variants.Select(v => v.VariantId).ToArray());
            Assert.AreEqual(B9Overrides.VariantKey("testMass", "Shielding", "Heavy"), set.Variants[1].VariantLocalizationKey);
            CollectionAssert.IsEmpty(set.Variants[0].Transformers, "a variant without override stays a choice");
            Assert.AreEqual(0.25f, set.Variants[1].Transformers.OfType<MassModifier>().Single().Modifier);
            CollectionAssert.AreEqual(new[] { "Light" }, r.Data.DefaultActiveVariants);
            CollectionAssert.AreEqual(new[] { "Light" }, r.Data.ActiveVariants);
            CollectionAssert.Contains(_lines, B9Lines.NewSet("testMass", "Shielding", "B9 #0", 2, "Light"));
            Assert.AreEqual(1, B9Overrides.TallyOf("testMass").NewSetsWithModule);

            var terms = new Dictionary<string, string>();
            Ksp1B9PartSwitchVariantBuilder.AddLocalizationTerms(r.Part, "testMass", s => "[" + s + "]", (k, v) => terms[k] = v);
            Assert.AreEqual("[Shielding]", terms[B9Overrides.SetKey("testMass", "Shielding")]);
            Assert.AreEqual("[Light shield]", terms[B9Overrides.VariantKey("testMass", "Shielding", "Light")]);
            Assert.AreEqual("[Heavy]", terms[B9Overrides.VariantKey("testMass", "Shielding", "Heavy")], "no title: the id, as Redux");

            B9Overrides.LogImportSummaryOnce();
            B9Overrides.LogImportSummaryOnce();
            Assert.AreEqual(1, _lines.Count(l => l.Contains(" part(s), ") && l.Contains("cost computed")));
            CollectionAssert.Contains(_lines, B9Lines.ImportSummary(1, 1, 1, 1, 1, 0, 0, 0));
        }

        [Test] public void TryBuild_NewPartSwitchesOff_ReduxAsIn080()
        {
            Assert.IsTrue(Arm("{\"b9_new_part_switches\": false}"));
            var r = Import(MassOnlyPart);
            Assert.IsFalse(r.Result);
            Assert.IsNull(r.Data);
        }

        [Test] public void TryBuild_EmptyBase_AddsResourceCapacities_NoRemover()
        {
            Assert.IsTrue(Arm());
            var r = Import(EmptyBasePart, emptyBase: true);
            Assert.IsTrue(r.Result);
            var mono = r.Data.VariantSets.Single().Variants.Single();
            CollectionAssert.AreEqual(new[] { "MassModifier", "ResourceContainerAdder" }, Kinds(mono));
            Assert.AreEqual(1, r.Context.PartData.serializedPartModules.Count(m => m.BehaviourType == typeof(Module_ResourceCapacities)));
            Assert.AreEqual(1, r.Context.ModuleData.OfType<Data_ResourceCapacities>().Count());
            CollectionAssert.Contains(_lines, B9Lines.PartNote("testEmpty", B9Lines.Notes.ResourceCapacitiesAdded));
        }

        [Test] public void TryBuild_ThrowInsideThePlan_LeavesReduxsResultUnchanged_StopsAfterThree()
        {
            var reduxTank = Import(TankPart);
            var reduxKinds = reduxTank.Data.VariantSets.Select(s => s.Variants.Select(Kinds).ToList()).ToList();

            Assert.IsTrue(Arm());
            B9Overrides.AfterPlanForTests = _ => throw new InvalidOperationException("boom");
            var tank = Import(TankPart);
            Assert.IsTrue(tank.Result);
            Assert.AreEqual(reduxKinds, tank.Data.VariantSets.Select(s => s.Variants.Select(Kinds).ToList()).ToList());
            CollectionAssert.AreEqual(new[] { "lfoMesh", "monoMesh" }, tank.Baseline);

            var mass = Import(MassOnlyPart);
            Assert.IsFalse(mass.Result);
            Assert.IsNull(mass.Data);
            Assert.IsTrue(_lines.Any(l => l.StartsWith(B9Lines.Prefix + "testMass: failed, Redux's variants kept: System.InvalidOperationException: boom")));

            Import(EmptyBasePart);
            CollectionAssert.Contains(_lines, B9Lines.Stopped);
            Assert.IsFalse(B9Overrides.IsArmed);
            B9Overrides.AfterPlanForTests = null;
            var after = Import(MassOnlyPart);
            Assert.IsFalse(after.Result, "stopped for the session");
        }

        /// <summary>The installed BDB Titan Transtage (raw cfg, before ModuleManager): its <c>engineSwitch</c> carries the
        /// tank and has no visual data, and its two texture switches get their TEXTURE nodes from patches only, so Redux
        /// builds no set at all: the part gains its own <c>Module_PartSwitch</c>.</summary>
        [Test] public void TryBuild_RealBdbTranstage_AppendedTankSet()
        {
            var root = InstalledKsp1.Root;
            var tanks = System.IO.Path.Combine(root, "Bluedog_DB/Compatibility/B9PartSwitch/B9TankTypes.cfg");
            var partPath = System.IO.Path.Combine(root, "Bluedog_DB/Parts/Titan/bluedog_Titan_Transtage.cfg");
            if (!System.IO.File.Exists(tanks) || !System.IO.File.Exists(partPath)) Assert.Ignore("BDB files not installed at " + root);
            Assert.IsTrue(Arm());

            var modRoot = new Ksp1ModRoot(root, root);
            var tankFile = new Ksp1ConfigFile(modRoot, tanks, "Bluedog_DB", Ksp1ConfigNode.ParseFile(tanks));
            var partFile = new Ksp1ConfigFile(modRoot, partPath, "Bluedog_DB", Ksp1ConfigNode.ParseFile(partPath));
            var catalog = Ksp1B9TankCatalog.FromConfigFiles(new[] { tankFile, partFile });
            var part = partFile.Node.GetNodes("PART").First();
            var name = part.GetValue("name");
            var context = new Ksp1PartImportContext(name, new Ksp1ImportReport()) { PartNode = part };
            context.PartData = new PartData
            {
                partName = name,
                resourceContainers = Ksp1ResourceMapper.MapPartResources(part.GetNodes("RESOURCE").Concat(catalog.GetResourceNodes(part, name)),
                    null, context.Report, name, null),
            };
            var result = Ksp1B9PartSwitchVariantBuilder.TryBuild(partFile, part, 1f, context, new string[0], out var data, out _);

            TestContext.WriteLine(string.Join(Environment.NewLine, _lines));
            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "engineSwitch" }, data.VariantSets.Select(v => v.VariantSetId).ToArray());
            Assert.AreEqual(1, B9Overrides.TallyOf(name).NewSetsWithModule, "Redux built nothing: a new Module_PartSwitch");
            var set = data.VariantSets[0];
            CollectionAssert.AreEqual(new[] { "Transtage", "Transtage-34D" }, set.Variants.Select(v => v.VariantId).ToArray());
            Assert.AreEqual("Transtage-34D", data.DefaultActiveVariants[0], "highest defaultSubtypePriority");
            foreach (var v in set.Variants)
                CollectionAssert.AreEqual(new[] { "MassModifier", "ResourceContainerRemover", "ResourceContainerAdder" }, Kinds(v), v.VariantId);
            Assert.AreEqual(0.25f, set.Variants[0].Transformers.OfType<MassModifier>().Single().Modifier, 1e-6f, "0.000625 x 400");
            Assert.AreEqual(0.038125f, set.Variants[1].Transformers.OfType<MassModifier>().Single().Modifier, 1e-6f, "-0.24 + 0.000625 x 445");
            var baseNames = context.PartData.resourceContainers.Select(c => c.name).OrderBy(n => n).ToList();
            CollectionAssert.AreEquivalent(baseNames, set.Variants[0].Transformers.OfType<ResourceContainerRemover>().Single().Containers);
            Methalox(set.Variants[0], out var meth400);
            Methalox(set.Variants[1], out var meth445);
            Assert.AreEqual(445.0 / 400.0, meth445 / meth400, 1e-9, "volumeAdded 45 on the 34D");
            foreach (var v in set.Variants)
                CollectionAssert.AreEquivalent(baseNames, v.Transformers.OfType<ResourceContainerAdder>().Single().Containers.Select(c => c.name),
                    "the part's own ElectricCharge and MonoPropellant come back with the tank");
            Assert.IsFalse(_lines.Any(l => l.Contains("failed") || l.Contains("dropped")), string.Join(Environment.NewLine, _lines));
        }

        private static void Methalox(Variant v, out double capacity) =>
            capacity = v.Transformers.OfType<ResourceContainerAdder>().Single().Containers.Single(c => c.name == "Methalox").capacityUnits;

        [Test] public void TryBuild_ReduxSetMissing_PartFails_ReduxKept()
        {
            Assert.IsTrue(Arm());
            // The plan expects Redux's set 'fuelSwitch'; an ignored transform list that drops every visual makes Redux skip it.
            var root = new Ksp1ModRoot("C:/import", "C:/import/GameData");
            var node = Ksp1ConfigNode.Parse(TankTypes + TankPart);
            var file = new Ksp1ConfigFile(root, "C:/import/GameData/Test/Parts/test.cfg", "Test", node);
            Ksp1B9TankCatalog.FromConfigFiles(new[] { file });
            var part = node.GetNodes("PART").First();
            var context = new Ksp1PartImportContext("testTank", new Ksp1ImportReport()) { PartNode = part, PartData = new PartData() };
            var result = Ksp1B9PartSwitchVariantBuilder.TryBuild(file, part, 1f, context, new[] { "lfoMesh", "monoMesh" }, out var data, out _);
            Assert.IsFalse(result);
            Assert.IsNull(data);
            Assert.IsTrue(_lines.Any(l => l.StartsWith(B9Lines.Prefix + "testTank: failed, Redux's variants kept:") && l.Contains("'fuelSwitch' not found")),
                string.Join("\n", _lines));
        }
    }
}
