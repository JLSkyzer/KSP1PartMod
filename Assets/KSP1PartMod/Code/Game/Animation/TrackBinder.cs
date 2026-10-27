using System;
using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The transforms of one part instance under its part object, in hierarchy pre-order, with their paths
    /// relative to the part object ('/'-separated). Collected once per instance, at the first bind.</summary>
    internal sealed class PartTransforms
    {
        public readonly List<Transform> Transforms = new List<Transform>();
        public readonly List<string> Paths = new List<string>();

        public PartTransforms(Transform root)
        {
            for (var i = 0; i < root.childCount; i++) Walk(root.GetChild(i), "");
        }

        /// <summary>Explicit pre-order walk: two copies of one model have equal paths, and <see cref="HostPicker"/>
        /// tells them apart by this order.</summary>
        private void Walk(Transform t, string parent)
        {
            var path = parent.Length == 0 ? t.name : parent + "/" + t.name;
            Transforms.Add(t);
            Paths.Add(path);
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), path);
        }

        public string PathOf(Transform t)
        {
            var i = t == null ? -1 : Transforms.IndexOf(t);
            return i >= 0 ? Paths[i] : t != null ? t.name : "";
        }
    }

    /// <summary>The samplers of one TrackSet on one part instance: one per host bound.</summary>
    internal sealed class BoundTracks
    {
        public TrackSampler[] Samplers;
        /// <summary>The host of each sampler.</summary>
        public Transform[] Hosts;
        /// <summary>Distinct renderers and lights given a channel, and tracks that found no target, over all hosts.</summary>
        public int Renderers, Lights, Unbound;
        /// <summary>Paths (from the part object) of the lights left to <c>Module_Light</c>.</summary>
        public readonly List<string> ReduxLights = new List<string>();
    }

    /// <summary>Binds the tracks of a <see cref="TrackSet"/> on a part instance (spec 2a.3): per host, each track's target
    /// is <c>host.Find(relativePath)</c>; a material track takes the <c>Renderer</c> of that object only (KSP1 binds per
    /// object), a light track its <c>Light</c>. The KSP2 property is picked per material through
    /// <c>shader_property_map</c> (<see cref="MaterialRules.Pick"/>, the first material of the renderer that has a
    /// candidate), and the rest value is read from that material (or the light) now. Lights of a <c>Module_Light</c> are
    /// never taken. Runs at bind time only; registration may allocate, sampling does not.</summary>
    internal static class TrackBinder
    {
        /// <summary>The objects of <paramref name="part"/> that carry <paramref name="set"/>'s host, as indices into
        /// <see cref="PartTransforms.Transforms"/>, the set's model found by name and rank
        /// (<see cref="PartTracks.ModelRoots"/>, <see cref="HostPicker"/>).</summary>
        public static IReadOnlyList<int> Hosts(PartTransforms part, TrackSet set, PartTracks tracks, out string note) =>
            HostPicker.Pick(part.Paths, set.HostPath, tracks?.ModelRoots, set.ModelIndex, out note);

        /// <param name="hosts">The hosts to bind, from <see cref="Hosts"/> after the owner's choice.</param>
        /// <param name="entryOrNull">The owning entry of ours (its module index ranks the channels), or null for a clip
        /// that Redux plays.</param>
        /// <param name="note">The bind line after <c>anim: &lt;part&gt; </c>.</param>
        public static BoundTracks Bind(PartTransforms part, IReadOnlyList<Transform> hosts, TrackSet set, AnimEntryDef entryOrNull,
            ChannelTable table, RendererChannels sink, AnimationSettings s, IReadOnlyCollection<Light> reduxLights, out string note)
        {
            var bound = new BoundTracks();
            var samplers = new List<TrackSampler>();
            var boundHosts = new List<Transform>();
            var renderers = new HashSet<Renderer>();
            var lights = new HashSet<Light>();
            var props = new SortedSet<string>(StringComparer.Ordinal);
            var unbound = new List<string>();
            var moduleIndex = entryOrNull?.ModuleIndex ?? -1;
            var tracks = set.Tracks ?? new Track[0];

            foreach (var host in hosts)
            {
                if (host == null) continue;
                var channels = new int[tracks.Count];
                for (var k = 0; k < tracks.Count; k++)
                {
                    channels[k] = -1;
                    var track = tracks[k];
                    if (track == null || !Wanted(track, s)) continue;
                    var relative = track.RelativePath ?? "";
                    var target = relative.Length == 0 ? host : host.Find(HostPicker.InstancePath(relative));
                    if (target == null)
                    {
                        unbound.Add(Shown(relative));
                        continue;
                    }
                    try
                    {
                        channels[k] = track.IsLight
                            ? BindLight(part, target, track, moduleIndex, table, sink, reduxLights, lights, bound)
                            : BindMaterial(target, track, moduleIndex, table, sink, s, renderers, props);
                    }
                    catch (ArgumentException)
                    {
                        channels[k] = -1; // the slot holds another value type: not ours to write
                    }
                    if (channels[k] < 0 && !(track.IsLight && IsReduxLight(target, reduxLights))) unbound.Add(Shown(relative));
                }
                samplers.Add(new TrackSampler(set, table, channels));
                boundHosts.Add(host);
            }

            bound.Samplers = samplers.ToArray();
            bound.Hosts = boundHosts.ToArray();
            bound.Renderers = renderers.Count;
            bound.Lights = lights.Count;
            bound.Unbound = unbound.Count;
            var first = new List<string>();
            foreach (var p in unbound)
            {
                if (first.Count == 3) break;
                if (!first.Contains(p)) first.Add(p);
            }
            note = $"tracks '{set.Clip}': host '{HostPicker.InstancePath(set.HostPath)}' x{bound.Hosts.Length}, " +
                   $"{bound.Renderers} renderer(s) via [{string.Join(", ", props)}], {bound.Lights} light(s), " +
                   $"{bound.Unbound} unbound ({string.Join(", ", first)})";
            return bound;
        }

        /// <summary>Whether the settings keep this kind of track (<c>material_curves</c>, <c>light_curves</c>).</summary>
        public static bool Wanted(Track track, AnimationSettings s) =>
            s == null || (track.IsLight ? s.LightCurves : s.MaterialCurves);

        private static int BindMaterial(Transform target, Track track, int moduleIndex, ChannelTable table, RendererChannels sink,
            AnimationSettings s, HashSet<Renderer> renderers, SortedSet<string> props)
        {
            var r = target.GetComponent<Renderer>();
            if (r == null) return -1;
            var candidates = MaterialRules.Candidates(track.Property, s?.ShaderPropertyMap);
            Material material = null;
            string name = null;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                name = MaterialRules.Pick(candidates, m.HasProperty);
                if (name == null) continue;
                material = m;
                break;
            }
            if (material == null) return -1;
            var id = Shader.PropertyToID(name);
            var type = MaterialType(track.Kind);
            var rest = MaterialRest(material, id, type);
            var t = sink.AddRenderer(r, s, out _);
            var channel = table.Register(t, id, type, WriterClass.Track, moduleIndex, rest);
            renderers.Add(r);
            props.Add(name);
            return channel;
        }

        private static int BindLight(PartTransforms part, Transform target, Track track, int moduleIndex, ChannelTable table,
            RendererChannels sink, IReadOnlyCollection<Light> reduxLights, HashSet<Light> lights, BoundTracks bound)
        {
            var l = target.GetComponent<Light>();
            if (l == null) return -1;
            if (IsReduxLight(target, reduxLights))
            {
                var path = part.PathOf(target);
                if (!bound.ReduxLights.Contains(path)) bound.ReduxLights.Add(path);
                return -1;
            }
            var type = LightValueType(track.Kind);
            var t = sink.AddLight(l);
            var channel = table.Register(t, (int)type, type, WriterClass.Track, moduleIndex, LightRest(l, type));
            lights.Add(l);
            return channel;
        }

        private static bool IsReduxLight(Transform target, IReadOnlyCollection<Light> reduxLights)
        {
            if (reduxLights == null || reduxLights.Count == 0) return false;
            var l = target.GetComponent<Light>();
            if (l == null) return false;
            foreach (var r in reduxLights)
                if (r == l) return true;
            return false;
        }

        private static ChannelValueType MaterialType(TrackKind kind) =>
            kind == TrackKind.MaterialColor ? ChannelValueType.Color
            : kind == TrackKind.MaterialVector ? ChannelValueType.Vector : ChannelValueType.Float;

        private static ChannelValueType LightValueType(TrackKind kind)
        {
            switch (kind)
            {
                case TrackKind.LightColor: return ChannelValueType.LightColor;
                case TrackKind.LightRange: return ChannelValueType.LightRange;
                case TrackKind.LightSpotAngle: return ChannelValueType.LightSpotAngle;
                case TrackKind.LightEnabled: return ChannelValueType.LightEnabled;
                default: return ChannelValueType.LightIntensity;
            }
        }

        private static Value4 MaterialRest(Material m, int id, ChannelValueType type)
        {
            switch (type)
            {
                case ChannelValueType.Color:
                {
                    var c = m.GetColor(id);
                    return new Value4(c.r, c.g, c.b, c.a);
                }
                case ChannelValueType.Vector:
                {
                    var v = m.GetVector(id);
                    return new Value4(v.x, v.y, v.z, v.w);
                }
                default:
                    return new Value4(m.GetFloat(id), 0f, 0f, 0f);
            }
        }

        private static Value4 LightRest(Light l, ChannelValueType type)
        {
            switch (type)
            {
                case ChannelValueType.LightColor: return new Value4(l.color.r, l.color.g, l.color.b, l.color.a);
                case ChannelValueType.LightRange: return new Value4(l.range, 0f, 0f, 0f);
                case ChannelValueType.LightSpotAngle: return new Value4(l.spotAngle, 0f, 0f, 0f);
                case ChannelValueType.LightEnabled: return new Value4(l.enabled ? 1f : 0f, 0f, 0f, 0f);
                default: return new Value4(l.intensity, 0f, 0f, 0f);
            }
        }

        private static string Shown(string relative) => relative.Length == 0 ? "." : relative;
    }
}
