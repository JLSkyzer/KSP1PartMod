using System.Collections.Generic;

namespace KSP1PartMod.Animation.Mu
{
    /// <summary>A KSP1 <c>.mu</c> model as read by <see cref="MuReader"/>: the object tree, its legacy
    /// Animation components, and where every entry sits in the file. Meshes, materials, colliders and the
    /// other components are read to find their length but not kept.</summary>
    public sealed class MuFile
    {
        public int Version { get; }
        /// <summary>The string after the version in the header (KSP ignores it).</summary>
        public string Name { get; }
        public MuNode Root { get; }
        /// <summary>Every tagged entry in file order, structural ones (child start and end) included.</summary>
        public IReadOnlyList<MuEntryInfo> Entries { get; }

        public MuFile(int version, string name, MuNode root, IReadOnlyList<MuEntryInfo> entries)
        {
            Version = version;
            Name = name;
            Root = root;
            Entries = entries;
        }
    }

    /// <summary>One object (a Unity GameObject with its Transform) of the tree.</summary>
    public sealed class MuNode
    {
        private readonly List<MuNode> _children = new List<MuNode>();
        private readonly List<int> _entryTypes = new List<int>();

        public string Name { get; }
        public MuNode Parent { get; }
        public IReadOnlyList<MuNode> Children => _children;
        /// <summary>Types of the component entries read on this object, in file order; child start and end
        /// tags are not listed.</summary>
        public IReadOnlyList<int> EntryTypes => _entryTypes;
        /// <summary>The Animation component on this object, or null. If a file carries several Animation
        /// entries on one object, the last one wins (Unity would refuse the second component).</summary>
        public MuAnim Animation { get; internal set; }
        /// <summary>The WheelCollider entry (type 29) on this object, or null; the last one wins.</summary>
        public MuWheelCollider WheelCollider { get; internal set; }

        public MuNode(string name, MuNode parent)
        {
            Name = name;
            Parent = parent;
            parent?._children.Add(this);
        }

        internal void AddEntryType(int type) => _entryTypes.Add(type);

        /// <summary>Names from the root to this object, joined by '/' (the root's own name included).</summary>
        public string Path() => Parent == null ? Name : Parent.Path() + "/" + Name;
    }

    /// <summary>The part of a <c>.mu</c> WheelCollider entry the wheel census compares with the cfg: radius, suspension
    /// distance and centre (the mass, spring and friction curves are read past).</summary>
    public sealed class MuWheelCollider
    {
        public float Radius { get; }
        public float SuspensionDistance { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float CenterZ { get; }

        public MuWheelCollider(float radius, float suspensionDistance, float centerX, float centerY, float centerZ)
        {
            Radius = radius;
            SuspensionDistance = suspensionDistance;
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
        }
    }

    /// <summary>Where one tagged entry sits: <see cref="Offset"/> is the position of its 4-byte type tag,
    /// <see cref="Length"/> covers the tag and the payload. A child start tag is 4 bytes long: the child's
    /// name and transform that follow it are object headers, not entry bytes.</summary>
    public readonly struct MuEntryInfo
    {
        public int Type { get; }
        public long Offset { get; }
        public long Length { get; }
        /// <summary>Path of the object whose entry loop read the tag (for a child start tag, the parent).</summary>
        public string NodePath { get; }

        public MuEntryInfo(int type, long offset, long length, string nodePath)
        {
            Type = type;
            Offset = offset;
            Length = length;
            NodePath = nodePath;
        }
    }

    /// <summary>A legacy Unity Animation component.</summary>
    public sealed class MuAnim
    {
        public IReadOnlyList<MuAnimClip> Clips { get; }
        /// <summary>The default clip name; empty when the component has none.</summary>
        public string DefaultClip { get; }
        public bool AutoPlay { get; }

        public MuAnim(IReadOnlyList<MuAnimClip> clips, string defaultClip, bool autoPlay)
        {
            Clips = clips;
            DefaultClip = defaultClip;
            AutoPlay = autoPlay;
        }
    }

    public sealed class MuAnimClip
    {
        public string Name { get; }
        /// <summary>Unity <c>WrapMode</c> value as stored.</summary>
        public int WrapMode { get; }
        public IReadOnlyList<MuAnimCurve> Curves { get; }
        /// <summary>The largest key time over all curves, 0 when there is no key.</summary>
        public float Length { get; }

        public MuAnimClip(string name, int wrapMode, IReadOnlyList<MuAnimCurve> curves)
        {
            Name = name;
            WrapMode = wrapMode;
            Curves = curves;
            var length = 0f;
            foreach (var curve in curves)
            foreach (var key in curve.Keys)
                if (key.Time > length) length = key.Time;
            Length = length;
        }
    }

    public sealed class MuAnimCurve
    {
        /// <summary>Path relative to the object carrying the Animation; empty for that object itself.</summary>
        public string Path { get; }
        /// <summary>Unity binding name, e.g. <c>m_LocalRotation.x</c> or <c>_EmissiveColor.r</c>.</summary>
        public string Property { get; }
        /// <summary>Animated component: 0 Transform, 1 Material, 2 Light, 3 AudioSource, 4 ParticleEmitter.
        /// Kept as stored, even when out of that range.</summary>
        public int Type { get; }
        public int PreWrap { get; }
        public int PostWrap { get; }
        public IReadOnlyList<MuKey> Keys { get; }

        public MuAnimCurve(string path, string property, int type, int preWrap, int postWrap, IReadOnlyList<MuKey> keys)
        {
            Path = path;
            Property = property;
            Type = type;
            PreWrap = preWrap;
            PostWrap = postWrap;
            Keys = keys;
        }
    }

    public readonly struct MuKey
    {
        public float Time { get; }
        public float Value { get; }
        public float InTangent { get; }
        public float OutTangent { get; }
        /// <summary>Stored by PartTools; KSP reads and discards it.</summary>
        public int TangentMode { get; }

        public MuKey(float time, float value, float inTangent, float outTangent, int tangentMode)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            TangentMode = tangentMode;
        }
    }
}
