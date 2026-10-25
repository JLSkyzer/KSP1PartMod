using System.Collections.Generic;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Behaviours
{
    public class ThrottleFollowerTests
    {
        private const string Key = "throttle:colorAnimation:0";

        private static ThrottleFollower Throttle(float response = 0.5f, float? saved = null, bool playInEditor = false,
            bool weightOnOperational = false)
        {
            var f = new ThrottleFollower(new AnimEntryDef
            {
                Kind = EntryKind.Throttle, Key = Key, Clip = "colorAnimation", Layer = 1, WrapMode = 8,
                ResponseSpeed = response, PlayInEditor = playInEditor, WeightOnOperational = weightOnOperational,
            });
            if (saved != null) f.Load(SavedValues.Of(Key, ("s", saved.Value)));
            return f;
        }

        [Test] public void Throttle_Smooths_ByResponseSpeed()
        {
            var f = Throttle(response: 0.5f); f.Tick(0.02f, false, 1f, true); Assert.AreEqual(0.25f, f.Pose.NormalizedTime, 1e-4f);
        }

        [Test] public void Throttle_NotOperational_TargetsZero()
        {
            var f = Throttle(saved: 1f); f.Tick(1f, false, 1f, false); Assert.Less(f.Pose.NormalizedTime, 1f);
        }

        [Test] public void Throttle_Editor_IdleUnlessPlayInEditor()
        {
            var f = Throttle(); f.Tick(1f, true, 1f, true); Assert.AreEqual(0f, f.Pose.NormalizedTime);
        }

        [Test] public void Throttle_Editor_PlayInEditor_TargetsOne_WithoutKeepingIt()
        {
            // KSP1 writes the lerped value to the clip in the editor but never stores it back into animState.
            var f = Throttle(response: 0.5f, playInEditor: true);
            f.Tick(0.02f, true, 0f, false); f.Tick(0.02f, true, 0f, false);
            Assert.AreEqual((0.25f, 0f), (f.Pose.NormalizedTime, f.Value));
        }

        [Test] public void Throttle_LerpFactor_ClampedToOne()
        {
            var f = Throttle(response: 10f); f.Tick(1f, false, 0.6f, true); Assert.AreEqual(0.6f, f.Pose.NormalizedTime, 1e-6f);
        }

        [Test] public void Throttle_ClampsThrottleInput()
        {
            var f = Throttle(response: 10f); f.Tick(1f, false, 3f, true); Assert.AreEqual(1f, f.Pose.NormalizedTime);
        }

        [Test] public void Throttle_SnapsToOneNearTop()
        {
            var f = Throttle(response: 0.5f, saved: 0.99999f); f.Tick(0.0001f, false, 1f, true); Assert.AreEqual(1f, f.Value);
        }

        [Test] public void Throttle_SnapsToZeroNearBottom()
        {
            var f = Throttle(response: 0.5f, saved: 0.00001f); f.Tick(0.0001f, false, 0f, true); Assert.AreEqual(0f, f.Value);
        }

        [Test] public void Throttle_Weight_FollowsOperationalAt75()
        {
            var f = Throttle(response: 0.05f, weightOnOperational: true);
            f.Tick(0.02f, false, 1f, false); // weight 1 -> 0 by 0.05 * 75 * 0.02 = 0.075
            Assert.AreEqual(0.925f, f.Pose.Weight, 1e-5f);
        }

        [Test] public void Throttle_Weight_StaysOneWithoutWeightOnOperational()
        {
            var f = Throttle(); f.Tick(1f, false, 1f, false); Assert.AreEqual(1f, f.Pose.Weight);
        }

        [Test] public void Throttle_SaveLoad_RoundTrips()
        {
            var f = Throttle(); f.Tick(0.02f, false, 1f, true);
            var saved = new Dictionary<string, float>(); f.Save(saved);
            CollectionAssert.AreEquivalent(new[] { Key + ".s" }, saved.Keys);
            Assert.AreEqual(f.Value, Throttle(saved: saved[Key + ".s"]).Value);
        }

        [Test] public void Throttle_Pose_EnabledAtSpeedZero()
        {
            var p = Throttle(saved: 0.4f).Pose; Assert.AreEqual((true, 0f, 0.4f), (p.Enabled, p.Speed, p.NormalizedTime));
        }

        [Test] public void Throttle_Changed_OnlyWhenValueMoves()
        {
            var f = Throttle(); f.Tick(0.02f, false, 0f, true); Assert.IsFalse(f.Changed);
            f.Tick(0.02f, true, 1f, true); Assert.IsFalse(f.Changed, "the editor never stores animState");
            f.Tick(0.02f, false, 1f, true); Assert.IsTrue(f.Changed);
            f.Save(new Dictionary<string, float>()); Assert.IsFalse(f.Changed);
        }
    }
}
