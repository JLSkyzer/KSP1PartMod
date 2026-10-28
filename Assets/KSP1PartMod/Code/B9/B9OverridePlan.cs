using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>A part value a subtype overrides (<c>maxTemp</c>, <c>skinMaxTemp</c>, <c>crashTolerance</c>: one value;
    /// <c>CoMOffset</c>: x, y, z, before the part's rescale).</summary>
    public sealed class B9PartScalar
    {
        public string Key;
        public double[] Values;
    }

    /// <summary>One edit of a KSP1 module by a subtype: its DATA written over the module, and/or its off form.</summary>
    public sealed class B9ModuleEditPlan
    {
        /// <summary>The index of the target among the part's MODULE nodes.</summary>
        public int TargetIndex;
        /// <summary>The target's KSP1 module name.</summary>
        public string TargetModule;
        /// <summary>The edited copy of the target (DATA, then the off form), or null when nothing is written (a module
        /// turned off that has no native off form).</summary>
        public B9Node Edited;
        /// <summary>The DATA keys and node names written (the off form's keys are not listed).</summary>
        public List<string> DataKeys = new List<string>();
        /// <summary>The subtype turns the module off (<c>moduleActive = false</c>, with <c>b9_module_active</c>).</summary>
        public bool Off;
        /// <summary><see cref="Off"/> and no native off form: left on (counted).</summary>
        public bool NoOffForm;
    }

    /// <summary>The overrides of one subtype (one V-SwiFT variant).</summary>
    public sealed class B9SubtypePlan
    {
        public string VariantId;
        /// <summary><c>addedMass</c> plus the tank's dry mass, in tonnes (0 with <c>b9_mass</c> off).</summary>
        public double MassDelta;
        /// <summary><c>addedCost</c> plus the tank's cost (logged only, never applied).</summary>
        public double Cost;
        /// <summary>The KSP1 resources of the subtype on the part's tank switch (<c>b9_resources</c>), or null when the
        /// switch does not set the resources. An empty list is an empty tank (<c>Structural</c>).</summary>
        public List<B9Resource> Resources;
        public List<B9ModuleEditPlan> ModuleEdits = new List<B9ModuleEditPlan>();
        public List<B9PartScalar> Scalars = new List<B9PartScalar>();

        public bool HasOverrides => MassDelta != 0 || Resources != null || ModuleEdits.Count > 0 || Scalars.Count > 0;
    }

    /// <summary>The plan of one ModuleB9PartSwitch.</summary>
    public sealed class B9SwitchPlan
    {
        public IKsp1Node Module;
        /// <summary>The index of the module among the part's MODULE nodes.</summary>
        public int ModuleIndex;
        /// <summary>The index of the module among the part's B9 modules (cfg order).</summary>
        public int B9Index;
        /// <summary>Redux's set id, or the appended set's id when <see cref="Appended"/>.</summary>
        public string SetId;
        /// <summary>A new set appended after Redux's (the switch had no Redux set).</summary>
        public bool Appended;
        public string[] VariantIds;
        public string DefaultVariant;
        /// <summary>The part's handled tank switch: its subtypes carry <see cref="B9SubtypePlan.Resources"/> (with
        /// <c>b9_resources</c>) and their dry mass.</summary>
        public bool IsTank;
        public List<B9SubtypePlan> Subtypes = new List<B9SubtypePlan>();

        public bool HasOverrides => Subtypes.Any(s => s.HasOverrides);
    }

    /// <summary>B9 overrides plan, (c) to (f): turns a part's ModuleB9PartSwitch subtypes into the overrides the glue
    /// writes as Redux's own V-SwiFT transformers. Field meanings from B9PartSwitch's public wiki (read 2026-10-28); set
    /// and variant ids from <see cref="VariantSwitches"/>; IDENTIFIER matching from <see cref="B9GatePlanner.Match"/>. No
    /// B9 code is used. Pure: no game type.</summary>
    public sealed class B9OverridePlan
    {
        private const string B9Name = "ModuleB9PartSwitch";
        private static readonly string[] ScalarKeys = { "maxTemp", "skinMaxTemp", "crashTolerance" };

        /// <summary>The switches with a Redux set, and the switches without one that get an appended set (overrides and
        /// <c>b9_new_part_switches</c>), in cfg order.</summary>
        public List<B9SwitchPlan> Switches = new List<B9SwitchPlan>();
        /// <summary>Distinct notes for the <c>b9: &lt;part&gt;: &lt;note&gt;</c> lines (<see cref="B9Lines.Notes"/>).</summary>
        public List<string> Notes = new List<string>();
        /// <summary>The part has no Redux set at all and gets an appended one: it gains a <c>Module_PartSwitch</c>.</summary>
        public bool GainsPartSwitch;
        /// <summary>A second B9 module with a tank was left alone.</summary>
        public bool HasSecondTankSwitch;
        /// <summary>A tank switch whose volume cannot be read.</summary>
        public bool HasUnparseableVolume;
        /// <summary>Child B9 modules that add volume to a parent (<c>parentID</c>).</summary>
        public int ParentChildPairs;

        /// <summary>The <c>appendSet</c> test of <see cref="VariantSwitches.All"/> that gives this plan's appended sets (the
        /// switches <see cref="B9SwitchPlan.Appended"/>, matched by module node), for <see cref="B9GatePlanner.Apply"/>: the
        /// gates then use the same set ids as the sets. Null when the plan appends no set (the 0.8.0 gates).</summary>
        public Func<VariantSwitch, bool> AppendSetTest()
        {
            var appended = new HashSet<IKsp1Node>(Switches.Where(s => s.Appended).Select(s => s.Module));
            if (appended.Count == 0) return null;
            return sw => sw?.Module != null && appended.Contains(sw.Module);
        }

        public static B9OverridePlan For(IReadOnlyList<IKsp1Node> partModules, B9TankTypes tankTypes, B9Switches settings)
        {
            var plan = new B9OverridePlan();
            if (partModules == null) return plan;
            settings = settings ?? B9Switches.AllOn;
            tankTypes = tankTypes ?? B9TankTypes.From(null);

            var b9Modules = partModules.Where(IsB9).ToList();
            if (b9Modules.Count == 0) return plan;
            var switches = VariantSwitches.All(partModules).Where(s => !s.IsStock).ToList();

            // The first switch with a tank (cfg order) is the part's tank switch; the others are noted.
            VariantSwitch tankSwitch = null;
            foreach (var sw in switches)
            {
                if (!HasTank(sw)) continue;
                if (tankSwitch == null) tankSwitch = sw;
                else
                {
                    plan.HasSecondTankSwitch = true;
                    var dry = DryMasses(sw, b9Modules, tankTypes);
                    plan.Note(B9Lines.Notes.SecondTankSwitch(Label(sw), dry.Count == 0 ? (double?)null : dry.Min(),
                        dry.Count == 0 ? (double?)null : dry.Max()));
                }
            }

            // Keyed by module node: the second listing below holds new VariantSwitch objects for the same nodes.
            var planned = new Dictionary<IKsp1Node, B9SwitchPlan>();
            foreach (var sw in switches)
            {
                if (sw.Subtypes.Count == 0 || planned.ContainsKey(sw.Module)) continue;
                planned[sw.Module] = plan.PlanSwitch(sw, sw == tankSwitch, partModules, b9Modules, tankTypes, settings);
            }

            var ids = VariantSwitches.All(partModules, sw =>
                settings.NewPartSwitches && planned.TryGetValue(sw.Module, out var p) && p.HasOverrides);
            var reduxSets = ids.Any(s => s.SetId != null);
            foreach (var listed in ids)
            {
                if (listed.IsStock || !planned.TryGetValue(listed.Module, out var p)) continue;
                if (listed.SetId != null) p.SetId = listed.SetId;
                else if (listed.OverrideSetId != null)
                {
                    p.SetId = listed.OverrideSetId;
                    p.Appended = true;
                }
                else continue;
                plan.Switches.Add(p);
            }
            plan.GainsPartSwitch = !reduxSets && plan.Switches.Any(s => s.Appended);
            return plan;
        }

        private B9SwitchPlan PlanSwitch(VariantSwitch sw, bool isTank, IReadOnlyList<IKsp1Node> modules,
            List<IKsp1Node> b9Modules, B9TankTypes tankTypes, B9Switches settings)
        {
            var label = Label(sw);
            var p = new B9SwitchPlan
            {
                Module = sw.Module, ModuleIndex = IndexOf(modules, sw.Module), B9Index = IndexOf(b9Modules, sw.Module),
                VariantIds = sw.VariantIds, DefaultVariant = sw.DefaultSubtype,
            };

            // Volume per subtype (tank switch only); one unreadable volume drops the switch's tank override.
            var volumes = new double?[sw.Subtypes.Count];
            if (isTank)
            {
                for (var i = 0; i < sw.Subtypes.Count; i++) volumes[i] = B9Volume.Of(sw.Module, sw.Subtypes[i], b9Modules);
                if (volumes.Any(v => v == null))
                {
                    isTank = false;
                    HasUnparseableVolume = true;
                    Note(B9Lines.Notes.UnparseableVolume(label));
                }
                else
                    foreach (var child in B9Volume.Children(sw.Module, b9Modules))
                    {
                        ParentChildPairs++;
                        var childSwitch = B9Values.FirstNonBlank(child.GetValue("moduleID"), child.GetValue("switcherDescription"), "?");
                        var childDefault = B9Volume.DefaultSubtypeNode(child);
                        Note(B9Lines.Notes.ChildVolumeAtDefault(childSwitch, label, B9TankRule.SubtypeId(childDefault)));
                    }
            }
            p.IsTank = isTank;

            for (var i = 0; i < sw.Subtypes.Count; i++)
            {
                var st = sw.Subtypes[i];
                var sp = new B9SubtypePlan { VariantId = sw.VariantIds[i] };
                var notes = new List<string>();
                B9TankType tank = null;
                if (isTank)
                {
                    var tankName = B9Values.Trim(st.GetValue("tankType"));
                    if (!string.IsNullOrEmpty(tankName))
                    {
                        tank = tankTypes.Find(tankName);
                        if (tank == null) notes.Add(B9Lines.Notes.UnknownTankType(tankName));
                    }
                    else tank = tankTypes.Find(B9TankTypes.StructuralName);
                }
                var volume = isTank ? volumes[i] : null;
                var mass = B9MassRule.Delta(st, tank, volume, notes);
                if (settings.Mass) sp.MassDelta = mass;
                sp.Cost = B9MassRule.Cost(st, tank, volume, notes);
                if (isTank && settings.Resources) sp.Resources = B9TankRule.Resources(st, tank, volume.Value, notes);
                if (settings.ModuleData) PlanModuleEdits(sp, st, label, modules, settings);
                if (settings.PartScalars) PlanScalars(sp, st, notes);
                if (!string.IsNullOrWhiteSpace(st.GetValue("upgradeRequired"))) notes.Add(B9Lines.Notes.UpgradeRequiredIgnored);
                if (st.GetValues("node").Any() || st.GetNodes("TRANSFORM").Any()) notes.Add(B9Lines.Notes.NodeTransformNotApplied);
                foreach (var n in notes) Note(n);
                p.Subtypes.Add(sp);
            }
            return p;
        }

        private void PlanModuleEdits(B9SubtypePlan sp, IKsp1Node st, string label, IReadOnlyList<IKsp1Node> modules, B9Switches settings)
        {
            foreach (var block in st.GetNodes("MODULE"))
            {
                var identifier = block.GetNodes("IDENTIFIER").FirstOrDefault();
                if (identifier == null) continue;
                var off = settings.ModuleActive && B9Values.IsFalse(block.GetValue("moduleActive"));
                var datas = block.GetNodes("DATA").ToList();
                if (!off && datas.Count == 0) continue;
                var matches = B9GatePlanner.Match(modules, identifier);
                var name = B9Values.Trim(identifier.GetValue("name")) ?? "?";
                if (matches.Count != 1)
                {
                    Note(B9Lines.Notes.IdentifierMatches(label, sp.VariantId, name, matches.Count));
                    continue;
                }
                var index = matches[0];
                var edit = sp.ModuleEdits.FirstOrDefault(e => e.TargetIndex == index);
                if (edit == null)
                {
                    edit = new B9ModuleEditPlan { TargetIndex = index, TargetModule = B9Values.Trim(modules[index].GetValue("name")) };
                    sp.ModuleEdits.Add(edit);
                }
                foreach (var data in datas)
                {
                    edit.Edited = B9ModuleEdit.EditedCopy(edit.Edited ?? modules[index], data);
                    foreach (var k in B9ModuleEdit.Keys(data))
                        if (!edit.DataKeys.Contains(k)) edit.DataKeys.Add(k);
                }
                if (!off) continue;
                edit.Off = true;
                var form = B9OffForms.DataNode(edit.TargetModule);
                if (form == null) edit.NoOffForm = true;
                else edit.Edited = B9ModuleEdit.EditedCopy(edit.Edited ?? modules[index], form);
            }
        }

        private static void PlanScalars(B9SubtypePlan sp, IKsp1Node st, List<string> notes)
        {
            var id = B9TankRule.SubtypeId(st);
            foreach (var key in ScalarKeys)
            {
                var raw = st.GetValue(key);
                var v = B9Values.Optional(raw, out var bad);
                if (bad) notes.Add(B9Lines.Notes.Unparseable(id, key, raw.Trim()));
                else if (v.HasValue) sp.Scalars.Add(new B9PartScalar { Key = key, Values = new[] { v.Value } });
            }
            var com = st.GetValue("CoMOffset");
            if (string.IsNullOrWhiteSpace(com)) return;
            var parts = com.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new double[3];
            if (parts.Length == 3 && Enumerable.Range(0, 3).All(i =>
                    double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])))
                sp.Scalars.Add(new B9PartScalar { Key = "CoMOffset", Values = values });
            else notes.Add(B9Lines.Notes.Unparseable(id, "CoMOffset", com.Trim()));
        }

        /// <summary>The dry mass (<c>tankMass × volume</c>, tonnes) of each subtype of a tank switch that is not applied,
        /// for its note; subtypes whose volume cannot be read are left out, a tank type without <c>tankMass</c> counts 0.</summary>
        private static List<double> DryMasses(VariantSwitch sw, List<IKsp1Node> b9Modules, B9TankTypes tankTypes)
        {
            var result = new List<double>();
            foreach (var st in sw.Subtypes)
            {
                var volume = B9Volume.Of(sw.Module, st, b9Modules);
                if (volume == null) continue;
                var tankName = B9Values.Trim(st.GetValue("tankType"));
                var tank = tankTypes.Find(string.IsNullOrEmpty(tankName) ? B9TankTypes.StructuralName : tankName);
                result.Add((tank?.TankMass ?? 0) * volume.Value);
            }
            return result;
        }

        private void Note(string note) => B9Values.Add(Notes, note);

        /// <summary>A B9 switch with a tank: a subtype with a <c>tankType</c> or a RESOURCE node.</summary>
        private static bool HasTank(VariantSwitch sw) =>
            sw.Subtypes.Any(st => !string.IsNullOrWhiteSpace(st.GetValue("tankType")) || st.GetNodes("RESOURCE").Any());

        /// <summary>The switch's name in notes: <c>moduleID</c>, else <c>switcherDescription</c>, else its raw id.</summary>
        private static string Label(VariantSwitch sw) =>
            B9Values.FirstNonBlank(sw.Module.GetValue("moduleID"), sw.Module.GetValue("switcherDescription"), sw.RawId);

        private static bool IsB9(IKsp1Node m) => m != null && B9Name.Equals(B9Values.Trim(m.GetValue("name")), StringComparison.OrdinalIgnoreCase);

        private static int IndexOf(IReadOnlyList<IKsp1Node> list, IKsp1Node node)
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], node)) return i;
            return -1;
        }
    }
}
