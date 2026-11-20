using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Game.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the editor's Unity.Entities (same package version as the game's, 6.6.0): the reflected
    /// members exist and both module types get an index.</summary>
    public class EcsTypeRegistrationTests
    {
        [SetUp] public void SetUp() => EcsTypeRegistration.ResetForTests();
        [TearDown] public void TearDown() => EcsTypeRegistration.ResetForTests();

        private sealed class PlainClass { }

        [Test] public void IsEcsComponent_FollowsIComponentData()
        {
            // Against the 26w42c game the editor references, PartComponentModule still implements IComponentData;
            // in 26w46a it does not, and TryRegister then skips the type instead of failing.
            Assert.That(EcsTypeRegistration.IsEcsComponent(typeof(PartComponentModule_Ksp1Animations)),
                Is.EqualTo(typeof(KSP.Sim.impl.PartComponentModule).GetInterfaces().Any(i => i.FullName == "Unity.Entities.IComponentData")));
            Assert.That(EcsTypeRegistration.IsEcsComponent(typeof(PlainClass)), Is.False);
        }

        [Test] public void Find_ResolvesEveryMember()
        {
            Assert.IsNotNull(EcsTypeRegistration.TypeManagerApi.Find(out var missing), missing);
        }

        [Test] public void TryRegister_GivesBothTypesAnIndex_AndIsIdempotent()
        {
            // By reflection: referencing Unity.Entities would run its code generation on this test assembly.
            AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Unity.Entities")
                .GetType("Unity.Entities.TypeManager").GetMethod("Initialize", Type.EmptyTypes).Invoke(null, null);
            var info = new List<string>();
            var errors = new List<string>();
            Assert.IsTrue(EcsTypeRegistration.TryRegister("test", info.Add, errors.Add), string.Join(" | ", errors));
            CollectionAssert.IsEmpty(errors);
            var api = EcsTypeRegistration.TypeManagerApi.Find(out _);
            // Since Redux 26w46a the flight module type is no longer an ECS component and is skipped, not registered.
            var registered = EcsTypeRegistration.ModuleTypes
                .Where(t => typeof(UnityEngine.Object).IsAssignableFrom(t) || EcsTypeRegistration.IsEcsComponent(t)).ToArray();
            Assert.IsNotEmpty(registered);
            foreach (var type in registered)
                Assert.IsTrue(api.TryGetIndex(type, out var index) && index != "0", type.Name);

            EcsTypeRegistration.ResetForTests();
            info.Clear();
            Assert.IsTrue(EcsTypeRegistration.TryRegister("again", info.Add, errors.Add));
            Assert.AreEqual(registered.Length, info.FindAll(l => l.Contains("already registered")).Count, string.Join(" | ", info));
        }
    }
}
