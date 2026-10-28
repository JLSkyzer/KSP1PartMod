using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>Reads one KSP1 MODULE node into an <see cref="AnimEntryDef"/>, with KSP1 1.12.5's field
    /// defaults (research notes <c>stock/NOTES.md</c> §1–§10, <c>mods/NOTES.md</c> §3.3).</summary>
    public static class EntryParser
    {
        /// <summary>Unity WrapMode.ClampForever.</summary>
        private const int ClampForever = 8;
        /// <summary>Editor speed that ends a move in one frame (ModuleAnimationGroup's ±1000).</summary>
        private const float Snap = 1000f;

        /// <summary>Null for a module we never own, and for one with nothing to drive (no clip, no property).
        /// The key uses <paramref name="ordinal"/>; the planner computes it.</summary>
        public static AnimEntryDef Parse(IKsp1Node module, int moduleIndex, int ordinal)
        {
            if (module == null) return null;
            AnimEntryDef e;
            var kind = Kind(module.GetValue("name"));
            if (kind == null) return PluginEntryParser.Parse(module, moduleIndex, ordinal);
            switch (kind)
            {
                case EntryKind.Toggle: e = Toggle(module); break;
                case EntryKind.Deploy: e = Deploy(module); break;
                case EntryKind.Gear: e = Gear(module); break;
                case EntryKind.Throttle: e = Throttle(module); break;
                case EntryKind.Heat: e = Heat(module); break;
                case EntryKind.Color: e = Color(module); break;
                case EntryKind.Group: e = Group(module); break;
                case EntryKind.AeroGate: e = AeroGate(module); break;
                default: return null;
            }
            if (e == null) return null;
            e.ModuleIndex = moduleIndex;
            SetKey(e, ordinal);
            return e;
        }

        /// <summary>A follower of Redux's deployable source <paramref name="module"/>: the source's own entry (its KSP1
        /// speeds, direction and layer), re-kinded. Null when the module has no clip.</summary>
        internal static AnimEntryDef ReduxFollower(IKsp1Node module, int moduleIndex)
        {
            var e = Parse(module, moduleIndex, 0);
            if (e == null || string.IsNullOrEmpty(e.Clip)) return null;
            e.Kind = EntryKind.ReduxFollower;
            e.WrapMode = ClampForever;
            e.ActionGroup = null;
            SetKey(e, 0);
            return e;
        }

        /// <summary>KSP1 ModuleParachute fallbacks: clip names as Redux reads them
        /// (<c>Ksp1ParachuteModuleBuilder.ConfigurePrefab</c>); speeds assumed from KSP1 1.12 (semi 0.5 is stock,
        /// research note <c>stock/NOTES.md</c> §9; full 0.16667 is the upper end of the stock cfg values).</summary>
        internal static AnimEntryDef ChuteFollower(IKsp1Node m, int moduleIndex)
        {
            if (m == null) return null;
            var e = new AnimEntryDef
            {
                Kind = EntryKind.ChuteFollower,
                ModuleIndex = moduleIndex,
                Clip = Text(m, "semiDeployedAnimation") ?? "semiDeploy",
                SecondClip = Text(m, "fullyDeployedAnimation") ?? "fullyDeploy",
                SpeedFactorIn = Float(m, "semiDeploymentSpeed", 0.5f),
                SpeedFactorOut = Float(m, "deploymentSpeed", 0.16667f),
                WrapMode = ClampForever,
            };
            SetKey(e, 0);
            return e;
        }

        /// <summary>The entry kind a KSP1 module name gives (never GearBridge), or null.</summary>
        internal static EntryKind? Kind(string moduleName)
        {
            switch (moduleName?.Trim().ToLowerInvariant())
            {
                case "moduleanimategeneric": return EntryKind.Toggle;
                case "moduledeployablesolarpanel":
                case "moduledeployableantenna":
                case "moduledeployableradiator":
                case "moduledeployablepart": return EntryKind.Deploy;
                case "modulewheeldeployment":
                case "modulelandingleg": return EntryKind.Gear;
                case "fxmoduleanimatethrottle": return EntryKind.Throttle;
                case "moduleanimateheat": return EntryKind.Heat;
                case "modulecolorchanger": return EntryKind.Color;
                case "moduleanimationgroup": return EntryKind.Group;
                case "moduledeployableaero": return EntryKind.AeroGate;
                default: return null;
            }
        }

        /// <summary>The name an entry is known by in its key: its clip, else its shader property, else (AeroGate)
        /// its controller index, or (Shroud) its first transform name.</summary>
        internal static string KeyName(AnimEntryDef e) =>
            FirstNonBlank(e.Clip, e.SecondClip, e.ThirdClip, e.ShaderProperty)
            ?? (e.Kind == EntryKind.AeroGate ? e.ControllerModuleIndex.ToString(CultureInfo.InvariantCulture)
                : e.Kind == EntryKind.Shroud && e.TransformNames != null && e.TransformNames.Length > 0 ? e.TransformNames[0] : "");

        internal static void SetKey(AnimEntryDef e, int ordinal) =>
            e.Key = e.Kind.ToString().ToLowerInvariant() + ":" + KeyName(e) + ":" + ordinal.ToString(CultureInfo.InvariantCulture);

        /// <summary>The clip name Redux takes from a deployable source: <c>animationName</c>, else
        /// <c>animationStateName</c> (Redux 26w42c <c>Ksp1DeployableModuleBuilder.GetDeployableSources</c>).</summary>
        internal static string SourceClip(IKsp1Node m) => FirstNonBlank(m.GetValue("animationName"), m.GetValue("animationStateName"));

        private static AnimEntryDef Toggle(IKsp1Node m)
        {
            var clip = SourceClip(m);
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.Toggle,
                Clip = clip,
                Layer = Int(m, "layer", 1),
                EditorSpeedMult = Bool(m, "instantAnimInEditor", true) ? 10f : 0f,
                Retractable = !Bool(m, "isOneShot", false),
                StartLabel = Text(m, "startEventGUIName"),
                EndLabel = Text(m, "endEventGUIName"),
                ActionLabel = Text(m, "actionGUIName"),
                ActionGroup = ActionGroupMap.Map(m.GetValue("defaultActionGroup")),
            };
        }

        private static AnimEntryDef Deploy(IKsp1Node m)
        {
            var clip = SourceClip(m);
            if (clip == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.Deploy,
                Clip = clip,
                Layer = Int(m, "layer", 0),
                WrapMode = ClampForever,
                EditorSpeedMult = Float(m, "editorAnimationSpeedMult", 10f),
                Retractable = Bool(m, "retractable", true),
                StartLabel = Text(m, "extendActionName"),
                EndLabel = Text(m, "retractActionName"),
                ActionLabel = Text(m, "extendpanelsActionName"),
                ActionGroup = ActionGroupMap.Map(m.GetValue("defaultActionGroup")),
            };
        }

        private static AnimEntryDef Gear(IKsp1Node m)
        {
            var clip = SourceClip(m);
            if (clip == null) return null;
            var wheel = "ModuleWheelDeployment".Equals(m.GetValue("name")?.Trim(), StringComparison.OrdinalIgnoreCase);
            // ModuleWheelDeployment: deployedPosition defaults to 0 (clip reversed to deploy). KSP1 reads only
            // `retractDurationFactor`; the `retractDuractionFactor` spelling found in cfgs is never read, so it
            // stays at 1 here too.
            var deployed = wheel ? Float(m, "deployedPosition", 0f) : 1f;
            return new AnimEntryDef
            {
                Kind = EntryKind.Gear,
                Clip = clip,
                AnimatorName = Text(m, "animationTrfName"),
                WrapMode = ClampForever,
                EditorSpeedMult = Snap,
                DeployedPosition = deployed,
                DeployForward = deployed > 0.5f,
                SpeedFactorIn = wheel ? Float(m, "extendDurationFactor", 1f) : 1f,
                SpeedFactorOut = wheel ? Float(m, "retractDurationFactor", 1f) : 1f,
                ActionGroup = "Gear",
            };
        }

        private static AnimEntryDef Throttle(IKsp1Node m) => new AnimEntryDef
        {
            Kind = EntryKind.Throttle,
            Clip = Text(m, "animationName") ?? "throttleAnim",
            Layer = Int(m, "layer", 1),
            WrapMode = Int(m, "animWrapMode", ClampForever),
            ResponseSpeed = Float(m, "responseSpeed", 0.5f),
            EngineName = Text(m, "engineName"),
            WeightOnOperational = Bool(m, "weightOnOperational", false),
            PlayInEditor = Bool(m, "playInEditor", false),
        };

        private static AnimEntryDef Heat(IKsp1Node m)
        {
            var curve = m.GetNodes("animStateCurve").FirstOrDefault();
            return new AnimEntryDef
            {
                Kind = EntryKind.Heat,
                // OnLoad copies a non-empty ThermalAnim over animName.
                Clip = FirstNonBlank(m.GetValue("ThermalAnim"), m.GetValue("animName")) ?? "HeatAnimationEmissive",
                Layer = Int(m, "layer", 1),
                Curves = curve == null ? null : new[] { Keys(curve) },
            };
        }

        private static AnimEntryDef Color(IKsp1Node m)
        {
            var property = Text(m, "shaderProperty");
            if (property == null) return null;
            return new AnimEntryDef
            {
                Kind = EntryKind.Color,
                ShaderProperty = property,
                Curves = new[] { "redCurve", "greenCurve", "blueCurve", "alphaCurve" }
                    .Select(n => m.GetNodes(n).FirstOrDefault())
                    .Select(n => n == null ? new float[0] : Keys(n)).ToArray(),
                Rate = Float(m, "animRate", 1f),
                UseRate = Bool(m, "useRate", true),
                ToggleInEditor = Bool(m, "toggleInEditor", true),
                ToggleInFlight = Bool(m, "toggleInFlight", true),
                StartLabel = Text(m, "eventOnName"),
                EndLabel = Text(m, "eventOffName"),
                ActionLabel = Text(m, "toggleName") ?? "Toggle Color",
                ActionGroup = ActionGroupMap.Map(m.GetValue("defaultActionGroup")),
                IncludedRenderers = Values(m, "includedRenderer"),
                ExcludedRenderers = Values(m, "excludedRenderer"),
            };
        }

        private static AnimEntryDef Group(IKsp1Node m)
        {
            var e = new AnimEntryDef
            {
                Kind = EntryKind.Group,
                Clip = Text(m, "deployAnimationName"),
                SecondClip = Text(m, "activeAnimationName"),
                ThirdClip = Text(m, "deactivateAnimationName"),
                Layer = 3,
                EditorSpeedMult = Snap,
                AlwaysActive = Bool(m, "alwaysActive", false),
                StartLabel = Text(m, "deployActionName"),
                EndLabel = Text(m, "retractActionName"),
                ActionLabel = Text(m, "toggleActionName"),
            };
            return e.Clip == null && e.SecondClip == null && e.ThirdClip == null ? null : e;
        }

        private static AnimEntryDef AeroGate(IKsp1Node m) => new AnimEntryDef
        {
            Kind = EntryKind.AeroGate,
            ControllerModuleIndex = Int(m, "DeployModuleIndex", 0),
            DeployedPosition = Int(m, "DeployModulePos", 0),
        };

        /// <summary>FloatCurve keys flattened as time, value, in, out. KSP1 splits on space, comma, semicolon and
        /// tab; a key with fewer than four numbers keeps NaN tangents; a key it cannot parse is skipped.</summary>
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

        private static string FirstNonBlank(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string Text(IKsp1Node m, string key) => FirstNonBlank(m.GetValue(key));

        private static string[] Values(IKsp1Node m, string key)
        {
            var list = m.GetValues(key).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
            return list.Length == 0 ? null : list;
        }

        private static bool TryFloat(string s, out float value) =>
            float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static float Float(IKsp1Node m, string key, float fallback) =>
            TryFloat(m.GetValue(key), out var v) ? v : fallback;

        private static int Int(IKsp1Node m, string key, int fallback) =>
            int.TryParse(m.GetValue(key)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static bool Bool(IKsp1Node m, string key, bool fallback) =>
            bool.TryParse(m.GetValue(key)?.Trim(), out var v) ? v : fallback;
    }
}
