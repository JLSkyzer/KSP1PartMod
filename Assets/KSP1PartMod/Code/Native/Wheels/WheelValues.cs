using System;
using System.Collections.Generic;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>The nine KSP1 wheel modules this plan converts, in the order of the summary line.</summary>
    public enum WheelKind { Base, Suspension, Steering, Motor, MotorSteering, Brakes, Damage, Lock, Bogey }

    /// <summary>KSP2's <c>WheelType</c> names (KSP1's own).</summary>
    public enum WheelTypeName { FREE, MOTORIZED, LEG }

    /// <summary>A vector without UnityEngine.</summary>
    public readonly struct WheelVector
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public WheelVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({IsruCfg.Inv(X)}, {IsruCfg.Inv(Y)}, {IsruCfg.Inv(Z)})";
    }

    /// <summary>The values of <c>Data_WheelBase</c> that the mapping sets (plan (f)). Every field starts at KSP2's default;
    /// <c>AutoFriction</c> and <c>FrictionMultiplier</c> are initial values of module properties.</summary>
    public sealed class WheelBaseValues
    {
        public WheelTypeName WheelType = WheelTypeName.FREE;
        public string WheelColliderTransformName = "wheelCol";
        public string WheelTransformName = "";
        public float Radius;
        public WheelVector Center;
        public float Mass;
        public bool FitWheelColliderToMesh;
        public float GroundHeightOffset;
        public string ClipObject = "";
        public bool UseNewFrictionModel = true;
        public float FrictionSharpness;
        public float TireForceSharpness = 10f;
        public float AdherentStart = 0.5f;
        public float FrictionAdherent = 0.25f;
        public float PeakStart = 4f;
        public float LimitStart = 7f;
        public float FrictionPeak = 1.45f;
        public float FrictionLimit = 1.1f;
        public bool AutoFriction = true;
        public float FrictionMultiplier = 1f;
        public float DeploymentSubsystemNormalized = 0.98f;
        public float RetractionSubsystemNormalized = 0.02f;
        public bool UseStandinCollider;
    }

    /// <summary>The values of <c>Data_WheelSuspension</c> that the mapping sets. <c>naturalFrequency</c> and <c>dampingRatio</c>
    /// are not set: they stay 0, Redux's per-type defaults.</summary>
    public sealed class WheelSuspensionValues
    {
        public string SuspensionTransformName = "";
        public float SuspensionDistance;
        public float TargetPosition = 1f;
        public float SuspensionOffset;
        public string SuspensionColliderName = "";
        public float MaximumLoad;
        // Copied for JSON parity, unused by Redux's solver.
        public float SpringRatio = 50f;
        public float DamperRatio = 1f;
        public float BoostRatio;
        public bool UseDistributedMass = true;
        public bool UseAutoBoost = true;
        public bool AdjustForHighGee = true;
    }

    /// <summary>The values of <c>Data_WheelSteering</c> that the mapping sets. Curves are flattened as time, value, in, out;
    /// a key without both tangents keeps NaN for them: a smooth key, as KSP1 loads it (<see cref="WheelMapping.IsSmoothKey"/>).</summary>
    public sealed class WheelSteeringValues
    {
        public string CaliperTransformName = "";
        public float SteeringResponse = 8f;
        public bool AutoSteeringAdjust = true;
        public float[] SteeringCurve = Array.Empty<float>();
        public float[] SteeringMaxAngleCurve = Array.Empty<float>();
    }

    /// <summary>The values of <c>Data_WheelMotor</c> that the mapping sets. When <see cref="UseResources"/> is false,
    /// <see cref="RequiredResourceName"/> is electric charge at rate 0: KSP2 sets up the motor's resource request in the VAB
    /// whatever <c>useResources</c> says, and a null name throws there.</summary>
    public sealed class WheelMotorValues
    {
        public float WheelSpeedMax = 120f;
        public float DriveResponse = 3f;
        public double IdleDrain = 0.3;
        public float[] TorqueCurve = Array.Empty<float>();
        public bool UseResources;
        public string RequiredResourceName;
        public double RequiredResourceRate;
        public bool MotorEnabled = true;
        /// <summary>The initial <c>autoTorque</c> (a state property in KSP2).</summary>
        public bool AutoTorque = true;
        /// <summary>The initial <c>tractionControlScale</c>, 0 to 5 (a state property in KSP2).</summary>
        public float TractionControlScale = 1f;
    }

    /// <summary>The values of <c>Data_WheelMotor</c> and <c>Data_WheelMotorSteering</c> of a motor-steering module.</summary>
    public sealed class WheelMotorSteeringValues
    {
        public WheelMotorValues Motor = new WheelMotorValues();
        public float SteeringTorque = 50f;
    }

    public sealed class WheelBrakesValues
    {
        /// <summary>kN·m (S), clamped by Redux to the tyre's grip.</summary>
        public float MaxBrakeTorque = 2000f;
        public float BrakeResponse = 10f;
    }

    public sealed class WheelDamageValues
    {
        public float StressTolerance = 10f;
        public float DeflectionMagnitude = 1f;
        public float DeflectionSharpness = 1f;
        public float SlipMagnitude = 1f;
        public float SlipSharpness = 2f;
        public string DamagedTransformName = "";
        public string UndamagedTransformName = "";
        public float ExplodeMultiplier = 5f;
        public float ImpactDamageVelocity;
        public bool IsRepairable = true;
        // Copied for JSON parity, unused by Redux's solver.
        public float ImpactTolerance = 40f;
        public string ImpactDamageColliderName = "";
    }

    public sealed class WheelLockValues
    {
        public float MaxTorque = 1000f;
    }

    public sealed class WheelBogeyValues
    {
        public string BogeyTransformName = "";
        public string BogeyRefTransformName = "";
        public string WheelTransformRefName = "";
        public string WheelTransformBaseName = "";
        public float MaxPitch = 25f;
        public float MinPitch = -25f;
        public float RestPitch = -25f;
        public float PitchResponse = 8f;
        public WheelVector BogeyAxis = new WheelVector(1, 0, 0);
        public WheelVector BogeyUpAxis = new WheelVector(0, 1, 0);
    }

    /// <summary>One mapped module: its values, whether it is converted, and its notes. <see cref="NotReproduced"/> and
    /// <see cref="UnusedBySolver"/> list cfg field names once each; <see cref="Notes"/> are the texts.</summary>
    public sealed class WheelMapped<T> where T : class
    {
        public WheelKind Kind { get; }
        public T Values { get; }
        public bool Converted { get; internal set; }
        public IReadOnlyList<string> NotReproduced => _notReproduced;
        public IReadOnlyList<string> UnusedBySolver => _unused;
        public IReadOnlyList<string> Notes => _notes;

        internal readonly List<string> _notReproduced = new List<string>();
        internal readonly List<string> _unused = new List<string>();
        internal readonly List<string> _notes = new List<string>();

        internal WheelMapped(WheelKind kind, T values)
        {
            Kind = kind;
            Values = values;
        }
    }
}
