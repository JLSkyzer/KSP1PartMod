using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Reads the KSP1 plugin modules phase 2b animates (DeployableEngines, Bluedog_DB, DMagic science
    /// toggle, stock FXModuleAnimateRCS) into <see cref="AnimEntryDef"/>s. Cfg field names and behaviour come from
    /// the research notes (<c>mods/NOTES.md</c> section 3); no plugin code is read or copied.</summary>
    public static class PluginEntryParser
    {
        private static readonly string[] Names =
        {
            "ModuleDeployableEngine", "ModuleBdbAnimatedEngine", "ModuleMultiStateEngine",
            "ModuleAdvancedLookAtConstraint", "ModuleBdbDecouplerAnimation", "FXModuleAnimateRCS",
            "ModuleBdbRCSEmissive", "ModuleBDBDeployableMeshHider", "DMModuleScienceAnimateGeneric",
            "ModuleBdbAccelAnimation", "ModuleBdbMercuryLandingBag",
        };

        /// <summary>The KSP1 module names this parser owns (compared case-insensitively).</summary>
        public static readonly IReadOnlyCollection<string> ModuleNames = Array.AsReadOnly(Names);

        private static readonly HashSet<string> Lower = new HashSet<string>(Names.Select(n => n.ToLowerInvariant()));

        /// <summary>True when <paramref name="moduleName"/> is one of <see cref="ModuleNames"/>.</summary>
        public static bool Handles(string moduleName) =>
            moduleName != null && Lower.Contains(moduleName.Trim().ToLowerInvariant());

        /// <summary>Null for an unknown module, and for one with nothing to drive. The key is built by
        /// <c>EntryParser.SetKey</c>.</summary>
        public static AnimEntryDef Parse(IKsp1Node module, int moduleIndex, int ordinal)
        {
            if (module == null) return null;
            AnimEntryDef e;
            switch (module.GetValue("name")?.Trim().ToLowerInvariant())
            {
                case "moduledeployableengine": e = DeployableEngine(module); break;
                case "modulebdbanimatedengine": e = BdbAnimatedEngine(module); break;
                case "modulemultistateengine": e = MultiState(module); break;
                case "moduleadvancedlookatconstraint": e = LookAt(module); break;
                case "modulebdbdecoupleranimation": e = Decoupler(module); break;
                case "fxmoduleanimatercs": e = RcsGlow(module); break;
                case "modulebdbrcsemissive": e = RcsEmissive(module); break;
                case "modulebdbdeployablemeshhider": e = MeshHider(module); break;
                case "dmmodulescienceanimategeneric": e = Science(module); break;
                case "modulebdbaccelanimation": e = Accel(module); break;
                case "modulebdbmercurylandingbag": e = LandingBag(module); break;
                default: return null;
            }
            if (e == null) return null;
            e.ModuleIndex = moduleIndex;
            EntryParser.SetKey(e, ordinal);
            return e;
        }

        private static AnimEntryDef DeployableEngine(IKsp1Node m)
        {
            var clip = Text(m, "EngineAnimationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.EngineDeploy,
                Clip = clip,
                Layer = Int(m, "Layer", 1),
                WaitForAnimation = Float(m, "WaitForAnimation", 0f),
                Retractable = true,
                EditorToggle = true,
                EditorSpeedMult = 1f,
            };
        }

        private static AnimEntryDef BdbAnimatedEngine(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            var oneShot = Bool(m, "isOneShot", true);
            return new AnimEntryDef
            {
                Kind = EntryKind.EngineDeploy,
                Clip = clip,
                Layer = Int(m, "Layer", 1),
                WaitForAnimation = Float(m, "WaitForAnimation", 0f),
                // The module is one-shot by default: it retracts and toggles in the editor only when not one-shot.
                Retractable = !oneShot,
                EditorToggle = !oneShot,
                FirstAnimatorOnly = true,
                PersistDeployed = true,
                EditorSpeedMult = 1f,
            };
        }

        private static AnimEntryDef MultiState(IKsp1Node m)
        {
            var clip = Text(m, "animationName") ?? Text(m, "EngineAnimationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.EngineMode,
                Clip = clip,
                Layer = Int(m, "Layer", 1),
                SpeedMultiplier = Float(m, "SpeedMultiplier", 1f),
                ModeAtTimeZero = Text(m, "EngineIDAtTimeZero"),
                WaitForAnimation = Float(m, "WaitForAnimation", 0f),
                EditorSpeedMult = 2f,
            };
        }

        private static AnimEntryDef LookAt(IKsp1Node m)
        {
            var targets = new List<string>();
            var rotators = new List<string>();
            foreach (var c in m.GetNodes("CONSTRAINLOOKFX"))
            {
                var t = Text(c, "targetName");
                var r = Text(c, "rotatorsName");
                if (t == null || r == null) continue;
                targets.Add(t);
                rotators.Add(r);
            }
            if (targets.Count == 0) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.LookAt,
                TargetNames = targets.ToArray(),
                RotatorNames = rotators.ToArray(),
                AxisConstrained = List(m, "axisConstrained").Select(s => bool.TryParse(s, out var b) && b).ToArray(),
                AxisScales = List(m, "axisScales").Select(s => TryFloat(s, out var f) ? f : 1f).ToArray(),
            };
        }

        private static AnimEntryDef Decoupler(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.DecouplerClip,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                WaitForAnimation = Float(m, "waitForAnimation", 0f),
                StartLabel = Text(m, "editorGUIName"),
                EditorSpeedMult = 1f,
            };
        }

        private static AnimEntryDef RcsGlow(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.RcsGlow,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                ThrustForceMult = Float(m, "thrustForceMult", 1f),
            };
        }

        private static AnimEntryDef RcsEmissive(IKsp1Node m)
        {
            var curve = m.GetNodes("alphaCurve").FirstOrDefault();
            if (curve == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.RcsEmissive,
                ShaderProperty = "_EmissiveColor",
                Curves = new[] { Keys(curve) },
            };
        }

        private static AnimEntryDef MeshHider(IKsp1Node m)
        {
            var names = m.GetValues("transformName").Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
            return names.Length == 0 ? null : new AnimEntryDef { Kind = EntryKind.MeshHider, TransformNames = names };
        }

        private static AnimEntryDef Science(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.ScienceToggle,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                StartLabel = Text(m, "startEventGUIName"),
                EndLabel = Text(m, "endEventGUIName"),
                ToggleInEditor = Bool(m, "showEditorEvents", true),
                OneWay = Bool(m, "oneWayAnimation", false),
                SpeedMultiplier = Float(m, "animSpeed", 1f),
                EditorSpeedMult = 1f,
            };
        }

        private static AnimEntryDef Accel(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.AccelSpring,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                SpringK = Float(m, "springK", 0f),
                SpringDamping = Float(m, "springDamping", 0f),
                AnchorDeployed = Float(m, "deployedAnchor", 1f),
                AnchorRetracted = Float(m, "stowedAnchor", 0f),
                ToggleInEditor = Bool(m, "showEditor", true),
            };
        }

        private static AnimEntryDef LandingBag(IKsp1Node m)
        {
            var clip = Text(m, "animationName");
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.LandingBag,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                RetractSpeed = 0.2f,
                EditorSpeedMult = 1f,
            };
        }

        private static string Text(IKsp1Node m, string key)
        {
            var v = m.GetValue(key);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        private static string[] List(IKsp1Node m, string key) =>
            (m.GetValue(key) ?? "").Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static float Float(IKsp1Node m, string key, float fallback) =>
            TryFloat(m.GetValue(key), out var v) ? v : fallback;

        private static int Int(IKsp1Node m, string key, int fallback) =>
            int.TryParse(m.GetValue(key)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static bool Bool(IKsp1Node m, string key, bool fallback) =>
            bool.TryParse(m.GetValue(key)?.Trim(), out var v) ? v : fallback;

        /// <summary>FloatCurve keys flattened as time, value, in, out (same rules as <c>EntryParser</c>).</summary>
        private static float[] Keys(IKsp1Node curve)
        {
            var flat = new List<float>();
            foreach (var key in curve.GetValues("key"))
            {
                var parts = (key ?? "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !TryFloat(parts[0], out var t) || !TryFloat(parts[1], out var v)) continue;
                float tin = float.NaN, tout = float.NaN;
                if (parts.Length == 4 && (!TryFloat(parts[2], out tin) || !TryFloat(parts[3], out tout)))
                    tin = tout = float.NaN;
                flat.Add(t); flat.Add(v); flat.Add(tin); flat.Add(tout);
            }
            return flat.ToArray();
        }
    }
}
