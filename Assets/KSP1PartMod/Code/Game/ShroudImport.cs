using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using Redux.Ksp1Import.Assets;
using Redux.Ksp1Import.Config;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>Keeps the KSP1 <c>ModuleJettison</c> shroud meshes of imported parts (<c>ksp1_engine_shrouds</c>, 0.7.0):
    /// <list type="bullet">
    /// <item>a prefix on <c>Ksp1EngineShroudBuilder.AddFairingModule</c> skips it, so a KSP1 part gets no
    /// <c>Data_Fairing</c> and <c>ConfigurePrefab</c> returns before it destroys the shroud;</item>
    /// <item>a postfix on the private <c>Ksp1PartTranslator.BuildConfiguredPrefab</c> (import and rebuild) hides the
    /// shroud roots of the prefab, after Redux's node-size and drag-cube steps;</item>
    /// <item>a postfix on <c>PartBehavior.ActivateAllModules</c> tells our module that its part was staged.</item>
    /// </list>
    /// Only <c>GameObject.activeSelf</c> is written, never a material or texture. Every member is resolved by name; the
    /// two import patches are armed together or not at all. Never throws into the game: after one failure the import
    /// patches stop for the session (Redux's own behaviour, kept quiet by <c>fix_fairing_init</c>), with one log line.
    /// Session-wide: armed at pre-initialization, before Redux's import.</summary>
    internal static class ShroudImport
    {
        public const string HarmonyId = "KSP1PartMod.ShroudImport";
        public const string BuilderTypeName = "Redux.Ksp1Import.Ksp1EngineShroudBuilder";
        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";
        public const string ContextTypeName = "Redux.Ksp1Import.Modules.Ksp1PartImportContext";

        /// <summary>The members, resolved by name against the loaded game.</summary>
        internal sealed class Targets
        {
            public MethodInfo AddFairingModule;      // static void (Ksp1ConfigNode, PartData, Ksp1PartImportContext)
            public MethodInfo GetShroudNames;        // static IReadOnlyList<string> (Ksp1ConfigNode)
            public MethodInfo BuildConfiguredPrefab; // private instance GameObject (file, Ksp1ConfigNode, core, report, string partName, …), 7 parameters
            public MethodInfo ActivateAllModules;    // PartBehavior.ActivateAllModules()

            /// <summary>The import members missing, in the order of the log (empty when all are found).</summary>
            public readonly List<string> MissingImport = new List<string>();
        }

        internal static Targets Resolve()
        {
            var t = new Targets();
            var builder = AccessTools.TypeByName(BuilderTypeName);
            var context = AccessTools.TypeByName(ContextTypeName);
            if (builder == null || context == null)
            {
                t.MissingImport.Add(builder == null ? BuilderTypeName : ContextTypeName);
            }
            else
            {
                t.AddFairingModule = AccessTools.Method(builder, "AddFairingModule",
                    new[] { typeof(Ksp1ConfigNode), typeof(PartData), context });
                if (t.AddFairingModule == null || !t.AddFairingModule.IsStatic || t.AddFairingModule.ReturnType != typeof(void))
                {
                    t.AddFairingModule = null;
                    t.MissingImport.Add("Ksp1EngineShroudBuilder.AddFairingModule");
                }
                t.GetShroudNames = AccessTools.Method(builder, "GetFairingShroudTransformNames", new[] { typeof(Ksp1ConfigNode) });
                if (t.GetShroudNames == null || !t.GetShroudNames.IsStatic ||
                    !typeof(IEnumerable<string>).IsAssignableFrom(t.GetShroudNames.ReturnType))
                {
                    t.GetShroudNames = null;
                    t.MissingImport.Add("Ksp1EngineShroudBuilder.GetFairingShroudTransformNames");
                }
            }

            var translator = AccessTools.TypeByName(TranslatorTypeName);
            if (translator != null)
            {
                var candidates = AccessTools.GetDeclaredMethods(translator).Where(m =>
                {
                    if (m.Name != "BuildConfiguredPrefab" || m.IsStatic || m.ReturnType != typeof(GameObject)) return false;
                    var ps = m.GetParameters();
                    return ps.Length == 7 && ps[1].ParameterType == typeof(Ksp1ConfigNode) && ps[4].ParameterType == typeof(string);
                }).ToList();
                if (candidates.Count == 1) t.BuildConfiguredPrefab = candidates[0];
            }
            if (t.BuildConfiguredPrefab == null) t.MissingImport.Add("Ksp1PartTranslator.BuildConfiguredPrefab");

            var activate = AccessTools.Method(typeof(PartBehavior), "ActivateAllModules", Type.EmptyTypes);
            if (activate != null && !activate.IsStatic) t.ActivateAllModules = activate;
            return t;
        }

        /// <summary>The import patches are armed (Redux adds no <c>Module_Fairing</c> to KSP1 parts this session).</summary>
        internal static bool IsArmed { get; private set; }

        /// <summary>Staging reaches <see cref="Module_Ksp1Animations.OnPartStaged"/> through <c>ActivateAllModules</c>;
        /// when false, the runtime uses the first engine ignition instead.</summary>
        internal static bool StagingSignalArmed { get; private set; }

        private static Func<Ksp1ConfigNode, IEnumerable<string>> _shroudNames;
        private static readonly Tally Counts = new Tally();
        private static bool _broken, _stagingBroken, _summaryLogged;

        public static void TryApply(Harmony harmony, ShroudArming arming)
        {
            var log = KSP1PartModPlugin.Log;
            if (arming == ShroudArming.OffBySwitch)
            {
                log.LogInfo(ShroudLines.OffBySwitch);
                return;
            }
            if (arming == ShroudArming.OffAnimationsDisabled)
            {
                log.LogInfo(ShroudLines.OffAnimationsDisabled);
                return;
            }

            var targets = Resolve();
            if (targets.MissingImport.Count > 0)
            {
                foreach (var member in targets.MissingImport) log.LogWarning(ShroudLines.MemberNotFound(member));
                return; // neither half: kept meshes without the prefab hide would show forever
            }
            var getNames = targets.GetShroudNames;
            _shroudNames = node => (IEnumerable<string>)getNames.Invoke(null, new object[] { node });
            try
            {
                harmony.Patch(targets.AddFairingModule, prefix: new HarmonyMethod(typeof(ShroudImport), nameof(BeforeAddFairingModule)));
                harmony.Patch(targets.BuildConfiguredPrefab, postfix: new HarmonyMethod(typeof(ShroudImport), nameof(AfterBuildConfiguredPrefab)));
                IsArmed = true;
            }
            catch (Exception e)
            {
                // Never one half without the other.
                harmony.UnpatchAll(HarmonyId);
                IsArmed = false;
                log.LogError(ShroudLines.PatchFailed(e.ToString()));
                return;
            }
            log.LogInfo(ShroudLines.Armed);

            if (targets.ActivateAllModules == null)
            {
                log.LogWarning(ShroudLines.StagingSignalMissing);
                return;
            }
            try
            {
                harmony.Patch(targets.ActivateAllModules, postfix: new HarmonyMethod(typeof(ShroudImport), nameof(AfterActivateAllModules)));
                StagingSignalArmed = true;
                log.LogInfo(ShroudLines.StagingSignalArmed);
            }
            catch (Exception e)
            {
                log.LogError("[KSP1PartMod] shroud: staging signal not armed: " + e);
                log.LogWarning(ShroudLines.StagingSignalMissing);
            }
        }

        /// <summary>Once per session, from the animation load step (the import is over by then).</summary>
        public static void LogImportSummaryOnce()
        {
            if (!IsArmed || _summaryLogged) return;
            _summaryLogged = true;
            KSP1PartModPlugin.Log?.LogInfo(ShroudLines.ImportSummary(Counts.Kept, Counts.WithMissing, Counts.Rebuilds));
        }

        /// <summary>False skips Redux's method: no <c>Data_Fairing</c>, so <c>ConfigurePrefab</c> keeps the meshes.</summary>
        private static bool BeforeAddFairingModule(Ksp1ConfigNode __0)
        {
            if (_broken) return true;
            try
            {
                var names = _shroudNames(__0)?.ToList();
                if (names == null || names.Count == 0) return true; // Redux adds nothing either
                var part = __0?.GetValue("name") ?? "?";
                if (Counts.Keep(part)) KSP1PartModPlugin.Log?.LogInfo(ShroudLines.ModuleNotAdded(part, names));
                return false;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        /// <summary>The prefab as Redux built it, node sizes and drag cube included: its shroud roots are hidden.</summary>
        private static void AfterBuildConfiguredPrefab(Ksp1ConfigNode __1, string __4, GameObject __result)
        {
            if (_broken || __result == null || __1 == null) return;
            try
            {
                var names = ShroudParser.ConvertibleNames(Ksp1NodeAdapter.Modules(__1));
                if (names.Count == 0) return;
                var part = __4 ?? __1.GetValue("name") ?? "?";
                var hidden = HideRoots(__result, names, out var missing);
                if (!Counts.Built(part, missing.Count > 0)) return; // a rebuild: counted, silent
                if (hidden > 0) KSP1PartModPlugin.Log?.LogInfo(ShroudLines.HiddenInPrefab(part, hidden));
                if (missing.Count > 0) KSP1PartModPlugin.Log?.LogWarning(ShroudLines.NotFoundInModel(part, missing));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary><c>SetActive(false)</c> on the outermost transforms under <paramref name="prefab"/> (never the prefab
        /// itself) whose name is one of <paramref name="names"/>, compared ignoring case as Redux does.</summary>
        /// <returns>The number of transforms hidden.</returns>
        /// <param name="missing">The names matched by no transform, in their order.</param>
        internal static int HideRoots(GameObject prefab, ICollection<string> names, out List<string> missing)
        {
            var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var matches = new List<(string path, Transform transform)>();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(prefab.transform, "", nameSet, matches, found);
            missing = names.Where(n => !found.Contains(n)).ToList();
            var roots = new HashSet<string>(ShroudImportRule.RootsToHide(names, matches.Select(m => m.path)), StringComparer.Ordinal);
            var hidden = 0;
            foreach (var (path, transform) in matches)
            {
                if (!roots.Contains(path)) continue;
                // Two siblings may share a name, hence a path: both are roots.
                transform.gameObject.SetActive(false);
                hidden++;
            }
            return hidden;
        }

        private static void Collect(Transform parent, string prefix, HashSet<string> names, List<(string, Transform)> matches, HashSet<string> found)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var path = prefix.Length == 0 ? child.name : prefix + "/" + child.name;
                if (names.Contains(child.name))
                {
                    matches.Add((path, child));
                    found.Add(child.name);
                }
                Collect(child, path, names, matches, found);
            }
        }

        /// <summary>The part's stage was activated: KSP1 parts with our module hear it.</summary>
        private static void AfterActivateAllModules(PartBehavior __instance)
        {
            if (_stagingBroken || __instance == null) return;
            try
            {
                var model = __instance.Model;
                var partName = model?.PartData?.partName;
                if (partName == null || !Ksp1ImportedPartRegistry.IsImportedPart(partName)) return;
                var module = __instance.GetModule<Module_Ksp1Animations>();
                if (module != null) module.OnPartStaged();
            }
            catch (Exception e)
            {
                _stagingBroken = true;
                KSP1PartModPlugin.Log?.LogError(ShroudLines.PatchFailed(e.ToString()));
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            KSP1PartModPlugin.Log?.LogError(ShroudLines.PatchFailed(e.ToString()));
        }

        /// <summary>What the import did, by part name, for the summary line and the once-per-part lines.</summary>
        internal sealed class Tally
        {
            private readonly HashSet<string> _kept = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _built = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _withMissing = new HashSet<string>(StringComparer.Ordinal);

            public int Kept => _kept.Count;
            public int WithMissing => _withMissing.Count;
            public int Rebuilds { get; private set; }

            /// <summary>Redux's <c>Module_Fairing</c> skipped for <paramref name="part"/>; true the first time.</summary>
            public bool Keep(string part) => _kept.Add(part);

            /// <summary>A prefab built for <paramref name="part"/>; true the first time, false for a rebuild (counted).</summary>
            public bool Built(string part, bool missing)
            {
                if (missing) _withMissing.Add(part);
                if (_built.Add(part)) return true;
                Rebuilds++;
                return false;
            }
        }
    }
}
