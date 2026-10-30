using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Native.Wheels;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>The runtime of converted KSP1 wheels (wheels plan, Task 4), armed at pre-initialization when <c>ksp1_wheels</c>
    /// is on. Only for the parts whose <c>ModuleWheelBase</c> our importer converted (<see cref="WheelImporter.RecordOf"/>, by
    /// part name, with its base); KSP2's own wheels are never touched.
    /// <list type="bullet">
    /// <item><b>setup line</b>: a postfix on <c>Module_WheelBase.OnInitialize</c>, flight only, once per part name per session,
    /// from public members only (<c>WheelColliderHost</c>, <c>WheelTransform</c>, the part's deployables);</item>
    /// <item><b>gear bridge</b>: <see cref="NativeWheelGear"/> tells <c>Module_Ksp1Animations</c> when the wheel base owns the
    /// part's Gear toggle (<see cref="GearBridgeRule.NativeWheelGear"/>). It resolves its members whether the postfix is armed
    /// or not; a missing member keeps the bridge's own action, as before 0.14.0.</item>
    /// </list>
    /// Read only: nothing of the wheel is written. A missing member turns the line off (one line). A patch that throws stops the
    /// line for the session (one line) and Redux's code runs as is.</summary>
    internal static class WheelRuntime
    {
        public const string HarmonyId = "KSP1PartMod.WheelRuntime";

        /// <summary>The public members of <c>Module_WheelBase</c> read here, resolved by name once per session.</summary>
        internal sealed class Members
        {
            public Type WheelBase;
            public MethodInfo OnInitialize;
            public FieldInfo Host, Transform;
            public MethodInfo InopSystems;
            /// <summary>The first member not found (null: all found).</summary>
            public string Missing;
        }

        private static Members _members;
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        private static bool _on, _broken, _ownFirst;

        internal static bool Broken => _broken;

        internal static Members Get() => _members ??= Resolve();

        internal static Members Resolve()
        {
            var m = new Members();
            var type = WheelAccess.Get().ModuleOf(WheelKind.Base);
            const string name = "Module_WheelBase";
            if (type == null || !typeof(PartBehaviourModule).IsAssignableFrom(type))
            {
                m.Missing = WheelAccess.Namespace + name;
                return m;
            }
            m.WheelBase = type;
            m.OnInitialize = AccessTools.DeclaredMethod(type, "OnInitialize", Type.EmptyTypes);
            m.Host = PublicTransform(type, "WheelColliderHost");
            m.Transform = PublicTransform(type, "WheelTransform");
            var inop = type.GetProperty("InopSystems", BindingFlags.Public | BindingFlags.Instance);
            m.InopSystems = inop?.GetGetMethod();
            if (m.OnInitialize == null || m.OnInitialize.ReturnType != typeof(void)) m.Missing = name + ".OnInitialize";
            else if (m.Host == null) m.Missing = name + ".WheelColliderHost";
            else if (m.Transform == null) m.Missing = name + ".WheelTransform";
            else if (m.InopSystems == null || m.InopSystems.ReturnType.IsValueType) m.Missing = name + ".InopSystems";
            return m;
        }

        private static FieldInfo PublicTransform(Type type, string name)
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null && f.FieldType == typeof(Transform) ? f : null;
        }

        /// <summary>Arms the setup line when <c>ksp1_wheels</c> is on. Returns true when the postfix was applied.</summary>
        public static bool TryApply(Harmony harmony, AnimationSettings settings)
        {
            settings = settings ?? AnimationSettings.Defaults;
            _ownFirst = settings.OwnFirstDeployable;
            if (!settings.Ksp1Wheels)
            {
                NativeModules.Info(WheelLines.Armed(null));
                return false;
            }
            try
            {
                var m = Get();
                if (m.Missing != null)
                {
                    NativeModules.Warn(WheelLines.RuntimeMemberNotFound(m.Missing));
                    return false;
                }
                harmony.Patch(m.OnInitialize, postfix: new HarmonyMethod(typeof(WheelRuntime), nameof(AfterInitialize)));
                _on = true;
            }
            catch (Exception e)
            {
                Break(e);
                harmony.UnpatchAll(HarmonyId);
                _on = false;
                return false;
            }
            var kinds = Enum.GetValues(typeof(WheelKind)).Cast<WheelKind>().Where(k => WheelImporter.IsOn(settings, k)).Select(WheelMapping.Label);
            NativeModules.Info(WheelLines.Armed(kinds));
            return true;
        }

        /// <summary>Every <c>Module_WheelBase</c>: in flight, ours log their setup line once per part name.</summary>
        internal static void AfterInitialize(PartBehaviourModule __instance)
        {
            if (_broken || !_on || __instance == null) return;
            try
            {
                if (__instance.PartBackingMode != PartBehaviourModule.PartBackingModes.Flight) return;
                var part = IsruRuntime.PartNameOf(__instance);
                var record = WheelImporter.RecordOf(part);
                if (record == null || !record.Summary.BaseConverted || record.Base == null) return;
                if (!Logged.Add(part)) return;
                var m = Get();
                var host = m.Host.GetValue(__instance) as Transform;
                var wheel = m.Transform.GetValue(__instance) as Transform;
                Deployables(__instance.gameObject, out var native, out var legacy);
                var gate = WheelLines.Gate(native, legacy, record.HasDeployment, _ownFirst);
                NativeModules.Info(WheelLines.Setup(part, record.Type, record.Base.WheelColliderTransformName, host != null,
                    record.Base.WheelTransformName, wheel != null, gate));
            }
            catch (Exception e)
            {
                Break(e);
            }
        }

        /// <summary>The part's deployables as <c>Module_WheelBase</c> sees them: <paramref name="native"/>, a
        /// <c>Module_Deployable</c> (or subclass, as <c>GetComponent</c> finds it) carrying a <c>Data_Deployable</c>;
        /// <paramref name="legacy"/>, Redux's <c>Ksp1Module_Deployable</c>, which the wheel base cannot see.</summary>
        internal static void Deployables(GameObject part, out bool native, out bool legacy)
        {
            native = legacy = false;
            if (part == null) return;
            var d = part.GetComponent<Module_Deployable>();
            native = d != null && d.DataModules != null && d.DataModules.TryGetByType<Data_Deployable>(out var data) && data != null;
            legacy = part.GetComponent<Ksp1Module_Deployable>() != null;
        }

        /// <summary>Whether the part's <c>Module_WheelBase</c> owns its Gear toggle, so the gear bridge stands down
        /// (<see cref="GearBridgeRule.NativeWheelGear"/>). False when the base is not ours, or a member is missing.</summary>
        internal static bool NativeWheelGear(PartBehaviourModule module, string part)
        {
            if (module == null) return false;
            var record = WheelImporter.RecordOf(part);
            var ours = record != null && record.Summary.BaseConverted;
            if (!ours) return false;
            try
            {
                var m = Get();
                if (m.Missing != null) return false;
                var go = module.gameObject;
                var wheel = go.GetComponent(m.WheelBase);
                var hasWheel = wheel != null;
                Deployables(go, out var native, out _);
                var inFlight = module.PartBackingMode == PartBehaviourModule.PartBackingModes.Flight;
                var initialized = hasWheel && m.InopSystems.Invoke(wheel, null) != null;
                var host = hasWheel && (m.Host.GetValue(wheel) as Transform) != null;
                return GearBridgeRule.NativeWheelGear(true, hasWheel, native, inFlight, initialized, host);
            }
            catch (Exception)
            {
                return false; // the bridge keeps its own action, as before 0.14.0
            }
        }

        private static void Break(Exception e)
        {
            if (_broken) return;
            _broken = true;
            NativeModules.Error(WheelLines.PatchFailed(e.ToString()));
        }

        internal static void Reset()
        {
            _on = _broken = _ownFirst = false;
            _members = null;
            Logged.Clear();
        }
    }
}
