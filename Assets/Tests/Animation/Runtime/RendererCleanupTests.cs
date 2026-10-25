using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class RendererCleanupTests
    {
        private static PartRendererInfo R(int materials, int nulls, bool hull = false, bool active = true, bool enabled = true) =>
            new PartRendererInfo("p/r", materials, nulls, hull, active, enabled);

        [Test] public void WithMaterial_Kept() => Assert.AreEqual(CleanupAction.Keep, RendererCleanup.Decide(R(2, 1)));
        [Test] public void NoMaterialSlot_Kept() => Assert.AreEqual(CleanupAction.Keep, RendererCleanup.Decide(R(0, 0)));
        [Test] public void AllNull_Disabled() => Assert.AreEqual(CleanupAction.DisableRenderer, RendererCleanup.Decide(R(2, 2)));
        [Test] public void AllNull_AlreadyDisabled_Kept() =>
            Assert.AreEqual(CleanupAction.Keep, RendererCleanup.Decide(R(1, 1, enabled: false)));

        // Redux's generated reentry hull (ReentryMeshGenerator): no material until ReentryMesh.OnEnable gives it
        // ContextualFxSystem.ReentryMaterial. Disabling its renderer would kill the reentry effect for good.
        [Test] public void ReentryHull_Inactive_Kept() =>
            Assert.AreEqual(CleanupAction.Keep, RendererCleanup.Decide(R(1, 1, hull: true, active: false)));
        [Test] public void ReentryHull_ActiveWithoutMaterial_HullDeactivated() =>
            Assert.AreEqual(CleanupAction.DeactivateHull, RendererCleanup.Decide(R(1, 1, hull: true, active: true)));
        [Test] public void ReentryHull_WithMaterial_Kept() =>
            Assert.AreEqual(CleanupAction.Keep, RendererCleanup.Decide(R(1, 0, hull: true, active: true)));

        [Test] public void Line_ListsPaths()
        {
            var line = RendererCleanup.Line("Mk1", new[] { "a/b", "c" }, new[] { "h" });
            Assert.AreEqual("[KSP1PartMod] render: Mk1 hid 2 renderer(s) without material (a/b, c); " +
                            "deactivated 1 reentry hull(s) without material (h)", line);
        }

        [Test] public void Line_OnlyRenderers()
        {
            Assert.AreEqual("[KSP1PartMod] render: Mk1 hid 1 renderer(s) without material (a)",
                RendererCleanup.Line("Mk1", new[] { "a" }, new string[0]));
        }

        [Test] public void Line_NothingHidden_Null() => Assert.IsNull(RendererCleanup.Line("Mk1", new string[0], new string[0]));

        [Test] public void Line_ManyPaths_Truncated()
        {
            var paths = new string[12];
            for (var i = 0; i < paths.Length; i++) paths[i] = "r" + i;
            StringAssert.Contains("r7, … 4 more", RendererCleanup.Line("X", paths, new string[0]));
        }

        [Test] public void HullSummary()
        {
            Assert.AreEqual("[KSP1PartMod] render: Mk1 reentry hull(s): 3 renderer(s) without material, 0 active (left to ReentryMesh)",
                RendererCleanup.HullLine("Mk1", 3, 0));
        }
    }
}
