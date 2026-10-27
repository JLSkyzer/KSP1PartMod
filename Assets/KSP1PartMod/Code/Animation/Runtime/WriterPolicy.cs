using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>How the channel flush treats a renderer that another writer also touches.</summary>
    public enum WriterDecision
    {
        /// <summary>The other writer merges into the block, or writes materials or a command buffer: our flush, after
        /// it in the frame, writes only when a value changed.</summary>
        After,
        /// <summary>The other writer replaces the whole block each frame: our flush rewrites all our values every frame
        /// while it is active.</summary>
        EveryFrame,
        /// <summary>We never write the renderer.</summary>
        Exclude,
    }

    /// <summary>What a bound renderer does when its material or shader is found changed.</summary>
    public enum RebindDecision
    {
        Keep,
        /// <summary>Release owned copies and bind again from the current material and shader.</summary>
        Rebind,
        /// <summary>The OAB selection shader is on: wait until it is reverted before binding.</summary>
        Defer,
    }

    /// <summary>The writers of KSP1 part renderers found in the Redux 26w42c decompile and the decision for each, as
    /// settled in the spec section "Material writers and shader timing (settled 2026-10-27)". Pure data for the
    /// glue's flush and bind.</summary>
    public static class WriterPolicy
    {
        /// <summary>Set by the OAB on the instanced materials of a grabbed part, and reverted on the same instances.</summary>
        public const string OabSelectionShader = "KSP2/Scenery/Standard_OAB_Selection";

        /// <summary>Ordinal keys. <c>ReentryMesh</c>: <c>UpdatePartGlowData</c> replaces the block of every source
        /// renderer each <c>LateUpdate</c> while the hull is active (flight). The others merge, write materials, or
        /// only draw with their own material. No writer is excluded.</summary>
        public static readonly IReadOnlyDictionary<string, WriterDecision> Table =
            new ReadOnlyDictionary<string, WriterDecision>(new Dictionary<string, WriterDecision>(StringComparer.Ordinal)
            {
                ["ReentryMesh"] = WriterDecision.EveryFrame,
                ["OabPartSelectionShader"] = WriterDecision.After,
                ["Outline"] = WriterDecision.After,
                ["Ksp1TextureSwitch"] = WriterDecision.After,
                ["ModuleColor"] = WriterDecision.After,
                ["ModuleEngineEmissive"] = WriterDecision.After,
                ["ModuleLight"] = WriterDecision.After,
            });

        /// <summary>The decision for <paramref name="writer"/>; a writer not in the table is written after.</summary>
        public static WriterDecision For(string writer) =>
            writer != null && Table.TryGetValue(writer, out var d) ? d : WriterDecision.After;

        /// <summary>A renderer rebinds when its material reference or its shader changed since the bind (the OAB grab
        /// instances the materials, then swaps the shader on those same instances), but never while the shader is
        /// <see cref="OabSelectionShader"/>.</summary>
        public static RebindDecision Rebind(bool materialChanged, bool shaderChanged, string currentShader)
        {
            if (string.Equals(currentShader, OabSelectionShader, StringComparison.Ordinal)) return RebindDecision.Defer;
            return materialChanged || shaderChanged ? RebindDecision.Rebind : RebindDecision.Keep;
        }
    }
}
