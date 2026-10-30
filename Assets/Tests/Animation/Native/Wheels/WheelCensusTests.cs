using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Mu;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native.Wheels
{
    /// <summary>Every KSP1 wheel module of the real files maps without an exception (raw <c>PART</c> nodes, ModuleManager patches
    /// not applied), with the importers' one-wheel-per-part rule and every wheel switch on (plan (d), (f)). Resources:
    /// <see cref="IsruFakes"/> with every name known, as Redux imports the KSP1 resources it does not map.</summary>
    public class WheelCensusTests
    {
        private sealed class Row
        {
            public int Parts;
            public readonly WheelTally Tally = new WheelTally();
            public readonly SortedDictionary<string, int> Friction = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> Timing = new SortedDictionary<string, int>(StringComparer.Ordinal);

            public override string ToString()
            {
                var t = Tally;
                string Join(IEnumerable<(string, int)> l) { var x = l.Select(p => p.Item1 + " " + p.Item2).ToList(); return x.Count == 0 ? "none" : string.Join(", ", x); }
                return $"{Parts} part(s); converted " + string.Join(", ", Enum.GetValues(typeof(WheelKind)).Cast<WheelKind>()
                           .Select(k => WheelMapping.Label(k) + " " + t.Converted(k))) +
                       $"; types LEG {t.Bases(WheelTypeName.LEG)}, FREE {t.Bases(WheelTypeName.FREE)}, MOTORIZED {t.Bases(WheelTypeName.MOTORIZED)}" +
                       $"; refused {Join(t.RefusedByReason())}; friction {Join(Friction.Select(p => (p.Key, p.Value)))}" +
                       $"; TsubSys -> deploy/retract {Join(Timing.Select(p => (p.Key, p.Value)))}" +
                       $"; not reproduced {Join(WheelTally.ByCount(t.NotReproduced))}; unused by Redux's solver {Join(WheelTally.ByCount(t.UnusedBySolver))}";
            }
        }

        private sealed class Census
        {
            public readonly SortedDictionary<string, Row> PerMod = new SortedDictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            public readonly Row Total = new Row();
            public readonly List<string> Failures = new List<string>();
            public readonly List<(string Mod, string Cfg, string Name, IKsp1Node Part, Converted Values)> WheelParts =
                new List<(string, string, string, IKsp1Node, Converted)>();
            /// <summary>B9 subtype edits of wheel modules: (part, kind, field, state).</summary>
            public readonly List<(string Part, WheelKind Kind, string Field, bool State)> B9Edits = new List<(string, WheelKind, string, bool)>();
        }

        /// <summary>The values of one part's converted modules.</summary>
        private sealed class Converted
        {
            public WheelBaseValues Base;
            public WheelSuspensionValues Suspension;
            public WheelSteeringValues Steering;
            public WheelDamageValues Damage;
            public WheelBogeyValues Bogey;
        }

        private static string Inv(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        private static Census Run(string root, IEnumerable<string> files)
        {
            var r = IsruFakes.Table(exists: n => true);
            var c = new Census();
            foreach (var path in files)
            {
                IKsp1Node file;
                try { file = CfgText.Parse(File.ReadAllText(path)); }
                catch (Exception) { continue; }
                var mod = InstalledKsp1.TopFolderUnder(root, path);
                foreach (var part in file.GetNodes("PART"))
                {
                    var name = part.GetValue("name")?.Trim() ?? "?";
                    try { Count(c, mod, path, name, part, r); }
                    catch (Exception e) { c.Failures.Add($"{mod}/{name}: {e}"); }
                }
            }
            return c;
        }

        private static void Count(Census c, string mod, string cfg, string name, IKsp1Node part, IsruResources r)
        {
            var modules = part.GetNodes("MODULE").ToList();
            var kinds = modules.Select(m => WheelMapping.TryKind(IsruCfg.Value(m, "name"), out var k) ? (WheelKind?)k : null).ToList();
            CountB9(c, name, modules);
            if (kinds.All(k => k == null)) return;
            if (!c.PerMod.TryGetValue(mod, out var row)) c.PerMod[mod] = row = new Row();
            var rows = new[] { row, c.Total };
            foreach (var x in rows) x.Parts++;

            var deployment = modules.FirstOrDefault(m => IsruCfg.IsModule(m, WheelMapping.DeploymentModule));
            var firstBase = kinds.FindIndex(k => k == WheelKind.Base);
            var summary = new WheelPartSummary(name);
            var values = new Converted();
            for (var i = 0; i < modules.Count; i++)
            {
                if (kinds[i] == null) continue;
                var kind = kinds[i].Value;
                WheelRefusal? refusal = kind == WheelKind.Base
                    ? WheelPartRule.RefuseBase(true, i, firstBase)
                    : WheelPartRule.RefuseSub(true, summary.BaseConverted, false, summary.HasSlot(kind));
                if (refusal == null)
                {
                    var (converted, notRep, unused, type) = Map(kind, modules[i], deployment, r, values);
                    foreach (var x in rows) x.Tally.AddNotes(notRep, unused);
                    if (converted)
                    {
                        summary.Convert(kind);
                        foreach (var x in rows)
                        {
                            if (kind == WheelKind.Base) x.Tally.AddBase(type);
                            else x.Tally.AddConverted(kind);
                        }
                        continue;
                    }
                    refusal = WheelRefusal.Mapping;
                }
                summary.Refuse(kind, refusal.Value);
                foreach (var x in rows) x.Tally.AddRefused(refusal.Value);
            }
            if (values.Base != null)
            {
                var b = values.Base;
                var friction = $"{Inv(b.FrictionPeak)}/{Inv(b.FrictionLimit)} {(b.AutoFriction ? "auto" : "manual")}";
                var timing = deployment == null ? "fixed" : $"{IsruCfg.Value(deployment, "TsubSys") ?? "none"} -> {Inv(b.DeploymentSubsystemNormalized)}/{Inv(b.RetractionSubsystemNormalized)}";
                foreach (var x in rows)
                {
                    x.Friction[friction] = x.Friction.TryGetValue(friction, out var n) ? n + 1 : 1;
                    x.Timing[timing] = x.Timing.TryGetValue(timing, out var m) ? m + 1 : 1;
                }
            }
            c.WheelParts.Add((mod, cfg, name, part, values));
        }

        private static (bool, IReadOnlyList<string>, IReadOnlyList<string>, WheelTypeName) Map(WheelKind kind, IKsp1Node node, IKsp1Node deployment,
            IsruResources r, Converted values)
        {
            switch (kind)
            {
                case WheelKind.Base:
                {
                    var m = WheelMapping.Base(node, deployment, true, true);
                    if (m.Converted) values.Base = m.Values;
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, m.Values.WheelType);
                }
                case WheelKind.Suspension:
                {
                    var m = WheelMapping.Suspension(node);
                    if (m.Converted) values.Suspension = m.Values;
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.Steering:
                {
                    var m = WheelMapping.Steering(node);
                    if (m.Converted) values.Steering = m.Values;
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.Motor:
                {
                    var m = WheelMapping.Motor(node, r);
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.MotorSteering:
                {
                    var m = WheelMapping.MotorSteering(node, r);
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.Brakes:
                {
                    var m = WheelMapping.Brakes(node);
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.Damage:
                {
                    var m = WheelMapping.Damage(node);
                    if (m.Converted) values.Damage = m.Values;
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                case WheelKind.Lock:
                {
                    var m = WheelMapping.Lock(node);
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
                default:
                {
                    var m = WheelMapping.Bogey(node);
                    if (m.Converted) values.Bogey = m.Values;
                    return (m.Converted, m.NotReproduced, m.UnusedBySolver, default);
                }
            }
        }

        /// <summary>B9PartSwitch subtypes whose <c>MODULE</c> edits a wheel module: one entry per DATA field.</summary>
        private static void CountB9(Census c, string part, IReadOnlyList<IKsp1Node> modules)
        {
            foreach (var b9 in modules.Where(m => IsruCfg.IsModule(m, "ModuleB9PartSwitch")))
            foreach (var subtype in b9.GetNodes("SUBTYPE"))
            foreach (var edit in subtype.GetNodes("MODULE"))
            {
                var id = edit.GetNodes("IDENTIFIER").FirstOrDefault();
                if (!WheelMapping.TryKind(IsruCfg.Value(id, "name"), out var kind)) continue;
                foreach (var data in edit.GetNodes("DATA"))
                foreach (var field in KSP1PartMod.B9.B9ModuleEdit.Keys(data))
                    c.B9Edits.Add((part, kind, field, WheelMapping.IsStateField(kind, field)));
            }
        }

        private static void Print(string title, Census c)
        {
            TestContext.WriteLine($"== {title}: {c.Total}");
            foreach (var kv in c.PerMod) TestContext.WriteLine($"  {kv.Key}: {kv.Value}");
            TestContext.WriteLine("  " + WheelLines.Summary(c.Total.Tally));
            TestContext.WriteLine("  " + WheelLines.B9(c.B9Edits.Count, c.B9Edits.Where(e => e.State).Select(e => (e.Part, e.Field)).ToList()));
            foreach (var e in c.B9Edits)
                TestContext.WriteLine($"    B9 edit: {e.Part}: {WheelMapping.Ksp1Module(e.Kind)}.{e.Field} ({(e.State ? "state" : "definition")})");
        }

        private static Census _installed;

        private static Census Installed()
        {
            if (_installed != null) return _installed;
            var c = Run(InstalledKsp1.Root, InstalledKsp1.AllCfg());
            Assume.That(c.Total.Parts, Is.GreaterThan(0), "no installed wheel part");
            return _installed = c;
        }

        [Test] public void Installed_EveryWheelModuleMaps_ExpectedCounts()
        {
            var c = Installed();
            Print("installed mods/KSP1", c);
            CollectionAssert.IsEmpty(c.Failures, string.Join("\n", c.Failures));
            var t = c.Total.Tally;
            Assert.AreEqual(36, c.Total.Parts, "plan (d): 36 parts with a wheel module");
            Assert.AreEqual(36, t.Converted(WheelKind.Base));
            Assert.AreEqual(23, t.Bases(WheelTypeName.LEG));
            Assert.AreEqual(7, t.Bases(WheelTypeName.FREE));
            Assert.AreEqual(6, t.Bases(WheelTypeName.MOTORIZED));
            Assert.AreEqual(36, t.Converted(WheelKind.Suspension));
            Assert.AreEqual(9, t.Converted(WheelKind.Steering));
            Assert.AreEqual(5, t.Converted(WheelKind.Motor));
            Assert.AreEqual(1, t.Converted(WheelKind.MotorSteering));
            Assert.AreEqual(12, t.Converted(WheelKind.Brakes));
            Assert.AreEqual(35, t.Converted(WheelKind.Damage));
            Assert.AreEqual(23, t.Converted(WheelKind.Lock));
            Assert.AreEqual(12, t.Converted(WheelKind.Bogey));
            Assert.AreEqual(0, t.RefusedTotal, "no refusal");
            Assert.AreEqual(13, c.PerMod["Squad"].Parts);
            Assert.AreEqual(20, c.PerMod["KerbalReusabilityExpansion"].Parts);
            Assert.AreEqual(2, c.PerMod["Bluedog_DB"].Parts);
            Assert.AreEqual(1, c.PerMod["SquadExpansion"].Parts);
        }

        /// <summary>Plan (d): one B9 subtype edits a wheel module, and it is a state field (<c>frictionMultiplier</c> of
        /// <c>bluedog_LRV_Wheel</c> "Tire"), which the 0.9.0 module-data route does not carry.</summary>
        [Test] public void Installed_B9EditsOfWheelModules_OneStateField()
        {
            var c = Installed();
            Print("installed B9", c);
            Assert.AreEqual(1, c.B9Edits.Count);
            var e = c.B9Edits.Single();
            Assert.AreEqual(("bluedog_LRV_Wheel", WheelKind.Base, "frictionMultiplier", true), (e.Part, e.Kind, e.Field, e.State));
        }

        private static ModelPathResolver _resolver;

        private static IEnumerable<MuNode> All(MuNode n) => new[] { n }.Concat(n.Children.SelectMany(All));

        /// <summary>What the check below found on 2026-10-28, reported to the planner and left as is (no workaround in this plan):
        /// <list type="bullet">
        /// <item>7 KRE legs name a <c>clipObject</c> their model does not have. KSP2 looks it up by name among the part's
        /// transforms and keeps none when it is absent (V, <c>Module_WheelBase</c>): no error, no clip collider;</item>
        /// <item>4 collider hosts exist but carry no <c>.mu</c> WheelCollider entry. KSP2 adds its own collider to the host from
        /// <c>Data_WheelBase</c> (plan (b)): the transform is what matters.</item>
        /// </list>
        /// Every other name exists. A new finding fails the test.</summary>
        private static readonly string[] KnownFindings =
        {
            "Bluedog_DB/bluedog_LRV_Wheel: collider host 'Wheel_COLLIDER' has no WheelCollider entry",
            "Bluedog_DB/bluedog_X15_NoseGear: collider host 'Wheel_Collider' has no WheelCollider entry",
            "KerbalReusabilityExpansion/KRE-HexLegsL: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/KRE-HexLegsM: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/KRE-ShepardLeg-M: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/KRE-ShepardLeg-S: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/SpaceXLegs-XL: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/SpaceXLegs-L: clipObject 'piston_collider' not in the model",
            "KerbalReusabilityExpansion/SpaceXLegs-S: clipObject 'piston_collider' not in the model",
            "Squad/landingLeg1: collider host 'wheelCollider' has no WheelCollider entry",
            "Squad/landingLeg1-2: collider host 'wheelCollider' has no WheelCollider entry",
        };

        /// <summary>Every transform name the KSP2 modules look up exists, exactly, in the part's <c>.mu</c>; the collider host carries
        /// a WheelCollider entry. KSP2 finds them by a recursive search by name (plan (b)). The exceptions are
        /// <see cref="KnownFindings"/>.</summary>
        [Test] public void Installed_TransformNames_ExistInTheModel()
        {
            var c = Installed();
            var root = InstalledKsp1.Root;
            _resolver = _resolver ?? new ModelPathResolver(root, () => DirectoryIndex.Build(root));
            var missing = new List<string>();
            foreach (var (mod, cfg, name, part, v) in c.WheelParts)
            {
                var snapshot = PartSnapshot.From(name, cfg, root, part,
                    OwnershipPlanner.Plan(part.GetNodes("MODULE").ToList(), false, new HashSet<string>(), false));
                var nodes = new List<MuNode>();
                for (var i = 0; i < snapshot.ModelRefs.Length; i++)
                {
                    var path = _resolver.Resolve(cfg, snapshot.ModelRefs[i], snapshot.RefIsMesh[i], out var tried);
                    if (path == null) { missing.Add($"{mod}/{name}: model {snapshot.ModelRefs[i]} not found ({tried})"); continue; }
                    nodes.AddRange(All(MuReader.Read(path).Root));
                }
                var names = new HashSet<string>(nodes.Select(n => n.Name), StringComparer.Ordinal);
                var wanted = new List<(string Field, string Value)>();
                if (v.Base != null)
                {
                    wanted.Add(("wheelColliderTransformName", v.Base.WheelColliderTransformName));
                    wanted.Add(("wheelTransformName", v.Base.WheelTransformName));
                    wanted.Add(("clipObject", v.Base.ClipObject));
                }
                if (v.Suspension != null) wanted.Add(("suspensionTransformName", v.Suspension.SuspensionTransformName));
                if (v.Steering != null) wanted.Add(("caliperTransformName", v.Steering.CaliperTransformName));
                if (v.Bogey != null) wanted.Add(("bogeyTransformName", v.Bogey.BogeyTransformName));
                if (v.Damage != null)
                {
                    wanted.Add(("damagedTransformName", v.Damage.DamagedTransformName));
                    wanted.Add(("undamagedTransformName", v.Damage.UndamagedTransformName));
                }
                foreach (var (field, value) in wanted.Where(w => !string.IsNullOrEmpty(w.Value)))
                    if (!names.Contains(value))
                    {
                        var near = names.FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
                        missing.Add($"{mod}/{name}: {field} '{value}' not in the model{(near == null ? "" : $" (case differs: '{near}')")}");
                    }
                if (v.Base == null) continue;
                var hosts = nodes.Where(n => n.Name == v.Base.WheelColliderTransformName).ToList();
                var wheel = hosts.Select(h => h.WheelCollider).FirstOrDefault(w => w != null);
                if (hosts.Count > 0 && wheel == null)
                    missing.Add($"{mod}/{name}: collider host '{v.Base.WheelColliderTransformName}' has no WheelCollider entry");
                TestContext.WriteLine($"{mod}/{name}: host '{v.Base.WheelColliderTransformName}' ({hosts.Count} node(s)), radius cfg {Inv(v.Base.Radius)}, " +
                                      $".mu {(wheel == null ? "none" : Inv(wheel.Radius))}; suspension cfg " +
                                      $"{(v.Suspension == null ? "none" : Inv(v.Suspension.SuspensionDistance))}, .mu {(wheel == null ? "none" : Inv(wheel.SuspensionDistance))}");
            }
            CollectionAssert.AreEquivalent(KnownFindings, missing, string.Join("\n", missing));
        }

        [Test] public void GameData_EveryWheelModuleMaps()
        {
            var root = InstalledKsp1.GameDataRoot;
            Assume.That(Directory.Exists(root), $"KSP1 GameData not found at {root} (set KSP1_GAMEDATA)");
            var c = Run(root, InstalledKsp1.GameDataCfg());
            Print("I:/ksp1 GameData", c);
            CollectionAssert.IsEmpty(c.Failures, string.Join("\n", c.Failures));
            Assert.Greater(c.Total.Tally.Converted(WheelKind.Base), 0);
        }
    }
}
