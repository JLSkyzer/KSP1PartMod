using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Engines
{
    /// <summary>One <c>ENGINESET</c> of the default engine type, with the number of engines the model holds.</summary>
    public sealed class SepSet
    {
        public string Name, EngineId, TransformName;
        public double SingleEngineThrust, Mass;
        public int Count;
    }

    /// <summary>The thrust of each mode and the engine mass of an SEP cluster, from its <c>ModuleSEPProceduralEngineGUI</c>.</summary>
    public sealed class SepComputation
    {
        public bool Converted;
        public string Reason;
        /// <summary>The default <c>ENGINETYPE</c>'s name (the first).</summary>
        public string TypeName;
        /// <summary>The mode ids, in <see cref="EngineModeOrder.Sep"/> order.</summary>
        public string[] ModeIds = Array.Empty<string>();
        /// <summary>kN per mode: mode k runs its own sets and those of every later mode.</summary>
        public double[] ModeThrust = Array.Empty<double>();
        /// <summary>Per mode, how many transforms of its <c>thrustVectorTransformName</c> the model holds.</summary>
        public int[] ThrustTransforms = Array.Empty<int>();
        public string[] ThrustTransformNames = Array.Empty<string>();
        public readonly List<SepSet> Sets = new List<SepSet>();
        /// <summary>t, engines only (covers and other selectables excluded).</summary>
        public double EngineMass;
        /// <summary>What SEP's own GUI does that is not reproduced: the custom selectables, the other engine types.</summary>
        public readonly List<string> NotReproduced = new List<string>();
    }

    /// <summary>SEP clusters (0.16.0, plan (b)). SEP's plugin sets the thrust and mass in KSP1; under Redux the cfg keeps
    /// placeholders (<c>maxThrust = 1000</c>, <c>mass = 0</c>). The default <c>ENGINETYPE</c> (the first) lists its
    /// <c>ENGINESET</c>s; each set's engine count is the number of model transforms named exactly
    /// <c>&lt;transformName&gt;&lt;transformNameSeperator&gt;&lt;n&gt;</c>. Mode k runs the sets of mode k and of every later
    /// mode (supposed: "AllEngines" and SEP's list of active engines). Pure.</summary>
    public static class SepEngineSets
    {
        public const string GuiModule = "ModuleSEPProceduralEngineGUI";

        private static SepComputation Refuse(SepComputation c, string reason)
        {
            c.Converted = false;
            c.Reason = reason;
            return c;
        }

        public static SepComputation Compute(IKsp1Node part, IReadOnlyCollection<string> prefabTransformNames)
        {
            var c = new SepComputation();
            var modules = EngineModeOrder.Modules(part);
            var order = EngineModeOrder.Sep(part);
            if (order.Length < 2) return Refuse(c, "no " + EngineModeOrder.SepSwitchModule + " over two engine modules");
            c.ModeIds = order.Select(i => EngineModeOrder.EngineIdOf(modules[i])).ToArray();
            c.ThrustTransformNames = order.Select(i => IsruCfg.Value(modules[i], "thrustVectorTransformName") ?? "thrustTransform").ToArray();
            var names = prefabTransformNames ?? (IReadOnlyCollection<string>)Array.Empty<string>();
            c.ThrustTransforms = c.ThrustTransformNames.Select(n => names.Count(x => string.Equals(x, n, StringComparison.Ordinal))).ToArray();
            c.ModeThrust = new double[order.Length];

            var gui = modules.FirstOrDefault(m => m != null && IsruCfg.IsModule(m, GuiModule));
            if (gui == null) return Refuse(c, "no " + GuiModule);
            var types = gui.GetNodes("ENGINETYPE").Where(n => n != null).ToList();
            if (types.Count == 0) return Refuse(c, "no ENGINETYPE");
            var type = types[0];
            c.TypeName = IsruCfg.Value(type, "name") ?? "";
            foreach (var other in types.Skip(1)) c.NotReproduced.Add("ENGINETYPE '" + (IsruCfg.Value(other, "name") ?? "") + "'");
            foreach (var cs in type.AllValues.Where(v => Key(v.Key, "customSelectableName")))
                c.NotReproduced.Add("CUSTOMSELECTABLE '" + IsruCfg.Text(cs.Value) + "'");

            var sep = IsruCfg.Value(gui, "transformNameSeperator") ?? "";
            var sets = gui.GetNodes("ENGINESET").Where(n => n != null).ToList();
            foreach (var setName in type.AllValues.Where(v => Key(v.Key, "engineSetName")).Select(v => IsruCfg.Text(v.Value)))
            {
                var node = sets.FirstOrDefault(s => string.Equals(IsruCfg.Value(s, "name"), setName, StringComparison.Ordinal));
                if (node == null) return Refuse(c, "ENGINESET '" + setName + "' not found");
                var set = new SepSet
                {
                    Name = setName,
                    EngineId = IsruCfg.Value(node, "engineId") ?? "",
                    TransformName = IsruCfg.Value(node, "transformName") ?? "",
                    SingleEngineThrust = IsruCfg.Number(node, "singleEngineThrust") ?? double.NaN,
                    Mass = IsruCfg.Number(node, "mass") ?? 0,
                };
                if (set.TransformName.Length == 0) return Refuse(c, "ENGINESET '" + setName + "' has no transformName");
                if (double.IsNaN(set.SingleEngineThrust) || set.SingleEngineThrust <= 0)
                    return Refuse(c, "ENGINESET '" + setName + "' has no singleEngineThrust");
                var pattern = new Regex("^" + Regex.Escape(set.TransformName + sep) + "[0-9]+$", RegexOptions.CultureInvariant);
                set.Count = names.Count(n => n != null && pattern.IsMatch(n));
                c.Sets.Add(set);
                if (set.Count == 0) return Refuse(c, "ENGINESET '" + setName + "': no transform " + set.TransformName + sep + "<n> in the model");
                if (Array.FindIndex(c.ModeIds, id => string.Equals(id, set.EngineId, StringComparison.OrdinalIgnoreCase)) < 0)
                    return Refuse(c, "ENGINESET '" + setName + "': engineId '" + set.EngineId + "' is no engine mode");
            }
            if (c.Sets.Count == 0) return Refuse(c, "ENGINETYPE '" + c.TypeName + "' lists no ENGINESET");

            for (var k = 0; k < c.ModeIds.Length; k++)
            {
                var thrust = 0.0;
                foreach (var set in c.Sets)
                {
                    var mode = Array.FindIndex(c.ModeIds, id => string.Equals(id, set.EngineId, StringComparison.OrdinalIgnoreCase));
                    if (mode >= k) thrust += set.Count * set.SingleEngineThrust;
                }
                if (thrust <= 0) return Refuse(c, "mode '" + c.ModeIds[k] + "' runs no engine set");
                c.ModeThrust[k] = thrust;
            }
            c.EngineMass = c.Sets.Sum(s => s.Count * s.Mass);
            c.Converted = true;
            return c;
        }

        private static bool Key(string key, string name) => string.Equals(key?.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }
}
