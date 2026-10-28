using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;

namespace KSP1PartMod.Game
{
    /// <summary>The runtime of converted KSP1 radiators (radiators plan, Task 3), armed at pre-initialization when
    /// <c>ksp1_radiators</c> is on. Only for the parts our importer converted (<see cref="NativeModules.Record"/>, by part name);
    /// Redux's own coolers are never touched.
    /// <list type="bullet">
    /// <item><b>deploy bridge</b> (<c>radiator_deploy_bridge</c>): a postfix on <c>Module_ActiveRadiator.OnInitialize</c> (VAB and
    /// flight) gives a radiator that found no <c>Data_Deployable</c> one of ours, a plain instance no part, PAM or action group
    /// knows. The native radiator is wired to it as it wires the part's own: its private <c>OnDeployStateChanged</c> subscribed (one
    /// delegate per instance, the one its <c>OnShutdown</c> unsubscribes) and its private <c>UpdateCoolerPAMVisibility</c> called.
    /// <c>Module_Ksp1Animations</c> of the part drives it from the Deploy entry of the part's <c>ModuleDeployableRadiator</c>
    /// (<see cref="RadiatorAnimRules.CanDriveBridge"/>); none, animations off, or the entry gated by B9: <c>Extended</c>;</item>
    /// <item><b>auto-on</b> (<c>radiator_auto_on</c>, flight only): at a transition of the bridge to <c>Extended</c>, the cooler is
    /// switched on once; the native radiator switches it off at the retraction, and the player's choice in between is kept. The
    /// first transition is seeded from the saved cooler: only a new part (just launched) counts its extended start as one;</item>
    /// <item><b>start-state repair</b> (flight): only <c>coolerEnabled</c> is saved, the cooler's state comes back from the
    /// definition, and only the switch's change event moves it. A postfix on <c>Module_Cooler.OnInitialize</c> puts the state back
    /// in line with the switch (on with <c>OFF</c>: <c>IDLE</c>, else it never cools; off with <c>IDLE</c>: <c>OFF</c>, else it cools
    /// switched off). At an extension of the bridge, a cooler on but left <c>OFF</c> or <c>RETRACTED</c> gets <c>IDLE</c>;</item>
    /// <item>a postfix on <c>Module_Cooler.OnModuleFixedUpdate</c>: the <c>cooling</c> line once per part instance, at its first
    /// <c>REMOVING</c>.</item>
    /// </list>
    /// Visual and native only: no PAM or action-group control is added, and nothing is written beyond the native switch and state.
    /// <c>Data_Cooler</c> implements Unity.Entities' <c>IComponentData</c>, which this assembly does not reference: it is handled
    /// through <see cref="RadiatorAccess"/> and delegates bound once per part. A missing member turns its feature off (one line). A
    /// patch that throws stops every patch here for the session (one line) and Redux's code runs as is.</summary>
    internal static class RadiatorRuntime
    {
        public const string HarmonyId = "KSP1PartMod.RadiatorRuntime";

        /// <summary>One converted cooler (a <c>Module_Cooler</c> or <c>Module_ActiveRadiator</c> instance of ours).</summary>
        internal sealed class Cooler
        {
            public string Part;
            public bool Flight;
            public ModuleData Data;
            public ModuleProperty<bool> Enabled;
            public Func<CoolerStates> GetState;
            public Action<CoolerStates> SetState;
            public bool Repaired, CoolingLogged, TurnedOnLogged;

            // ---- the deploy bridge (Module_ActiveRadiator only)
            public Data_Deployable Bridge;
            /// <summary>The native radiator's private <c>OnDeployStateChanged</c>, bound once to the instance.</summary>
            public Action<Data_Deployable.DeployState> OnDeploy;
            /// <summary>The MODULE index of the part's <c>ModuleDeployableRadiator</c> (-1: none recorded).</summary>
            public int DeployIndex = -1;
            /// <summary>The last state written into the bridge.</summary>
            public BridgeDeploy? Last;
            /// <summary>The previous state for <see cref="RadiatorAnimRules.ShouldAutoEnable"/>, once <see cref="Seeded"/>.</summary>
            public BridgeDeploy? AutoPrevious;
            public bool Seeded;
            /// <summary>A part just launched: its first state counts as a transition (an extended start turns it on).</summary>
            public bool NewPart;
            /// <summary><c>coolerEnabled</c> as loaded, before the bridge's first write.</summary>
            public bool SavedOn;
        }

