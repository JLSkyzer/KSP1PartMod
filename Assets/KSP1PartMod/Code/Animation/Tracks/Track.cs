using System.Collections.Generic;
using Curve = KSP1PartMod.Animation.Behaviours.FloatCurve;

namespace KSP1PartMod.Animation.Tracks
{
    public enum TrackKind
    {
        MaterialColor, MaterialVector, MaterialFloat,
        LightIntensity, LightColor, LightRange, LightSpotAngle, LightEnabled,
    }

    /// <summary>One animated property of a KSP1 clip that is not a Transform: a material property or a light
    /// property, with one curve per channel.</summary>
    public sealed class Track
    {
        /// <summary>Path from the object carrying the Animation to the animated object; empty for that object.</summary>
        public string RelativePath;
        public TrackKind Kind;
        /// <summary>KSP1 name without the channel suffix: <c>_EmissiveColor</c>, <c>m_Intensity</c>.</summary>
        public string Property;
        /// <summary>4 for a colour or vector, 1 otherwise; a null item keeps the rest value of that channel.</summary>
        public Curve[] Channels;
        public bool IsLight;
    }

    /// <summary>The non-Transform tracks of one clip.</summary>
    public sealed class TrackSet
    {
        public string Clip;
        /// <summary>Path of the object carrying the Animation, from the model root, in .mu terms.</summary>
        public string HostPath;
        public int ModelIndex;
        /// <summary><see cref="Mu.MuAnimClip.Length"/>.</summary>
        public float Length;
        public int WrapMode;
        public IReadOnlyList<Track> Tracks;
    }

    /// <summary>Curves a clip carried that no track takes, by reason.</summary>
    public struct TrackDrops
    {
        public int Transform, DrawHalo, Audio, Particle, Unknown;
    }
}
