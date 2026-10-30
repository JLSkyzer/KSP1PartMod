using System.Collections.Generic;

namespace KSP1PartMod.Native.Fairings
{
    /// <summary>The two KSP1 fairing modules this plan converts.</summary>
    public enum FairingKind { Procedural, Saf }

    /// <summary>KSP2's <c>FairingDeployType</c> names, in its order (the game side parses the name).</summary>
    public enum FairingDeploy { None, Shroud, Clamshellx2, Clamshellx3, Clamshellx4 }

    /// <summary>A vector without UnityEngine.</summary>
    public readonly struct FairingVector
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public FairingVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsZero => X == 0f && Y == 0f && Z == 0f;

        public static readonly FairingVector Up = new FairingVector(0f, 1f, 0f);

        public override string ToString() => $"({IsruCfg.Inv(X)}, {IsruCfg.Inv(Y)}, {IsruCfg.Inv(Z)})";
    }

    /// <summary>The <c>Data_Fairing</c> values the mapping sets (fairings plan (d)). Every field starts at KSP2's default
    /// (<c>Data_Fairing</c> 26w42c), except <see cref="MassAreaRatio"/>, <see cref="DefaultDeployType"/> and
    /// <see cref="AllowInterstageNode"/>, which the plan sets for every KSP1 base.</summary>
    public sealed class FairingValues
    {
        public const string Ksp2BaseModelTransformName = "Base";
        public const float Ksp2EjectionForce = 100f;
        /// <summary>The median <c>UnitAreaMass</c> of the stock bases (t/m²), used where the cfg has none.</summary>
        public const float DefaultMassAreaRatio = 0.01f;

        public float BaseRadius = 1.25f;
        public float MaxRadius = 6f;
        public float CapRadius = 0.375f;
        public float CloseRadius = 0.375f;
        public float SnapThreshold = 0.25f;
        public float CrossSectionHeightMin = 0.3f;
        public float CrossSectionHeightMax = 3f;
        public float NoseTip = 0.5f;
        public float EdgeWarp = 0.02f;
        public FairingVector Pivot;
        public FairingVector LocalUpAxis = FairingVector.Up;
        public string BaseModelTransformName = Ksp2BaseModelTransformName;
        public int FairingSideCount = 24;
        public float MassAreaRatio = DefaultMassAreaRatio;
        public FairingDeploy DefaultDeployType = FairingDeploy.Clamshellx4;
        /// <summary>A state value (<c>Data_Fairing.EjectionForce</c>), N.</summary>
        public float EjectionForce = Ksp2EjectionForce;
        public bool StageToggleDefault = true;
        public bool AllowInterstageNode;
    }

    /// <summary>The result of one KSP1 fairing module read as a <c>Data_Fairing</c>.</summary>
    public sealed class FairingMap
    {
        public FairingKind Kind { get; internal set; }
        /// <summary>False: the module has no usable size (no <c>baseRadius</c> above 0, no SAF top node of a known size).</summary>
        public bool Converted { get; internal set; }
        public FairingValues Values { get; } = new FairingValues();
        /// <summary>The KSP1 fields present in the cfg that nothing in KSP2 reproduces, in the order of
        /// <see cref="FairingMapping.ProceduralNotReproduced"/> / <see cref="FairingMapping.SafNotReproduced"/>.</summary>
        public List<string> NotReproduced { get; } = new List<string>();
        /// <summary>Why <see cref="Converted"/> is false (null otherwise).</summary>
        public string Reason { get; internal set; }
    }
}
