using System;
using System.Collections.Generic;
using System.Text;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The renderers and lights of one part instance that channels of its <see cref="ChannelTable"/> target, and
    /// the sink its flush writes through (spec 2a.5 / 2a.6).
    /// <list type="bullet">
    /// <item>One table target per renderer or light, made on first use.</item>
    /// <item>A renderer is written with one <c>GetPropertyBlock</c> / <c>SetPropertyBlock</c> pair per flush: the block is
    /// read first, so the values of other writers stay. A light's fields are set only when they differ.</item>
    /// <item>Emission (<see cref="EmissionPolicy"/>), decided per material from the shader found at bind, for renderers
    /// with an <c>_EmissionColor</c> channel: material copies made once, through <c>renderer.materials</c> (never by
    /// writing the shared, hot-swapped material), and a white <c>_EmissionMap</c> in the block once our <c>_EmissionColor</c>
    /// is.</item>
    /// <item>Before each write the first slot's material and shader are compared with the bind
    /// (<see cref="WriterPolicy.Rebind"/>): a change rebinds (the only allocating path of the flush), except while the
    /// OAB selection shader is on.</item>
    /// <item>Copies are destroyed by <see cref="ReleaseAll"/> (shutdown, new table) and, when no slot holds them any
    /// more, by a rebind.</item>
    /// </list></summary>
    internal sealed class RendererChannels : IChannelSink
    {
        private const string EmissionKeyword = "_EMISSION", EmissionMap = "_EmissionMap";
        private static readonly int EmissionColorId = Shader.PropertyToID(EmissionPolicy.EmissionColor);
        private static readonly int EmissionMapId = Shader.PropertyToID(EmissionMap);
        /// <summary>(shader, material) pairs whose emission line was logged: once per session.</summary>
        private static readonly HashSet<string> LoggedEmission = new HashSet<string>(StringComparer.Ordinal);
        private static Texture2D _white;

        private readonly ChannelTable _table;
        private readonly string _part;
        private readonly Transform _root;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Dictionary<Object, int> _targetOf = new Dictionary<Object, int>();
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly Dictionary<int, string> _propertyNames = new Dictionary<int, string>();
        private readonly SortedSet<string> _shaders = new SortedSet<string>(StringComparer.Ordinal);
        private AnimationSettings _settings;

        // Watched hulls (ReentryMesh): the targets rewritten every frame while one of their hulls is active.
        private Watch[] _watches = new Watch[0];
        private int _watchCount;
        private bool[] _activeNow = new bool[0], _activeShown = new bool[0];

        // The target being written, between Begin and End.
        private Binding _current;
        private Light _light;
        private int _lastProperty = -1;
        private ChannelValueType _lastType;
        private Value4 _lastValue;
        /// <summary>This sink no longer looks for a renderer to check (<see cref="MpbCheck"/>).</summary>
        private bool _checkDone;
        /// <summary>The reentry check: the watched target to check at its next write (-1: none), and whether this sink
        /// armed it already.</summary>
        private int _reentryCheckTarget = -1;
        private bool _reentryArmed;

        /// <param name="partName">For the log lines and the <c>mpb check</c>; null (tests): no check.</param>
        /// <param name="partRoot">The part object, for renderer paths in the log and <see cref="Orphaned"/>.</param>
        public RendererChannels(ChannelTable table, string partName = null, Transform partRoot = null)
        {
            _table = table;
            _part = partName;
            _root = partRoot;
        }

        public ChannelTable Table => _table;
        public int RendererCount { get; private set; }
        public int LightCount { get; private set; }
        /// <summary>Rebinds done since construction.</summary>
        public int Rebinds { get; private set; }
        /// <summary>Names of the shaders of the bound renderers' materials, at their bind.</summary>
        public IReadOnlyCollection<string> Shaders => _shaders;
        /// <summary>The part object was given and is destroyed: nothing here will be written again.</summary>
        public bool Orphaned => !ReferenceEquals(_root, null) && _root == null;

        /// <summary>The table target of <paramref name="r"/>, added and bound on first use. <paramref name="property"/>
        /// is the KSP2 property the caller writes on it: an <c>_EmissionColor</c> one decides the emission setup of each
        /// material, at the first such call. <paramref name="note"/>: what was decided for the materials.</summary>
        public int AddRenderer(Renderer r, AnimationSettings s, out string note, string property = EmissionPolicy.EmissionColor)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            _settings = s;
            if (property != null) _propertyNames[Shader.PropertyToID(property)] = property;
            var emission = property == EmissionPolicy.EmissionColor;
            if (_targetOf.TryGetValue(r, out var target))
            {
                var b = _bindings[target];
                if (emission && !b.EmissionWanted)
                {
                    b.EmissionWanted = true;
                    Decide(b);
                }
                note = b.Note;
                return target;
            }
            RendererCount++;
            target = Add(r, TargetKind.Renderer);
            var binding = new Binding(r) { EmissionWanted = emission };
            _bindings[target] = binding;
            Decide(binding);
            note = binding.Note;
            return target;
        }

        /// <summary>The table target of <paramref name="l"/>, added on first use.</summary>
        public int AddLight(Light l)
        {
            if (_targetOf.TryGetValue(l, out var target)) return target;
            LightCount++;
            return Add(l, TargetKind.Light);
        }

        /// <summary>The table target of a renderer or light already added.</summary>
        public bool TryGetTarget(Object o, out int target) => _targetOf.TryGetValue(o, out target);

        /// <summary>The renderer or light of a table target; null when unknown or destroyed.</summary>
        public Object TargetObject(int target) => target >= 0 && target < _objects.Count ? _objects[target] : null;

        /// <summary>Owned material copies of <paramref name="r"/>.</summary>
        public int CopiesOf(Renderer r) =>
            r != null && _targetOf.TryGetValue(r, out var t) && _bindings[t] != null ? _bindings[t].Copies.Count : 0;

        /// <summary>The target is rewritten every flush while <paramref name="hull"/> is active in the hierarchy, and once
        /// more on the flush after it goes inactive (<see cref="ChannelTable.SetRewriteEveryFrame"/>): the hull's
        /// <c>ReentryMesh</c> replaces the renderer's block each <c>LateUpdate</c>.</summary>
        public void WatchActive(int target, GameObject hull)
        {
            if (hull == null || target < 0 || target >= _objects.Count) return;
            if (_watchCount == _watches.Length) Array.Resize(ref _watches, Math.Max(4, _watchCount * 2));
            _watches[_watchCount++] = new Watch { Target = target, Hull = hull };
            if (_activeNow.Length < _objects.Count)
            {
                Array.Resize(ref _activeNow, _objects.Count);
                Array.Resize(ref _activeShown, _objects.Count);
            }
        }

        /// <summary>Called by the flush before <see cref="ChannelTable.Flush"/>: follows the watched hulls.
        /// Allocation-free.</summary>
        public void BeforeFlush()
        {
            if (_watchCount == 0) return;
            for (var i = 0; i < _watchCount; i++) _activeNow[_watches[i].Target] = false;
            for (var i = 0; i < _watchCount; i++)
            {
                var hull = _watches[i].Hull;
                if (hull != null && hull.activeInHierarchy) _activeNow[_watches[i].Target] = true;
            }
            for (var i = 0; i < _watchCount; i++)
            {
                var t = _watches[i].Target;
                if (_activeNow[t] == _activeShown[t]) continue;
                _activeShown[t] = _activeNow[t];
                _table.SetRewriteEveryFrame(t, _activeNow[t]);
                if (_activeNow[t]) ArmReentryCheck(t);
            }
        }

        /// <summary>The first time a watched hull of this part type goes active in the session: the <c>mpb check</c> of
        /// its renderer runs at that renderer's next write (<see cref="End"/>), so that a <c>ReentryMesh</c> running after
        /// our flush is seen. Runs on a hull's activation only.</summary>
        private void ArmReentryCheck(int target)
        {
            if (_part == null || _reentryArmed || MpbCheck.HasRun(MpbCheck.ReentryKey(_part))) return;
            _reentryArmed = true;
            _reentryCheckTarget = target;
            var r = _objects[target] as Renderer;
            Log($"{_part} reentry hull active on '{(r != null ? PathOf(r) : "?")}': mpb check armed");
        }

        // ---------------------------------------------------------------- IChannelSink

        public void Begin(int target, TargetKind kind)
        {
            _current = null;
            _light = null;
            _lastProperty = -1;
            if (kind == TargetKind.Light)
            {
                _light = _objects[target] as Light;
                return;
            }
            var b = _bindings[target];
            if (b == null || b.Renderer == null) return; // destroyed with the part
            CheckMaterial(b);
            b.Renderer.GetPropertyBlock(_block);
            _current = b;
        }

        public void Write(int property, ChannelValueType type, in Value4 v)
        {
            if (_current != null)
            {
                switch (type)
                {
                    case ChannelValueType.Color: _block.SetColor(property, new Color(v.X, v.Y, v.Z, v.W)); break;
                    case ChannelValueType.Vector: _block.SetVector(property, new Vector4(v.X, v.Y, v.Z, v.W)); break;
                    case ChannelValueType.Float: _block.SetFloat(property, v.X); break;
                    default: return;
                }
                if (property == EmissionColorId) _current.EmissionWritten = true;
                _lastProperty = property;
                _lastType = type;
                _lastValue = v;
                return;
            }
            var l = _light;
            if (l == null) return;
            switch (type)
            {
                case ChannelValueType.LightIntensity:
                    if (l.intensity != v.X) l.intensity = v.X;
                    break;
                case ChannelValueType.LightColor:
                {
                    var c = new Color(v.X, v.Y, v.Z, v.W);
                    if (l.color != c) l.color = c;
                    break;
                }
                case ChannelValueType.LightRange:
                    if (l.range != v.X) l.range = v.X;
                    break;
                case ChannelValueType.LightSpotAngle:
                    if (l.spotAngle != v.X) l.spotAngle = v.X;
                    break;
                case ChannelValueType.LightEnabled:
                {
                    var on = v.X > 0.5f;
                    if (l.enabled != on) l.enabled = on;
                    break;
                }
            }
        }

        public void End(int target, TargetKind kind)
        {
            var b = _current;
            _current = null;
            _light = null;
            if (b == null) return;
            // Not before our emission colour is in the block: an unset map and the material's own colour would glow at rest.
            if (b.WhiteMap && b.EmissionWritten) _block.SetTexture(EmissionMapId, White);
            b.Renderer.SetPropertyBlock(_block);
            if (_part == null || _lastProperty < 0 || MpbCheck.Pending) return;
            if (target == _reentryCheckTarget)
            {
                _reentryCheckTarget = -1;
                MpbCheck.TryStart(MpbCheck.ReentryKey(_part), _part, " at reentry", _table, target, b.Renderer, _lastProperty,
                    Name(_lastProperty), _lastType, _lastValue, everyTarget: false);
            }
            else if (!_checkDone)
            {
                _checkDone = true;
                MpbCheck.TryStart(_part, _part, null, _table, target, b.Renderer, _lastProperty, Name(_lastProperty), _lastType,
                    _lastValue, everyTarget: true);
            }
        }

        // ---------------------------------------------------------------- copies

        /// <summary>Destroys every owned copy; a slot that still holds one gets its material of the bind back.</summary>
        public void ReleaseAll()
        {
            foreach (var b in _bindings)
                if (b != null) Release(b);
        }

        private static void Release(Binding b)
        {
            if (b.Copies.Count == 0) return;
            var r = b.Renderer;
            if (r != null)
            {
                var slots = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < slots.Length; i++)
                {
                    var k = b.Copies.IndexOf(slots[i]);
                    if (k < 0 || b.Originals[k] == null) continue;
                    slots[i] = b.Originals[k];
                    changed = true;
                }
                if (changed) r.sharedMaterials = slots;
            }
            foreach (var c in b.Copies) DestroyMaterial(c);
            b.Copies.Clear();
            b.Originals.Clear();
        }

        private static void DestroyMaterial(Material m)
        {
            if (m == null) return;
            if (Application.isPlaying) Object.Destroy(m);
            else Object.DestroyImmediate(m);
        }

        // ---------------------------------------------------------------- bind

        /// <summary>Allocation-free while the first slot's material and shader are those of the bind (or of a deferral).</summary>
        private void CheckMaterial(Binding b)
        {
            var m = b.Renderer.sharedMaterial;
            var shader = m != null ? m.shader : null;
            if (ReferenceEquals(m, b.First) && ReferenceEquals(shader, b.FirstShader))
            {
                b.DeferredOn = null;
                return;
            }
            if (b.DeferredOn != null && ReferenceEquals(shader, b.DeferredOn)) return;
            var name = shader != null ? shader.name : null;
            switch (WriterPolicy.Rebind(!ReferenceEquals(m, b.First), !ReferenceEquals(shader, b.FirstShader), name))
            {
                case RebindDecision.Defer:
                    b.DeferredOn = shader;
                    return;
                case RebindDecision.Rebind:
                    Rebind(b, name);
                    return;
            }
        }

        /// <summary>The material or shader changed: copies no slot holds any more are destroyed (those still held stay
        /// ours), and the emission setup is decided again from the current materials.</summary>
        private void Rebind(Binding b, string newShader)
        {
            var old = b.FirstShaderName ?? "none";
            var slots = b.Renderer.sharedMaterials;
            for (var k = b.Copies.Count - 1; k >= 0; k--)
            {
                if (Array.IndexOf(slots, b.Copies[k]) >= 0) continue;
                DestroyMaterial(b.Copies[k]);
                b.Copies.RemoveAt(k);
                b.Originals.RemoveAt(k);
            }
            b.DeferredOn = null;
            Decide(b);
            Rebinds++;
            Log($"{_part ?? "?"} renderer '{PathOf(b.Renderer)}' rebound: material changed ({old} -> {newShader ?? "none"})");
        }

        /// <summary>Facts and <see cref="EmissionPolicy"/> decision per material, copies through one
        /// <c>renderer.materials</c> call and one <c>sharedMaterials</c> write, then the record of the first slot.</summary>
        private void Decide(Binding b)
        {
            var r = b.Renderer;
            var shared = r.sharedMaterials;
            var setup = _settings?.EmissionSetup ?? true;
            var copy = new bool[shared.Length];
            int wanted = 0, whiteWanted = 0, mapSet = 0;
            for (var i = 0; i < shared.Length; i++)
            {
                var m = shared[i];
                if (m == null) continue;
                var shader = m.shader;
                if (shader != null) _shaders.Add(shader.name);
                if (!b.EmissionWanted) continue;
                var facts = Facts(m);
                if (facts.HasEmissionMap && facts.EmissionMapSet) mapSet++;
                var d = EmissionPolicy.Decide(facts, EmissionPolicy.EmissionColor, setup);
                copy[i] = d.CopyMaterial && b.Copies.IndexOf(m) < 0;
                if (copy[i]) wanted++;
                if (d.WhiteMap) whiteWanted++;
                var key = facts.Shader + "|" + m.name;
                if (LoggedEmission.Add(key))
                    Log($"emission on '{facts.Shader}' material '{m.name}': {d.Action}");
            }

            if (wanted > 0)
            {
                var instances = r.materials; // Unity-owned: Redux's later renderer.materials calls return them as they are
                for (var i = 0; i < instances.Length; i++)
                {
                    var inst = instances[i];
                    if (inst == null) continue;
                    var fresh = !ReferenceEquals(inst, shared[i]);
                    if (copy[i])
                    {
                        inst.EnableKeyword(EmissionKeyword);
                        inst.SetColor(EmissionColorId, Color.black);
                        if (fresh)
                        {
                            b.Copies.Add(inst);
                            b.Originals.Add(shared[i]);
                        }
                    }
                    else if (fresh)
                    {
                        Object.DestroyImmediate(inst); // instanced by our call, not needed: the slot keeps its material
                        instances[i] = shared[i];
                    }
                }
                r.sharedMaterials = instances;
            }

            // The block is per renderer: a white map would replace a set map of another material of it.
            b.WhiteMap = whiteWanted > 0 && mapSet == 0;
            b.First = r.sharedMaterial;
            b.FirstShader = b.First != null ? b.First.shader : null;
            b.FirstShaderName = b.FirstShader != null ? b.FirstShader.name : null;
            b.Note = $"{shared.Length} material(s), {b.Copies.Count} own cop{(b.Copies.Count == 1 ? "y" : "ies")}" +
                     (b.WhiteMap ? ", white _EmissionMap" : whiteWanted > 0 ? ", no white _EmissionMap (another material has a map)" : "");
        }

        private static MaterialFacts Facts(Material m)
        {
            var shader = m.shader;
            var hasMap = m.HasProperty(EmissionMapId);
            return new MaterialFacts(
                shader != null ? shader.name : "none",
                m.HasProperty(EmissionColorId),
                shader != null && shader.keywordSpace.FindKeyword(EmissionKeyword).isValid,
                m.IsKeywordEnabled(EmissionKeyword),
                hasMap,
                hasMap && m.GetTexture(EmissionMapId) != null);
        }

        private int Add(Object o, TargetKind kind)
        {
            var target = _table.AddTarget(kind, false, false);
            while (_objects.Count <= target)
            {
                _objects.Add(null);
                _bindings.Add(null);
            }
            _objects[target] = o;
            _targetOf[o] = target;
            if (_activeNow.Length > 0 && _activeNow.Length < _objects.Count)
            {
                Array.Resize(ref _activeNow, _objects.Count);
                Array.Resize(ref _activeShown, _objects.Count);
            }
            return target;
        }

        private string PathOf(Renderer r)
        {
            var sb = new StringBuilder(r.name);
            for (var t = r.transform.parent; t != null && t != _root; t = t.parent) sb.Insert(0, t.name + "/");
            return sb.ToString();
        }

        private string Name(int property) => _propertyNames.TryGetValue(property, out var n) ? n : "#" + property;

        private static Texture2D White => _white != null ? _white : _white = Texture2D.whiteTexture;

        private static void Log(string message) => KSP1PartModPlugin.Log?.LogInfo("[KSP1PartMod] anim: " + message);

        private sealed class Binding
        {
            public readonly Renderer Renderer;
            /// <summary>The first slot's material and shader recorded at the last (re)bind.</summary>
            public Material First;
            public Shader FirstShader;
            public string FirstShaderName;
            /// <summary>The OAB selection shader seen on the first slot: the rebind waits until it changes.</summary>
            public Shader DeferredOn;
            /// <summary>A channel of this renderer writes <c>_EmissionColor</c>.</summary>
            public bool EmissionWanted;
            public bool WhiteMap;
            /// <summary>Our <c>_EmissionColor</c> is in the renderer's block (the white map may follow).</summary>
            public bool EmissionWritten;
            /// <summary>Our copies, and the material each replaced in its slot.</summary>
            public readonly List<Material> Copies = new List<Material>();
            public readonly List<Material> Originals = new List<Material>();
            public string Note;

            public Binding(Renderer r) => Renderer = r;
        }

        private struct Watch
        {
            public int Target;
            public GameObject Hull;
        }
    }
}
