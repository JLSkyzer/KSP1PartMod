using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class RebindRuleTests
    {
        [TestCase(VariantEffect.None, RebindScope.None)]
        [TestCase(VariantEffect.MaterialSwap, RebindScope.Renderers)]
        [TestCase(VariantEffect.TransformToggle, RebindScope.None)]
        [TestCase(VariantEffect.MeshSwap, RebindScope.Full)]
        [TestCase(VariantEffect.ObjectRebuild, RebindScope.Full)]
        [TestCase(VariantEffect.MaterialSwap | VariantEffect.MeshSwap, RebindScope.Full)]
        [TestCase(VariantEffect.MaterialSwap | VariantEffect.TransformToggle, RebindScope.Renderers)]
        public void RebindRule_PerVariantEffect(VariantEffect e, RebindScope s) => Assert.AreEqual(s, RebindRule.For(e));

        /// <summary>Redux 26w42c's KSP1 variants: texture switch and transform toggle, so a pick rebinds the renderers.</summary>
        [Test] public void RebindRule_KnownEffects_Renderers()
        {
            Assert.AreEqual(VariantEffect.MaterialSwap | VariantEffect.TransformToggle, RebindRule.KnownEffects);
            Assert.AreEqual(RebindScope.Renderers, RebindRule.For(RebindRule.KnownEffects));
        }

        /// <summary>The pure side of the poll: the same value seen again changes nothing.</summary>
        [Test] public void Gate_VariantReferenceUnchanged_NoRecompute()
        {
            var s = new GateState(new[] { new B9Gate { SetId = "s", InactiveIn = new[] { "x" }, DefaultSubtype = "a", SubtypeIds = new[] { "a", "x" } } });
            s.Set(0, "x");
            Assert.IsFalse(s.Set(0, "x"));
            Assert.IsFalse(s.Set(0, new string(new[] { 'x' })), "a new string with the same value");
        }
    }
}
