using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.B9;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    /// <summary>B9 overrides, Task 4: the 2b B9 gates follow the sets appended for the switches Redux makes no set of, and
    /// no entry key moves.</summary>
    public class B9GateFollowTests
    {
        private static readonly B9TankTypes Types = B9TankTypes.From(B9Text.Roots(B9Text.BdbLfox + B9Text.BdbMono));

        /// <summary>A non-visual switch (no Redux set): its <c>Heavy</c> subtype adds mass and turns the deployable engine
        /// off.</summary>
        private const string NonVisualGate = @"
PART
{
	MODULE
	{
		name = ModuleDeployableEngine
		EngineAnimationName = Nozzle
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = nozzleSwitch
		SUBTYPE
		{
			name = Light
		}
		SUBTYPE
		{
			name = Heavy
			addedMass = 0.1
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleDeployableEngine
				}
				moduleActive = false
			}
		}
	}
}";

        /// <summary>The same switch with visual data: Redux makes its set.</summary>
        private const string VisualGate = @"
PART
{
	MODULE
	{
		name = ModuleDeployableEngine
		EngineAnimationName = Nozzle
	}
	MODULE
	{
		name = ModuleB9PartSwitch
		moduleID = nozzleSwitch
		SUBTYPE
		{
			name = Light
			transform = light
		}
		SUBTYPE
		{
			name = Heavy
			transform = heavy
			addedMass = 0.1
			MODULE
			{
				IDENTIFIER
				{
					name = ModuleDeployableEngine
				}
				moduleActive = false
			}
		}
	}
}";

        private static PartAnimationPlan Planned(IReadOnlyList<IKsp1Node> modules, Func<VariantSwitch, bool> appendSet,
            bool own = false, bool follow = false, bool shrouds = false) =>
            OwnershipPlanner.Plan(modules, false, new HashSet<string>(), own, follow, null, true, true, shrouds, false, null, appendSet);

        private static AnimEntryDef Engine(PartAnimationPlan plan) => plan.Entries.Single(e => e.Kind == EntryKind.EngineDeploy);

        [Test] public void Gate_NonVisualSwitch_CarriesTheAppendedSetId()
        {
            var modules = B9Text.Modules(NonVisualGate);
            var b9 = B9OverridePlan.For(modules, Types, B9Switches.AllOn);
            var sw = b9.Switches.Single();
            Assert.IsTrue(sw.Appended);
            Assert.AreEqual("nozzleSwitch", sw.SetId);

            var gate = Engine(Planned(modules, b9.AppendSetTest())).Gates.Single();
            Assert.AreEqual(sw.SetId, gate.SetId, "the gate names the appended set");
            CollectionAssert.AreEqual(new[] { "Heavy" }, gate.InactiveIn);
            Assert.AreEqual("Light", gate.DefaultSubtype);
            CollectionAssert.AreEqual(new[] { "Light", "Heavy" }, gate.SubtypeIds);
        }

        [Test] public void Gate_NewPartSwitchesOff_The080GateIsUnchanged()
        {
            var modules = B9Text.Modules(NonVisualGate);
            var b9 = B9OverridePlan.For(modules, Types, new B9Switches { NewPartSwitches = false });
            Assert.IsNull(b9.AppendSetTest(), "no appended set, no test");
            var off = Engine(Planned(modules, b9.AppendSetTest())).Gates.Single();
            var before = Engine(Planned(modules, null)).Gates.Single();
            Assert.IsNull(off.SetId, "0.8.0: the gate stays at its default subtype");
            Assert.IsNull(before.SetId);
            CollectionAssert.AreEqual(before.InactiveIn, off.InactiveIn);
            Assert.AreEqual(before.DefaultSubtype, off.DefaultSubtype);
            CollectionAssert.AreEqual(before.SubtypeIds, off.SubtypeIds);
        }

        [Test] public void Gate_ReduxSet_Unchanged()
        {
            var modules = B9Text.Modules(VisualGate);
            var b9 = B9OverridePlan.For(modules, Types, B9Switches.AllOn);
            Assert.IsFalse(b9.Switches.Single().Appended);
            Assert.IsNull(b9.AppendSetTest());
            Assert.AreEqual("nozzleSwitch", Engine(Planned(modules, null)).Gates.Single().SetId);
            Assert.AreEqual("nozzleSwitch", Engine(Planned(modules, sw => true)).Gates.Single().SetId,
                "a Redux set is never replaced by an appended one");
        }

        [Test] public void Gate_SecondPassOfThePlanner_SameIds()
        {
            // B9GatePlanner.Apply runs twice with the plugin modules (before and after DropSharedClips): same result.
            var modules = B9Text.Modules(NonVisualGate);
            var test = B9OverridePlan.For(modules, Types, B9Switches.AllOn).AppendSetTest();
            var entries = Planned(modules, test).Entries.ToList();
            B9GatePlanner.Apply(modules, entries, new HashSet<int>(), test);
            Assert.AreEqual("nozzleSwitch", entries.Single(e => e.Kind == EntryKind.EngineDeploy).Gates.Single().SetId);
        }

        // ---- every installed part ----

        private static List<(string Name, List<IKsp1Node> Modules)> _parts;
        private static B9TankTypes _types;

        private static List<(string Name, List<IKsp1Node> Modules)> RealParts()
        {
            if (_parts != null) return _parts;
            var parts = new List<(string, List<IKsp1Node>)>();
            var roots = new List<IKsp1Node>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                var root = CfgText.Parse(File.ReadAllText(path));
                roots.AddRange(root.AllNodes);
                foreach (var part in root.GetNodes("PART"))
                    parts.Add((part.GetValue("name") ?? "", part.GetNodes("MODULE").ToList()));
            }
            Assume.That(parts.Count, Is.GreaterThan(0), "no PART node found");
            _types = B9TankTypes.From(roots);
            return _parts = parts;
        }

        private static bool SameGate(B9Gate a, B9Gate b) =>
            a.InactiveIn.SequenceEqual(b.InactiveIn) && a.SubtypeIds.SequenceEqual(b.SubtypeIds) && a.DefaultSubtype == b.DefaultSubtype;

        /// <summary>Every installed part, planned with and without the appended sets, under every planner switch: the same
        /// keys, each on the same module, the same gates (only a null set id may become the appended one, and only the one
        /// the B9 plan appends for that switch), and the same shroud name gates.</summary>
        [Test] public void Plan_AppendedSets_KeepEveryKeyAndGate_AllRealParts()
        {
            int partsWithAppended = 0, partsFollowing = 0, gatesFollowing = 0, gatesWithoutSet = 0;
            foreach (var (name, modules) in RealParts())
            {
                if (!modules.Any(m => "ModuleB9PartSwitch".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                var b9 = B9OverridePlan.For(modules, _types, B9Switches.AllOn);
                var test = b9.AppendSetTest();
                // 0.8.0 gates without a set (default switches): each must follow an appended set now.
                gatesWithoutSet += Planned(modules, null).Entries.Where(e => e.Gates != null).SelectMany(e => e.Gates).Count(g => g.SetId == null);
                if (test == null) continue;
                partsWithAppended++;
                var appendedIds = new HashSet<string>(b9.Switches.Where(s => s.Appended).Select(s => s.SetId), StringComparer.Ordinal);
                var following = false;
                foreach (var own in new[] { false, true })
                foreach (var follow in new[] { false, true })
                foreach (var shrouds in new[] { false, true })
                {
                    var without = Planned(modules, null, own, follow, shrouds);
                    var with = Planned(modules, test, own, follow, shrouds);
                    CollectionAssert.AreEqual(without.Entries.Select(e => e.Key), with.Entries.Select(e => e.Key), $"{name}: keys");
                    for (var i = 0; i < without.Entries.Count; i++)
                    {
                        var a = without.Entries[i];
                        var b = with.Entries[i];
                        Assert.AreEqual(a.ModuleIndex, b.ModuleIndex, $"{name}: {a.Key} moved");
                        Assert.AreEqual(a.Gates?.Length ?? 0, b.Gates?.Length ?? 0, $"{name}: {a.Key} gates");
                        for (var g = 0; g < (a.Gates?.Length ?? 0); g++)
                        {
                            Assert.IsTrue(SameGate(a.Gates[g], b.Gates[g]), $"{name}: {a.Key} gate {g}");
                            if (a.Gates[g].SetId != null) Assert.AreEqual(a.Gates[g].SetId, b.Gates[g].SetId, $"{name}: {a.Key} Redux set id");
                            else if (b.Gates[g].SetId != null)
                            {
                                Assert.IsTrue(appendedIds.Contains(b.Gates[g].SetId), $"{name}: {a.Key} names '{b.Gates[g].SetId}', not an appended set");
                                following = true;
                                if (!own && !follow && !shrouds) gatesFollowing++;
                            }
                        }
                        Assert.AreEqual(a.NameGates?.Length ?? 0, b.NameGates?.Length ?? 0, $"{name}: {a.Key} shroud gates");
                    }
                    CollectionAssert.AreEqual(without.Notes.Where(n => !n.StartsWith("B9 ", StringComparison.Ordinal)),
                        with.Notes.Where(n => !n.StartsWith("B9 ", StringComparison.Ordinal)), $"{name}: notes");
                }
                if (following) partsFollowing++;
            }
            TestContext.WriteLine($"{partsWithAppended} installed part(s) with an appended set; {partsFollowing} with gates that now follow it " +
                                  $"({gatesFollowing} gate(s), default switches); 0.8.0 gates without a set: {gatesWithoutSet}");
            Assert.Greater(partsWithAppended, 0);
            Assert.AreEqual(gatesWithoutSet, gatesFollowing, "every gate without a Redux set follows an appended set");
        }

        /// <summary>The shroud variant gates (0.7.0) read Redux's sets only: an appended set changes none of them.</summary>
        [Test] public void ShroudVariantPlanner_Unchanged_AllRealParts()
        {
            var compared = 0;
            foreach (var (name, modules) in RealParts())
            {
                var shrouds = modules.Select((m, i) => ShroudParser.Parse(m, i, 0)).Where(e => e != null).ToList();
                if (shrouds.Count == 0) continue;
                var b9 = B9OverridePlan.For(modules, _types, B9Switches.AllOn);
                var without = Planned(modules, null, shrouds: true);
                var with = Planned(modules, b9.AppendSetTest(), shrouds: true);
                foreach (var (a, b) in without.Entries.Zip(with.Entries, (a, b) => (a, b)).Where(p => p.a.Kind == EntryKind.Shroud))
                {
                    compared++;
                    Assert.AreEqual(Describe(a.NameGates), Describe(b.NameGates), $"{name}: {a.Key}");
                    Assert.AreEqual(Describe(ShroudVariantPlanner.Gates(modules, a.TransformNames)), Describe(b.NameGates), $"{name}: {a.Key} planner");
                }
            }
            TestContext.WriteLine($"{compared} shroud entr(ies) compared");
        }

        private static string Describe(B9Gate[][] gates) => gates == null
            ? "null"
            : string.Join(" | ", gates.Select(gs => gs == null ? "-" : string.Join(",", gs.Select(g =>
                $"{g.SetId}:{string.Join("/", g.InactiveIn ?? new string[0])}:{g.DefaultSubtype}"))));
    }
}
