using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.ModuleManager;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;

namespace KSP1PartMod.Game
{
    /// <summary>The ModuleManager glue on Redux's <c>Ksp1ConfigFinalizer</c> (plan (b), (c), Task 2). Everything happens in
    /// memory, during Redux's import; no cfg is written.
    /// <list type="bullet">
    /// <item><b>Plugin safety net</b>: a prefix on <c>ModIsAvailable</c> answers false for a blocked name (so
    /// <c>:NEEDS[X]</c> fails and <c>:NEEDS[!X]</c> holds), and a postfix on the root calls of
    /// <c>CopyWithSatisfiedNeeds</c> drops the root patches <see cref="MmPluginRule.RootPatchDrop"/> drops. The folder scan
    /// and the FOR harvest run once per <c>Finalize</c>, from its prefix (it has the raw file list).</item>
    /// <item><b>BEFORE/AFTER rule</b> (MM): the same postfix drops a patch whose BEFORE/AFTER names an absent mod.</item>
    /// <item><b>Arithmetic and value indices</b>: a prefix on <c>ApplyNodeContents</c> takes over every patch holding an
    /// operator, an index or a vector-component line, and applies all of its values with <see cref="MmValueEditor"/>
    /// (plain lines give Redux's result), then hands each child node to Redux's <c>ApplyChildPatch</c>. Any other patch
    /// goes to Redux untouched, after one scan of its value names that does not allocate.</item>
    /// <item><b>Node indices</b>: a prefix on <c>ApplyChildPatch</c> picks the n-th match of Redux's own
    /// <c>NodeMatches</c> and runs Redux's own <c>ApplyNodeContents</c> / <c>Clone</c> on it.</item>
    /// <item>A postfix on <c>Finalize</c> logs the <c>mm:</c> summary of the run (Redux caches the result: once per
    /// session).</item>
    /// </list>
    /// Every member is resolved by name (<see cref="MmAccess"/>); a missing one turns off only its feature, with one line
    /// per member. An exception in our code stops every patch for the session, with one line, and Redux's own code runs
    /// for that call; Redux's own exceptions reach Redux's own handlers, as before. Session-wide: armed at
    /// pre-initialization, before Redux's import and before the other import patches.</summary>
    internal static class MmPatches
    {
        public const string HarmonyId = "KSP1PartMod.MmPatches";
        private const int DropLinesLogged = 20;

        /// <summary>The <c>mm_*</c> switches of <c>animations.json</c>.</summary>
        internal sealed class Switches
        {
            public bool SafetyNet = true, Auto = true, BeforeAfter = true, Arithmetic = true, Index = true, FirstMatch;
            public IReadOnlyCollection<string> Curated = MmPluginRule.DefaultCurated.ToList();
            public IReadOnlyCollection<string> Keep = MmPluginRule.DefaultKeep.ToList();

            public static Switches From(AnimationSettings s) => new Switches
            {
                SafetyNet = s.MmPluginSafetyNet,
                Auto = s.MmPluginFoldersAuto,
                BeforeAfter = s.MmBeforeAfterNeedMod,
                Arithmetic = s.MmArithmetic,
                Index = s.MmIndexSelectors,
                FirstMatch = s.MmEditFirstMatch,
                Curated = s.MmPluginFolders,
                Keep = s.MmNeedsKeep,
            };
        }

        /// <summary>Per name: NEEDS lookups answered false (where Redux would have said true), patches dropped by FOR, by BEFORE/AFTER, from its folder.</summary>
        private sealed class NameCounts
        {
            public int Needs, For, BeforeAfter, Folder;
        }

        private static MmAccess _a;
        private static Switches _s = new Switches();
        private static bool _safety, _beforeAfter, _arith, _indexValues, _nodeIndex, _firstMatch, _broken;
        private static bool _valueEdits, _nodeEdits;
        private static int _depth, _dropLines;
        private static HashSet<string> _forNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _autoFolders = new List<string>();
        private static readonly Dictionary<string, NameCounts> PerName = new Dictionary<string, NameCounts>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Absent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static MmCounters _c = new MmCounters();
        private static readonly Func<string, bool> BlockedRule = IsBlocked;

