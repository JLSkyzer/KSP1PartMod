using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KSP1PartMod.Animation.Mu
{
    /// <summary>A <c>.mu</c> file that does not follow the layout the reader knows. <see cref="Offset"/> is where
    /// the problem was found.</summary>
    public sealed class MuFormatException : Exception
    {
        public long Offset { get; }

        public MuFormatException(long offset, string message) : base($"{message} (at byte {offset})") => Offset = offset;
    }

    /// <summary>Entry tags of a <c>.mu</c> file (PartTools' entry type numbering).</summary>
    public static class MuEntryType
    {
        public const int ChildStart = 0, ChildEnd = 1, Animation = 2;
        public const int MeshCollider = 3, SphereCollider = 4, CapsuleCollider = 5, BoxCollider = 6;
        public const int MeshFilter = 7, MeshRenderer = 8, SkinnedMeshRenderer = 9, Materials = 10, Material = 11, Textures = 12;
        public const int MeshStart = 13, MeshVerts = 14, MeshUV = 15, MeshUV2 = 16, MeshNormals = 17, MeshTangents = 18;
        public const int MeshTriangles = 19, MeshBoneWeights = 20, MeshBindPoses = 21, MeshEnd = 22;
        public const int Light = 23, TagAndLayer = 24;
        public const int MeshCollider2 = 25, SphereCollider2 = 26, CapsuleCollider2 = 27, BoxCollider2 = 28;
        public const int WheelCollider = 29, Camera = 30, ParticleEmitter = 31, MeshVertexColors = 32;
    }

    /// <summary>Reads KSP1 <c>.mu</c> models (versions 0 to 5) down to their last byte. Animation components are
    /// kept; every other component is walked over by its known size. Any tag or layout the reader does not know
    /// throws <see cref="MuFormatException"/>: no byte is ever skipped on a guess.</summary>
    public static class MuReader
    {
        public const int Magic = 76543;

        public const int MaxVersion = 5;

        /// <summary>Floats an object header carries after its position, rotation and scale: one in versions 0 to
        /// 2 (it equals the x scale in all 219 headers of the 50 stock files of those versions, like a fourth
        /// scale component), none from version 3.</summary>
        public static int ObjectHeaderExtraFloats(int version) => version < 3 ? 1 : 0;

        private const int FloatSize = 4, IntSize = 4, ColorSize = 16, TextureRefSize = 20;

        public static MuFile Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
                       FileOptions.SequentialScan))
                return Read(fs);
        }

        public static MuFile Read(Stream s)
        {
            if (!s.CanSeek)
            {
                var copy = new MemoryStream();
                s.CopyTo(copy);
                copy.Position = 0;
                s = copy;
            }
            var stream = s is MemoryStream || s is BufferedStream || s is FileStream ? s : new BufferedStream(s, 1 << 16);
            using (var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                return new Session(br, stream).ReadFile();
        }

        /// <summary>The state of one read: position checks and the entry log.</summary>
        private sealed class Session
        {
            private readonly BinaryReader _br;
            private readonly Stream _s;
            private readonly long _length;
            private readonly List<MuEntryInfo> _entries = new List<MuEntryInfo>();
            private int _version;

            public Session(BinaryReader br, Stream s)
            {
                _br = br;
                _s = s;
                _length = s.Length;
            }

            private long Pos => _s.Position;

            public MuFile ReadFile()
            {
                try
                {
                    var magic = Int();
                    if (magic != Magic) throw new MuFormatException(0, $"not a .mu file: magic {magic}, expected {Magic}");
                    _version = Int();
                    if (_version < 0 || _version > MaxVersion)
                        throw new MuFormatException(4, $"unsupported version {_version}, expected 0 to {MaxVersion}");
                    var name = Str();
                    var root = ReadObject(null);
                    if (Pos < _length) throw new MuFormatException(Pos, $"{_length - Pos} byte(s) after the root object");
                    return new MuFile(_version, name, root, _entries);
                }
                catch (EndOfStreamException)
                {
                    throw new MuFormatException(Pos, "unexpected end of file");
                }
            }

            /// <summary>Reads an object header and its entries. A child ends at its end tag; the root ends at the
            /// end of the file (or at an end tag, as KSP's reader allows).</summary>
            private MuNode ReadObject(MuNode parent)
            {
                var node = new MuNode(Str(), parent);
                // local position (3), rotation (4), scale (3), then the extra float of versions 0 to 2, which
                // KSP's reader takes for an unknown entry tag and steps over
                Skip((10 + ObjectHeaderExtraFloats(_version)) * FloatSize);
                var path = node.Path();
                while (true)
                {
                    if (Pos >= _length)
                    {
                        if (parent == null) return node;
                        throw new MuFormatException(Pos, $"end of file inside '{path}': child end tag missing");
                    }
                    var offset = Pos;
                    var type = Int();
                    if (type == MuEntryType.ChildStart)
                    {
                        Log(type, offset, path);
                        ReadObject(node);
                        continue;
                    }
                    if (type == MuEntryType.ChildEnd)
                    {
                        Log(type, offset, path);
                        return node;
                    }
                    ReadComponent(type, offset, node, path);
                    node.AddEntryType(type);
                    Log(type, offset, path);
                }
            }

            private void Log(int type, long offset, string path) =>
                _entries.Add(new MuEntryInfo(type, offset, Pos - offset, path));

            private void ReadComponent(int type, long offset, MuNode node, string path)
            {
                switch (type)
                {
                    case MuEntryType.Animation:
                        node.Animation = ReadAnimation();
                        return;
                    case MuEntryType.MeshFilter:
                        SkipMesh();
                        return;
                    case MuEntryType.MeshRenderer:
                        if (_version >= 1) Skip(2); // cast shadows, receive shadows (bools)
                        SkipArray(IntSize); // material indices
                        return;
                    case MuEntryType.SkinnedMeshRenderer:
                        SkipArray(IntSize); // material indices
                        Skip(6 * FloatSize + IntSize + 1); // local bounds, quality, update when offscreen
                        var bones = Count(1);
                        for (var i = 0; i < bones; i++) Str();
                        SkipMesh();
                        return;
                    case MuEntryType.Materials:
                        var materials = Count(1);
                        for (var i = 0; i < materials; i++)
                            if (_version >= 4) SkipNamedShaderMaterial();
                            else SkipLegacyMaterial();
                        return;
                    case MuEntryType.Textures:
                        var textures = Count(1);
                        for (var i = 0; i < textures; i++)
                        {
                            Str(); // file name
                            Skip(IntSize); // texture type (0 texture, 1 normal map)
                        }
                        return;
                    case MuEntryType.Light:
                        // type, intensity, range, color, culling mask; spot angle from version 2
                        Skip(IntSize + 2 * FloatSize + ColorSize + IntSize + (_version >= 2 ? FloatSize : 0));
                        return;
                    case MuEntryType.TagAndLayer:
                        Str();
                        Skip(IntSize);
                        return;
                    case MuEntryType.MeshCollider:
                        Skip(1); // convex
                        SkipMesh();
                        return;
                    case MuEntryType.MeshCollider2:
                        Skip(2); // is trigger, convex
                        SkipMesh();
                        return;
                    case MuEntryType.SphereCollider: Skip(4 * FloatSize); return; // radius, center
                    case MuEntryType.SphereCollider2: Skip(1 + 4 * FloatSize); return; // is trigger, radius, center
                    case MuEntryType.CapsuleCollider: Skip(FloatSize + IntSize + 3 * FloatSize); return; // radius, direction, center
                    case MuEntryType.CapsuleCollider2: Skip(1 + 2 * FloatSize + IntSize + 3 * FloatSize); return; // trigger, radius, height, direction, center
                    case MuEntryType.BoxCollider: Skip(6 * FloatSize); return; // size, center
                    case MuEntryType.BoxCollider2: Skip(1 + 6 * FloatSize); return; // is trigger, size, center
                    case MuEntryType.WheelCollider:
                    {
                        // mass, radius, suspension distance, center (3), spring (3), forward and sideways friction (5 each)
                        Need(19 * FloatSize);
                        Float(); // mass
                        var radius = Float();
                        var suspension = Float();
                        var cx = Float(); var cy = Float(); var cz = Float();
                        Skip(13 * FloatSize); // spring, damper, target position, two friction curves of 5
                        node.WheelCollider = new MuWheelCollider(radius, suspension, cx, cy, cz);
                        return;
                    }
                    case MuEntryType.Camera:
                        // clear flags, background color, culling mask, orthographic, fov, near, far, depth
                        Skip(IntSize + ColorSize + IntSize + 1 + 4 * FloatSize);
                        return;
                    case MuEntryType.ParticleEmitter:
                        SkipParticleEmitter();
                        return;
                    default:
                        throw new MuFormatException(offset, $"unknown entry type {type} in '{path}' (version {_version})");
                }
            }

            private MuAnim ReadAnimation()
            {
                var clipCount = Count(1);
                var clips = new List<MuAnimClip>(clipCount);
                for (var c = 0; c < clipCount; c++)
                {
                    var name = Str();
                    Skip(6 * FloatSize); // local bounds: center, size
                    var wrapMode = Int();
                    var curveCount = Count(1);
                    var curves = new List<MuAnimCurve>(curveCount);
                    for (var i = 0; i < curveCount; i++)
                    {
                        var curvePath = Str();
                        var property = Str();
                        var type = Int();
                        var preWrap = Int();
                        var postWrap = Int();
                        var keyCount = Count(5 * 4);
                        var keys = new MuKey[keyCount];
                        for (var k = 0; k < keyCount; k++)
                            keys[k] = new MuKey(Float(), Float(), Float(), Float(), Int());
                        curves.Add(new MuAnimCurve(curvePath, property, type, preWrap, postWrap, keys));
                    }
                    clips.Add(new MuAnimClip(name, wrapMode, curves));
                }
                var defaultClip = Str();
                var autoPlay = Bool();
                return new MuAnim(clips, defaultClip, autoPlay);
            }

            /// <summary>A mesh: start tag, vertex count, submesh count, tagged vertex streams, end tag.</summary>
            private void SkipMesh()
            {
                var start = Pos;
                var tag = Int();
                if (tag != MuEntryType.MeshStart) throw new MuFormatException(start, $"mesh start tag expected, found {tag}");
                var verts = Count(0);
                Skip(IntSize); // submesh count
                while (true)
                {
                    var at = Pos;
                    var stream = Int();
                    switch (stream)
                    {
                        case MuEntryType.MeshEnd: return;
                        case MuEntryType.MeshVerts:
                        case MuEntryType.MeshNormals: Skip((long)verts * 3 * FloatSize); break;
                        case MuEntryType.MeshUV:
                        case MuEntryType.MeshUV2: Skip((long)verts * 2 * FloatSize); break;
                        case MuEntryType.MeshTangents: Skip((long)verts * 4 * FloatSize); break;
                        case MuEntryType.MeshBoneWeights: Skip((long)verts * 4 * (IntSize + FloatSize)); break;
                        case MuEntryType.MeshVertexColors: Skip((long)verts * 4); break; // Color32
                        case MuEntryType.MeshTriangles: SkipArray(IntSize); break;
                        case MuEntryType.MeshBindPoses: SkipArray(16 * FloatSize); break;
                        default: throw new MuFormatException(at, $"unknown mesh stream {stream} (version {_version})");
                    }
                }
            }

            /// <summary>Version 4 and later: name, shader name, then typed properties.</summary>
            private void SkipNamedShaderMaterial()
            {
                Str(); // material name
                Str(); // shader name
                var properties = Count(1);
                for (var i = 0; i < properties; i++)
                {
                    Str();
                    var at = Pos;
                    var kind = Int();
                    switch (kind)
                    {
                        case 0: // color
                        case 1: Skip(ColorSize); break; // vector
                        case 2: // float
                        case 3: Skip(FloatSize); break; // range
                        case 4: Skip(TextureRefSize); break; // texture
                        default: throw new MuFormatException(at, $"unknown material property kind {kind}");
                    }
                }
            }

            /// <summary>Byte size, after the shader type, of each legacy (version 0 to 3) PartTools shader. T is a
            /// texture reference (index, scale, offset), C a colour, F a float.</summary>
            private static readonly int[] LegacyShaderSizes =
            {
                TextureRefSize, // 0 custom: falls back to diffuse in KSP
                TextureRefSize, // 1 diffuse: T
                TextureRefSize + ColorSize + FloatSize, // 2 specular: T C F
                2 * TextureRefSize, // 3 bumped: T T
                2 * TextureRefSize + ColorSize + FloatSize, // 4 bumped specular: T T C F
                2 * TextureRefSize + ColorSize, // 5 emissive: T T C
                2 * TextureRefSize + 2 * ColorSize + FloatSize, // 6 emissive specular: T C F T C
                3 * TextureRefSize + 2 * ColorSize + FloatSize, // 7 emissive bumped specular: T T C F T C
                TextureRefSize + FloatSize, // 8 alpha cutout: T F
                2 * TextureRefSize + FloatSize, // 9 alpha cutout bumped: T T F
                TextureRefSize, // 10 alpha: T
                TextureRefSize + 2 * FloatSize + ColorSize, // 11 alpha specular: T F C F
                TextureRefSize + ColorSize, // 12 alpha unlit: T C
                TextureRefSize + ColorSize, // 13 unlit: T C
                TextureRefSize + ColorSize + FloatSize, // 14 particle alpha: T C F
                TextureRefSize + ColorSize + FloatSize, // 15 particle additive: T C F
                3 * TextureRefSize + 2 * FloatSize, // 16 bumped specular mapped: T T T F F
            };

            private void SkipLegacyMaterial()
            {
                Str(); // material name
                var at = Pos;
                var shader = Int();
                if (shader < 0 || shader >= LegacyShaderSizes.Length)
                    throw new MuFormatException(at, $"unknown legacy shader type {shader} (version {_version})");
                Skip(LegacyShaderSizes[shader]);
            }

            private void SkipParticleEmitter()
            {
                const int b = 1, f = FloatSize, i = IntSize, c = ColorSize;
                Skip(b + i // emit, shape
                       + 6 * f // shape 3D, 2D, 1D
                       + c + b // color, use world space
                       + 4 * f + 2 * i // min/max size, min/max energy, min/max emission
                       + 9 * f // world, local, random velocity
                       + 3 * f // emitter velocity scale, angular velocity, random angular velocity
                       + 2 * b // random rotation, animate color
                       + 5 * c // color animation
                       + 6 * f + f // world and local rotation axis, size grow
                       + 6 * f + f // random force, force, damping
                       + 2 * b // cast shadows, receive shadows
                       + 3 * f // length scale, velocity scale, max particle size
                       + i + 3 * i // render mode, uv animation x tile, y tile, cycles
                       + i); // material index
            }

            // ---- primitives ----

            private int Int() => _br.ReadInt32();
            private float Float() => _br.ReadSingle();
            private bool Bool() => _br.ReadBoolean();

            private string Str()
            {
                var at = Pos;
                try { return _br.ReadString(); }
                catch (FormatException) { throw new MuFormatException(at, "bad string length prefix"); }
            }

            /// <summary>Reads a count and checks that its smallest possible payload fits in the file.</summary>
            private int Count(int minBytesEach)
            {
                var at = Pos;
                var n = Int();
                if (n < 0 || (long)n * minBytesEach > _length - Pos)
                    throw new MuFormatException(at, $"count {n} does not fit in the file (version {_version})");
                return n;
            }

            private void SkipArray(int bytesEach) => Skip((long)Count(bytesEach) * bytesEach);

            /// <summary>Throws as <see cref="Skip"/> does when fewer than <paramref name="bytes"/> remain.</summary>
            private void Need(long bytes)
            {
                if (bytes > _length - Pos) throw new MuFormatException(Pos, $"{bytes} bytes expected, file ends first");
            }

            private void Skip(long bytes)
            {
                Need(bytes);
                _s.Seek(bytes, SeekOrigin.Current);
            }
        }
    }
}
