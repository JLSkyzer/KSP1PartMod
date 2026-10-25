using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary><see cref="Plan(IReadOnlyList{IKsp1Node},bool,ISet{string},bool)"/> with a given counterpart
        /// table.</summary>
        public static PartAnimationPlan Plan(IReadOnlyList<IKsp1Node> modules, bool hasNativeSolarPanel,
            ISet<string> nativeKsp2ModuleNames, bool ownFirstDeployable, IReadOnlyDictionary<string, string[]> counterparts)
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
            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                var name = NameOf(module);
                var kind = EntryParser.Kind(name);
                if (kind == null) continue;
                var isSource = sources.Any(s => s.Index == i);
                if (isSource && reduxSource.Index == i && !remove)
                {
                    // (4) Redux keeps it; a gear gets our Gear action group on top.
                    if (kind == EntryKind.Gear) Add(entries, Bridge(EntryParser.Parse(module, i, 0)));
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
                    notes.Add($"MODULE[{i}] {name}: moduleID '{id}' is driven by ModuleAblator, stays at 0, not ours");
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
            return new PartAnimationPlan(entries, reduxClips, reduxModule, remove, notes);
        }

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

        private static IEnumerable<string> Clips(AnimEntryDef e) =>
            new[] { e.Clip, e.SecondClip, e.ThirdClip }.Where(c => !string.IsNullOrEmpty(c));

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