        /// <summary>The summary lines of the last <c>Finalize</c> run (for the audit report header).</summary>
        internal static IReadOnlyList<string> LastSummary { get; private set; } = new List<string>();

        /// <summary>Counters of the current run (tests).</summary>
        internal static MmCounters Counters => _c;

        /// <summary>Tests: receives every line instead of the game log.</summary>
        internal static Action<string> Sink;

        internal static bool Broken => _broken;

        private static void Info(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogInfo(line);
        }

        private static void Warn(string line)
        {
            if (Sink != null) Sink(line);
            else KSP1PartModPlugin.Log?.LogWarning(line);
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            if (Sink != null) Sink(MmLines.PatchFailed(e.ToString()));
            else KSP1PartModPlugin.Log?.LogError(MmLines.PatchFailed(e.ToString()));
        }

        /// <summary>Back to the state before any arming (tests).</summary>
        internal static void Reset()
        {
            _a = null;
            _s = new Switches();
            _safety = _beforeAfter = _arith = _indexValues = _nodeIndex = _firstMatch = _broken = _valueEdits = _nodeEdits = false;
            _depth = _dropLines = 0;
            _forNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _autoFolders = new List<string>();
            PerName.Clear();
            Absent.Clear();
            _c = new MmCounters();
            LastSummary = new List<string>();
            ImportRootsOverride = null;
            FailWriteBackAfter = -1;
        }

        public static void TryApply(Harmony harmony, Switches switches)
        {
            Reset();
            _s = switches ?? new Switches();
            var a = MmAccess.TryBind(out _);
            _a = a;

            _safety = _s.SafetyNet && Ready(a, MmAccess.SafetyNetMembers, MmAccess.SafetyNetFeature);
            _beforeAfter = _s.BeforeAfter && Ready(a, MmAccess.BeforeAfterMembers, MmAccess.BeforeAfterFeature);
            var values = a.ValuesReady;
            if (!values)
            {
                if (_s.Arithmetic) Ready(a, MmAccess.ValueMembers, MmAccess.ArithmeticFeature);
                if (_s.Index) Ready(a, MmAccess.ValueMembers, MmAccess.IndexFeature);
                if (_s.FirstMatch) Ready(a, MmAccess.ValueMembers, "first-match edits");
            }
            _arith = _s.Arithmetic && values;
            _indexValues = _s.Index && values;
            _firstMatch = _s.FirstMatch && values;
            _nodeIndex = _s.Index && values && Ready(a, MmAccess.NodeIndexMembers, MmAccess.IndexFeature + " on child nodes");
            _valueEdits = _arith || _indexValues || _firstMatch;
            _nodeEdits = _nodeIndex || _firstMatch && a.NodeIndexReady;

            if (_safety || _beforeAfter || _valueEdits)
            {
                try
                {
                    harmony.Patch(a.Finalize, prefix: Method(nameof(BeforeFinalize)), postfix: Method(nameof(AfterFinalize)));
                    harmony.Patch(a.CopyWithSatisfiedNeeds, prefix: Method(nameof(BeforeCopy)), postfix: Method(nameof(AfterCopy)),
                        finalizer: Method(nameof(FinalCopy)));
                    if (_safety) harmony.Patch(a.ModIsAvailable, prefix: Method(nameof(BeforeModIsAvailable)));
                    if (_valueEdits) harmony.Patch(a.ApplyNodeContents, prefix: Method(nameof(BeforeApplyNodeContents)));
                    if (_nodeEdits) harmony.Patch(a.ApplyChildPatch, prefix: Method(nameof(BeforeApplyChildPatch)));
                }
                catch (Exception e)
                {
                    harmony.UnpatchAll(HarmonyId);
                    _safety = _beforeAfter = _arith = _indexValues = _nodeIndex = _firstMatch = _valueEdits = _nodeEdits = false;
                    Break(e);
                }
            }
            Info(MmLines.Armed(_safety, _arith, _indexValues, _firstMatch, _beforeAfter));
        }

        private static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(MmPatches), name);

