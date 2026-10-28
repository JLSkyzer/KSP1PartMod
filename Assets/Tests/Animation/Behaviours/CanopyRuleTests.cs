using KSP1PartMod.Animation.Behaviours;
using NUnit.Framework;
using UnityEngine;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class CanopyRuleTests
    {
        private static readonly Quaternion Parent = Quaternion.Euler(20f, 35f, -10f);
        private static readonly Quaternion Canopy = Quaternion.Euler(-70f, 10f, 45f);

        /// <summary>100 frames of Redux: one fixed-step spread <c>Rotate</c> on the bone (space self), then our reset (or
        /// not), then the LateUpdate compose. Returns the bone's world rotation after the last frame.</summary>
        private static Quaternion Run(bool reset, float spread)
        {
            var local = Quaternion.identity;
            var world = Parent * local;
            for (var frame = 0; frame < 100; frame++)
            {
                local = local * Quaternion.Euler(spread, 0f, 0f); // FixedUpdate: _canopyBoneTransform.Rotate(num, 0, 0, Self)
                if (reset) local = Quaternion.identity;          // our Update-phase reset
                world = Parent * local;
                world = CanopyRule.ReduxCompose(world, Parent, Canopy); // OnLateUpdate
                local = Quaternion.Inverse(Parent) * world;
            }
            return world;
        }

        [TestCase(0f)]
        [TestCase(7f * 3f)]
        public void WithoutTheReset_TheRotationDrifts(float spread) =>
            Assert.Greater(Quaternion.Angle(Run(false, spread), Canopy), 1f);

        [TestCase(0f)]
        [TestCase(7f * 3f)]
        public void WithTheReset_TheBoneEndsOnTheCanopyRotation(float spread) =>
            Assert.Less(Quaternion.Angle(Run(true, spread), Canopy), 0.01f);

        [Test] public void AppliesOnlyWhileReduxComposes()
        {
            Assert.IsFalse(CanopyRule.Applies(ReduxChuteState.Stowed));
            Assert.IsFalse(CanopyRule.Applies(ReduxChuteState.Armed));
            Assert.IsTrue(CanopyRule.Applies(ReduxChuteState.SemiDeployed));
            Assert.IsTrue(CanopyRule.Applies(ReduxChuteState.Deployed));
            Assert.IsFalse(CanopyRule.Applies(ReduxChuteState.Cut));
        }

        [Test] public void Lines() =>
            Assert.AreEqual("[KSP1PartMod] chute: parachuteRadial: canopy reset on 'canopy' (animator on 'chute')",
                CanopyRule.ResetLine("parachuteRadial", "canopy", "chute"));
    }
}
