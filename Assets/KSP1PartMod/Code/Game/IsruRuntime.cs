using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Modules;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>The runtime of converted KSP1 drills and scanners (converters and drills plan, Task 4), armed at
    /// pre-initialization when <c>ksp1_drills</c> or a scanner switch is on. Only for the parts our importers converted
    /// (<see cref="NativeModules.Isru"/>, by part name); Redux's own parts are never touched.
    /// <list type="bullet">
    /// <item><b>ground probe</b>: a prefix on <c>Module_Mine.OnInitialize</c> (VAB and flight) sets the private
    /// <c>vfxArmTopTransform</c> to the part's KSP1 <c>ImpactTransform</c> and <c>vfxArmBottomTransform</c> to a child
    /// <c>ksp1_impact_end</c> made once under it, <c>ImpactRange</c> metres along its +Z (S: KSP1 probes along the
    /// transform's forward axis). Not found: the fields stay null;</item>
    /// <item><b>deploy bridge</b>: a postfix on <c>Module_Mine.OnInitialize</c> and <c>Module_ResourceScanner.OnInitialize</c>
    /// gives a module that found no <c>Data_Deployable</c> one of ours, a plain instance that no part, PAM or action group
    /// knows. <c>Module_Ksp1Animations</c> of the same part drives it from its first Group entry with a deploy clip; with
    /// none, it is <c>Extended</c> once. A drill gets one only when its arm transforms were set (ruling of 2026-10-28:
    /// Redux's ground check reads them on every frame once the drill is landed and extended);</item>
    /// <item>a prefix on the private <c>Module_Mine.CheckCollisions</c>, for our drills only: null arms read as "no ground"
    /// (never reached while the bridge rule holds, a guard), and once per drill and flight, after 50 checks in
    /// <c>Operational</c>, the <c>drilling on</c> line from the native data's request flows and the ground hit.</item>
    /// </list>
    /// Visual and native only: nothing here writes gameplay data beyond what the native modules do with the deploy state.
    /// A missing member turns its feature off (one line). A patch that throws stops every patch here for the session (one
    /// line) and Redux's code runs as is.</summary>
    internal static class IsruRuntime
    {
        public const string HarmonyId = "KSP1PartMod.IsruRuntime";
        public const string ImpactEndName = "ksp1_impact_end";
        /// <summary>Ground checks in <c>Operational</c> before the drilling line (Redux checks once per fixed update).</summary>
        internal const int OperationalChecksBeforeLine = 50;

        private sealed class DrillState
        {
            public string Part;
            public bool ArmsSet;
            public int Operational;
            public bool Logged;
        }

        private static readonly ConditionalWeakTable<Module_Mine, DrillState> Drills = new ConditionalWeakTable<Module_Mine, DrillState>();
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object Lock = new object();

        private static AccessTools.FieldRef<Module_Mine, Transform> _top, _bottom;
        private static AccessTools.FieldRef<Module_Mine, Data_Deployable> _mineDeployable;
        private static AccessTools.FieldRef<Module_Mine, Data_Mine> _mineData;
        private static AccessTools.FieldRef<Module_Mine, RaycastHit> _hit;
        private static AccessTools.FieldRef<Module_ResourceScanner, Data_Deployable> _scannerDeployable;
        private static bool _impactOn, _mineBridgeOn, _scannerBridgeOn, _broken, _animationsEnabled = true;

        internal static bool ImpactTransformsOn => _impactOn;
        internal static bool DeployBridgeOn => _mineBridgeOn || _scannerBridgeOn;

        /// <summary>Arms what the settings and the game's members allow. Returns true when a patch was applied.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            _animationsEnabled = settings.Enabled;
            var drills = settings.Ksp1Drills;
            var scanners = settings.Ksp1SurveyScanners || settings.Ksp1ResourceScanners;
            var activeClips = settings.Enabled && settings.IsruActiveAnimation;
            if (!drills && !scanners)
            {
                NativeModules.Info(IsruLines.Armed(false, false, activeClips));
                return false;
            }
            var patched = false;
            try
            {
                if (drills)
                {
                    var init = AccessTools.DeclaredMethod(typeof(Module_Mine), "OnInitialize", Type.EmptyTypes);
                    var check = AccessTools.DeclaredMethod(typeof(Module_Mine), "CheckCollisions", Type.EmptyTypes);
                    var top = Field<Module_Mine, Transform>("vfxArmTopTransform");
                    var bottom = Field<Module_Mine, Transform>("vfxArmBottomTransform");
                    var data = Field<Module_Mine, Data_Mine>("_dataMine");
                    var hit = Field<Module_Mine, RaycastHit>("_hit");
                    var deployable = Field<Module_Mine, Data_Deployable>("_dataDeployable");
                    if (init == null) NativeModules.Warn(IsruLines.MemberNotFound("Module_Mine.OnInitialize", "impact transforms and deploy bridge"));
                    else if (check == null || check.ReturnType != typeof(bool))
                        NativeModules.Warn(IsruLines.MemberNotFound("Module_Mine.CheckCollisions", "impact transforms and deploy bridge"));
                    else if (top == null || bottom == null || data == null || hit == null)
                        NativeModules.Warn(IsruLines.MemberNotFound(
                            top == null ? "Module_Mine.vfxArmTopTransform" : bottom == null ? "Module_Mine.vfxArmBottomTransform"
                            : data == null ? "Module_Mine._dataMine" : "Module_Mine._hit", "impact transforms and deploy bridge"));
                    else
                    {
                        _top = top;
                        _bottom = bottom;
                        _mineData = data;
                        _hit = hit;
                        _mineDeployable = deployable;
                        _impactOn = true;
                        _mineBridgeOn = deployable != null;
                        if (deployable == null) NativeModules.Warn(IsruLines.MemberNotFound("Module_Mine._dataDeployable", "drill deploy bridge"));
                        harmony.Patch(init, prefix: new HarmonyMethod(typeof(IsruRuntime), nameof(BeforeMineInit)),
                            postfix: new HarmonyMethod(typeof(IsruRuntime), nameof(AfterMineInit)));
                        harmony.Patch(check, prefix: new HarmonyMethod(typeof(IsruRuntime), nameof(BeforeCheckCollisions)));
                        patched = true;
                    }
                }
                if (scanners)
                {
                    var init = AccessTools.DeclaredMethod(typeof(Module_ResourceScanner), "OnInitialize", Type.EmptyTypes);
                    var deployable = Field<Module_ResourceScanner, Data_Deployable>("_dataDeployable");
                    if (init == null) NativeModules.Warn(IsruLines.MemberNotFound("Module_ResourceScanner.OnInitialize", "scanner deploy bridge"));
                    else if (deployable == null) NativeModules.Warn(IsruLines.MemberNotFound("Module_ResourceScanner._dataDeployable", "scanner deploy bridge"));
                    else
                    {
                        _scannerDeployable = deployable;
                        _scannerBridgeOn = true;
                        harmony.Patch(init, postfix: new HarmonyMethod(typeof(IsruRuntime), nameof(AfterScannerInit)));
                        patched = true;
                    }
                }
            }
            catch (Exception e)
            {
                Break(e);
                harmony.UnpatchAll(HarmonyId);
                return false;
            }
            NativeModules.Info(IsruLines.Armed(_impactOn, DeployBridgeOn, activeClips));
            return patched;
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name) where T : class
        {
            var f = AccessTools.Field(typeof(T), name);
            return f == null || f.IsStatic || f.FieldType != typeof(F) ? null : AccessTools.FieldRefAccess<T, F>(f);
        }

        // ---------------------------------------------------------------- patches

        internal static void BeforeMineInit(Module_Mine __instance)
        {
            if (_broken || __instance == null) return;
            try
            {
                var part = PartNameOf(__instance);
                if (!NativeModules.Isru.TryGetDrill(part, out var impact))
                {
                    NotInRecord(part, nameof(Module_Mine));
                    return;
                }
                var state = new DrillState { Part = part };
                Drills.Remove(__instance);
                Drills.Add(__instance, state);
                state.ArmsSet = SetArms(__instance, part, impact);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        internal static void AfterMineInit(Module_Mine __instance)
        {
            if (_broken || __instance == null || !_mineBridgeOn) return;
            try
            {
                // Ruling: no bridge without the arms (the drill then stays NotDeployed, never reaching Redux's ground check).
                if (!Drills.TryGetValue(__instance, out var state) || !state.ArmsSet) return;
                if (_mineDeployable(__instance) != null) return; // Redux gave the part a deployable: kept
                var bridge = new Data_Deployable();
                _mineDeployable(__instance) = bridge;
                Attach(__instance, bridge, nameof(Module_Mine), state.Part);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        internal static void AfterScannerInit(Module_ResourceScanner __instance)
        {
            if (_broken || __instance == null) return;
            try
            {
                var part = PartNameOf(__instance);
                if (!NativeModules.Isru.IsScanner(part))
                {
                    NotInRecord(part, nameof(Module_ResourceScanner));
                    return;
                }
                if (_scannerDeployable(__instance) != null) return;
                var bridge = new Data_Deployable();
                _scannerDeployable(__instance) = bridge;
                Attach(__instance, bridge, nameof(Module_ResourceScanner), part);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>False skips Redux's method (<paramref name="__result"/> false: no ground).</summary>
        internal static bool BeforeCheckCollisions(Module_Mine __instance, ref bool __result)
        {
            if (_broken) return true;
            try
            {
                if (!Drills.TryGetValue(__instance, out var state)) return true; // not ours
                if (_top(__instance) == null || _bottom(__instance) == null)
                {
                    __result = false;
                    return false;
                }
                if (!state.Logged)
                {
                    var data = _mineData(__instance);
                    if (data != null && data.status == MineState.Operational)
                    {
                        if (++state.Operational >= OperationalChecksBeforeLine)
                        {
                            state.Logged = true;
                            LogDrilling(__instance, state.Part, data);
                        }
                    }
                    else state.Operational = 0;
                }
                return true;
            }
            catch (Exception e)
            {
                Break(e);
                return true;
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Sets the two arm transforms from the KSP1 impact data. False (and the fields untouched) when the transform
        /// is not on the part.</summary>
        private static bool SetArms(Module_Mine mine, string part, DrillImpact impact)
        {
            if (!_impactOn) return false;
            var top = string.IsNullOrEmpty(impact.Transform) ? null : FindDeep(mine.gameObject.transform, impact.Transform);
            var mode = mine.PartBackingMode.ToString();
            if (top == null)
            {
                LogOnce("probe|" + part + "|" + mode, IsruLines.DrillProbeNotFound(part, string.IsNullOrEmpty(impact.Transform) ? "(none)" : impact.Transform), warn: true);
                return false;
            }
            var end = top.Find(ImpactEndName);
            if (end == null)
            {
                end = new GameObject(ImpactEndName).transform;
                end.SetParent(top, false);
            }
            end.localPosition = ImpactEnd(impact.Range, top.lossyScale.z);
            end.localRotation = Quaternion.identity;
            _top(mine) = top;
            _bottom(mine) = end;
            LogOnce("probe|" + part + "|" + mode, IsruLines.DrillProbeFound(part, impact.Transform, impact.Range));
            return true;
        }

        /// <summary>The local position of the probe's end: <paramref name="range"/> metres in the world along the transform's
        /// <c>forward</c>, whatever its scale. The scale is signed: on a mirrored part (negative z scale) the local end is negative,
        /// so the world end stays along <c>forward</c>, as KSP1's ray (which ignores scale) does.</summary>
        internal static Vector3 ImpactEnd(double range, float lossyScaleZ)
        {
            return new Vector3(0f, 0f, (float)(Math.Abs(lossyScaleZ) > 1e-6f ? range / lossyScaleZ : range));
        }

        /// <summary>One line per session and part when a KSP1-imported part carries a native drill or scanner that our
        /// record does not hold (a key miss, or a module Redux or another mod added): the runtime leaves it as is.</summary>
        private static void NotInRecord(string part, string nativeModule)
        {
            if (string.IsNullOrEmpty(part) || !IsImported(part)) return;
            LogOnce("record|" + part + "|" + nativeModule, IsruLines.NotInRecord(part, nativeModule));
        }

        private static bool IsImported(string part)
        {
            if (IsImportedForTests != null) return IsImportedForTests(part);
            try
            {
                return Redux.Ksp1Import.Assets.Ksp1ImportedPartRegistry.IsImportedPart(part);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The first transform named <paramref name="name"/> under <paramref name="root"/> (itself included), depth first.</summary>
        internal static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Gives the bridge to the part's <c>Module_Ksp1Animations</c>; without one, the bridge is extended for good.</summary>
        private static void Attach(PartBehaviourModule native, Data_Deployable bridge, string nativeModule, string part)
        {
            var anim = native.GetComponent<Module_Ksp1Animations>();
            if (anim != null)
            {
                anim.AttachDeployBridge(bridge, nativeModule);
                return;
            }
            bridge.CurrentDeployState.SetValue(Data_Deployable.DeployState.Extended);
            var reason = _animationsEnabled ? ExtendedReason.NoDeployClip : ExtendedReason.AnimationsOff;
            LogOnce("deploy|" + part + "|" + nativeModule + "|" + native.PartBackingMode, IsruLines.AlwaysExtended(part, nativeModule, reason));
        }

        /// <summary>The bridge state of <paramref name="s"/>.</summary>
        internal static Data_Deployable.DeployState ToDeployState(BridgeDeploy s) =>
            s == BridgeDeploy.Extended ? Data_Deployable.DeployState.Extended
            : s == BridgeDeploy.Extending ? Data_Deployable.DeployState.Extending
            : s == BridgeDeploy.Retracting ? Data_Deployable.DeployState.Retracting
            : Data_Deployable.DeployState.Retracted;

        /// <summary>Writes <paramref name="s"/> into the bridge when it differs from <paramref name="last"/>. No allocation.
        /// True when it wrote (the radiator bridge acts on that change).</summary>
        internal static bool Write(Data_Deployable bridge, ref BridgeDeploy? last, BridgeDeploy s)
        {
            if (bridge == null || last == s) return false;
            last = s;
            bridge.CurrentDeployState.SetValue(ToDeployState(s));
            return true;
        }

        private static void LogDrilling(Module_Mine mine, string part, Data_Mine data)
        {
            var f = data.MiningFormulaDefinition;
            var configs = data.RequestConfigs;
            if (f == null) return;
            var inputs = f.InputResources?.Count ?? 0;
            double ec = 0;
            for (var i = 0; i < inputs && configs != null && i < configs.Length; i++)
                if (IsruResources.IsElectricCharge(f.InputResources[i].ResourceName)) ec += configs[i].FlowUnits;
            string body;
            try
            {
                body = mine.part.vessel.SimObjectComponent.mainBody.Name;
            }
            catch (Exception)
            {
                body = "?";
            }
            var ground = _hit(mine).distance;
            var outputs = f.OutputResources?.Count ?? 0;
            for (var j = 0; j < outputs; j++)
            {
                var flow = configs != null && inputs + j < configs.Length ? configs[inputs + j].FlowUnits : 0;
                var rate = f.OutputResources[j].Rate;
                NativeModules.Info(IsruLines.Drilling(part, body, f.OutputResources[j].ResourceName, flow, rate > 0 ? flow / rate : 0, ground, ec));
            }
        }

        /// <summary>A line once per session and key.</summary>
        internal static void LogOnce(string key, string line, bool warn = false)
        {
            lock (Lock)
                if (!LoggedOnce.Add(key))
                    return;
            if (warn) NativeModules.Warn(line);
            else NativeModules.Info(line);
        }

        /// <summary>The part's name: its module data's owning part, else the VAB part or the flight part.</summary>
        internal static string PartNameOf(PartBehaviourModule m)
        {
            if (m == null) return null;
            if (PartNameForTests != null) return PartNameForTests(m);
            try
            {
                var list = m.DataModules?.Values;
                if (list != null)
                    foreach (var d in list)
                    {
                        var n = d?.OwningPartData?.partName;
                        if (!string.IsNullOrEmpty(n)) return n;
                    }
            }
            catch (Exception)
            {
                // fall through
            }
            try
            {
                var oab = m.OABPart;
                if (oab != null) return oab.AvailablePart?.Name;
                return m.part != null ? m.part.Name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            NativeModules.Error(IsruLines.PatchFailed(e.ToString()));
        }

        /// <summary>Tests: the part name of a module built outside a part.</summary>
        internal static Func<PartBehaviourModule, string> PartNameForTests;

        /// <summary>Tests: whether a part name is a KSP1-imported part (else Redux's registry).</summary>
        internal static Func<string, bool> IsImportedForTests;

        /// <summary>Tests: whether a drill was registered and had its arms set.</summary>
        internal static bool TryGetDrillState(Module_Mine mine, out bool armsSet)
        {
            armsSet = false;
            if (mine == null || !Drills.TryGetValue(mine, out var s)) return false;
            armsSet = s.ArmsSet;
            return true;
        }

        internal static bool Broken => _broken;

        internal static void Reset()
        {
            _impactOn = _mineBridgeOn = _scannerBridgeOn = _broken = false;
            _animationsEnabled = true;
            PartNameForTests = null;
            IsImportedForTests = null;
            _top = _bottom = null;
            _mineDeployable = null;
            _mineData = null;
            _hit = null;
            _scannerDeployable = null;
            lock (Lock) LoggedOnce.Clear();
        }
    }
}
