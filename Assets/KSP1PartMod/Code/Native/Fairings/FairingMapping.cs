using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Native.Fairings
{
    /// <summary>A KSP1 fairing base read as KSP2's procedural <c>Data_Fairing</c> (fairings plan (d)). Pure.
    /// <list type="bullet">
    /// <item><c>ModuleProceduralFairing</c>: the radii, heights, nose, pivot and axis map one to one; <c>useClamshell = true</c>
    /// gives two clamshells, else four; <c>ejectionForce</c>, else KSP2's 100; <c>BaseModelTransformName</c>, else KSP2's
    /// <c>Base</c>; <c>nSides</c> kept (KSP2 clamps it to 12 or more in the VAB); <c>UnitAreaMass</c> (t/m²);
    /// <c>stagingEnabled</c>, else true. Not converted without a <c>baseRadius</c> above 0.</item>
    /// <item><c>ModuleSimpleAdjustableFairing</c> (SAF): the base radius is half the KSP1 diameter of the part's
    /// <c>node_stack_top</c> size (not converted without that node or with an unknown size); the max radius is
    /// max(2.4 × base, <c>shieldingBaseRadius</c>); cap and close radius 0.32 × base (the stock ratio 0.2 / 0.625); the pivot
    /// is the <c>WALL_BASE</c> <c>rootOffset</c>; <c>numSlices</c> 2, 3, 4+ gives that many clamshells (default 2).</item>
    /// </list>
    /// No interstage node is allowed (the KSP1 interstage nodes stay as Redux imported them).</summary>
    public static class FairingMapping
    {
        public const string ProceduralModule = "ModuleProceduralFairing";
        public const string SafModule = "ModuleSimpleAdjustableFairing";
        public const string NativeModule = "Module_Fairing";

        /// <summary>SAF max radius = this × base radius at least.</summary>
        public const float SafMaxRadiusFactor = 2.4f;
        /// <summary>SAF cap and close radius = this × base radius (stock <c>fairingSize1</c>: 0.2 / 0.625).</summary>
        public const float SafCapRadiusFactor = 0.32f;

        /// <summary>The <c>ModuleProceduralFairing</c> fields without a counterpart, noted when present.</summary>
        public static readonly IReadOnlyList<string> ProceduralNotReproduced = new[]
        {
            "nArcs", "panelGrouping", "edgeSlide", "TextureURL", "CapTextureURL", "TextureNormalURL", "DefaultBaseTextureURL",
            "DefaultBaseNormalsURL", "UnitAreaCost", "nCollidersPerXSection", "stagingToggleEnabledFlight",
        };

        /// <summary>The SAF fields without a counterpart, noted when present (<c>CoM</c> and <c>mass</c> are read in the
        /// <c>WALL_BASE</c>, <c>WALL</c> and <c>CONE</c> nodes).</summary>
        public static readonly IReadOnlyList<string> SafNotReproduced = new[]
        {
            "segmentLength", "maxSegments", "deploySpeed", "editorOpenOffset", "CoM", "mass",
        };

        /// <summary>The SAF piece nodes, in KSP1's order.</summary>
        public static readonly IReadOnlyList<string> SafPieces = new[] { "WALL_BASE", "WALL", "CONE" };

        /// <summary>KSP1 node size → radius (half of 0.625, 1.25, 2.5, 3.75, 5 m).</summary>
        public static float? NodeSizeRadius(int size)
        {
            switch (size)
            {
                case 0: return 0.3125f;
                case 1: return 0.625f;
                case 2: return 1.25f;
                case 3: return 1.875f;
                case 4: return 2.5f;
                default: return null;
            }
        }

        public static FairingMap Procedural(IKsp1Node module, IKsp1Node part)
        {
            var map = new FairingMap { Kind = FairingKind.Procedural };
            var v = map.Values;
            var baseRadius = IsruCfg.Number(module, "baseRadius");
            if (!(baseRadius > 0))
            {
                map.Reason = "no baseRadius above 0";
                Notes(module, ProceduralNotReproduced, map.NotReproduced);
                return map;
            }
            v.BaseRadius = (float)baseRadius.Value;
            v.MaxRadius = Positive(module, "maxRadius") ?? v.MaxRadius;
            var cap = Positive(module, "capRadius");
            if (cap.HasValue) v.CapRadius = v.CloseRadius = cap.Value;
            v.SnapThreshold = NonNegative(module, "snapThreshold") ?? v.SnapThreshold;
            v.CrossSectionHeightMin = Positive(module, "xSectionHeightMin") ?? v.CrossSectionHeightMin;
            v.CrossSectionHeightMax = Positive(module, "xSectionHeightMax") ?? v.CrossSectionHeightMax;
            v.NoseTip = NonNegative(module, "noseTip") ?? v.NoseTip;
            v.EdgeWarp = NonNegative(module, "edgeWarp") ?? v.EdgeWarp;
            v.Pivot = Vector(IsruCfg.Value(module, "pivot")) ?? v.Pivot;
            var axis = Vector(IsruCfg.Value(module, "axis"));
            v.LocalUpAxis = axis.HasValue && !axis.Value.IsZero ? axis.Value : FairingVector.Up;
            var transform = IsruCfg.Value(module, "BaseModelTransformName");
            if (!string.IsNullOrEmpty(transform)) v.BaseModelTransformName = transform;
            var sides = IsruCfg.Number(module, "nSides");
            if (sides >= 1 && sides <= 1000) v.FairingSideCount = (int)Math.Round(sides.Value);
            v.MassAreaRatio = NonNegative(module, "UnitAreaMass") ?? FairingValues.DefaultMassAreaRatio;
            v.DefaultDeployType = IsruCfg.Bool(module, "useClamshell") == true ? FairingDeploy.Clamshellx2 : FairingDeploy.Clamshellx4;
            v.EjectionForce = NonNegative(module, "ejectionForce") ?? FairingValues.Ksp2EjectionForce;
            v.StageToggleDefault = IsruCfg.Bool(module, "stagingEnabled") ?? true;
            v.AllowInterstageNode = false;
            map.Converted = true;
            Notes(module, ProceduralNotReproduced, map.NotReproduced);
            return map;
        }

        public static FairingMap Saf(IKsp1Node module, IKsp1Node part)
        {
            var map = new FairingMap { Kind = FairingKind.Saf };
            var v = map.Values;
            SafNotes(module, map.NotReproduced);
            var size = TopNodeSize(part, out var hasTop);
            if (!hasTop)
            {
                map.Reason = "no node_stack_top";
                return map;
            }
            var radius = size.HasValue ? NodeSizeRadius(size.Value) : null;
            if (!radius.HasValue)
            {
                map.Reason = "node_stack_top of unknown size";
                return map;
            }
            v.BaseRadius = radius.Value;
            v.MaxRadius = Math.Max(SafMaxRadiusFactor * v.BaseRadius, Positive(module, "shieldingBaseRadius") ?? 0f);
            v.CapRadius = v.CloseRadius = SafCapRadiusFactor * v.BaseRadius;
            var wallBase = module?.GetNodes("WALL_BASE")?.FirstOrDefault(n => n != null);
            v.Pivot = Vector(IsruCfg.Value(wallBase, "rootOffset")) ?? default(FairingVector);
            v.LocalUpAxis = FairingVector.Up;
            v.BaseModelTransformName = FairingValues.Ksp2BaseModelTransformName;
            v.MassAreaRatio = FairingValues.DefaultMassAreaRatio;
            var slices = IsruCfg.Number(module, "numSlices");
            v.DefaultDeployType = !slices.HasValue || slices.Value < 3 ? FairingDeploy.Clamshellx2
                : slices.Value < 4 ? FairingDeploy.Clamshellx3 : FairingDeploy.Clamshellx4;
            v.EjectionForce = NonNegative(module, "ejectionForce") ?? FairingValues.Ksp2EjectionForce;
            v.StageToggleDefault = IsruCfg.Bool(module, "stagingEnabled") ?? true;
            v.AllowInterstageNode = false;
            map.Converted = true;
            return map;
        }

        /// <summary>The size of the part's <c>node_stack_top</c> (7th value; KSP1's default 1 when absent); null when the
        /// value is not an integer.</summary>
        internal static int? TopNodeSize(IKsp1Node part, out bool hasTop)
        {
            var text = IsruCfg.Value(part, "node_stack_top");
            hasTop = !string.IsNullOrEmpty(text);
            if (!hasTop) return null;
            var parts = text.Split(',');
            if (parts.Length < 7) return 1;
            return int.TryParse(parts[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : (int?)null;
        }

        /// <summary><c>x, y, z</c>; null when not three numbers.</summary>
        internal static FairingVector? Vector(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var parts = text.Split(',');
            if (parts.Length != 3) return null;
            var xyz = new float[3];
            for (var i = 0; i < 3; i++)
            {
                if (!IsruCfg.TryNumber(parts[i], out var d)) return null;
                xyz[i] = (float)d;
            }
            return new FairingVector(xyz[0], xyz[1], xyz[2]);
        }

        private static float? Positive(IKsp1Node node, string key)
        {
            var d = IsruCfg.Number(node, key);
            return d > 0 ? (float)d.Value : (float?)null;
        }

        private static float? NonNegative(IKsp1Node node, string key)
        {
            var d = IsruCfg.Number(node, key);
            return d >= 0 ? (float)d.Value : (float?)null;
        }

        private static void Notes(IKsp1Node module, IEnumerable<string> fields, List<string> notes)
        {
            if (module == null) return;
            foreach (var f in fields)
                if (module.GetValue(f) != null) IsruCfg.AddOnce(notes, f);
        }

        private static void SafNotes(IKsp1Node module, List<string> notes)
        {
            if (module == null) return;
            var pieces = SafPieces.SelectMany(p => module.GetNodes(p) ?? Enumerable.Empty<IKsp1Node>()).Where(n => n != null).ToList();
            foreach (var f in SafNotReproduced)
                if (module.GetValue(f) != null || pieces.Any(p => p.GetValue(f) != null)) IsruCfg.AddOnce(notes, f);
        }
    }
}
