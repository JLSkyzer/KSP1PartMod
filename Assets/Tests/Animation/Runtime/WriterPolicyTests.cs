using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    /// <summary>One test per row of the spec section "Material writers and shader timing (settled 2026-10-27)".</summary>
    public class WriterPolicyTests
    {
        [Test] public void WriterPolicy_ReentryMesh_EveryFrame() => Assert.AreEqual(WriterDecision.EveryFrame, WriterPolicy.For("ReentryMesh"));
        [Test] public void WriterPolicy_OabPartSelectionShader_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("OabPartSelectionShader"));
        [Test] public void WriterPolicy_Outline_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("Outline"));
        [Test] public void WriterPolicy_Ksp1TextureSwitch_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("Ksp1TextureSwitch"));
        [Test] public void WriterPolicy_ModuleColor_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("ModuleColor"));
        [Test] public void WriterPolicy_ModuleEngineEmissive_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("ModuleEngineEmissive"));
        [Test] public void WriterPolicy_ModuleLight_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("ModuleLight"));

        [Test] public void WriterPolicy_NoRowExcludes() => CollectionAssert.DoesNotContain(WriterPolicy.Table.Values, WriterDecision.Exclude);
        [Test] public void WriterPolicy_TableHasTheSevenRows() => Assert.AreEqual(7, WriterPolicy.Table.Count);
        [Test] public void WriterPolicy_UnknownWriter_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("SomethingElse"));
        [Test] public void WriterPolicy_Null_After() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For(null));
        [Test] public void WriterPolicy_KeysAreOrdinal() => Assert.AreEqual(WriterDecision.After, WriterPolicy.For("reentrymesh"));

        [Test] public void Rebind_NothingChanged_Keep() =>
            Assert.AreEqual(RebindDecision.Keep, WriterPolicy.Rebind(false, false, "KSP2/Scenery/Standard (Opaque)"));
        [Test] public void Rebind_MaterialReferenceChanged_Rebind() =>
            Assert.AreEqual(RebindDecision.Rebind, WriterPolicy.Rebind(true, false, "KSP2/Scenery/Standard (Opaque)"));
        [Test] public void Rebind_ShaderChangedOnSameMaterial_Rebind() =>
            Assert.AreEqual(RebindDecision.Rebind, WriterPolicy.Rebind(false, true, "KSP2/Scenery/Standard (Cutout)"));
        [Test] public void Rebind_UnderOabSelectionShader_Deferred() =>
            Assert.AreEqual(RebindDecision.Defer, WriterPolicy.Rebind(true, true, WriterPolicy.OabSelectionShader));
        [Test] public void Rebind_OabSelectionShaderName() =>
            Assert.AreEqual("KSP2/Scenery/Standard_OAB_Selection", WriterPolicy.OabSelectionShader);
    }
}
