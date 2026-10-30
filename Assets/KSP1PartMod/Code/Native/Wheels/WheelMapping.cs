using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>The KSP1 wheel modules read as KSP2's wheel data, one function per module (wheels plan (f)):
    /// <list type="bullet">
    /// <item>fields are copied by name; a field absent from the cfg keeps KSP2's default. Numbers are parsed invariantly; an
    /// unparsable value keeps KSP2's default and gives a note;</item>
    /// <item>fields that Redux's coupled solver ignores (its own tooltips say so) are copied for JSON parity and listed in
    /// <see cref="WheelMapped{T}.UnusedBySolver"/>; fields with no counterpart are listed in
    /// <see cref="WheelMapped{T}.NotReproduced"/>;</item>
    /// <item>a module that would do nothing in KSP2 is not converted (<see cref="WheelMapped{T}.Converted"/> false, with a note):
    /// a base with no radius or no collider host name, steering without a <c>steeringCurve</c>, a motor without a
    /// <c>torqueCurve</c>.</item>
    /// </list>
    /// <c>baseModuleIndex</c> is quiet when 0: KSP2 finds the part's one base by component, which is what index 0 names. Pure.</summary>
    public static class WheelMapping
    {
        public const string DeploymentModule = "ModuleWheelDeployment";
        /// <summary>KSP1's spelling of the retract factor.</summary>
        public const string RetractFactor = "retractDuractionFactor";

        private static readonly string[] Ksp1Names =
        {
            "ModuleWheelBase", "ModuleWheelSuspension", "ModuleWheelSteering", "ModuleWheelMotor", "ModuleWheelMotorSteering",
            "ModuleWheelBrakes", "ModuleWheelDamage", "ModuleWheelLock", "ModuleWheelBogey",
        };

        /// <summary>The fields of each kind that Redux's solver ignores (wheels plan (b)), by cfg name.</summary>
        public static readonly IReadOnlyDictionary<WheelKind, IReadOnlyList<string>> UnusedBySolverFields =
            new Dictionary<WheelKind, IReadOnlyList<string>>
            {
                [WheelKind.Base] = new[] { "adherentStart", "frictionAdherent", "peakStart", "limitStart", "frictionSharpness", "TireForceSharpness" },
                [WheelKind.Suspension] = new[] { "springRatio", "damperRatio", "boostRatio", "useDistributedMass", "useAutoBoost", "adjustForHighGee" },
                [WheelKind.Damage] = new[] { "impactTolerance", "impactDamageColliderName" },
            };

        /// <summary>The cfg fields of each kind that become a KSP2 <b>state</b> value (a module property's initial value), which
        /// the 0.9.0 B9 module-data route does not carry.</summary>
        public static readonly IReadOnlyDictionary<WheelKind, IReadOnlyList<string>> StateFields =
            new Dictionary<WheelKind, IReadOnlyList<string>>
            {
                [WheelKind.Base] = new[] { "autoFrictionAvailable", "frictionMultiplier" },
                [WheelKind.Steering] = new[] { "autoSteeringAdjust" },
                [WheelKind.Motor] = new[] { "motorEnabled", "autoTorque", "tractionControlScale" },
                [WheelKind.MotorSteering] = new[] { "motorEnabled", "autoTorque", "tractionControlScale" },
            };

        public static string Ksp1Module(WheelKind kind) => Ksp1Names[(int)kind];

        public static string NativeModule(WheelKind kind) => "Module_" + Ksp1Names[(int)kind].Substring("Module".Length);

        /// <summary>The label of a kind in the log lines.</summary>
        public static string Label(WheelKind kind)
        {
            switch (kind)
            {
                case WheelKind.Base: return "base";
                case WheelKind.Suspension: return "suspension";
                case WheelKind.Steering: return "steering";
                case WheelKind.Motor: return "motor";
                case WheelKind.MotorSteering: return "motor-steering";
                case WheelKind.Brakes: return "brakes";
                case WheelKind.Damage: return "damage";
                case WheelKind.Lock: return "lock";
                default: return "bogey";
            }
        }

        /// <summary>The kind of a KSP1 module name (case-insensitive, as the importers match modules).</summary>
        public static bool TryKind(string ksp1Module, out WheelKind kind)
        {
            var name = ksp1Module?.Trim();
            for (var i = 0; i < Ksp1Names.Length; i++)
                if (string.Equals(Ksp1Names[i], name, StringComparison.OrdinalIgnoreCase)) { kind = (WheelKind)i; return true; }
            kind = WheelKind.Base;
            return false;
        }

        /// <summary>A field the B9 census counts as a state field of that kind.</summary>
        public static bool IsStateField(WheelKind kind, string field) =>
            StateFields.TryGetValue(kind, out var list) && list.Contains(field?.Trim(), StringComparer.Ordinal);

        // ------------------------------------------------------------------ the nine functions

        public static WheelMapped<WheelBaseValues> Base(IKsp1Node wheelBase, IKsp1Node deployment, bool ksp1Friction, bool ksp1DeployTiming)
        {
            var v = new WheelBaseValues();
            var m = new WheelMapped<WheelBaseValues>(WheelKind.Base, v);
            if (wheelBase == null) { m._notes.Add("ModuleWheelBase: no node, no wheel"); return m; }
            var r = new Reader<WheelBaseValues>(wheelBase, m);

            var typeText = r.Text("wheelType");
            if (typeText == null) r.Note("ModuleWheelBase: wheelType missing, FREE");
            else if (!TryWheelType(typeText, out v.WheelType)) r.Note($"ModuleWheelBase: wheelType '{typeText}' unknown, FREE");

            var host = r.Text("wheelColliderTransformName");
            if (!string.IsNullOrEmpty(host)) v.WheelColliderTransformName = host;
            v.WheelTransformName = r.Str("wheelTransformName", v.WheelTransformName);
            v.Radius = r.F("radius", v.Radius);
            v.Center = r.V("center", v.Center);
            v.Mass = r.F("mass", v.Mass);
            v.FitWheelColliderToMesh = r.B("FitWheelColliderToMesh", v.FitWheelColliderToMesh);
            v.GroundHeightOffset = r.F("groundHeightOffset", v.GroundHeightOffset);
            v.ClipObject = r.Str("clipObject", v.ClipObject);
            v.UseNewFrictionModel = r.B("useNewFrictionModel", v.UseNewFrictionModel);
            v.FrictionSharpness = r.F("frictionSharpness", v.FrictionSharpness, unused: true);
            v.TireForceSharpness = r.F("TireForceSharpness", v.TireForceSharpness, unused: true);
            v.TireForceSharpness = r.F("tireForceSharpness", v.TireForceSharpness, unused: true, field: "TireForceSharpness");
            v.AdherentStart = r.F("adherentStart", v.AdherentStart, unused: true);
            v.FrictionAdherent = r.F("frictionAdherent", v.FrictionAdherent, unused: true);
            v.PeakStart = r.F("peakStart", v.PeakStart, unused: true);
            v.LimitStart = r.F("limitStart", v.LimitStart, unused: true);
            var peak = r.F("frictionPeak", v.FrictionPeak);
            var limit = r.F("frictionLimit", v.FrictionLimit);
            var autoAvailable = r.B("autoFrictionAvailable", true);
            if (ksp1Friction)
            {
                v.FrictionPeak = peak;
                v.FrictionLimit = limit;
                v.AutoFriction = autoAvailable;
            }
            var multiplier = r.F("frictionMultiplier", v.FrictionMultiplier);
            v.FrictionMultiplier = Clamp(multiplier, 0, 10, "ModuleWheelBase: frictionMultiplier", m);
            r.NotReproduced("TooltipTitle");
            r.NotReproduced("TooltipPrimaryField");
            r.NotReproduced("suspensionEnabled");
            r.Finish();

            if (deployment != null && ksp1DeployTiming)
            {
                var t = IsruCfg.Value(deployment, "TsubSys");
                if (t != null)
                {
                    if (IsruCfg.TryNumber(t, out var tsub))
                    {
                        var clamped = Clamp((float)tsub, 0, 1, "ModuleWheelDeployment: TsubSys", m);
                        v.DeploymentSubsystemNormalized = clamped;
                        v.RetractionSubsystemNormalized = 1f - clamped;
                    }
                    else r.Note($"ModuleWheelDeployment: TsubSys '{t}' is not a number, KSP2's default kept");
                }
                var standIn = IsruCfg.Value(deployment, "useStandInCollider");
                if (standIn != null)
                {
                    if (bool.TryParse(standIn, out var b)) v.UseStandinCollider = b;
                    else r.Note($"ModuleWheelDeployment: useStandInCollider '{standIn}' is not true or false, KSP2's default kept");
                }
            }

            if (string.IsNullOrEmpty(host)) r.Note("ModuleWheelBase: no wheelColliderTransformName, no wheel");
            else if (!(v.Radius > 0)) r.Note($"ModuleWheelBase: radius {IsruCfg.Inv(v.Radius)} is not above 0, no wheel");
            else m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelSuspensionValues> Suspension(IKsp1Node node)
        {
            var v = new WheelSuspensionValues();
            var m = new WheelMapped<WheelSuspensionValues>(WheelKind.Suspension, v);
            if (node == null) return m;
            var r = new Reader<WheelSuspensionValues>(node, m);
            r.BaseIndex();
            v.SuspensionTransformName = r.Str("suspensionTransformName", v.SuspensionTransformName);
            v.SuspensionDistance = r.F("suspensionDistance", v.SuspensionDistance);
            v.TargetPosition = r.F("targetPosition", v.TargetPosition);
            v.SuspensionOffset = r.F("suspensionOffset", v.SuspensionOffset);
            v.SuspensionColliderName = r.Str("suspensionColliderName", v.SuspensionColliderName);
            v.MaximumLoad = r.F("maximumLoad", v.MaximumLoad);
            v.SpringRatio = r.F("springRatio", v.SpringRatio, unused: true);
            v.DamperRatio = r.F("damperRatio", v.DamperRatio, unused: true);
            v.BoostRatio = r.F("boostRatio", v.BoostRatio, unused: true);
            v.UseDistributedMass = r.B("useDistributedMass", v.UseDistributedMass, unused: true);
            v.UseAutoBoost = r.B("useAutoBoost", v.UseAutoBoost, unused: true);
            v.AdjustForHighGee = r.B("adjustForHighGee", v.AdjustForHighGee, unused: true);
            r.NotReproduced("extendDurationFactor");
            r.NotReproduced(RetractFactor);
            r.NotReproduced("suppressModuleInfo");
            r.Finish();
            m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelSteeringValues> Steering(IKsp1Node node)
        {
            var v = new WheelSteeringValues();
            var m = new WheelMapped<WheelSteeringValues>(WheelKind.Steering, v);
            if (node == null) return m;
            var r = new Reader<WheelSteeringValues>(node, m);
            r.BaseIndex();
            v.CaliperTransformName = r.Str("caliperTransformName", v.CaliperTransformName);
            v.SteeringResponse = r.F("steeringResponse", v.SteeringResponse);
            v.AutoSteeringAdjust = r.B("autoSteeringAdjust", v.AutoSteeringAdjust);
            r.NotReproduced("steeringRange", "ModuleWheelSteering: steeringRange not reproduced (KSP2 takes the curve maximum)");
            var curve = r.Curve("steeringCurve");
            v.SteeringMaxAngleCurve = r.Curve("steeringMaxAngleCurve") ?? v.SteeringMaxAngleCurve;
            r.Finish();
            if (curve == null || curve.Length == 0) { r.Note("ModuleWheelSteering: no steeringCurve key, no steering"); return m; }
            v.SteeringCurve = curve;
            m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelMotorValues> Motor(IKsp1Node node, IsruResources resources)
        {
            var v = new WheelMotorValues();
            var m = new WheelMapped<WheelMotorValues>(WheelKind.Motor, v);
            if (node == null) return m;
            var r = new Reader<WheelMotorValues>(node, m);
            var converted = ReadMotor(r, v, resources, "ModuleWheelMotor");
            r.Finish();
            m.Converted = converted;
            return m;
        }

        public static WheelMapped<WheelMotorSteeringValues> MotorSteering(IKsp1Node node, IsruResources resources)
        {
            var v = new WheelMotorSteeringValues();
            var m = new WheelMapped<WheelMotorSteeringValues>(WheelKind.MotorSteering, v);
            if (node == null) return m;
            var r = new Reader<WheelMotorSteeringValues>(node, m);
            var converted = ReadMotor(r, v.Motor, resources, "ModuleWheelMotorSteering");
            v.SteeringTorque = r.F("steeringTorque", v.SteeringTorque);
            r.Finish();
            m.Converted = converted;
            return m;
        }

        private static bool ReadMotor<T>(Reader<T> r, WheelMotorValues v, IsruResources resources, string module) where T : class
        {
            r.BaseIndex();
            v.WheelSpeedMax = r.F("wheelSpeedMax", v.WheelSpeedMax);
            v.DriveResponse = r.F("driveResponse", v.DriveResponse);
            v.IdleDrain = r.D("idleDrain", v.IdleDrain);
            v.MotorEnabled = r.B("motorEnabled", v.MotorEnabled);
            v.AutoTorque = r.B("autoTorque", v.AutoTorque);
            v.TractionControlScale = r.Clamped("tractionControlScale", v.TractionControlScale, 0, 5);
            var curve = r.Curve("torqueCurve");
            var first = true;
            foreach (var res in r.Nodes("RESOURCE"))
            {
                if (!first)
                {
                    r.NotReproducedField("RESOURCE (second)", $"{module}: a second RESOURCE not reproduced (KSP2's motor draws one resource)");
                    continue;
                }
                first = false;
                var name = IsruCfg.Value(res, "name");
                var rateText = IsruCfg.Value(res, "rate");
                if (string.IsNullOrEmpty(name)) { r.Note($"{module}: RESOURCE without a name, no drain"); continue; }
                if (!IsruCfg.TryNumber(rateText, out var rate) || rate < 0)
                {
                    r.Note($"{module}: {name}: rate '{rateText}' is not a number of 0 or more, no drain");
                    continue;
                }
                resources = resources ?? new IsruResources(null, null, null, null);
                var mapped = resources.Map(name);
                if (!resources.IsKnown(mapped))
                {
                    r.Note($"{module}: {name} -> {mapped} is not a game resource, no drain (the motor stays)");
                    continue;
                }
                v.UseResources = true;
                v.RequiredResourceName = mapped;
                v.RequiredResourceRate = rate;
            }
            if (curve == null || curve.Length == 0)
            {
                r.Note($"{module}: no torqueCurve key, no motor");
                return false;
            }
            v.TorqueCurve = curve;
            return true;
        }

        public static WheelMapped<WheelBrakesValues> Brakes(IKsp1Node node)
        {
            var v = new WheelBrakesValues();
            var m = new WheelMapped<WheelBrakesValues>(WheelKind.Brakes, v);
            if (node == null) return m;
            var r = new Reader<WheelBrakesValues>(node, m);
            r.BaseIndex();
            v.MaxBrakeTorque = r.F("maxBrakeTorque", v.MaxBrakeTorque);
            v.BrakeResponse = r.F("brakeResponse", v.BrakeResponse);
            r.NotReproduced("statusLightModuleIndex", "ModuleWheelBrakes: statusLightModuleIndex not reproduced (Redux drops ModuleStatusLight)");
            r.Finish();
            m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelDamageValues> Damage(IKsp1Node node)
        {
            var v = new WheelDamageValues();
            var m = new WheelMapped<WheelDamageValues>(WheelKind.Damage, v);
            if (node == null) return m;
            var r = new Reader<WheelDamageValues>(node, m);
            r.BaseIndex();
            v.StressTolerance = r.F("stressTolerance", v.StressTolerance);
            v.DeflectionMagnitude = r.F("deflectionMagnitude", v.DeflectionMagnitude);
            v.DeflectionSharpness = r.F("deflectionSharpness", v.DeflectionSharpness);
            v.SlipMagnitude = r.F("slipMagnitude", v.SlipMagnitude);
            v.SlipSharpness = r.F("slipSharpness", v.SlipSharpness);
            v.DamagedTransformName = r.Str("damagedTransformName", v.DamagedTransformName);
            v.UndamagedTransformName = r.Str("undamagedTransformName", v.UndamagedTransformName);
            v.ExplodeMultiplier = r.F("explodeMultiplier", v.ExplodeMultiplier);
            v.ImpactDamageVelocity = r.F("impactDamageVelocity", v.ImpactDamageVelocity);
            v.IsRepairable = r.B("isRepairable", v.IsRepairable);
            v.ImpactTolerance = r.F("impactTolerance", v.ImpactTolerance, unused: true);
            v.ImpactDamageColliderName = r.Str("impactDamageColliderName", v.ImpactDamageColliderName, unused: true);
            r.Finish();
            m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelLockValues> Lock(IKsp1Node node)
        {
            var v = new WheelLockValues();
            var m = new WheelMapped<WheelLockValues>(WheelKind.Lock, v);
            if (node == null) return m;
            var r = new Reader<WheelLockValues>(node, m);
            r.BaseIndex();
            v.MaxTorque = r.F("maxTorque", v.MaxTorque);
            r.Finish();
            m.Converted = true;
            return m;
        }

        public static WheelMapped<WheelBogeyValues> Bogey(IKsp1Node node)
        {
            var v = new WheelBogeyValues();
            var m = new WheelMapped<WheelBogeyValues>(WheelKind.Bogey, v);
            if (node == null) return m;
            var r = new Reader<WheelBogeyValues>(node, m);
            r.BaseIndex();
            v.BogeyTransformName = r.Str("bogeyTransformName", v.BogeyTransformName);
            v.BogeyRefTransformName = r.Str("bogeyRefTransformName", v.BogeyRefTransformName);
            v.WheelTransformRefName = r.Str("wheelTransformRefName", v.WheelTransformRefName);
            v.WheelTransformBaseName = r.Str("wheelTransformBaseName", v.WheelTransformBaseName);
            v.MaxPitch = r.F("maxPitch", v.MaxPitch);
            v.MinPitch = r.F("minPitch", v.MinPitch);
            v.RestPitch = r.F("restPitch", v.RestPitch);
            v.PitchResponse = r.F("pitchResponse", v.PitchResponse);
            v.BogeyAxis = r.V("bogeyAxis", v.BogeyAxis);
            v.BogeyUpAxis = r.V("bogeyUpAxis", v.BogeyUpAxis);
            r.NotReproduced("deployModuleIndex", "ModuleWheelBogey: deployModuleIndex not reproduced (KSP2 finds the deployable by component)");
            r.Finish();
            m.Converted = true;
            return m;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A KSP2 wheel type name, any case; numbers are refused.</summary>
        public static bool TryWheelType(string text, out WheelTypeName type)
        {
            foreach (WheelTypeName t in Enum.GetValues(typeof(WheelTypeName)))
                if (string.Equals(t.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase)) { type = t; return true; }
            type = WheelTypeName.FREE;
            return false;
        }

        /// <summary>KSP1 FloatCurve keys (<see cref="EntryParser.Keys"/>, four floats per key); null for no node. A key line
        /// without both tangents keeps NaN for them (see <see cref="IsSmoothKey"/>).</summary>
        public static float[] CurveKeys(IKsp1Node curve) => curve == null ? null : EntryParser.Keys(curve);

        /// <summary>Key <paramref name="key"/> of flattened <paramref name="keys"/> has no tangents: KSP1 loads such a key with
        /// Unity's <c>AddKey(time, value)</c>, which gives it smooth automatic tangents, so the importer adds it with
        /// <c>FloatCurve.Add(time, value)</c>, not with tangents 0.</summary>
        public static bool IsSmoothKey(float[] keys, int key) => float.IsNaN(keys[key * 4 + 2]) || float.IsNaN(keys[key * 4 + 3]);

        private static float Clamp<T>(float value, float min, float max, string what, WheelMapped<T> m) where T : class
        {
            if (value >= min && value <= max) return value;
            var clamped = value < min ? min : max;
            m._notes.Add($"{what} {IsruCfg.Inv(value)} clamped to {IsruCfg.Inv(clamped)}");
            return clamped;
        }

        /// <summary>Reads one module node and lists what it did not use.</summary>
        private sealed class Reader<T> where T : class
        {
            private readonly IKsp1Node _node;
            private readonly WheelMapped<T> _m;
            private readonly string _module;
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal) { "name" };
            private readonly HashSet<string> _nodes = new HashSet<string>(StringComparer.Ordinal);

            public Reader(IKsp1Node node, WheelMapped<T> m)
            {
                _node = node;
                _m = m;
                _module = Ksp1Module(m.Kind);
            }

            /// <summary>The comment-stripped first value, or null; the key counts as read.</summary>
            public string Text(string key)
            {
                _seen.Add(key);
                return IsruCfg.Value(_node, key);
            }

            private void Unused(string field)
            {
                if (!_m._unused.Contains(field)) _m._unused.Add(field);
            }

            public void Note(string note)
            {
                if (!_m._notes.Contains(note)) _m._notes.Add(note);
            }

            public void NotReproducedField(string field, string note)
            {
                if (!_m._notReproduced.Contains(field)) _m._notReproduced.Add(field);
                Note(note);
            }

            private string Bad(string key, string raw, string what) =>
                $"{_module}: {key} '{raw}' is not {what}, KSP2's default kept";

            public float F(string key, float current, bool unused = false, string field = null)
            {
                var t = Text(key);
                if (t == null) return current;
                if (unused) Unused(field ?? key);
                if (IsruCfg.TryNumber(t, out var d)) return (float)d;
                Note(Bad(key, t, "a number"));
                return current;
            }

            /// <summary>A number clamped to <paramref name="min"/>..<paramref name="max"/>, with a note when clamped.</summary>
            public float Clamped(string key, float current, float min, float max)
            {
                var value = F(key, current);
                return Clamp(value, min, max, _module + ": " + key, _m);
            }

            public double D(string key, double current)
            {
                var t = Text(key);
                if (t == null) return current;
                if (IsruCfg.TryNumber(t, out var d)) return d;
                Note(Bad(key, t, "a number"));
                return current;
            }

            public bool B(string key, bool current, bool unused = false)
            {
                var t = Text(key);
                if (t == null) return current;
                if (unused) Unused(key);
                if (bool.TryParse(t, out var b)) return b;
                Note(Bad(key, t, "true or false"));
                return current;
            }

            public string Str(string key, string current, bool unused = false)
            {
                var t = Text(key);
                if (t == null) return current;
                if (unused) Unused(key);
                return t;
            }

            /// <summary>KSP1's vector form <c>x, y, z</c>.</summary>
            public WheelVector V(string key, WheelVector current)
            {
                var t = Text(key);
                if (t == null) return current;
                var parts = t.Split(',');
                if (parts.Length == 3 && IsruCfg.TryNumber(parts[0], out var x) && IsruCfg.TryNumber(parts[1], out var y)
                    && IsruCfg.TryNumber(parts[2], out var z))
                    return new WheelVector((float)x, (float)y, (float)z);
                Note(Bad(key, t, "a vector x, y, z"));
                return current;
            }

            public float[] Curve(string nodeName)
            {
                _nodes.Add(nodeName);
                return CurveKeys(_node.GetNodes(nodeName).FirstOrDefault());
            }

            public IEnumerable<IKsp1Node> Nodes(string nodeName)
            {
                _nodes.Add(nodeName);
                return _node.GetNodes(nodeName);
            }

            public void NotReproduced(string key, string note = null)
            {
                var t = Text(key);
                if (t == null) return;
                NotReproducedField(key, note ?? $"{_module}: {key} not reproduced");
            }

            /// <summary><c>baseModuleIndex</c>: quiet when 0 (the one base KSP2 finds), not reproduced otherwise.</summary>
            public void BaseIndex()
            {
                var t = Text("baseModuleIndex");
                if (t == null || (IsruCfg.TryNumber(t, out var d) && d == 0)) return;
                NotReproducedField("baseModuleIndex", $"{_module}: baseModuleIndex = {t} not reproduced (KSP2 has one wheel per part)");
            }

            /// <summary>Every key and child node not read is not reproduced.</summary>
            public void Finish()
            {
                foreach (var kv in _node.AllValues)
                {
                    var key = kv.Key?.Trim() ?? "";
                    if (key.Length == 0 || _seen.Contains(key)) continue;
                    NotReproducedField(key, $"{_module}: {key} not reproduced");
                }
                foreach (var child in _node.AllNodes)
                {
                    var name = child.Name?.Trim() ?? "";
                    if (_nodes.Contains(name)) continue;
                    NotReproducedField("node " + name, $"{_module}: node {name} not reproduced");
                }
            }
        }
    }
}
