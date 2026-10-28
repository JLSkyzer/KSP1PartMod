using System;
using System.Collections.Generic;
using KSP.Modules;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Runtime;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The game values the phase 2b plugin entries follow (spec "Plugin inputs", read in the Redux 26w42c
    /// decompile). As <see cref="GameInputs"/>: each <c>Try…</c> runs once at bind and returns a reader built then, that
    /// only reads and does not allocate, plus where it reads, for the log. <b>Visual only:</b> nothing here writes
    /// <c>Data_Engine</c>, <c>Data_Decouple</c> or <c>Data_RCS</c>, or calls an engine, decoupler or staging method.</summary>
    internal static class PluginInputs
    {
        public const string EngineIgnitedField = "Module_Engine.EngineIgnited";
        public const string EngineModeField = "Data_Engine.currentEngineModeData.engineID";
        public const string DecoupledField = "Data_Decouple.isDecoupled";
        public const string AccelerationField = "VesselComponent.AccelerationOrbital-graviticAcceleration";
        public const string RcsForceField = "Module_RCS.thrustForces[i].RelativeForce";

        /// <summary>Any engine of the part ignited: <c>Data_Engine.EngineIgnited</c> (the field behind
        /// <c>Module_Engine.EngineIgnited</c>), true once the ignition wait is over. Redux merges a part's KSP1 engine
        /// modules into the modes of one <c>Module_Engine</c>; every one found is read anyway.</summary>
        public static bool TryAnyEngineIgnited(Module_Ksp1Animations m, out Func<bool> read, out string field)
        {
            read = null;
            var engines = EngineData(m);
            if (engines.Length == 0)
            {
                field = "Module_Engine with Data_Engine";
                return false;
            }
            field = EngineIgnitedField;
            read = () =>
            {
                for (var i = 0; i < engines.Length; i++)
                    if (engines[i] != null && engines[i].EngineIgnited) return true;
                return false;
            };
            return true;
        }

        /// <summary>The active mode's <c>engineID</c> of the part's first <c>Module_Engine</c>: null when the mode index
        /// is out of range.</summary>
        public static bool TryActiveMode(Module_Ksp1Animations m, out Func<string> read, out string field)
        {
            read = null;
            var engines = EngineData(m);
            if (engines.Length == 0)
            {
                field = "Module_Engine with Data_Engine";
                return false;
            }
            var d = engines[0];
            field = EngineModeField;
            read = () => d?.currentEngineModeData?.engineID;
            return true;
        }

        /// <summary>The part's first <c>Module_Decouple</c>'s <c>Data_Decouple.isDecoupled</c>, read with
        /// <c>GetValue()</c> (the ECS component, or the stored value when unbound), polled. Redux's own
        /// <c>Ksp1AnimatedDecouplerRuntime</c> takes the same data, and never handles <c>ModuleBdbDecouplerAnimation</c>.</summary>
        public static bool TryDecoupled(Module_Ksp1Animations m, out Func<bool> read, out string field)
        {
            read = null;
            Data_Decouple data = null;
            foreach (var module in m.GetComponents<Module_Decouple>())
                if (module != null && module.DataModules.TryGetByType<Data_Decouple>(out var d) && d != null)
                {
                    data = d;
                    break;
                }
            if (data == null || data.isDecoupled == null)
            {
                field = "Module_Decouple with Data_Decouple";
                return false;
            }
            var property = data.isDecoupled;
            field = DecoupledField;
            read = () => property.GetValue();
            return true;
        }

        /// <summary>Flight only: the vessel's acceleration less gravity (KSP1's <c>acceleration_immediate -
        /// graviticAcceleration</c>), <c>VesselComponent.AccelerationOrbital</c> minus <c>graviticAcceleration</c> (both
        /// <c>Vector</c> structs), turned into Unity space with <c>Game.UniverseView.PhysicsSpace.VectorToPhysics</c> and
        /// projected on the part's up axis, in m/s². The vessel is reached each read as
        /// <c>part.Model.PartOwner.SimulationObject.Vessel</c>; NaN (the spring holds its anchor) while there is no vessel
        /// or physics space. The decompiled path is struct math (<c>Vector.minus</c>, <c>TransformFrame.ToLocalVector</c>):
        /// no allocation seen, not measured (<c>perf_counter</c>). False in the OAB: the caller passes 1 g.</summary>
        public static bool TryAcceleration(Module_Ksp1Animations m, out Func<float> alongUp, out string field)
        {
            alongUp = null;
            if (m.PartBackingMode != PartBehaviourModule.PartBackingModes.Flight || m.part == null)
            {
                field = null;
                return false;
            }
            var behavior = m.part;
            Transform host = m.gameObject.transform;
            field = AccelerationField;
            alongUp = () =>
            {
                var vessel = behavior == null ? null : behavior.Model?.PartOwner?.SimulationObject?.Vessel;
                var space = m.Game?.UniverseView?.PhysicsSpace;
                if (vessel == null || space == null || host == null) return float.NaN;
                var accel = vessel.AccelerationOrbital;
                var gravity = vessel.graviticAcceleration;
                // Before the vessel's first acceleration update the vectors have no frame yet.
                if (accel.coordinateSystem == null || gravity.coordinateSystem == null) return float.NaN;
                var a = space.VectorToPhysics(Vector.minus(accel, gravity));
                Vector3 up = host.up;
                return (float)(a.x * up.x + a.y * up.y + a.z * up.z);
            };
            return true;
        }

        /// <summary>The part's first <c>Module_RCS</c> with a <c>Data_RCS</c>: its <c>ThrusterTransforms</c> (filled by
        /// Redux's <c>Ksp1PartModuleRuntimeSetup.ConfigureRcs</c> when the prefab is configured), and a reader of thruster
        /// i's <c>|thrustForces[i].RelativeForce|</c> (kN; 1 when <c>requiresFuel</c> is false). <c>thrustForces</c> exists in
        /// flight only, created by <c>Module_RCS.OnInitialize</c>, which may run after ours: the list is read on each call,
        /// 0 while it is missing. <paramref name="maxThrust"/> is what the force is divided by
        /// (<see cref="PluginRules.RcsReference"/>). Only reads.</summary>
        public static bool TryRcs(Module_Ksp1Animations m, out Transform[] thrusters, out Func<int, float> force, out float maxThrust,
            out string field)
        {
            thrusters = null;
            force = null;
            maxThrust = 0f;
            Module_RCS rcs = null;
            Data_RCS data = null;
            foreach (var module in m.GetComponents<Module_RCS>())
                if (module != null && module.DataModules.TryGetByType<Data_RCS>(out var d) && d != null)
                {
                    rcs = module;
                    data = d;
                    break;
                }
            if (rcs == null || rcs.ThrusterTransforms == null || rcs.ThrusterTransforms.Length == 0)
            {
                field = rcs == null ? "Module_RCS with Data_RCS" : "Module_RCS.ThrusterTransforms";
                return false;
            }
            thrusters = rcs.ThrusterTransforms;
            maxThrust = PluginRules.RcsReference(data.maxThrust, rcs.requiresFuel);
            field = rcs.requiresFuel ? RcsForceField + " / Data_RCS.maxThrust" : RcsForceField + " / 1 (requiresFuel false)";
            var r = rcs;
            force = i =>
            {
                var list = r == null ? null : r.thrustForces;
                if (list == null || i < 0 || i >= list.Count) return 0f;
                var f = list[i];
                if (f == null) return 0f;
                var v = f.RelativeForce;
                return (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            };
            return true;
        }

        /// <summary>The rotators Redux's own look-at runtime drives on this part
        /// (<c>Ksp1LookAtConstraintRuntime.Constraints[].RotatorName</c>, built from <c>FXModuleLookAtConstraint</c> only);
        /// empty when there is none.</summary>
        public static string[] ReduxLookAtRotators(Module_Ksp1Animations m)
        {
            var names = new List<string>();
            foreach (var runtime in m.GetComponentsInChildren<Ksp1LookAtConstraintRuntime>(true))
            {
                if (runtime == null || runtime.Constraints == null) continue;
                foreach (var c in runtime.Constraints)
                    if (c != null && !string.IsNullOrEmpty(c.RotatorName)) names.Add(c.RotatorName);
            }
            return names.ToArray();
        }

        private static Data_Engine[] EngineData(Module_Ksp1Animations m)
        {
            var found = new List<Data_Engine>();
            foreach (var engine in m.GetComponents<Module_Engine>())
                if (engine != null && engine.DataModules.TryGetByType<Data_Engine>(out var d) && d != null) found.Add(d);
            return found.ToArray();
        }
    }
}