        private static readonly ConditionalWeakTable<PartBehaviourModule, Cooler> Coolers = new ConditionalWeakTable<PartBehaviourModule, Cooler>();

        private static FieldInfo _dataCooler, _radDeployable;
        private static MethodInfo _stateGetter, _stateSetter, _onDeployStateChanged, _updatePamVisibility;
        private static PropertyInfo _fluxRemoved;
        private static FieldInfo _coolerEnabled;
        private static bool _coreOn, _bridgeOn, _coolingOn, _autoOn, _broken, _animationsEnabled = true;

        internal static bool DeployBridgeOn => _bridgeOn;
        internal static bool AutoOn => _autoOn;
        internal static bool Broken => _broken;

        /// <summary>Tests: whether a flight part is new (else <c>VesselComponent.justLaunched</c>).</summary>
        internal static Func<PartBehaviourModule, bool> NewPartForTests;

        /// <summary>Arms what the settings and the game's members allow. Returns true when a patch was applied.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            _animationsEnabled = settings.Enabled;
            if (!settings.Ksp1Radiators)
            {
                NativeModules.Info(RadiatorLines.Armed(false, false));
                return false;
            }
            var patched = false;
            try
            {
                var access = RadiatorAccess.Get();
                var coolerInit = access.ModuleCooler == null ? null : AccessTools.DeclaredMethod(access.ModuleCooler, "OnInitialize", Type.EmptyTypes);
                var dataCooler = access.ModuleCooler == null ? null : AccessTools.Field(access.ModuleCooler, "dataCooler");
                string missing = null;
                if (access.MissingCooler.Count > 0) missing = access.MissingCooler[0];
                else if (coolerInit == null) missing = "Module_Cooler.OnInitialize";
                else if (dataCooler == null || dataCooler.IsStatic || dataCooler.FieldType != access.DataCooler) missing = "Module_Cooler.dataCooler";
                if (missing != null)
                {
                    // Without these no part was converted either (the importer needs the same Data_Cooler members).
                    NativeModules.Warn(RadiatorLines.MemberNotFound(missing, "radiator runtime"));
                    NativeModules.Info(RadiatorLines.Armed(false, false));
                    return false;
                }
                _dataCooler = dataCooler;
                _coolerEnabled = access.CoolerEnabled;
                _stateGetter = access.CurrentCoolerState.GetGetMethod(true);
                _stateSetter = access.CurrentCoolerState.GetSetMethod(true);
                _coreOn = true;
                _autoOn = settings.RadiatorAutoOn;
                harmony.Patch(coolerInit, postfix: new HarmonyMethod(typeof(RadiatorRuntime), nameof(AfterCoolerInit)));
                patched = true;

                var fixedUpdate = AccessTools.DeclaredMethod(access.ModuleCooler, "OnModuleFixedUpdate", new[] { typeof(float) });
                var flux = AccessTools.Property(access.DataCooler, "fluxRemoved");
                if (fixedUpdate == null) NativeModules.Warn(RadiatorLines.MemberNotFound("Module_Cooler.OnModuleFixedUpdate", "cooling line"));
                else if (flux == null || flux.PropertyType != typeof(double) || flux.GetGetMethod(true) == null)
                    NativeModules.Warn(RadiatorLines.MemberNotFound("Data_Cooler.fluxRemoved", "cooling line"));
                else
                {
                    _fluxRemoved = flux;
                    _coolingOn = true;
                    harmony.Patch(fixedUpdate, postfix: new HarmonyMethod(typeof(RadiatorRuntime), nameof(AfterCoolerFixedUpdate)));
                }

                if (settings.RadiatorDeployBridge) ArmBridge(harmony, access);
            }
            catch (Exception e)
            {
                Break(e);
                harmony.UnpatchAll(HarmonyId);
                _coreOn = _bridgeOn = _coolingOn = false;
                return false;
            }
            NativeModules.Info(RadiatorLines.Armed(_bridgeOn, _autoOn));
            return patched;
        }