        private static bool Ready(MmAccess a, IEnumerable<string> members, string feature)
        {
            var missing = a.MissingOf(members).ToList();
            foreach (var m in missing) Warn(MmLines.MemberMissing(m, feature));
            return missing.Count == 0;
        }

        // ---------------------------------------------------------------- Finalize

        private static void BeforeFinalize(IReadOnlyList<Ksp1ConfigFile> __0)
        {
            PerName.Clear();
            Absent.Clear();
            _c = new MmCounters();
            _depth = 0;
            _dropLines = 0;
            _forNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _autoFolders = new List<string>();
            if (_broken || __0 == null) return;
            try
            {
                var rootNames = new List<string>();
                var partMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in __0)
                {
                    if (file?.Node == null) continue;
                    foreach (var node in file.Node.Nodes)
                    {
                        rootNames.Add(node.Name);
                        if (!string.IsNullOrWhiteSpace(file.ModName) && IsPartRootName(node.Name)) partMods.Add(file.ModName.Trim());
                    }
                }
                _forNames = MmPluginRule.ForNames(rootNames);
                if (_safety && _s.Auto) _autoFolders = ScanAutoFolders(partMods);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void AfterFinalize()
        {
            try
            {
                var lines = new List<string>();
                if (_safety) SafetyNetLines(lines);
                if (_beforeAfter)
                    lines.Add(MmLines.BeforeAfterAbsent(Absent.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)));
                if (_arith)
                {
                    lines.Add(MmLines.Arithmetic(_c));
                    lines.Add(MmLines.VectorComponentsSkipped(_c.VectorComponentSkipped));
                }
                if (_indexValues || _nodeIndex) lines.Add(MmLines.Index(_c));
                if (_valueEdits)
                {
                    lines.Add(MmLines.UnindexedMultiMatch(_c.UnindexedMultiMatch, _firstMatch));
                    lines.Add(MmLines.Variables(_c.VariableValues));
                    lines.Add(MmLines.DeleteWildcards(_c.DeleteWildcards));
                }
                LastSummary = lines;
                foreach (var line in lines) Info(line);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void SafetyNetLines(List<string> lines)
        {
            var keep = _s.Keep ?? Array.Empty<string>();
            var curated = (_s.Curated ?? Array.Empty<string>()).Where(n => !keep.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            var automatic = _autoFolders.Where(n => !keep.Contains(n, StringComparer.OrdinalIgnoreCase) &&
                                                    !curated.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            var kept = keep.Where(n => (_s.Curated ?? Array.Empty<string>()).Contains(n, StringComparer.OrdinalIgnoreCase) ||
                                       _autoFolders.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            lines.Add(MmLines.BlockedNames(curated.Concat(automatic), curated.Count, automatic.Count, kept));
            foreach (var p in PerName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add(MmLines.BlockedName(p.Key, p.Value.Needs, p.Value.For, p.Value.BeforeAfter, p.Value.Folder));
        }

        /// <summary>A root node that makes a part: a plain <c>PART</c>, or a <c>+PART[…]</c> copy (a mod whose parts are all
        /// copies still has parts), NEEDS or not.</summary>
        internal static bool IsPartRootName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var s = name.Trim();
            if (s[0] == '+') s = s.Substring(1).TrimStart();
            else if (IsPatchName(s)) return false;
            var cut = s.IndexOfAny(new[] { ':', '[' });
            if (cut >= 0) s = s.Substring(0, cut).Trim();
            return s.Equals("PART", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A root patch, as Redux reads it: the trimmed name starts with a command character.</summary>
        internal static bool IsPatchName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var s = name.TrimStart();
            return s.Length > 0 && MmKeyEdit.CommandOf(s[0]) != MmCommand.None;
        }

        /// <summary>Tests: the import roots to scan instead of Redux's.</summary>
        internal static Func<IEnumerable<string>> ImportRootsOverride;

        /// <summary>Top folders of the import roots (and of a <c>GameData</c> folder in one) that hold a <c>.dll</c> and no
        /// imported <c>PART</c>.</summary>
        private static List<string> ScanAutoFolders(HashSet<string> partMods)
        {
            var found = new List<string>();
            IEnumerable<string> roots;
            try
            {
                roots = ImportRootsOverride != null
                    ? ImportRootsOverride().ToList()
                    : Ksp1PartModDiscovery.DiscoverImportRoots().Select(r => r.ImportRoot).ToList();
            }
            catch (Exception e)
            {
                Warn("[KSP1PartMod] mm: import roots not read, no automatic plugin folder: " + e.Message);
                return found;
            }
            foreach (var root in roots)
            {
                try
                {
                    var dirs = new List<string>(Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly));
                    foreach (var d in dirs.ToList())
                    {
                        if (!IsGameData(d)) continue;
                        // GameData itself is never a candidate: its loose cfgs (Redux names their mod "GameData") are the
                        // player's own patches, and it holds ModuleManager's DLL in any full KSP1 copy.
                        dirs.Remove(d);
                        dirs.AddRange(Directory.GetDirectories(d, "*", SearchOption.TopDirectoryOnly));
                    }
                    foreach (var dir in dirs)
                    {
                        var name = Path.GetFileName(dir);
                        if (string.IsNullOrWhiteSpace(name) || found.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        var hasPart = partMods.Contains(name);
                        if (!hasPart && MmPluginRule.AutoFolder(HasDll(dir), false)) found.Add(name);
                    }
                }
                catch (Exception e)
                {
                    Warn("[KSP1PartMod] mm: import root '" + root + "' not scanned for plugin folders: " + e.Message);
                }
            }
            return found;
        }

        private static bool IsGameData(string dir) =>
            string.Equals(Path.GetFileName(dir.TrimEnd('/', '\\')), "GameData", StringComparison.OrdinalIgnoreCase);

        private static bool HasDll(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).Any();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsBlocked(string name) =>
            _safety && MmPluginRule.Blocked(name, _s.Curated, _s.Keep, _autoFolders);

        private static NameCounts CountsOf(string name)
        {
            var key = MmPluginRule.FirstSegment(name);
            if (!PerName.TryGetValue(key, out var c)) PerName[key] = c = new NameCounts();
            return c;
        }

        // ---------------------------------------------------------------- safety net

        /// <summary>Blocked: false, whatever Redux's set or the folders say. A term is counted only when Redux would have
        /// found the mod (set lookup, or the folder of a path form).</summary>
        private static bool BeforeModIsAvailable(string __0, string __1, HashSet<string> __2, ref bool __result)
        {
            if (_broken || !_safety) return true;
            try
            {
                if (!IsBlocked(__0)) return true;
                if (WouldBeAvailable(__0, __1, __2)) CountsOf(__0).Needs++;
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        private static bool WouldBeAvailable(string mod, string gameDataRoot, HashSet<string> available)
        {
            if (mod == null) return false;
            if (available != null && available.Contains(mod)) return true;
            if (mod.IndexOf('/') < 0 && mod.IndexOf('\\') < 0 || string.IsNullOrEmpty(gameDataRoot)) return false;
            try
            {
                var parts = new[] { gameDataRoot }.Concat(mod.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
                return Directory.Exists(Path.Combine(parts));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void BeforeCopy()
        {
            _depth++;
        }

        /// <summary>Root calls only (depth 1 here): a root patch the rule drops becomes null, as a failed NEEDS does. Runs
        /// after Redux's own NEEDS test, so only the patches the rule alone drops are counted.</summary>
        private static void AfterCopy(Ksp1ConfigNode __0, Ksp1ConfigFile __1, HashSet<string> __2, ref object __result)
        {
            if (_broken || _depth != 1 || __result == null || __0 == null) return;
            try
            {
                if (_safety || _beforeAfter)
                {
                    var available = __2;
                    var drop = MmPluginRule.RootPatchDrop(__0.Name, __1?.ModName, IsPatchName(__0.Name), BlockedRule,
                        m => available != null && available.Contains(m), _forNames, _beforeAfter);
                    if (drop.Reason != MmDropReason.Keep)
                    {
                        Record(drop, __0.Name, __1?.Path);
                        __result = null;
                        return;
                    }
                }
                if (_valueEdits) CountVariables(__result);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        private static void FinalCopy()
        {
            _depth--;
        }

        private static void Record(MmDrop drop, string rootName, string path)
        {
            switch (drop.Reason)
            {
                case MmDropReason.DropBlockedFolder: CountsOf(drop.Mod).Folder++; break;
                case MmDropReason.DropForBlocked: CountsOf(drop.Mod).For++; break;
                case MmDropReason.DropBeforeAfterBlocked: CountsOf(drop.Mod).BeforeAfter++; break;
                case MmDropReason.DropBeforeAfterAbsent:
                    Absent.TryGetValue(drop.Mod, out var n);
                    Absent[drop.Mod] = n + 1;
                    break;
            }
            if (_dropLines++ < DropLinesLogged)
                Info($"[KSP1PartMod] mm: dropped '{rootName}' from '{path}': {drop.Reason} {drop.Mod}" +
                     (_dropLines == DropLinesLogged ? " (further drops are only counted)" : ""));
        }

        private static void CountVariables(object node)
        {
            var a = _a;
            var n = a.Values(node).Count;
            for (var i = 0; i < n; i++)
            {
                if (MmValueEditor.HasVariable(a.ValueRaw(node, i))) _c.VariableValues++;
                if (MmKeyEdit.IsDeleteWildcard(a.ValueName(node, i))) _c.DeleteWildcards++;
            }
            var children = a.Nodes(node);
            for (var i = 0; i < children.Count; i++) CountVariables(children[i]);
        }

        // ---------------------------------------------------------------- values

        /// <summary>The cheap pre-scan: might a line of this patch need us? No allocation.</summary>
        internal static bool MightNeedUs(object patch)
        {
            var a = _a;
            var n = a.Values(patch).Count;
            for (var i = 0; i < n; i++)
            {
                var name = a.ValueName(patch, i);
                if (MmKeyEdit.MightNeedUs(name) || _firstMatch && IsEditName(name)) return true;
            }
            return false;
        }

        private static bool IsEditName(string name)
        {
            if (name == null) return false;
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsWhiteSpace(name[i])) continue;
                return name[i] == '@' || name[i] == '%';
            }
            return false;
        }

        /// <summary>How each line of a patch is applied, and whether the patch is ours. A line whose feature is off is
        /// read as Redux reads it (<see cref="MmKeyEdit.AsRedux"/>).</summary>
        internal static MmKeyEdit[] Plan(object patch, out bool ours)
        {
            var a = _a;
            var n = a.Values(patch).Count;
            var edits = new MmKeyEdit[n];
            ours = false;
            for (var i = 0; i < n; i++)
            {
                var name = a.ValueName(patch, i);
                var raw = a.ValueRaw(patch, i);
                var e = MmKeyEdit.Parse(name, raw);
                var arithmeticLine = MmKeyEdit.IsSupported(e.Operator) || e.CountedOnly || e.SkippedVectorEdit;
                var indexLine = e.Index.Kind != MmIndexKind.None;
                if (arithmeticLine && !_arith || indexLine && !_indexValues) e = MmKeyEdit.AsRedux(name, raw);
                else if (arithmeticLine || indexLine) ours = true;
                else if (_firstMatch && !e.PassedBack && (e.Command == MmCommand.Edit || e.Command == MmCommand.CreateOrEdit)) ours = true;
                edits[i] = e;
            }
            return edits;
        }

        private static bool BeforeApplyNodeContents(object __0, object __1, object __2)
        {
            if (_broken || !_valueEdits || __0 == null || __1 == null) return true;
            var a = _a;
            List<KeyValuePair<string, string>> snapshot = null;
            var cleared = false;
            try
            {
                if (!MightNeedUs(__1)) return true;
                var edits = Plan(__1, out var ours);
                if (!ours) return true;
                var targetValues = a.Values(__0);
                snapshot = new List<KeyValuePair<string, string>>(targetValues.Count);
                for (var i = 0; i < targetValues.Count; i++)
                    snapshot.Add(new KeyValuePair<string, string>(a.ValueName(__0, i), a.ValueRaw(__0, i)));
                var work = new List<KeyValuePair<string, string>>(snapshot);
                foreach (var edit in edits) MmValueEditor.Apply(work, edit, _firstMatch, _c);
                cleared = true;
                targetValues.Clear();
                var written = 0;
                foreach (var v in work)
                {
                    if (FailWriteBackAfter >= 0 && written++ >= FailWriteBackAfter)
                        throw new InvalidOperationException("test: write-back failure");
                    a.AddValue(__0, v.Key, v.Value);
                }
            }
            catch (Exception e)
            {
                Break(e);
                if (cleared) Restore(__0, snapshot);
                return true; // Redux applies the patch to the node as it found it
            }
            // Redux's own code from here: its exceptions reach Redux's handler, as before.
            var children = a.Nodes(__1);
            for (var i = 0; i < children.Count; i++) a.CallApplyChildPatch(__0, children[i], __2);
            return false;
        }

        /// <summary>Puts the values back as Redux had them, after a failed write-back.</summary>
        private static void Restore(object node, List<KeyValuePair<string, string>> snapshot)
        {
            try
            {
                _a.Values(node).Clear();
                foreach (var v in snapshot) _a.AddValue(node, v.Key, v.Value);
            }
            catch (Exception e)
            {
                Warn("[KSP1PartMod] mm: values not restored after a failed edit: " + e.Message);
            }
        }

        /// <summary>Tests: the write-back fails after this many values (negative: never).</summary>
        internal static int FailWriteBackAfter = -1;

        // ---------------------------------------------------------------- child nodes

        private static bool BeforeApplyChildPatch(object __0, object __1, object __2)
        {
            if (_broken || !_nodeEdits || __0 == null || __1 == null) return true;
            var a = _a;
            MmCommand command;
            List<object> picked;
            try
            {
                var name = a.NodeName(__1);
                var bare = name;
                var index = MmIndex.None;
                var hasIndex = _nodeIndex && MmNodeIndex.Split(name, out bare, out index);
                if (!hasIndex)
                {
                    if (!(_firstMatch && IsEditName(name))) return true;
                    bare = name;
                    index = MmIndex.None;
                }
                var descriptor = a.CallParseName(bare);
                if (descriptor == null || string.IsNullOrWhiteSpace(a.BaseNameOf(descriptor))) return true;
                var c = a.Command(descriptor);
                if (c == null) return true;
                command = c.Value;
                var edit = command == MmCommand.Edit || command == MmCommand.CreateOrEdit;
                if (!edit && command != MmCommand.Copy && command != MmCommand.Delete) return true; // add: no index in MM
                if (!hasIndex && !edit) return true;

                var nodes = a.Nodes(__0);
                var matches = new List<object>();
                for (var i = 0; i < nodes.Count; i++)
                    if (a.CallNodeMatches(nodes[i], descriptor)) matches.Add(nodes[i]);
                if (matches.Count == 0 && command == MmCommand.CreateOrEdit) return true; // Redux creates it, as before
                if (!hasIndex && matches.Count > 1) _c.UnindexedMultiMatch++;
                var pick = MmNodeIndex.Pick(matches.Count, index, _firstMatch);
                if (pick.OutOfRange)
                {
                    _c.IndexOutOfRange++;
                    return false;
                }
                picked = matches.GetRange(pick.Start, pick.Count);
                if (hasIndex && picked.Count > 0) _c.IndexNodeEdits++;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
            // Redux's own operations on the picked nodes.
            switch (command)
            {
                case MmCommand.Copy:
                    foreach (var node in picked)
                    {
                        var copy = a.CallClone(node);
                        a.CallApplyNodeContents(copy, __1, __2);
                        a.Nodes(__0).Add(copy);
                    }
                    break;
                case MmCommand.Delete:
                    foreach (var node in picked) a.Nodes(__0).Remove(node);
                    break;
                default:
                    foreach (var node in picked) a.CallApplyNodeContents(node, __1, __2);
                    break;
            }
            return false;
        }
    }
}
