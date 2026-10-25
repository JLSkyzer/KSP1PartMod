using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Injection
{
    /// <summary>What the load step does to one part's module list.</summary>
    public enum InjectionAction { None, Add, Refresh }

    /// <summary>The load step's decisions, apart from the game types (spec section 3).</summary>
    public static class ModuleInjection
    {
        /// <summary><see cref="InjectionAction.Refresh"/> when our module is already on the part (same feature set
        /// reloaded), even with no entries: its entry list is then emptied, never removed, so that the definition,
        /// <c>PartJson</c> and the saves of this session agree. <see cref="InjectionAction.Add"/> when the plan has
        /// entries, else <see cref="InjectionAction.None"/>.</summary>
        /// <param name="existingModuleNames">The <c>Name</c> of each module of the part definition.</param>
        public static InjectionAction Decide(IReadOnlyList<string> existingModuleNames, PartAnimationPlan plan, string ourName)
        {
            if (existingModuleNames != null && existingModuleNames.Any(n => string.Equals(n, ourName, StringComparison.Ordinal)))
                return InjectionAction.Refresh;
            return plan?.Entries != null && plan.Entries.Count > 0 ? InjectionAction.Add : InjectionAction.None;
        }

        /// <summary>One line per KSP1 mod folder: the deployables and parachutes whose Redux clip we draw.</summary>
        public static string FollowerLine(string modName, IEnumerable<PartAnimationPlan> plans)
        {
            var entries = (plans ?? Enumerable.Empty<PartAnimationPlan>()).Where(p => p?.Entries != null)
                .SelectMany(p => p.Entries).Where(e => e != null).ToList();
            return $"[KSP1PartMod] anim: mod {modName ?? "?"}: {entries.Count(e => e.Kind == EntryKind.ReduxFollower)} Redux deployable(s) followed, " +
                   $"{entries.Count(e => e.Kind == EntryKind.ChuteFollower)} Redux parachute(s) followed";
        }

        /// <summary>One line per KSP1 mod folder: its parts, our entries by kind (in <see cref="EntryKind"/> order),
        /// the parts where Redux keeps a clip, and the parts with neither.</summary>
        public static string SummaryLine(string modName, IEnumerable<PartAnimationPlan> plans)
        {
            var list = (plans ?? Enumerable.Empty<PartAnimationPlan>()).Where(p => p != null).ToList();
            var entries = list.SelectMany(p => p.Entries ?? new AnimEntryDef[0]).Where(e => e != null).ToList();
            var redux = list.Count(p => p.ReduxClips != null && p.ReduxClips.Count > 0);
            var none = list.Count(p => (p.Entries == null || p.Entries.Count == 0) && (p.ReduxClips == null || p.ReduxClips.Count == 0));
            var kinds = entries.GroupBy(e => e.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}").ToList();
            var entryText = $"{entries.Count} {(entries.Count == 1 ? "entry" : "entries")}" +
                            (kinds.Count > 0 ? $" ({string.Join(", ", kinds)})" : "");
            return $"[KSP1PartMod] anim: mod {modName ?? "?"}: {list.Count} part(s), {entryText}, {redux} left to Redux, {none} none";
        }
    }
}
