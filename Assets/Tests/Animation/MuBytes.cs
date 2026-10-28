using System;
using System.Collections.Generic;
using System.IO;

namespace KSP1PartMod.Tests.Animation
{
    /// <summary>Builds <c>.mu</c> files in memory. Entry tags are written as raw numbers so the fixtures do not
    /// depend on the reader's constants.</summary>
    internal sealed class MuBytes
    {
        public const int Magic = 76543;

        public byte[] Bytes { get; }
        public int Length => Bytes.Length;
        public MemoryStream Stream() => new MemoryStream(Bytes, false);

        private MuBytes(byte[] bytes) => Bytes = bytes;

        public static MuObj Obj(string name) => new MuObj(name);

        public static MuBytes Header(int magic = Magic, int version = 5, string name = "model") =>
            Build(w => WriteHeader(w, magic, version, name));

        public static MuBytes File(MuObj root, int version = 5, string name = "model") =>
            Build(w =>
            {
                WriteHeader(w, Magic, version, name);
                root.Write(w, version, isRoot: true);
            });

        /// <summary>Bytes taken by the header (magic, version, name).</summary>
        public static int HeaderLength(string name = "model") => 8 + StringLength(name);

        /// <summary>Bytes taken by an object header (name, position, rotation, scale, and an extra float before
        /// version 3).</summary>
        public static int ObjectHeaderLength(string name, int version = 5) => StringLength(name) + (version < 3 ? 11 : 10) * 4;

        public static int StringLength(string s)
        {
            var n = System.Text.Encoding.UTF8.GetByteCount(s);
            var prefix = 1;
            for (var v = n >> 7; v != 0; v >>= 7) prefix++;
            return prefix + n;
        }

        private static void WriteHeader(BinaryWriter w, int magic, int version, string name)
        {
            w.Write(magic);
            w.Write(version);
            w.Write(name);
        }