        private static void ArmBridge(Harmony harmony, RadiatorAccess.Targets access)
        {
            var type = access.ModuleActiveRadiator;
            const string feature = "deploy bridge";
            if (type == null)
            {
                NativeModules.Warn(RadiatorLines.MemberNotFound(RadiatorAccess.ModuleActiveRadiatorTypeName, feature));
                return;
            }
            var init = AccessTools.DeclaredMethod(type, "OnInitialize", Type.EmptyTypes);
            var deployable = AccessTools.Field(type, "_dataDeployable");
            var onDeploy = AccessTools.DeclaredMethod(type, "OnDeployStateChanged", new[] { typeof(Data_Deployable.DeployState) });
            var pam = AccessTools.DeclaredMethod(type, "UpdateCoolerPAMVisibility", new[] { typeof(bool) });
            string missing = null;
            if (init == null) missing = "Module_ActiveRadiator.OnInitialize";
            else if (deployable == null || deployable.IsStatic || deployable.FieldType != typeof(Data_Deployable)) missing = "Module_ActiveRadiator._dataDeployable";
            else if (onDeploy == null || onDeploy.IsStatic || onDeploy.ReturnType != typeof(void)) missing = "Module_ActiveRadiator.OnDeployStateChanged";
            else if (pam == null || pam.IsStatic || pam.ReturnType != typeof(void)) missing = "Module_ActiveRadiator.UpdateCoolerPAMVisibility";
            if (missing != null)
            {
                NativeModules.Warn(RadiatorLines.MemberNotFound(missing, feature));
                return;
            }
            _radDeployable = deployable;
            _onDeployStateChanged = onDeploy;
            _updatePamVisibility = pam;
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(RadiatorRuntime), nameof(AfterActiveRadiatorInit)));
            _bridgeOn = true;
        }

        // ---------------------------------------------------------------- patches

        /// <summary>Every <c>Module_Cooler</c> (a <c>Module_ActiveRadiator</c> too, from its base call): ours get a handle, and in flight
        /// their state is put back in line with the saved switch.</summary>
        internal static void AfterCoolerInit(PartBehaviourModule __instance)
        {
            if (_broken || !_coreOn || __instance == null) return;
            try
            {
                var part = IsruRuntime.PartNameOf(__instance);
                if (!IsOurs(part, null)) return;
                var c = Register(__instance, part);
                if (c == null || !c.Flight || c.Repaired) return;
                c.Repaired = true;
                var on = c.Enabled.GetValue();
                var state = c.GetState();
                if (on && state == CoolerStates.OFF) Repair(c, CoolerStates.IDLE, "init|", state);
                else if (!on && state == CoolerStates.IDLE) Repair(c, CoolerStates.OFF, "init|", state);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>After the native radiator looked for the part's <c>Data_Deployable</c>: ours, when it found none, get the bridge.</summary>
        internal static void AfterActiveRadiatorInit(PartBehaviourModule __instance)
        {
            if (_broken || !_bridgeOn || __instance == null) return;
            try
            {
                if (!Coolers.TryGetValue(__instance, out var c)) return; // not ours
                if (!IsOurs(c.Part, RadiatorMapping.NativeActiveRadiator)) return;
                var current = (Data_Deployable)_radDeployable.GetValue(__instance);
                if (current != null && !ReferenceEquals(current, c.Bridge)) return; // Redux gave the part a deployable: kept
                var bridge = c.Bridge ?? new Data_Deployable();
                c.Bridge = bridge;
                _radDeployable.SetValue(__instance, bridge);
                if (c.OnDeploy == null)
                    c.OnDeploy = (Action<Data_Deployable.DeployState>)Delegate.CreateDelegate(typeof(Action<Data_Deployable.DeployState>), __instance, _onDeployStateChanged);
                bridge.CurrentDeployState.OnChangedValue -= c.OnDeploy; // a second init never subscribes twice
                bridge.CurrentDeployState.OnChangedValue += c.OnDeploy;
                _updatePamVisibility.Invoke(__instance, new object[] { bridge.IsExtended });
                c.DeployIndex = RadiatorImporter.DeployIndexOf(c.Part);
                if (!c.Seeded)
                {
                    c.SavedOn = c.Enabled.GetValue();
                    c.NewPart = c.Flight && IsNewPart(__instance);
                    if (c.Flight && _autoOn)
                        IsruRuntime.LogOnce("seed|" + c.Part, RadiatorLines.AutoOnSeed(c.Part, c.NewPart && !c.SavedOn, c.SavedOn));
                }
                Attach(__instance, c);
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>The cooling line, once per part instance, at its first <c>REMOVING</c>. No allocation until then.</summary>
        internal static void AfterCoolerFixedUpdate(PartBehaviourModule __instance)
        {
            if (_broken || !_coolingOn || __instance == null) return;
            try
            {
                if (!Coolers.TryGetValue(__instance, out var c) || c.CoolingLogged || !c.Flight) return;
                if (c.GetState() != CoolerStates.REMOVING) return;
                c.CoolingLogged = true;
                var kw = (double)_fluxRemoved.GetValue(c.Data);
                NativeModules.Info(RadiatorLines.Cooling(c.Part, kw,
                    RadiatorAccess.ResourcesOf(c.Data).Select(r => (r.ResourceName, (double)r.Rate)).ToList()));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        // ---------------------------------------------------------------- the bridge

        /// <summary>Writes <paramref name="s"/> into the radiator's bridge when it changed, then auto-on and the state repair at an
        /// extension. No allocation while the state holds. True when it wrote.</summary>
        internal static bool Write(Cooler c, BridgeDeploy s)
        {
            if (c == null || !IsruRuntime.Write(c.Bridge, ref c.Last, s)) return false;
            if (_broken) return true;
            try
            {
                Changed(c, s);
            }
            catch (Exception e)
            {
                Break(e);
            }
            return true;
        }

        private static void Changed(Cooler c, BridgeDeploy s)
        {
            BridgeDeploy? previous;
            if (!c.Seeded)
            {
                // Seeded from the saved cooler: a reloaded part (or one saved on) starts from its restored state, so an extended
                // start is no transition and a cooler the player switched off stays off. Null only for a new part.
                c.Seeded = true;
                previous = c.NewPart && !c.SavedOn ? (BridgeDeploy?)null : s;
            }
            else previous = c.AutoPrevious;
            c.AutoPrevious = s;
            if (!c.Flight || s != BridgeDeploy.Extended || c.Enabled == null) return;
            if (RadiatorAnimRules.ShouldAutoEnable(previous, s, _autoOn) && !c.Enabled.GetValue())
            {
                c.Enabled.SetValue(true); // the native change event sets IDLE
                if (!c.TurnedOnLogged)
                {
                    c.TurnedOnLogged = true;
                    IsruRuntime.LogOnce("auto|" + c.Part, RadiatorLines.TurnedOnWhenExtended(c.Part));
                }
            }
            if (!c.Enabled.GetValue()) return;
            // On but stuck: switched on while folded (the action group still works then), or saved on with the definition's OFF.
            // The native radiator only leaves RETRACTED through the switch's change event.
            var state = c.GetState();
            if (state == CoolerStates.OFF || state == CoolerStates.RETRACTED) Repair(c, CoolerStates.IDLE, "extend|", state);
        }

        private static void Repair(Cooler c, CoolerStates to, string kind, CoolerStates from)
        {
            c.SetState(to);
            IsruRuntime.LogOnce("repair|" + kind + c.Part + "|" + from, RadiatorLines.StateRepaired(c.Part, to != CoolerStates.OFF, from.ToString(), to.ToString()));
        }

        /// <summary>Gives the bridge to the part's <c>Module_Ksp1Animations</c>; without one, the bridge is extended for good.</summary>
        private static void Attach(PartBehaviourModule native, Cooler c)
        {
            var anim = native.GetComponent<Module_Ksp1Animations>();
            if (anim != null)
            {
                anim.AttachDeployBridge(c.Bridge, RadiatorMapping.NativeActiveRadiator, c);
                return;
            }
            Write(c, BridgeDeploy.Extended);
            IsruRuntime.LogOnce(BridgeLogKey(c.Part, native.PartBackingMode.ToString()),
                RadiatorLines.AlwaysExtended(c.Part, _animationsEnabled ? ExtendedReason.NoDeployClip : ExtendedReason.AnimationsOff));
        }

        /// <summary>The once-per-session key of a radiator's bridge line (shared with <c>Module_Ksp1Animations</c>).</summary>
        internal static string BridgeLogKey(string part, string mode) => "deploy|" + part + "|" + RadiatorMapping.NativeActiveRadiator + "|" + mode;

        // ---------------------------------------------------------------- helpers

        /// <summary>The handle of a cooler of ours, made once per instance: the delegates are bound to its <c>Data_Cooler</c>.</summary>
        private static Cooler Register(PartBehaviourModule m, string part)
        {
            var data = _dataCooler.GetValue(m) as ModuleData;
            if (data == null) return null;
            var enabled = _coolerEnabled.GetValue(data) as ModuleProperty<bool>;
            if (enabled == null) return null;
            if (Coolers.TryGetValue(m, out var c) && ReferenceEquals(c.Data, data))
            {
                c.Flight = m.PartBackingMode == PartBehaviourModule.PartBackingModes.Flight;
                return c;
            }
            Coolers.Remove(m);
            c = new Cooler
            {
                Part = part,
                Flight = m.PartBackingMode == PartBehaviourModule.PartBackingModes.Flight,
                Data = data,
                Enabled = enabled,
                GetState = (Func<CoolerStates>)Delegate.CreateDelegate(typeof(Func<CoolerStates>), data, _stateGetter),
                SetState = (Action<CoolerStates>)Delegate.CreateDelegate(typeof(Action<CoolerStates>), data, _stateSetter),
            };
            Coolers.Add(m, c);
            return c;
        }

        /// <summary>A part of our record: a cooler of ours (<paramref name="nativeModule"/> null), or that one module.</summary>
        private static bool IsOurs(string part, string nativeModule)
        {
            if (string.IsNullOrEmpty(part)) return false;
            var modules = NativeModules.Record.ConvertedModules(part);
            for (var i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                if (nativeModule != null ? m == nativeModule : m == RadiatorMapping.NativeCooler || m == RadiatorMapping.NativeActiveRadiator)
                    return true;
            }
            return false;
        }

        /// <summary>A vessel just launched from the VAB or the launch pad (<c>VesselComponent.justLaunched</c>, never saved). S: its
        /// parts initialize before the flag is cleared; a miss only means an extended start does not turn the cooler on.</summary>
        private static bool IsNewPart(PartBehaviourModule m)
        {
            if (NewPartForTests != null) return NewPartForTests(m);
            try
            {
                return m.part?.vessel?.SimObjectComponent?.justLaunched == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Tests: the handle of an instance.</summary>
        internal static bool TryGet(PartBehaviourModule m, out Cooler c)
        {
            c = null;
            return m != null && Coolers.TryGetValue(m, out c);
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            NativeModules.Error(RadiatorLines.PatchFailed(e.ToString()));
        }

        internal static void Reset()
        {
            _coreOn = _bridgeOn = _coolingOn = _autoOn = _broken = false;
            _animationsEnabled = true;
            _dataCooler = _radDeployable = _coolerEnabled = null;
            _stateGetter = _stateSetter = _onDeployStateChanged = _updatePamVisibility = null;
            _fluxRemoved = null;
            NewPartForTests = null;
        }
    }
}
