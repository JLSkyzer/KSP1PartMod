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
            foreach (var type in EcsTypeRegistration.ModuleTypes)
                Assert.IsTrue(api.TryGetIndex(type, out var index) && index != "0", type.Name);

            EcsTypeRegistration.ResetForTests();
            info.Clear();
            Assert.IsTrue(EcsTypeRegistration.TryRegister("again", info.Add, errors.Add));
            Assert.AreEqual(2, info.FindAll(l => l.Contains("already registered")).Count, string.Join(" | ", info));
        }
    }
}