        private static MuBytes Build(Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) write(w);
                return new MuBytes(ms.ToArray());
            }
        }
    }

    /// <summary>One object of a fixture and its entries, written in the order they were added.</summary>
    internal sealed class MuObj
    {
        private readonly string _name;
        private readonly List<Action<BinaryWriter, int>> _entries = new List<Action<BinaryWriter, int>>();

        public MuObj(string name) => _name = name;

        public MuObj Child(MuObj child) =>
            Add((w, v) =>
            {
                w.Write(0); // ChildTransformStart
                child.Write(w, v, isRoot: false);
            });

        /// <summary>An Animation entry with one clip; each curve gets keys at the given times (default 0 and 1).</summary>
        public MuObj Anim(string clip, (string path, string property, int type)[] curves, bool autoPlay = true,
            string defaultClip = null, int wrapMode = 0, float[] keyTimes = null) =>
            Anims(new[] { (clip, curves) }, autoPlay, defaultClip ?? clip, wrapMode, keyTimes);

        public MuObj Anims((string clip, (string path, string property, int type)[] curves)[] clips, bool autoPlay,
            string defaultClip, int wrapMode = 0, float[] keyTimes = null)
        {
            keyTimes = keyTimes ?? new[] { 0f, 1f };
            return Add((w, v) =>
            {
                w.Write(2); // Animation
                w.Write(clips.Length);
                foreach (var (name, curves) in clips)
                {
                    w.Write(name);
                    for (var i = 0; i < 6; i++) w.Write(0.5f); // local bounds center, size
                    w.Write(wrapMode);
                    w.Write(curves.Length);
                    foreach (var (path, property, type) in curves)
                    {
                        w.Write(path);
                        w.Write(property);
                        w.Write(type);
                        w.Write(1); // pre wrap
                        w.Write(2); // post wrap
                        w.Write(keyTimes.Length);
                        foreach (var t in keyTimes)
                        {
                            w.Write(t);
                            w.Write(t * 10f); // value
                            w.Write(0.25f);
                            w.Write(-0.25f);
                            w.Write(3); // tangent mode
                        }
                    }
                }
                w.Write(defaultClip);
                w.Write(autoPlay);
            });
        }

        /// <summary>A MeshFilter with a mesh of <paramref name="verts"/> vertices: positions, normals, uv, one
        /// triangle list.</summary>
        public MuObj MeshFilter(int verts = 3) =>
            Add((w, v) =>
            {
                w.Write(7); // MeshFilter
                WriteMesh(w, verts);
            });

        public MuObj MeshRenderer(params int[] materials) =>
            Add((w, v) =>
            {
                w.Write(8); // MeshRenderer
                if (v >= 1)
                {
                    w.Write(true); // cast shadows
                    w.Write(true); // receive shadows
                }
                w.Write(materials.Length);
                foreach (var m in materials) w.Write(m);
            });

        public MuObj Light() =>
            Add((w, v) =>
            {
                w.Write(23); // Light
                w.Write(2); // point
                w.Write(1.5f); // intensity
                w.Write(10f); // range
                for (var i = 0; i < 4; i++) w.Write(1f); // color
                w.Write(-1); // culling mask
                if (v >= 2) w.Write(30f); // spot angle
            });

        public MuObj BoxCollider2() =>
            Add((w, v) =>
            {
                w.Write(28); // BoxCollider2
                w.Write(false);
                for (var i = 0; i < 6; i++) w.Write(1f);
            });

        /// <summary>A Materials entry with one material: legacy Bumped (shader type 3) before version 4, a named
        /// shader with one colour, one float and one texture from version 4.</summary>
        public MuObj Materials() =>
            Add((w, v) =>
            {
                w.Write(10); // Materials
                w.Write(1);
                w.Write("mat");
                if (v >= 4)
                {
                    w.Write("KSP/Bumped");
                    w.Write(3);
                    w.Write("_Color"); w.Write(0); for (var i = 0; i < 4; i++) w.Write(1f);
                    w.Write("_Shininess"); w.Write(2); w.Write(0.5f);
                    w.Write("_MainTex"); w.Write(4); WriteTextureRef(w, 0);
                }
                else
                {
                    w.Write(3); // Bumped: main texture, bump map
                    WriteTextureRef(w, 0);
                    WriteTextureRef(w, 1);
                }
            });

        public MuObj Textures(params string[] files) =>
            Add((w, v) =>
            {
                w.Write(12); // Textures
                w.Write(files.Length);
                foreach (var f in files)
                {
                    w.Write(f);
                    w.Write(0);
                }
            });

        /// <summary>A WheelCollider entry: mass 0.5, then the given radius, suspension distance and centre, then 13 floats
        /// (spring, damper, target position, two friction curves of 5) set to 9.</summary>
        public MuObj WheelCollider(float radius, float suspension, float cx, float cy, float cz) =>
            Add((w, v) =>
            {
                w.Write(29); // WheelCollider
                w.Write(0.5f);
                w.Write(radius); w.Write(suspension);
                w.Write(cx); w.Write(cy); w.Write(cz);
                for (var i = 0; i < 13; i++) w.Write(9f);
            });

        /// <summary>A raw tag with no payload, for unknown entry types.</summary>
        public MuObj Tag(int type) => Add((w, v) => w.Write(type));

        internal void Write(BinaryWriter w, int version, bool isRoot)
        {
            w.Write(_name);
            w.Write(1f); w.Write(2f); w.Write(3f); // position
            w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); // rotation
            w.Write(1f); w.Write(1f); w.Write(1f); // scale
            if (version < 3) w.Write(1f); // extra float of versions 0 to 2
            foreach (var entry in _entries) entry(w, version);
            if (!isRoot) w.Write(1); // ChildTransformEnd
        }

        private MuObj Add(Action<BinaryWriter, int> entry)
        {
            _entries.Add(entry);
            return this;
        }

        private static void WriteTextureRef(BinaryWriter w, int index)
        {
            w.Write(index);
            w.Write(1f); w.Write(1f); // scale
            w.Write(0f); w.Write(0f); // offset
        }

        private static void WriteMesh(BinaryWriter w, int verts)
        {
            w.Write(13); // MeshStart
            w.Write(verts);
            w.Write(1); // submesh count
            w.Write(14); // MeshVerts
            for (var i = 0; i < verts * 3; i++) w.Write((float)i);
            w.Write(17); // MeshNormals
            for (var i = 0; i < verts * 3; i++) w.Write(0f);
            w.Write(15); // MeshUV
            for (var i = 0; i < verts * 2; i++) w.Write(0f);
            w.Write(19); // MeshTriangles
            w.Write(3);
            w.Write(0); w.Write(1); w.Write(2);
            w.Write(22); // MeshEnd
        }
    }
}
