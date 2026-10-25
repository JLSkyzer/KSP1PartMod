using System;
using System.Linq;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Runtime;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The game values our machines follow, read from the other modules of the same part (Redux 26w42c,
    /// read in <c>Ksp2Redux.dll</c>). Each <c>Try…</c> runs once at init and returns a reader that does not
    /// allocate, plus a description of where it reads, for the log.</summary>
    internal static class GameInputs
    {
        /// <summary>The engine's KSP1-style status: <c>Module_Engine</c> implements <c>IEngineStatus</c>
        /// (<c>throttleSetting</c> = <c>Data_Engine.currentThrottle</c>, <c>isOperational</c> = ignited and not
        /// flamed out, <c>engineName</c> = the active mode's <c>engineID</c>). Redux makes one mode per KSP1
        /// <c>engineID</c>, so <paramref name="engineName"/> picks the engine that has that mode, and the clip
        /// follows it only while the mode is active (<see cref="EngineInput"/>).</summary>
        public static bool TryEngine(Module_Ksp1Animations m, string engineName, out Func<(float throttle, bool operational)> read, out string field)
        {
            read = null;
            field = "none (no Module_Engine on the part, held at 0)";
            var engines = m.GetComponents<Module_Engine>();
            if (engines == null || engines.Length == 0) return false;
            Module_Engine engine = null;
            foreach (var candidate in engines)
            {
                if (candidate == null) continue;
                if (EngineInput.HasMode(engineName, ModeIds(candidate)))
                {
                    engine = candidate;
                    break;
                }
            }
            var named = !string.IsNullOrWhiteSpace(engineName);
            if (engine == null)
            {
                engine = engines.FirstOrDefault(e => e != null);
                if (engine == null) return false;
                field = $"Module_Engine.throttleSetting/isOperational, any mode (no mode '{engineName}' among [{string.Join(", ", ModeIds(engine))}])";
                named = false;
            }
            else field = named
                ? $"Module_Engine.throttleSetting/isOperational while mode '{engineName}' is active"
                : "Module_Engine.throttleSetting/isOperational";
            var e1 = engine;
            e1.DataModules.TryGetByType<Data_Engine>(out var de);
            var name = named ? engineName : null;
            read = () =>
            {
                if (e1 == null) return (0f, false);
                var mode = name != null ? de?.currentEngineModeData?.engineID : null;
                return EngineInput.Read(name, mode, e1.throttleSetting, e1.isOperational);
            };
            return true;
        }

        /// <summary>Flight only: <c>PartComponent.Temperature</c> and <c>PartComponent.MaxTemp</c>, in kelvin. The OAB
        /// has no thermal simulation.</summary>
        public static bool TryTemperature(Module_Ksp1Animations m, out Func<(float t, float max)> read, out string field)
        {
            read = null;
            if (m.PartBackingMode != PartBehaviourModule.PartBackingModes.Flight || m.part == null)
            {
                field = "none in the OAB (0 K)";
                return false;
            }
            var behavior = m.part;
            field = "PartComponent.Temperature/MaxTemp";
            read = () =>
            {
                var model = behavior == null ? null : behavior.Model;
                return model == null ? (0f, 0f) : ((float)model.Temperature, (float)model.MaxTemp);
            };
            return true;
        }

        /// <summary>Redux's deployable module of the part (<see cref="AnimationBinder.ReduxDeployableModule"/>) and
        /// its <c>Data_Deployable</c>: <paramref name="scalar"/> reads <c>AnimationNormalizedTime</c>,
        /// <paramref name="set"/> writes <c>toggleExtend</c> (both Redux modules react to it:
        /// <c>OnToggleExtendChanged</c>), <paramref name="state"/> is <c>toggleExtend</c> itself.</summary>
        public static bool TryReduxDeployable(Module_Ksp1Animations m, out Func<float> scalar, out Action<bool> set,
            out ModuleProperty<bool> state, out string field)
        {
            scalar = null;
            set = null;
            state = null;
            var module = AnimationBinder.ReduxDeployableModule(m.gameObject);
            if (module == null)
            {
                field = "no Redux deployable module on the part";
                return false;
            }
            if (!module.DataModules.TryGetByType<Data_Deployable>(out var data) || data == null)
            {
                field = $"{module.GetType().Name} has no Data_Deployable";
                return false;
            }
            var d = data;
            scalar = () => d.AnimationNormalizedTime.GetValue();
            set = v => d.toggleExtend.SetValue(v);
            state = d.toggleExtend;
            field = $"{module.GetType().Name} Data_Deployable.toggleExtend/AnimationNormalizedTime";
            return true;
        }

        /// <summary>The name of Redux's deployable <c>Data_Deployable.DefaultActionGroup</c> (where Redux puts its own
        /// toggle action), or null.</summary>
        public static string ReduxDefaultActionGroup(Module_Ksp1Animations m)
        {
            var module = AnimationBinder.ReduxDeployableModule(m.gameObject);
            return module != null && module.DataModules.TryGetByType<Data_Deployable>(out var data) && data != null
                ? data.DefaultActionGroup.ToString()
                : null;
        }

        private static string[] ModeIds(Module_Engine engine)
        {
            if (engine == null || !engine.DataModules.TryGetByType<Data_Engine>(out var data) || data?.engineModes == null)
                return new string[0];
            return data.engineModes.Where(mode => mode != null).Select(mode => mode.engineID).ToArray();
        }
    }
}
