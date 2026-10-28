using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Who drives each KSP1 animation module of one part: our entries, and what stays with Redux.</summary>
    public sealed class PartAnimationPlan
    {
        /// <summary>Our entries, in KSP1 MODULE order (a GearBridge sits at the index of Redux's gear module).</summary>
        public IReadOnlyList<AnimEntryDef> Entries { get; }
        /// <summary>The clip names Redux's deployable runtime keeps; every other source name is ours.</summary>
        public IReadOnlyList<string> ReduxClips { get; }
        /// <summary>The deployable module Redux puts on the part: "Module_Deployable" (one source, bound to the
        /// generic Animator controller when that works), "Ksp1Module_Deployable" (several sources), or null.</summary>
        public string ReduxDeployableModule { get; }
        /// <summary>True when <c>own_first_deployable</c> takes Redux's deployable source: its module must go.</summary>
        public bool RemoveReduxDeployable { get; }
        /// <summary>One line per module left alone for a reason worth logging.</summary>
        public IReadOnlyList<string> Notes { get; }

        public PartAnimationPlan(IReadOnlyList<AnimEntryDef> entries, IReadOnlyList<string> reduxClips,
            string reduxDeployableModule, bool removeReduxDeployable, IReadOnlyList<string> notes)
        {
            Entries = entries;
            ReduxClips = reduxClips;
            ReduxDeployableModule = reduxDeployableModule;
            RemoveReduxDeployable = removeReduxDeployable;
            Notes = notes;
        }
    }

    /// <summary>The coexistence rule of the design (spec section 2): Redux keeps every clip it already animates,
    /// we take what it drops or merges, plus the Gear and aero bridges. No clip is driven by both.</summary>
    public static class OwnershipPlanner
    {
        /// <summary>Redux 26w42c's deployable sources (<c>Ksp1DeployableModuleBuilder.IsDeployableModule</c>).</summary>
        private static readonly string[] SourceModules =
            { "ModuleAnimateGeneric", "ModuleDeployableSolarPanel", "ModuleWheelDeployment", "ModuleLandingLeg" };

        /// <summary>KSP1 module -> the KSP2 behaviour names which, found on the part, mean Redux imports that
        /// module natively, so we leave it. Every entry needs evidence that Redux makes that module. Redux 26w42c
        /// (<c>Ksp2Redux.dll</c> 2026-10-18, <c>Ksp1PartModuleImporterRegistry</c> constructor, research note
        /// <c>redux/NOTES.md</c> §1 "Module importers" and §2) registers no importer for ModuleDeployableAntenna,
        /// ModuleDeployableRadiator, ModuleDeployablePart, FXModuleAnimateThrottle, ModuleAnimateHeat,
        /// ModuleColorChanger, ModuleAnimationGroup or ModuleDeployableAero: it logs them as unsupported. The table is
        /// therefore empty. The solar panel, which Redux does import (<c>Module_SolarPanel</c>), is a deployable
        /// source and goes through <c>hasNativeSolarPanel</c> instead.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> NativeCounterparts =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <param name="modules">The part's KSP1 MODULE nodes, in cfg order.</param>
        /// <param name="hasNativeSolarPanel">Redux made a <c>Module_SolarPanel</c> for the part.</param>
        /// <param name="nativeKsp2ModuleNames">The KSP2 behaviour names already on the part.</param>
        /// <param name="ownFirstDeployable">Settings <c>own_first_deployable</c>.</param>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable) =>
            Plan(modules, hasNativeSolarPanel, nativeKsp2ModuleNames, ownFirstDeployable, NativeCounterparts);

        /// <param name="followRedux">Settings <c>follow_redux_deployables</c>: a part with a single deployable source
        /// (Redux binds it to its Mecanim copy, which does not move) gets a <see cref="EntryKind.ReduxFollower"/> on
        /// that clip, and a part with a ModuleParachute a <see cref="EntryKind.ChuteFollower"/>. Redux keeps the
        /// state; we only draw it. Two or more sources go to Redux's legacy runtime, which moves: left alone.</param>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable, bool followRedux) =>
            Plan(modules, hasNativeSolarPanel, nativeKsp2ModuleNames, ownFirstDeployable, NativeCounterparts, followRedux);

        /// <param name="skipModules">Settings <c>skip_modules</c> (the caller builds the set once, with its comparer):
        /// a module of one of these names gets no entry and the note <c>skipped by skip_modules</c>. Keys are assigned
        /// before the filter, so every other entry keeps the key it has without the setting.</param>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable, bool followRedux, ISet<string> skipModules) =>
            Plan(modules, hasNativeSolarPanel, nativeKsp2ModuleNames, ownFirstDeployable, NativeCounterparts, followRedux, skipModules);

        /// <param name="pluginModules">Settings <c>plugin_modules</c>: the KSP1 plugin modules of
        /// <see cref="PluginEntryParser"/> become entries. They are planned after every phase 1 entry, with ordinals of
        /// their own kinds, so no older key moves. A plugin entry never takes a clip Redux plays, nor a clip an earlier
        /// entry of ours plays unless the B9 gates keep the two apart; each refusal is a note.</param>
        /// <param name="b9Gates">Settings <c>b9_module_gates</c>: <see cref="B9GatePlanner.Apply"/> runs last and gates
        /// our entries with the part's B9 subtypes. Off: no entry has gates.</param>
        /// <param name="shrouds">Settings <c>ksp1_engine_shrouds</c> (0.7.0): every <c>ModuleJettison</c> with a
        /// <c>jettisonName</c> becomes a <see cref="EntryKind.Shroud"/> entry, planned after the plugin entries with
        /// ordinals of its own kind. Its names get their variant gates (<see cref="ShroudVariantPlanner"/>) and leave any
        /// mesh hider that names them (<see cref="ShroudOwnership"/>). Off: no shroud entry, as in 0.6.0.</param>
        /// <param name="ablatorChar">Settings <c>ablator_char</c> with <c>ksp1_heatshields</c> (0.8.0): a colour changer that a
        /// <c>ModuleAblator</c> with <c>useChar</c> drives (<c>shieldChar</c>) becomes a Color entry whose scalar is the part's
        /// ablator fraction (<see cref="ScalarSource.Ablator"/>). Those entries are planned after every other one, so no
        /// older key moves. A char is not planned when another entry of the part writes the same shader property
        /// (compared through <paramref name="shaderPropertyMap"/>): the KV pods keep their light changer, and the note says
        /// so. Off: the 0.7.0 note, no entry.</param>
        /// <param name="shaderPropertyMap">Settings <c>shader_property_map</c>; null: the built-in map.</param>
        /// <param name="b9AppendSet">B9 overrides (0.9.0): the switches without a Redux set that get an appended set, as
        /// <see cref="B9GatePlanner.Apply"/> takes it. Their gates follow that set. Null: those gates stay at their default
        /// subtype (0.8.0). Keys never depend on it.</param>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable, bool followRedux, ISet<string> skipModules,
            bool pluginModules, bool b9Gates, bool shrouds = false, bool ablatorChar = false,
            IReadOnlyDictionary<string, string[]> shaderPropertyMap = null, Func<VariantSwitch, bool> b9AppendSet = null) =>
            Plan(modules, hasNativeSolarPanel, nativeKsp2ModuleNames, ownFirstDeployable, NativeCounterparts, followRedux, skipModules,
                pluginModules, b9Gates, shrouds, ablatorChar, shaderPropertyMap, b9AppendSet);

        /// <summary><see cref="Plan(IReadOnlyList{IKsp1Node},bool,ISet{string},bool)"/> with a given counterpart
        /// table. The plugin modules and the B9 gates are off unless asked for: older callers keep the 0.4.0 plan.</summary>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable, IReadOnlyDictionary<string, string[]> counterparts,
            bool followRedux = false, ISet<string> skipModules = null, bool pluginModules = false, bool b9Gates = false,
            bool shrouds = false, bool ablatorChar = false, IReadOnlyDictionary<string, string[]> shaderPropertyMap = null,
            Func<VariantSwitch, bool> b9AppendSet = null)
        {
            modules = modules ?? new IKsp1Node[0];
            var native = new HashSet<string>(nativeKsp2ModuleNames ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();

            // (1) Redux's sources, and the one its deployable module is made from.
            var sources = new List<Source>();
            for (var i = 0; i < modules.Count; i++)
            {
                var name = NameOf(modules[i]);
                var clip = modules[i] == null ? null : EntryParser.SourceClip(modules[i]);
                if (clip != null && SourceModules.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    sources.Add(new Source(i, name, clip));
            }
            var nonSolar = sources.Where(s => !s.IsSolar).ToList();
            // With a native solar panel Redux builds its module from the first non-solar source. With only solar
            // sources it builds none; the first panel is then the native module's own, and stays Redux's.
            var reduxSource = sources.Count == 0 ? null : hasNativeSolarPanel ? nonSolar.FirstOrDefault() ?? sources[0] : sources[0];
            string reduxModule = null;
            if (sources.Count > 0 && !(hasNativeSolarPanel && nonSolar.Count == 0))
                reduxModule = sources.Count == 1 ? "Module_Deployable" : "Ksp1Module_Deployable";

            // (2) own_first_deployable takes Redux's source, unless it is a solar panel (power stays Redux's).
            var remove = ownFirstDeployable && reduxModule != null && reduxSource != null && !reduxSource.IsSolar;
            var reduxClips = new List<string>();
            if (reduxSource != null && !remove) reduxClips.Add(reduxSource.Clip);

            var entries = new List<AnimEntryDef>();
            // The colour changers that follow the ablator (ablatorChar): planned after every other entry.
            var ablatorChars = new List<(int Index, string Id, string Resource)>();
            var chuteSeen = false;
            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                var name = NameOf(module);
                if (followRedux && !chuteSeen && "ModuleParachute".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    // Redux binds the first ModuleParachute (Ksp1ParachuteModuleBuilder, FirstOrDefault) to its Mecanim copy.
                    chuteSeen = true;
                    Add(entries, EntryParser.ChuteFollower(module, i));
                    continue;
                }
                var kind = EntryParser.Kind(name);
                if (kind == null) continue;
                var isSource = sources.Any(s => s.Index == i);
                if (isSource && reduxSource.Index == i && !remove)
                {
                    // (4) Redux keeps it; a gear gets our Gear action group on top.
                    if (kind == EntryKind.Gear) Add(entries, Bridge(EntryParser.Parse(module, i, 0)));
                    // One source: Redux's Mecanim copy (Ksp1GenericDeployableAnimatorBuilder). Several: its legacy runtime.
                    if (followRedux && sources.Count == 1) Add(entries, EntryParser.ReduxFollower(module, i));
                    continue;
                }
                if (!isSource && SourceModules.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue; // a source module without a clip: nothing to drive

                // (6) Redux imports this module natively.
                if (counterparts != null && counterparts.TryGetValue(name, out var names) && names != null)
                {
                    var hit = names.FirstOrDefault(native.Contains);
                    if (hit != null)
                    {
                        notes.Add($"MODULE[{i}] {name}: left to Redux, which has the native {hit}");
                        continue;
                    }
                }

                if (kind == EntryKind.Color && DrivenByAblator(modules, module, out var id))
                {
                    if (ablatorChar && TryCharAblator(modules, id, out var resource))
                        ablatorChars.Add((i, id, resource)); // its note comes with the plan, below
                    else notes.Add($"MODULE[{i}] {name}: moduleID '{id}' is driven by ModuleAblator, stays at 0, not ours");
                    continue;
                }

                // (3) other sources, (5) the modules Redux drops, (7) the aero gate.
                var e = EntryParser.Parse(module, i, 0);
                if (e == null) continue;
                if (e.Kind == EntryKind.AeroGate && (e.ControllerModuleIndex < 0 || e.ControllerModuleIndex >= modules.Count))
                {
                    notes.Add($"MODULE[{i}] {name}: DeployModuleIndex {e.ControllerModuleIndex} is not a module of the part, not ours");
                    continue;
                }
                var shared = Clips(e).FirstOrDefault(c => reduxClips.Contains(c, StringComparer.OrdinalIgnoreCase));
                if (shared != null && e.Kind != EntryKind.AeroGate)
                {
                    notes.Add($"MODULE[{i}] {name}: clip '{shared}' is played by Redux, left to Redux");
                    continue;
                }
                Add(entries, e);
            }

            // Phase 2b: the plugin modules, after every phase 1 entry (their kinds have their own ordinals).
            if (pluginModules) PlanPluginModules(modules, entries, reduxClips, notes);

            // Engine shrouds (0.7.0): after the plugin entries, so that no older key moves.
            if (shrouds)
                for (var i = 0; i < modules.Count; i++)
                    Add(entries, ShroudParser.Parse(modules[i], i, 0));

            // The ablator char (0.8.0): after the shrouds, last of all, so that no older Color key moves. It is not planned
            // when another entry of the part writes the same shader property (a KV pod's light changer, which would never
            // show again: the char submits every frame and outranks it). Dropping the last entries moves no key.
            var map = shaderPropertyMap ?? AnimationSettings.Defaults.ShaderPropertyMap;
            foreach (var (index, id, resource) in ablatorChars)
            {
                var e = EntryParser.Parse(modules[index], index, 0);
                if (e == null || e.Kind != EntryKind.Color) continue;
                var rival = entries.FirstOrDefault(o => !Skipped(modules, o.ModuleIndex, skipModules)
                                                        && SameSlot(o.ShaderProperty, e.ShaderProperty, map));
                if (rival != null)
                {
                    notes.Add($"MODULE[{index}] {NameOf(modules[index])}: moduleID '{id}' not planned, MODULE[{rival.ModuleIndex}] " +
                              $"{NameOf(modules[rival.ModuleIndex])} writes the same shader property ({e.ShaderProperty}): it keeps it");
                    continue;
                }
                notes.Add($"MODULE[{index}] {NameOf(modules[index])}: moduleID '{id}' follows the ablator (Module_Heatshield)");
                e.ScalarSource = ScalarSource.Ablator;
                e.ScalarLinked = true; // no ramp, nothing saved: the colour is the fraction
                e.ScalarResource = resource;
                e.ModuleId = id;
                e.ToggleInEditor = e.ToggleInFlight = false; // KSP1's ablator owns it: no button, no action group
                e.ActionGroup = null;
                Add(entries, e);
            }

            // skip_modules, after the keys: dropping an entry never renumbers another.
            if (skipModules != null && skipModules.Count > 0)
            {
                var skipped = new HashSet<int>();
                for (var i = 0; i < modules.Count; i++)
                {
                    var name = NameOf(modules[i]);
                    if (name == null || !skipModules.Contains(name)) continue;
                    skipped.Add(i);
                    notes.Add($"MODULE[{i}] {name}: skipped by skip_modules");
                }
                // An aero gate whose controller is skipped has nothing to follow.
                foreach (var gate in entries.Where(e => e.Kind == EntryKind.AeroGate && !skipped.Contains(e.ModuleIndex)
                                                        && skipped.Contains(e.ControllerModuleIndex)))
                    notes.Add($"MODULE[{gate.ModuleIndex}] {NameOf(modules[gate.ModuleIndex])}: controller MODULE[{gate.ControllerModuleIndex}] skipped by skip_modules");
                entries.RemoveAll(e => skipped.Contains(e.ModuleIndex)
                                       || e.Kind == EntryKind.AeroGate && skipped.Contains(e.ControllerModuleIndex));
                // A skip never stops an animation: own_first_deployable gives Redux's source back to Redux, and an
                // entry of ours on the same clip goes, so that no clip is driven by both.
                if (remove && skipped.Contains(reduxSource.Index))
                {
                    remove = false;
                    reduxClips.Add(reduxSource.Clip);
                    foreach (var e in entries.Where(e => e.Kind != EntryKind.AeroGate
                                                         && Clips(e).Contains(reduxSource.Clip, StringComparer.OrdinalIgnoreCase)).ToList())
                    {
                        notes.Add($"MODULE[{e.ModuleIndex}] {NameOf(modules[e.ModuleIndex])}: clip '{reduxSource.Clip}' is played by Redux, left to Redux");
                        entries.Remove(e);
                    }
                }
            }
            // FXModuleThrottleEffects: links our Color entries; never an entry itself.
            notes.AddRange(ScalarSourceParser.Apply(modules, entries, skipModules));

            // The shrouds that survived skip_modules: their variant gates per name, then the mesh hiders lose their names.
            if (shrouds)
            {
                foreach (var s in entries.Where(e => e.Kind == EntryKind.Shroud))
                    s.NameGates = ShroudVariantPlanner.Gates(modules, s.TransformNames);
                notes.AddRange(ShroudOwnership.TrimMeshHiders(entries));
            }

            // The modules whose animation Redux keeps: never gated.
            var reduxOwned = new HashSet<int>(entries.Where(e => e.Kind == EntryKind.GearBridge || e.Kind == EntryKind.ReduxFollower
                                                                 || e.Kind == EntryKind.ChuteFollower).Select(e => e.ModuleIndex));
            if (reduxSource != null && !remove) reduxOwned.Add(reduxSource.Index);
            if (pluginModules)
            {
                // A clip has one owner among our entries, unless the B9 gates keep its owners apart.
                if (b9Gates) B9GatePlanner.Apply(modules, entries, reduxOwned, b9AppendSet);
                DropSharedClips(modules, entries, notes);
            }
            // B9 gates last (Apply replaces every entry's gates, so the pass above leaves nothing behind).
            if (b9Gates) notes.AddRange(B9GatePlanner.Apply(modules, entries, reduxOwned, b9AppendSet));
            else
                foreach (var e in entries) e.Gates = null;
            return new PartAnimationPlan(entries, reduxClips, reduxModule, remove, notes);
        }

        /// <summary>The entries of <see cref="PluginEntryParser"/>, in MODULE order. A clip Redux plays stays Redux's.
        /// An advanced look-at rotator that Redux's look-at runtime also drives (<c>FXModuleLookAtConstraint</c>, matched
        /// ignoring case as Redux does) is noted: the runtime skips it there.</summary>
        private static void PlanPluginModules(IReadOnlyList<IKsp1Node> modules, List<AnimEntryDef> entries, List<string> reduxClips,
            List<string> notes)
        {
            for (var i = 0; i < modules.Count; i++)
            {
                var name = NameOf(modules[i]);
                if (!PluginEntryParser.Handles(name)) continue;
                var e = PluginEntryParser.Parse(modules[i], i, 0);
                if (e == null) continue;
                var shared = Clips(e).FirstOrDefault(c => reduxClips.Contains(c, StringComparer.OrdinalIgnoreCase));
                if (shared != null)
                {
                    notes.Add($"MODULE[{i}] {name}: clip '{shared}' is played by Redux, left to Redux");
                    continue;
                }
                if (e.Kind == EntryKind.LookAt && e.RotatorNames != null)
                    foreach (var rotator in e.RotatorNames)
                    {
                        var redux = ReduxLookAtModule(modules, rotator);
                        if (redux >= 0)
                            notes.Add($"MODULE[{i}] {name}: rotator '{rotator}' is also Redux's (FXModuleLookAtConstraint MODULE[{redux}]), " +
                                      "skipped there at runtime");
                    }
                Add(entries, e);
            }
        }

        /// <summary>The index of the first FXModuleLookAtConstraint whose CONSTRAINLOOKFX names <paramref name="rotator"/>,
        /// or -1.</summary>
        private static int ReduxLookAtModule(IReadOnlyList<IKsp1Node> modules, string rotator)
        {
            for (var i = 0; i < modules.Count; i++)
            {
                if (!"FXModuleLookAtConstraint".Equals(NameOf(modules[i]), StringComparison.OrdinalIgnoreCase)) continue;
                if (modules[i].GetNodes("CONSTRAINLOOKFX").Any(c =>
                        string.Equals(c.GetValue("rotatorsName")?.Trim(), rotator, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return -1;
        }

        /// <summary>Drops each plugin entry whose clip an earlier entry of ours already plays, unless the B9 gates keep
        /// them all apart (<see cref="B9GatePlanner.GatedApart"/>). Phase 1 entries come first and are never dropped.</summary>
        private static void DropSharedClips(IReadOnlyList<IKsp1Node> modules, List<AnimEntryDef> entries, List<string> notes)
        {
            for (var k = 0; k < entries.Count; k++)
            {
                var e = entries[k];
                if (e.Kind <= EntryKind.ChuteFollower) continue;
                foreach (var clip in Clips(e))
                {
                    var owners = entries.Take(k).Where(o => OwnsClipTime(o) && Clips(o).Contains(clip, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (owners.Count == 0 || B9GatePlanner.GatedApart(owners.Concat(new[] { e }).ToList())) continue;
                    notes.Add($"MODULE[{e.ModuleIndex}] {NameOf(modules[e.ModuleIndex])}: clip '{clip}' is already ours " +
                              $"(MODULE[{owners[0].ModuleIndex}] {NameOf(modules[owners[0].ModuleIndex])}), not taken");
                    entries.RemoveAt(k--);
                    break;
                }
            }
        }

        /// <summary>GearBridge plays nothing (Redux plays the clip) and AeroGate has no clip time.</summary>
        private static bool OwnsClipTime(AnimEntryDef e) => e.Kind != EntryKind.GearBridge && e.Kind != EntryKind.AeroGate;

        private sealed class Source
        {
            public readonly int Index;
            public readonly string Clip;
            public readonly bool IsSolar;

            public Source(int index, string name, string clip)
            {
                Index = index;
                Clip = clip;
                IsSolar = "ModuleDeployableSolarPanel".Equals(name, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NameOf(IKsp1Node module) => module?.GetValue("name")?.Trim();

        private static AnimEntryDef Bridge(AnimEntryDef gear)
        {
            if (gear != null) gear.Kind = EntryKind.GearBridge;
            return gear;
        }

        /// <summary>Appends with the ordinal: the number of earlier entries of the same kind and key name.</summary>
        private static void Add(List<AnimEntryDef> entries, AnimEntryDef e)
        {
            if (e == null) return;
            var name = EntryParser.KeyName(e);
            EntryParser.SetKey(e, entries.Count(o => o.Kind == e.Kind && EntryParser.KeyName(o) == name));
            entries.Add(e);
        }

        /// <summary>Two KSP1 shader properties can land on the same material slot: their KSP2 candidates
        /// (<c>shader_property_map</c>) share a name.</summary>
        private static bool SameSlot(string a, string b, IReadOnlyDictionary<string, string[]> map)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            var mine = MaterialRules.Candidates(b.Trim(), map);
            return MaterialRules.Candidates(a.Trim(), map).Any(c => !string.IsNullOrEmpty(c) && mine.Contains(c, StringComparer.Ordinal));
        }

        private static bool Skipped(IReadOnlyList<IKsp1Node> modules, int index, ISet<string> skipModules) =>
            skipModules != null && index >= 0 && index < modules.Count && NameOf(modules[index]) is string n && skipModules.Contains(n);

        private static IEnumerable<string> Clips(AnimEntryDef e) =>
            new[] { e.Clip, e.SecondClip, e.ThirdClip }.Where(c => !string.IsNullOrEmpty(c));

        /// <summary>The first <c>ModuleAblator</c> of the part that drives the colour changer <paramref name="id"/> (its
        /// <c>charModuleName</c>, default <c>shieldChar</c>) with <c>useChar = true</c>, and its <c>ablativeResource</c>
        /// (default <c>Ablator</c>). KSP1's default for a missing <c>useChar</c> is taken as false (S; every installed
        /// shield says true).</summary>
        private static bool TryCharAblator(IReadOnlyList<IKsp1Node> modules, string id, out string resource)
        {
            resource = null;
            foreach (var m in modules)
            {
                if (!"ModuleAblator".Equals(NameOf(m), StringComparison.OrdinalIgnoreCase)) continue;
                if (!id.Equals((m.GetValue("charModuleName") ?? "shieldChar").Trim(), StringComparison.Ordinal)) continue;
                if (!bool.TryParse(m.GetValue("useChar")?.Trim(), out var useChar) || !useChar) continue;
                var r = m.GetValue("ablativeResource")?.Trim();
                resource = string.IsNullOrEmpty(r) ? "Ablator" : r;
                return true;
            }
            return false;
        }

        /// <summary>A ModuleColorChanger that a ModuleAblator of the part drives through its <c>charModuleName</c>
        /// (KSP1 default <c>shieldChar</c>), or whose moduleID is <c>shieldChar</c>: its colour follows ablation,
        /// which we do not model.</summary>
        private static bool DrivenByAblator(IReadOnlyList<IKsp1Node> modules, IKsp1Node color, out string id)
        {
            id = color.GetValue("moduleID")?.Trim();
            if (string.IsNullOrEmpty(id)) return false;
            if (id.Equals("shieldChar", StringComparison.Ordinal)) return true;
            var own = id;
            return modules.Any(m => "ModuleAblator".Equals(NameOf(m), StringComparison.OrdinalIgnoreCase)
                                    && own.Equals((m.GetValue("charModuleName") ?? "shieldChar").Trim(), StringComparison.Ordinal));
        }
    }
}
