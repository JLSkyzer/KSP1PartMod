using System;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>What a V-SwiFT variant change can do to the objects our bindings hold.</summary>
    [Flags]
    public enum VariantEffect
    {
        None = 0,
        /// <summary>Materials of a renderer replaced or rewritten (texture switch, material swap).</summary>
        MaterialSwap = 1,
        /// <summary>GameObjects switched on or off; nothing created or destroyed.</summary>
        TransformToggle = 2,
        /// <summary>A mesh replaced on a renderer or filter.</summary>
        MeshSwap = 4,
        /// <summary>GameObjects destroyed and created again.</summary>
        ObjectRebuild = 8,
    }

    /// <summary>How much of a part's bindings a variant change makes stale.</summary>
    public enum RebindScope
    {
        /// <summary>Nothing: every binding stays valid.</summary>
        None,
        /// <summary>The material decisions and copies of every bound renderer.</summary>
        Renderers,
        /// <summary>States, samplers, table targets and helpers: bound again; machines and saved values kept.</summary>
        Full,
    }

    /// <summary>Spec "Variant application and plugin inputs (settled 2026-10-28)": the scope of the rebind a variant change
    /// calls for. A toggled object keeps its bindings (its hidden renderers and states keep their values); a material
    /// change needs the renderers decided again; a mesh swap or a rebuilt object needs everything bound again.</summary>
    public static class RebindRule
    {
        /// <summary>What Redux 26w42c's variants do on a KSP1 part: <c>TransformActivator</c> (toggle) and
        /// <c>Ksp1TextureSwitchTransformer</c> (material properties rewritten in place); <c>AttachNodeMover</c> touches no
        /// renderer. No mesh swap, no rebuild.</summary>
        public static readonly VariantEffect KnownEffects = VariantEffect.MaterialSwap | VariantEffect.TransformToggle;

        public static RebindScope For(VariantEffect e)
        {
            if ((e & (VariantEffect.MeshSwap | VariantEffect.ObjectRebuild)) != 0) return RebindScope.Full;
            if ((e & VariantEffect.MaterialSwap) != 0) return RebindScope.Renderers;
            return RebindScope.None;
        }
    }
}
