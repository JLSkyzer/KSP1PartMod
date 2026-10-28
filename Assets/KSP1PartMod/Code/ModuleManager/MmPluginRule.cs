using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KSP1PartMod.ModuleManager
{
    /// <summary>The plugin safety net (plan (b)) and MM's BEFORE/AFTER rule (MM's public wiki, Patch-Ordering). A KSP1
    /// plugin never runs under Redux, so its folder proves nothing: a blocked name makes <c>:NEEDS[X]</c> false and
    /// <c>:NEEDS[!X]</c> true, and drops the root patches written for it. Names compare case-insensitively.</summary>
    public static class MmPluginRule
    {
        /// <summary>Default <c>mm_plugin_folders</c>: names always blocked.</summary>
        public static readonly IReadOnlyList<string> DefaultCurated = new ReadOnlyCollection<string>(new[]
        {
            "RealFuels", "RealFuels-Stockalike", "SolverEngines", "AJE", "AJEExtended", "FerramAerospaceResearch",
            "GroundEffectFAR", "RealismOverhaul", "Kerbalism", "KerbalismConfig", "SystemHeat", "RealAntennas",
            "RemoteTech", "TweakScale", "TestFlight", "EngineIgnitor", "DeadlyReentry", "KerbalJointReinforcement",
            "RealPlume", "SmokeScreen",
        });

        /// <summary>Default <c>mm_needs_keep</c>: names never blocked (Redux's aliases, and libraries whose behaviour
        /// Redux or KSP1PartMod reproduces).</summary>
        public static readonly IReadOnlyList<string> DefaultKeep = new ReadOnlyCollection<string>(new[]
        {
            "B9PartSwitch", "CommunityResourcePack", "Waterfall", "DeployableEngines", "B9AnimationModules",
            "DMagicScienceAnimate", "AnimatedDecouplers", "ReStock", "SimpleAdjustableFairings", "ModuleManager",
        });

        /// <summary>The first segment of a NEEDS term in path form (<c>Bluedog_DB/Parts/Generic</c> → <c>Bluedog_DB</c>),
        /// trimmed.</summary>
        public static string FirstSegment(string name)
        {
            if (name == null) return "";
            var s = name.Trim();
            var cut = s.IndexOfAny(new[] { '/', '\\' });
            return (cut >= 0 ? s.Substring(0, cut) : s).Trim();
        }

        /// <summary>Blocked: not kept, and curated or an automatic folder (the caller passes no automatic folders when
        /// <c>mm_plugin_folders_auto</c> is off, and does not call this when the safety net is off).</summary>
        public static bool Blocked(string name, IEnumerable<string> curated, IEnumerable<string> keep, IEnumerable<string> autoFolders)
        {
            var first = FirstSegment(name);
            if (first.Length == 0 || Contains(keep, first)) return false;
            return Contains(curated, first) || Contains(autoFolders, first);
        }

        private static bool Contains(IEnumerable<string> names, string name)
        {
            if (names == null) return false;
            foreach (var n in names)
                if (string.Equals(n?.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A top folder is a plugin folder when it holds a <c>.dll</c> and no <c>PART</c> root node.</summary>
        public static bool AutoFolder(bool hasDll, bool hasPart) => hasDll && !hasPart;

        /// <summary>A NEEDS expression: terms joined by <c>,</c> or <c>&amp;</c> (and) and <c>|</c> (or, binding tighter),
        /// each optionally negated by <c>!</c>. <paramref name="modAvailable"/> answers for one name, blocked names
        /// already false. An empty expression is met.</summary>
        public static bool NeedsMet(string expression, Func<string, bool> modAvailable)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;
            foreach (var group in expression.Split(',', '&'))
            {
                var any = false;
                foreach (var term in group.Split('|'))
                {
                    var t = term.Trim();
                    if (t.Length == 0) continue;
                    var negated = t[0] == '!';
                    var mod = negated ? t.Substring(1).Trim() : t;
                    if (modAvailable(mod) != negated) { any = true; break; }
                }
                if (!any) return false;
            }
            return true;
        }

        /// <summary>The top-level <c>:TAG[arg]</c> / <c>:TAG</c> tags of a node name, in order (arg null without
        /// brackets). Tags inside selector brackets are not read.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Tags(string name)
        {
            if (string.IsNullOrEmpty(name)) yield break;
            var depth = 0;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '[') { depth++; continue; }
                if (c == ']') { if (depth > 0) depth--; continue; }
                if (c != ':' || depth != 0) continue;
                var start = i + 1;
                var j = start;
                while (j < name.Length && name[j] != '[' && name[j] != ':' && name[j] != ',') j++;
                var tag = name.Substring(start, j - start).Trim();
                string arg = null;
                if (j < name.Length && name[j] == '[')
                {
                    var close = MatchingBracket(name, j);
                    if (close < 0) yield break;
                    arg = name.Substring(j + 1, close - j - 1);
                    j = close + 1;
                }
                if (tag.Length > 0) yield return new KeyValuePair<string, string>(tag, arg);
                i = j - 1;
            }
        }

        private static int MatchingBracket(string s, int open)
        {
            var depth = 0;
            for (var i = open; i < s.Length; i++)
            {
                if (s[i] == '[') depth++;
                else if (s[i] == ']' && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Every term of every <c>:NEEDS[…]</c> of a name, trimmed, <c>!</c> kept.</summary>
        public static IEnumerable<string> NeedsTerms(string name)
        {
            foreach (var tag in Tags(name))
            {
                if (!tag.Key.Equals("NEEDS", StringComparison.OrdinalIgnoreCase) || tag.Value == null) continue;
                foreach (var term in tag.Value.Split(',', '&', '|'))
                {
                    var t = term.Trim();
                    if (t.Length > 0) yield return t;
                }
            }
        }

        /// <summary>The name without its top-level <c>:NEEDS[…]</c> tags (Redux removes them before applying a line).</summary>
        public static string WithoutNeeds(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf(":NEEDS[", StringComparison.OrdinalIgnoreCase) < 0) return name ?? "";
            var s = name;
            var depth = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '[') depth++;
                else if (c == ']') { if (depth > 0) depth--; }
                else if (c == ':' && depth == 0 && string.Compare(s, i, ":NEEDS[", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var close = MatchingBracket(s, i + 6);
                    if (close < 0) break;
                    s = s.Remove(i, close - i + 1);
                    i--;
                }
            }
            return s;
        }

        /// <summary>Every <c>:FOR[X]</c> name of the given root node names (MM harvests them all: each counts as a
        /// present mod). Case-insensitive set.</summary>
        public static HashSet<string> ForNames(IEnumerable<string> rootNames)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rootNames == null) return set;
            foreach (var name in rootNames)
            foreach (var tag in Tags(name))
                if (tag.Key.Equals("FOR", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tag.Value))
                    set.Add(tag.Value.Trim());
            return set;
        }

        /// <summary>Whether a root node is dropped, in this order: a patch from a blocked folder; <c>:FOR</c> of a blocked
        /// name; with <paramref name="beforeAfterRule"/>, <c>:BEFORE</c>/<c>:AFTER</c> of a name neither available nor a
        /// FOR name (MM drops it anyway); <c>:BEFORE</c>/<c>:AFTER</c> of a blocked name. A non-patch root node and
        /// <c>:LAST</c> are never dropped. <paramref name="available"/> is Redux's available set (before blocking);
        /// <paramref name="forNames"/> should be <see cref="ForNames"/>.</summary>
        public static MmDrop RootPatchDrop(string rootName, string fileModName, bool isPatch, Func<string, bool> blocked,
            Func<string, bool> available, ICollection<string> forNames, bool beforeAfterRule)
        {
            if (!isPatch) return MmDrop.Keep;
            if (!string.IsNullOrWhiteSpace(fileModName) && blocked(fileModName.Trim()))
                return new MmDrop(MmDropReason.DropBlockedFolder, fileModName.Trim());
            string absent = null, blockedOrder = null;
            foreach (var tag in Tags(rootName))
            {
                if (tag.Value == null) continue;
                var mod = tag.Value.Trim();
                if (mod.Length == 0) continue;
                if (tag.Key.Equals("FOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (blocked(mod)) return new MmDrop(MmDropReason.DropForBlocked, mod);
                }
                else if (tag.Key.Equals("BEFORE", StringComparison.OrdinalIgnoreCase)
                         || tag.Key.Equals("AFTER", StringComparison.OrdinalIgnoreCase))
                {
                    // MM's own rule first: a mod that is not there drops the patch whatever the safety net says, so the
                    // safety net is only credited with the patches it alone drops
                    if (beforeAfterRule && !available(mod) && !(forNames != null && forNames.Contains(mod)))
                        absent = absent ?? mod;
                    else if (blocked(mod)) blockedOrder = blockedOrder ?? mod;
                }
            }
            if (absent != null) return new MmDrop(MmDropReason.DropBeforeAfterAbsent, absent);
            return blockedOrder != null ? new MmDrop(MmDropReason.DropBeforeAfterBlocked, blockedOrder) : MmDrop.Keep;
        }
    }
}
