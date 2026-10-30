using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Sim.Definitions;
using KSP1PartMod.Native.Wheels;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>The Redux members the wheel importers need (wheels plan, Task 3), resolved by name once:
    /// <list type="bullet">
    /// <item>the nine <c>KSP.Modules.Module_Wheel*</c> behaviours and their <c>Data_Wheel*</c> types (a parameterless
    /// constructor each);</item>
    /// <item>every field of plan (f) on its data type, with its exact type. A <c>ModuleProperty</c> (or
    /// <c>EntityModuleProperty</c>) field is a state value: its initial value is set through the property's public
    /// <c>SetValue</c>;</item>
    /// <item><c>KSP.Modules.WheelType</c> with KSP1's names <c>FREE</c>, <c>MOTORIZED</c>, <c>LEG</c>;</item>
    /// <item>the global <c>FloatCurve</c> and its <c>Add(time, value)</c> and <c>Add(time, value, in, out)</c>;</item>
    /// <item>for <c>Module_WheelMotorSteering</c> (two data objects), the list overload of <c>CreateSerialized</c>, from
    /// <see cref="RadiatorAccess"/>.</item>
    /// </list>
    /// A missing member turns off its own kind (<see cref="Targets.MissingFor"/>); a missing base member turns off every kind,
    /// because a sub-module without a base is worse than no wheel. <c>Data_WheelBrakes</c> and <c>Data_WheelMotorSteering</c>
    /// implement Unity.Entities' <c>IComponentData</c>, which this assembly does not reference: every data type is handled by
    /// reflection only. Import time only, never per frame.</summary>
    internal static class WheelAccess
    {
        public const string Namespace = "KSP.Modules.";
        public const string WheelTypeTypeName = "KSP.Modules.WheelType";
        public const string FloatCurveTypeName = "FloatCurve";

        /// <summary>The kind of field a plan (f) value goes into.</summary>
        internal enum FieldKind { Float, Double, Bool, String, Vector, WheelType, Curve, Resource, BoolProperty, FloatProperty }

        /// <summary>The fields each data type must have (plan (f), and the review items of Tasks 1+2), by KSP2 name.</summary>
        internal static readonly IReadOnlyDictionary<WheelKind, (string Name, FieldKind Kind)[]> Fields =
            new Dictionary<WheelKind, (string, FieldKind)[]>
            {
                [WheelKind.Base] = new[]
                {
                    ("WheelType", FieldKind.WheelType), ("WheelColliderTransformName", FieldKind.String), ("WheelTransformName", FieldKind.String),
                    ("Radius", FieldKind.Float), ("Center", FieldKind.Vector), ("Mass", FieldKind.Float), ("FitWheelColliderToMesh", FieldKind.Bool),
                    ("GroundHeightOffset", FieldKind.Float), ("ClipObject", FieldKind.String), ("UseNewFrictionModel", FieldKind.Bool),
                    ("FrictionSharpness", FieldKind.Float), ("TireForceSharpness", FieldKind.Float), ("AdherentStart", FieldKind.Float),
                    ("FrictionAdherent", FieldKind.Float), ("PeakStart", FieldKind.Float), ("LimitStart", FieldKind.Float),
                    ("FrictionPeak", FieldKind.Float), ("FrictionLimit", FieldKind.Float), ("AutoFriction", FieldKind.BoolProperty),
                    ("FrictionMultiplier", FieldKind.FloatProperty), ("DeploymentSubsystemNormalized", FieldKind.Float),
                    ("RetractionSubsystemNormalized", FieldKind.Float), ("UseStandinCollider", FieldKind.Bool),
                },
                [WheelKind.Suspension] = new[]
                {
                    ("suspensionTransformName", FieldKind.String), ("suspensionDistance", FieldKind.Float), ("targetPosition", FieldKind.Float),
                    ("suspensionOffset", FieldKind.Float), ("suspensionColliderName", FieldKind.String), ("maximumLoad", FieldKind.Float),
                    ("springRatio", FieldKind.Float), ("damperRatio", FieldKind.Float), ("boostRatio", FieldKind.Float),
                    ("useDistributedMass", FieldKind.Bool), ("useAutoBoost", FieldKind.Bool), ("adjustForHighGee", FieldKind.Bool),
                },
                [WheelKind.Steering] = new[]
                {
                    ("CaliperTransformName", FieldKind.String), ("SteeringResponse", FieldKind.Float), ("AutoSteeringAdjust", FieldKind.BoolProperty),
                    ("SteeringCurve", FieldKind.Curve), ("SteeringMaxAngleCurve", FieldKind.Curve),
                },
                [WheelKind.Motor] = new[]
                {
                    ("wheelSpeedMax", FieldKind.Float), ("driveResponse", FieldKind.Float), ("idleDrain", FieldKind.Double),
                    ("torqueCurve", FieldKind.Curve), ("useResources", FieldKind.Bool), ("requiredResource", FieldKind.Resource),
                    ("motorEnabled", FieldKind.BoolProperty), ("autoTorque", FieldKind.BoolProperty), ("tractionControlScale", FieldKind.FloatProperty),
                },
                [WheelKind.MotorSteering] = new[] { ("SteeringTorque", FieldKind.Float) },
                [WheelKind.Brakes] = new[] { ("MaxBrakeTorque", FieldKind.Float), ("BrakeResponse", FieldKind.Float) },
                [WheelKind.Damage] = new[]
                {
                    ("stressTolerance", FieldKind.Float), ("deflectionMagnitude", FieldKind.Float), ("deflectionSharpness", FieldKind.Float),
                    ("slipMagnitude", FieldKind.Float), ("slipSharpness", FieldKind.Float), ("damagedTransformName", FieldKind.String),
                    ("undamagedTransformName", FieldKind.String), ("explodeMultiplier", FieldKind.Float), ("impactDamageVelocity", FieldKind.Float),
                    ("isRepairable", FieldKind.Bool), ("impactTolerance", FieldKind.Float), ("impactDamageColliderName", FieldKind.String),
                },
                [WheelKind.Lock] = new[] { ("MaxTorque", FieldKind.Float) },
                [WheelKind.Bogey] = new[]
                {
                    ("bogeyTransformName", FieldKind.String), ("bogeyRefTransformName", FieldKind.String), ("wheelTransformRefName", FieldKind.String),
                    ("wheelTransformBaseName", FieldKind.String), ("maxPitch", FieldKind.Float), ("minPitch", FieldKind.Float),
                    ("restPitch", FieldKind.Float), ("pitchResponse", FieldKind.Float), ("bogeyAxis", FieldKind.Vector), ("bogeyUpAxis", FieldKind.Vector),
                },
            };

        /// <summary>The bound members (null when missing).</summary>
        internal sealed class Targets
        {
            public readonly Type[] Module = new Type[9];
            public readonly Type[] Data = new Type[9];
            public readonly Dictionary<WheelKind, Dictionary<string, FieldInfo>> Field = new Dictionary<WheelKind, Dictionary<string, FieldInfo>>();
            public Type WheelType, FloatCurve;
            public MethodInfo CurveAdd2, CurveAdd4;
            public bool HasListForm;
            /// <summary>The members each kind lacks on its own (its types and fields).</summary>
            public readonly Dictionary<WheelKind, List<string>> Own = new Dictionary<WheelKind, List<string>>();
            public readonly List<string> CurveMissing = new List<string>();

            public Type ModuleOf(WheelKind kind) => Module[(int)kind];
            public Type DataOf(WheelKind kind) => Data[(int)kind];

            /// <summary>Every member <paramref name="kind"/> needs that the game lacks: the base's, its own, <c>FloatCurve</c>
            /// for the curve kinds, and for <c>Module_WheelMotorSteering</c> the motor's data and the list form.</summary>
            public List<string> MissingFor(WheelKind kind)
            {
                var list = new List<string>(Own[WheelKind.Base]);
                if (kind != WheelKind.Base) list.AddRange(Own[kind]);
                if (kind == WheelKind.Steering || kind == WheelKind.Motor || kind == WheelKind.MotorSteering) list.AddRange(CurveMissing);
                if (kind == WheelKind.MotorSteering)
                {
                    list.AddRange(Own[WheelKind.Motor]);
                    if (!HasListForm)
                        list.Add("Ksp1PartModuleImporterRegistry." + NativeModules.CreateSerializedName + "(Type, IReadOnlyList<ModuleData>, Ksp1PartImportContext)");
                }
                return list.Distinct().ToList();
            }
        }

        private static readonly object Lock = new object();
        private static Targets _targets;

        /// <summary>Resolved once per session.</summary>
        internal static Targets Get()
        {
            lock (Lock) return _targets ?? (_targets = Resolve());
        }

        internal static void Reset()
        {
            lock (Lock) _targets = null;
        }

        internal static Targets Resolve()
        {
            var t = new Targets();
            t.WheelType = AccessTools.TypeByName(WheelTypeTypeName);
            var typeMissing = t.WheelType == null || !t.WheelType.IsEnum ||
                              Enum.GetValues(typeof(WheelTypeName)).Cast<WheelTypeName>().Any(n => !Enum.GetNames(t.WheelType).Contains(n.ToString()));
            if (typeMissing) t.WheelType = null;

            t.FloatCurve = AccessTools.TypeByName(FloatCurveTypeName);
            if (t.FloatCurve != null && t.FloatCurve.GetConstructor(Type.EmptyTypes) != null)
            {
                t.CurveAdd2 = AccessTools.Method(t.FloatCurve, "Add", new[] { typeof(float), typeof(float) });
                t.CurveAdd4 = AccessTools.Method(t.FloatCurve, "Add", new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
                if (t.CurveAdd2 == null || t.CurveAdd2.IsStatic) { t.CurveAdd2 = null; t.CurveMissing.Add("FloatCurve.Add(float, float)"); }
                if (t.CurveAdd4 == null || t.CurveAdd4.IsStatic) { t.CurveAdd4 = null; t.CurveMissing.Add("FloatCurve.Add(float, float, float, float)"); }
            }
            else
            {
                t.FloatCurve = null;
                t.CurveMissing.Add("FloatCurve()");
            }
            t.HasListForm = RadiatorAccess.Get().HasListForm;

            foreach (WheelKind kind in Enum.GetValues(typeof(WheelKind)))
            {
                var own = t.Own[kind] = new List<string>();
                if (kind == WheelKind.Base && typeMissing) own.Add(WheelTypeTypeName + " (FREE, MOTORIZED, LEG)");
                var moduleName = WheelMapping.NativeModule(kind);
                var dataName = "Data_" + moduleName.Substring("Module_".Length);
                var module = AccessTools.TypeByName(Namespace + moduleName);
                var data = AccessTools.TypeByName(Namespace + dataName);
                if (module == null) own.Add(Namespace + moduleName);
                if (data == null || !typeof(ModuleData).IsAssignableFrom(data) || data.GetConstructor(Type.EmptyTypes) == null)
                {
                    data = null;
                    own.Add(Namespace + dataName + "()");
                }
                t.Module[(int)kind] = module;
                t.Data[(int)kind] = data;
                var fields = t.Field[kind] = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                if (data == null) continue;
                foreach (var (name, fieldKind) in Fields[kind])
                {
                    var f = AccessTools.Field(data, name);
                    if (f != null && !f.IsStatic && Matches(t, f.FieldType, fieldKind)) fields[name] = f;
                    else own.Add(dataName + "." + name);
                }
            }
            return t;
        }

        private static bool Matches(Targets t, Type type, FieldKind kind)
        {
            switch (kind)
            {
                case FieldKind.Float: return type == typeof(float);
                case FieldKind.Double: return type == typeof(double);
                case FieldKind.Bool: return type == typeof(bool);
                case FieldKind.String: return type == typeof(string);
                case FieldKind.Vector: return type == typeof(Vector3);
                case FieldKind.WheelType: return t.WheelType != null && type == t.WheelType;
                case FieldKind.Curve: return t.FloatCurve != null && type == t.FloatCurve;
                case FieldKind.Resource: return type == typeof(PartModuleResourceSetting);
                case FieldKind.BoolProperty: return typeof(ModuleProperty<bool>).IsAssignableFrom(type);
                default: return typeof(ModuleProperty<float>).IsAssignableFrom(type);
            }
        }

        // ------------------------------------------------------------------ building

        private static ModuleData New(WheelKind kind)
        {
            var t = Get();
            var missing = t.MissingFor(kind);
            if (missing.Count > 0) throw new MissingMemberException(string.Join(", ", missing));
            return (ModuleData)Activator.CreateInstance(t.DataOf(kind));
        }

        private static void Set(ModuleData data, WheelKind kind, string name, object value) => Get().Field[kind][name].SetValue(data, value);

        private static void SetProperty<T>(ModuleData data, WheelKind kind, string name, T value)
        {
            var property = (ModuleProperty<T>)Get().Field[kind][name].GetValue(data);
            if (property == null) throw new InvalidOperationException(data.GetType().Name + "." + name + " is null");
            property.SetValue(value);
        }

        private static Vector3 V(WheelVector v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>A KSP2 <c>FloatCurve</c> from flattened keys: a key with both tangents through <c>Add(t, v, in, out)</c>, a
        /// key without (<see cref="WheelMapping.IsSmoothKey"/>) through <c>Add(t, v)</c>, as KSP1 loads it.</summary>
        internal static object NewCurve(float[] keys)
        {
            var t = Get();
            if (t.CurveMissing.Count > 0) throw new MissingMemberException(string.Join(", ", t.CurveMissing));
            var curve = Activator.CreateInstance(t.FloatCurve);
            keys = keys ?? Array.Empty<float>();
            for (var k = 0; k < keys.Length / 4; k++)
            {
                if (WheelMapping.IsSmoothKey(keys, k)) t.CurveAdd2.Invoke(curve, new object[] { keys[k * 4], keys[k * 4 + 1] });
                else t.CurveAdd4.Invoke(curve, new object[] { keys[k * 4], keys[k * 4 + 1], keys[k * 4 + 2], keys[k * 4 + 3] });
            }
            return curve;
        }

        internal static ModuleData NewBase(WheelBaseValues v)
        {
            const WheelKind k = WheelKind.Base;
            var d = New(k);
            Set(d, k, "WheelType", Enum.Parse(Get().WheelType, v.WheelType.ToString()));
            Set(d, k, "WheelColliderTransformName", v.WheelColliderTransformName);
            Set(d, k, "WheelTransformName", v.WheelTransformName ?? "");
            Set(d, k, "Radius", v.Radius);
            Set(d, k, "Center", V(v.Center));
            Set(d, k, "Mass", v.Mass);
            Set(d, k, "FitWheelColliderToMesh", v.FitWheelColliderToMesh);
            Set(d, k, "GroundHeightOffset", v.GroundHeightOffset);
            Set(d, k, "ClipObject", v.ClipObject ?? "");
            Set(d, k, "UseNewFrictionModel", v.UseNewFrictionModel);
            Set(d, k, "FrictionSharpness", v.FrictionSharpness);
            Set(d, k, "TireForceSharpness", v.TireForceSharpness);
            Set(d, k, "AdherentStart", v.AdherentStart);
            Set(d, k, "FrictionAdherent", v.FrictionAdherent);
            Set(d, k, "PeakStart", v.PeakStart);
            Set(d, k, "LimitStart", v.LimitStart);
            Set(d, k, "FrictionPeak", v.FrictionPeak);
            Set(d, k, "FrictionLimit", v.FrictionLimit);
            Set(d, k, "DeploymentSubsystemNormalized", v.DeploymentSubsystemNormalized);
            Set(d, k, "RetractionSubsystemNormalized", v.RetractionSubsystemNormalized);
            Set(d, k, "UseStandinCollider", v.UseStandinCollider);
            SetProperty(d, k, "AutoFriction", v.AutoFriction);
            SetProperty(d, k, "FrictionMultiplier", v.FrictionMultiplier);
            return d;
        }

        internal static ModuleData NewSuspension(WheelSuspensionValues v)
        {
            const WheelKind k = WheelKind.Suspension;
            var d = New(k);
            Set(d, k, "suspensionTransformName", v.SuspensionTransformName ?? "");
            Set(d, k, "suspensionDistance", v.SuspensionDistance);
            Set(d, k, "targetPosition", v.TargetPosition);
            Set(d, k, "suspensionOffset", v.SuspensionOffset);
            Set(d, k, "suspensionColliderName", v.SuspensionColliderName ?? "");
            Set(d, k, "maximumLoad", v.MaximumLoad);
            Set(d, k, "springRatio", v.SpringRatio);
            Set(d, k, "damperRatio", v.DamperRatio);
            Set(d, k, "boostRatio", v.BoostRatio);
            Set(d, k, "useDistributedMass", v.UseDistributedMass);
            Set(d, k, "useAutoBoost", v.UseAutoBoost);
            Set(d, k, "adjustForHighGee", v.AdjustForHighGee);
            return d;
        }

        internal static ModuleData NewSteering(WheelSteeringValues v)
        {
            const WheelKind k = WheelKind.Steering;
            var d = New(k);
            Set(d, k, "CaliperTransformName", v.CaliperTransformName ?? "");
            Set(d, k, "SteeringResponse", v.SteeringResponse);
            Set(d, k, "SteeringCurve", NewCurve(v.SteeringCurve));
            Set(d, k, "SteeringMaxAngleCurve", NewCurve(v.SteeringMaxAngleCurve));
            SetProperty(d, k, "AutoSteeringAdjust", v.AutoSteeringAdjust);
            return d;
        }

        internal static ModuleData NewMotor(WheelMotorValues v)
        {
            const WheelKind k = WheelKind.Motor;
            var d = New(k);
            Set(d, k, "wheelSpeedMax", v.WheelSpeedMax);
            Set(d, k, "driveResponse", v.DriveResponse);
            Set(d, k, "idleDrain", v.IdleDrain);
            Set(d, k, "torqueCurve", NewCurve(v.TorqueCurve));
            Set(d, k, "useResources", v.UseResources);
            if (v.UseResources)
                Set(d, k, "requiredResource", new PartModuleResourceSetting { ResourceName = v.RequiredResourceName, Rate = (float)v.RequiredResourceRate });
            SetProperty(d, k, "motorEnabled", v.MotorEnabled);
            SetProperty(d, k, "autoTorque", v.AutoTorque);
            SetProperty(d, k, "tractionControlScale", v.TractionControlScale);
            return d;
        }

        /// <summary><c>Data_WheelMotor</c> then <c>Data_WheelMotorSteering</c>, the order of <c>Module_WheelMotorSteering</c>'s
        /// data (the motor's first, as the base class).</summary>
        internal static ModuleData[] NewMotorSteering(WheelMotorSteeringValues v)
        {
            const WheelKind k = WheelKind.MotorSteering;
            var steering = New(k);
            Set(steering, k, "SteeringTorque", v.SteeringTorque);
            return new[] { NewMotor(v.Motor), steering };
        }

        internal static ModuleData NewBrakes(WheelBrakesValues v)
        {
            const WheelKind k = WheelKind.Brakes;
            var d = New(k);
            Set(d, k, "MaxBrakeTorque", v.MaxBrakeTorque);
            Set(d, k, "BrakeResponse", v.BrakeResponse);
            return d;
        }

        internal static ModuleData NewDamage(WheelDamageValues v)
        {
            const WheelKind k = WheelKind.Damage;
            var d = New(k);
            Set(d, k, "stressTolerance", v.StressTolerance);
            Set(d, k, "deflectionMagnitude", v.DeflectionMagnitude);
            Set(d, k, "deflectionSharpness", v.DeflectionSharpness);
            Set(d, k, "slipMagnitude", v.SlipMagnitude);
            Set(d, k, "slipSharpness", v.SlipSharpness);
            Set(d, k, "damagedTransformName", v.DamagedTransformName ?? "");
            Set(d, k, "undamagedTransformName", v.UndamagedTransformName ?? "");
            Set(d, k, "explodeMultiplier", v.ExplodeMultiplier);
            Set(d, k, "impactDamageVelocity", v.ImpactDamageVelocity);
            Set(d, k, "isRepairable", v.IsRepairable);
            Set(d, k, "impactTolerance", v.ImpactTolerance);
            Set(d, k, "impactDamageColliderName", v.ImpactDamageColliderName ?? "");
            return d;
        }

        internal static ModuleData NewLock(WheelLockValues v)
        {
            const WheelKind k = WheelKind.Lock;
            var d = New(k);
            Set(d, k, "MaxTorque", v.MaxTorque);
            return d;
        }

        internal static ModuleData NewBogey(WheelBogeyValues v)
        {
            const WheelKind k = WheelKind.Bogey;
            var d = New(k);
            Set(d, k, "bogeyTransformName", v.BogeyTransformName ?? "");
            Set(d, k, "bogeyRefTransformName", v.BogeyRefTransformName ?? "");
            Set(d, k, "wheelTransformRefName", v.WheelTransformRefName ?? "");
            Set(d, k, "wheelTransformBaseName", v.WheelTransformBaseName ?? "");
            Set(d, k, "maxPitch", v.MaxPitch);
            Set(d, k, "minPitch", v.MinPitch);
            Set(d, k, "restPitch", v.RestPitch);
            Set(d, k, "pitchResponse", v.PitchResponse);
            Set(d, k, "bogeyAxis", V(v.BogeyAxis));
            Set(d, k, "bogeyUpAxis", V(v.BogeyUpAxis));
            return d;
        }

        // ------------------------------------------------------------------ reading (reference line, tests)

        /// <summary>The wheel kind whose data type <paramref name="data"/> is, or null.</summary>
        internal static WheelKind? KindOf(ModuleData data)
        {
            if (data == null) return null;
            var t = Get();
            for (var i = 0; i < t.Data.Length; i++)
                if (t.Data[i] != null && t.Data[i] == data.GetType()) return (WheelKind)i;
            return null;
        }

        /// <summary>A bound field's value; a <c>ModuleProperty</c> gives its current value.</summary>
        internal static object Read(ModuleData data, WheelKind kind, string name)
        {
            var value = Get().Field[kind][name].GetValue(data);
            if (value is ModuleProperty<bool> b) return b.GetValue();
            if (value is ModuleProperty<float> f) return f.GetValue();
            return value;
        }

        /// <summary>The keys of a <c>FloatCurve</c> (its <c>Curve</c>), empty for null.</summary>
        internal static Keyframe[] KeysOf(object floatCurve)
        {
            if (floatCurve == null) return Array.Empty<Keyframe>();
            var curve = AccessTools.Property(floatCurve.GetType(), "Curve")?.GetValue(floatCurve) as AnimationCurve;
            return curve?.keys ?? Array.Empty<Keyframe>();
        }
    }
}
