using System;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class RuntimeKindsTests
    {
        [Test] public void HasBinder_EveryKind_AfterTask8()
        {
            var all = (EntryKind[])Enum.GetValues(typeof(EntryKind));
            CollectionAssert.AreEqual(all, all.Where(RuntimeKinds.HasBinder));
            Assert.IsTrue(RuntimeKinds.HasBinder(EntryKind.LookAt));
            Assert.IsTrue(RuntimeKinds.HasBinder(EntryKind.RcsGlow));
            Assert.IsTrue(RuntimeKinds.HasBinder(EntryKind.RcsEmissive));
            Assert.IsTrue(RuntimeKinds.HasBinder(EntryKind.MeshHider));
            Assert.IsFalse(RuntimeKinds.HasBinder((EntryKind)999), "a kind of a later version");
        }

        [Test] public void OncePerLoad_StringKeys_OncePerKeyAndGeneration()
        {
            var once = new OncePerLoad();
            Assert.IsTrue(once.First("partA|enginedeploy:n:0|Flight", 1));
            Assert.IsFalse(once.First("partA|enginedeploy:n:0|Flight", 1));
            Assert.IsTrue(once.First("partA|enginedeploy:n:0|OAB", 1), "another scene logs again");
            Assert.IsTrue(once.First(EntryKind.EngineDeploy, 1), "kinds and keys are apart");
            Assert.IsTrue(once.First("partA|enginedeploy:n:0|Flight", 2), "a new load logs again");
            Assert.IsTrue(once.First(EntryKind.EngineDeploy, 2), "a key of the new load cleared the kinds of the old one");
        }

        [Test] public void OncePerLoad_OncePerKindAndGeneration()
        {
            var once = new OncePerLoad();
            Assert.IsTrue(once.First(EntryKind.EngineDeploy, 1));
            Assert.IsFalse(once.First(EntryKind.EngineDeploy, 1));
            Assert.IsTrue(once.First(EntryKind.LookAt, 1));
            Assert.IsTrue(once.First(EntryKind.EngineDeploy, 2), "a new load logs again");
            Assert.IsFalse(once.First(EntryKind.EngineDeploy, 2));
        }
    }
}
