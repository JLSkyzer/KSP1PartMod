using System;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Perf
{
    /// <summary>The per-frame features we time (performance plan Task 5). Values are only ever appended.</summary>
    public enum PerfFeature
    {
        Clips, Material, Plugins, Shrouds, Variants, Bridge, Science, Save, Flush, LookAt, RadiatorPatch, DrillPatch, SubcategoryBar,
    }

    /// <summary>The feature of each animation entry kind (performance plan, research (d)). Pure.</summary>
    public static class PerfFeatures
    {
        public static readonly int Count = Enum.GetValues(typeof(PerfFeature)).Length;

        /// <summary>The short name printed on the frame lines.</summary>
        public static string Label(PerfFeature f)
        {
            switch (f)
            {
                case PerfFeature.Clips: return "clips";
                case PerfFeature.Material: return "2a material";
                case PerfFeature.Plugins: return "2b plugins";
                case PerfFeature.Shrouds: return "shrouds";
                case PerfFeature.Variants: return "B9 gates";
                case PerfFeature.Bridge: return "ISRU/radiator bridge";
                case PerfFeature.Science: return "science";
                case PerfFeature.Save: return "save";
                case PerfFeature.Flush: return "flush";
                case PerfFeature.LookAt: return "look-at";
                case PerfFeature.RadiatorPatch: return "radiator patch";
                case PerfFeature.DrillPatch: return "drill patch";
                case PerfFeature.SubcategoryBar: return "subcategory bar";
                default: return f.ToString();
            }
        }

        /// <summary>Whether the frame line prints part and entry counts for <paramref name="f"/>. The two patches and the
        /// sub-category bar run per call (several fixed steps per frame, one bar), so a count would be calls, not parts: their
        /// lines stop after the timings.</summary>
        public static bool HasCounts(PerfFeature f) =>
            f != PerfFeature.RadiatorPatch && f != PerfFeature.DrillPatch && f != PerfFeature.SubcategoryBar;

        /// <summary>The feature an entry's <c>Step</c> is counted in. A kind not classified here throws, so that a new kind
        /// fails its test until it is.</summary>
        public static PerfFeature Of(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Toggle:
                case EntryKind.Deploy:
                case EntryKind.Gear:
                case EntryKind.Group:
                case EntryKind.AeroGate:
                case EntryKind.GearBridge:
                case EntryKind.ReduxFollower:
                case EntryKind.ChuteFollower:
                    return PerfFeature.Clips;
                case EntryKind.Throttle:
                case EntryKind.Heat:
                case EntryKind.Color:
                    return PerfFeature.Material;
                case EntryKind.EngineDeploy:
                case EntryKind.EngineMode:
                case EntryKind.LookAt:
                case EntryKind.DecouplerClip:
                case EntryKind.RcsGlow:
                case EntryKind.RcsEmissive:
                case EntryKind.MeshHider:
                case EntryKind.ScienceToggle:
                case EntryKind.AccelSpring:
                case EntryKind.LandingBag:
                    return PerfFeature.Plugins;
                case EntryKind.Shroud:
                    return PerfFeature.Shrouds;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "entry kind without a perf feature");
            }
        }

        /// <summary><see cref="Of"/> for the runtime, resolved once when an entry is bound: never throws, a kind not classified
        /// here counts in <paramref name="fallback"/>.</summary>
        public static PerfFeature OfOrDefault(EntryKind kind, PerfFeature fallback)
        {
            try
            {
                return Of(kind);
            }
            catch (ArgumentOutOfRangeException)
            {
                return fallback;
            }
        }
    }
}
