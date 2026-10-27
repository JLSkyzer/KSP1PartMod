using System.Collections.Generic;
using Curve = KSP1PartMod.Animation.Behaviours.FloatCurve;
using KSP1PartMod.Animation.Mu;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Turns the material and light curves of a KSP1 clip into <see cref="Track"/>s.</summary>
    public static class TrackBuilder
    {
        /// <summary>Null when the clip has no curve to keep. Transform curves are left to Redux; every other
        /// curve that is not kept is counted in <paramref name="drops"/>.</summary>
        public static TrackSet FromClip(MuAnimClip clip, string hostPath, int modelIndex, out TrackDrops drops)
        {
            drops = default;
            var tracks = new List<Track>();
            var index = new Dictionary<(string, string, bool), Track>();
            foreach (var curve in clip.Curves)
            {
                var property = curve.Property ?? "";
                switch (curve.Type)
                {
                    case 0: drops.Transform++; continue;
                    case 3: drops.Audio++; continue;
                    case 4: drops.Particle++; continue;
                    case 1:
                    case 2:
                        break;
                    default: drops.Unknown++; continue;
                }

                var isLight = curve.Type == 2;
                SplitChannel(property, out var baseName, out var channel, out var colorSuffix);
                TrackKind kind;
                if (isLight)
                {
                    if (property == "m_DrawHalo") { drops.DrawHalo++; continue; }
                    switch (baseName)
                    {
                        case "m_Intensity" when channel < 0: kind = TrackKind.LightIntensity; break;
                        case "m_Color" when channel >= 0 && colorSuffix: kind = TrackKind.LightColor; break;
                        case "m_Range" when channel < 0: kind = TrackKind.LightRange; break;
                        case "m_SpotAngle" when channel < 0: kind = TrackKind.LightSpotAngle; break;
                        case "m_Enabled" when channel < 0: kind = TrackKind.LightEnabled; break;
                        default: drops.Unknown++; continue;
                    }
                }
                else
                {
                    kind = channel < 0 ? TrackKind.MaterialFloat
                        : colorSuffix ? TrackKind.MaterialColor : TrackKind.MaterialVector;
                }

                var key = (curve.Path ?? "", baseName, isLight);
                if (!index.TryGetValue(key, out var track))
                {
                    var wide = kind == TrackKind.MaterialColor || kind == TrackKind.MaterialVector || kind == TrackKind.LightColor;
                    track = new Track
                    {
                        RelativePath = key.Item1, Kind = kind, Property = baseName, IsLight = isLight,
                        Channels = new Curve[wide ? 4 : 1],
                    };
                    index[key] = track;
                    tracks.Add(track);
                }
                var slot = channel < 0 ? 0 : channel;
                if (slot >= track.Channels.Length) { drops.Unknown++; continue; }
                // A curve with no key leaves its channel null, so the channel keeps its rest value: an empty
                // curve would evaluate to 0 and blacken an emissive colour.
                track.Channels[slot] = curve.Keys.Count == 0 ? null : ToFloatCurve(curve.Keys);
            }

            tracks.RemoveAll(t => System.Array.TrueForAll(t.Channels, c => c == null));
            if (tracks.Count == 0) return null;
            return new TrackSet
            {
                Clip = clip.Name, HostPath = hostPath, ModelIndex = modelIndex,
                Length = clip.Length, WrapMode = clip.WrapMode, Tracks = tracks,
            };
        }

        private static Curve ToFloatCurve(IReadOnlyList<MuKey> keys)
        {
            var flat = new float[keys.Count * 4];
            for (var i = 0; i < keys.Count; i++)
            {
                flat[4 * i] = keys[i].Time; flat[4 * i + 1] = keys[i].Value;
                flat[4 * i + 2] = keys[i].InTangent; flat[4 * i + 3] = keys[i].OutTangent;
            }
            return new Curve(flat);
        }

        /// <summary>"_Color.g" gives ("_Color", 1, true); "_BumpMap_ST.z" gives ("_BumpMap_ST", 2, false); a name
        /// without channel suffix gives channel -1.</summary>
        private static void SplitChannel(string property, out string baseName, out int channel, out bool colorSuffix)
        {
            baseName = property; channel = -1; colorSuffix = false;
            var n = property.Length;
            if (n < 3 || property[n - 2] != '.') return;
            var c = "rgba".IndexOf(property[n - 1]);
            if (c >= 0) colorSuffix = true;
            else { c = "xyzw".IndexOf(property[n - 1]); if (c < 0) return; }
            baseName = property.Substring(0, n - 2);
            channel = c;
        }
    }
}
