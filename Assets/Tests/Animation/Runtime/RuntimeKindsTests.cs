using System;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class RuntimeKindsTests
    {
        [Test] public void HasBinder_KindsOf040_Only()
        {
            var all = (EntryKind[])Enum.GetValues(typeof(EntryKind));
            CollectionAssert.AreEqual(all.Where(k => k <= EntryKind.ChuteFollower), all.Where(RuntimeKinds.HasBinder));
            Assert.IsFalse(RuntimeKinds.HasBinder(EntryKind.EngineDeploy));
            Assert.IsFalse(RuntimeKinds.HasBinder(EntryKind.LandingBag));
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
