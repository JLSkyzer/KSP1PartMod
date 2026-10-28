using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    /// <summary>The exact log lines of the engine shroud feature (plan, Tasks 2 and 3).</summary>
    public class ShroudLinesTests
    {
        [Test] public void Armed() =>
            Assert.AreEqual("[KSP1PartMod] shroud: armed: Redux's Module_Fairing is not added to KSP1 parts with a ModuleJettison shroud; " +
                            "the KSP1 meshes are kept, hidden until a part is attached below", ShroudLines.Armed);

        [Test] public void OffBySwitch() =>
            Assert.AreEqual("[KSP1PartMod] shroud: off (ksp1_engine_shrouds)", ShroudLines.OffBySwitch);

        [Test] public void OffAnimationsDisabled() =>
            Assert.AreEqual("[KSP1PartMod] shroud: off because animations are disabled (enabled false): Redux's Module_Fairing left as in 0.6.0",
                ShroudLines.OffAnimationsDisabled);

        [Test] public void MemberNotFound() =>
            Assert.AreEqual("[KSP1PartMod] shroud: Ksp1PartTranslator.BuildConfiguredPrefab not found, shrouds off (Redux's Module_Fairing left as in 0.6.0)",
                ShroudLines.MemberNotFound("Ksp1PartTranslator.BuildConfiguredPrefab"));

        [Test] public void StagingSignal()
        {
            Assert.AreEqual("[KSP1PartMod] shroud: staging signal armed on PartBehavior.ActivateAllModules", ShroudLines.StagingSignalArmed);
            Assert.AreEqual("[KSP1PartMod] shroud: PartBehavior.ActivateAllModules not found, non-fairing shrouds jettison at the first engine ignition instead",
                ShroudLines.StagingSignalMissing);
        }

        [Test] public void ImportSummary() =>
            Assert.AreEqual("[KSP1PartMod] shroud: import: 84 part(s) kept their KSP1 shroud, 3 with a transform missing, 12 prefab rebuild(s)",
                ShroudLines.ImportSummary(84, 3, 12));

        [Test] public void ImportLines()
        {
            Assert.AreEqual("[KSP1PartMod] shroud: liquidEngineRE-I2: Redux's Module_Fairing not added, KSP1 shroud 'Shroud1p5x3,Shroud2x3' kept",
                ShroudLines.ModuleNotAdded("liquidEngineRE-I2", new[] { "Shroud1p5x3", "Shroud2x3" }));
            Assert.AreEqual("[KSP1PartMod] shroud: p: 2 shroud transform(s) hidden in the prefab", ShroudLines.HiddenInPrefab("p", 2));
            Assert.AreEqual("[KSP1PartMod] shroud: p: shroud transform(s) 'A,B' not found in the model",
                ShroudLines.NotFoundInModel("p", new[] { "A", "B" }));
            Assert.AreEqual("[KSP1PartMod] shroud: patch failed, stopped for this session: boom", ShroudLines.PatchFailed("boom"));
        }

        [Test] public void Resolved() =>
            Assert.AreEqual("[KSP1PartMod] anim: p shroud 'A,B': 1/2 transform(s) found, node 'bottom' found, isFairing True, 2 variant gate(s)",
                ShroudLines.Resolved("p", new[] { "A", "B" }, 1, "bottom", true, true, 2));

        [Test] public void Resolved_NodeMissing() =>
            StringAssert.Contains("1/1 transform(s) found, node 'bottom' missing, isFairing False, 0 variant gate(s)",
                ShroudLines.Resolved("p", new[] { "A" }, 1, "bottom", false, false, 0));

        [Test] public void Colliders() =>
            Assert.AreEqual("[KSP1PartMod] anim: p shroud: 4 collider(s) disabled in flight", ShroudLines.CollidersDisabled("p", 4));

        [Test] public void Runtime()
        {
            Assert.AreEqual("[KSP1PartMod] anim: p shroud 'Shroud2x3' shown (part attached below)", ShroudLines.Shown("p", "Shroud2x3"));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud hidden (nothing attached below)", ShroudLines.Hidden("p"));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud: variant 'PartVariant0' -> 'Bare': shroud 'Shroud1p5x3'",
                ShroudLines.VariantChanged("p", "PartVariant0", "Bare", "Shroud1p5x3"));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud 'Shroud2x3' jettisoned (bottom node detached)",
                ShroudLines.Jettisoned("p", "Shroud2x3", ShroudReason.BottomDetached));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud 'Shroud2x3' jettisoned (staged)",
                ShroudLines.Jettisoned("p", "Shroud2x3", ShroudReason.Staged));
            StringAssert.Contains("jettisoned (restored from the saved state)", ShroudLines.Jettisoned("p", "S", ShroudReason.Restored));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud hide override on", ShroudLines.HideOverride("p", true));
            Assert.AreEqual("[KSP1PartMod] anim: p shroud hide override off", ShroudLines.HideOverride("p", false));
        }

        [Test] public void ControlLabelsAndAudit()
        {
            Assert.AreEqual("Hide shroud", ShroudLines.HideLabel);
            Assert.AreEqual("Show shroud", ShroudLines.ShowLabel);
            Assert.AreEqual("audit: shrouds: 10 part(s) with a KSP1 shroud kept, 2 with a shroud transform missing, 1 without the node, 0 still carrying Redux's Module_Fairing",
                ShroudLines.AuditSummary(10, 2, 1, 0));
        }
    }
}
