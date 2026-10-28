using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.B9;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    public class B9ModuleEditTests
    {
        private const string Engine = @"
name = ModuleEnginesFX
engineID = Main
maxThrust = 100
heatProduction = 200
atmosphereCurve
{
	key = 0 310
	key = 1 280
}
PROPELLANT
{
	name = LiquidFuel
	ratio = 0.9
}
PROPELLANT
{
	name = Oxidizer
	ratio = 1.1
}
";

        private static IKsp1Node Data(string body) => CfgText.Parse("DATA\n{\n" + body + "\n}").GetNodes("DATA").Single();

        [Test] public void Edit_MaxThrust_Replaced_InPlace()
        {
            var module = CfgText.Module(Engine);
            var copy = B9ModuleEdit.EditedCopy(module, Data("maxThrust = 120"));
            Assert.AreEqual("120", copy.GetValue("maxThrust"));
            CollectionAssert.AreEqual(new[] { "name", "engineID", "maxThrust", "heatProduction" }, copy.AllValues.Select(v => v.Key));
            Assert.AreEqual("100", module.GetValue("maxThrust"), "the original is untouched");
            Assert.AreEqual("ModuleEnginesFX", copy.GetValue("name"));
            Assert.AreEqual("MODULE", copy.Name);
        }

        [Test] public void Edit_AtmosphereCurve_ReplacesTheCurve()
        {
            var copy = B9ModuleEdit.EditedCopy(CfgText.Module(Engine), Data("atmosphereCurve\n{\nkey = 0 340\nkey = 1 300\nkey = 6 0.001\n}"));
            var curves = copy.GetNodes("atmosphereCurve").ToList();
            Assert.AreEqual(1, curves.Count);
            CollectionAssert.AreEqual(new[] { "0 340", "1 300", "6 0.001" }, curves[0].GetValues("key"));
            CollectionAssert.AreEqual(new[] { "atmosphereCurve", "PROPELLANT", "PROPELLANT" }, copy.AllNodes.Select(n => n.Name), "order kept");
        }

        [Test] public void Edit_TwoPropellants_ReplaceTheList()
        {
            var copy = B9ModuleEdit.EditedCopy(CfgText.Module(Engine),
                Data("PROPELLANT\n{\nname = LqdHydrogen\nratio = 15\n}\nPROPELLANT\n{\nname = Oxidizer\nratio = 1\n}"));
            var props = copy.GetNodes("PROPELLANT").ToList();
            CollectionAssert.AreEqual(new[] { "LqdHydrogen", "Oxidizer" }, props.Select(p => p.GetValue("name")));
            CollectionAssert.AreEqual(new[] { "15", "1" }, props.Select(p => p.GetValue("ratio")));
            Assert.AreEqual(1, copy.GetNodes("atmosphereCurve").Count(), "other nodes kept");
        }

        [Test] public void Edit_UnknownKey_Added()
        {
            var copy = B9ModuleEdit.EditedCopy(CfgText.Module(Engine), Data("powerEffectName = rocket-big"));
            Assert.AreEqual("rocket-big", copy.GetValue("powerEffectName"));
            Assert.AreEqual("powerEffectName", copy.AllValues.Last().Key);
        }

        [Test] public void Edit_RepeatedDataKey_ReplacesAllOfThem()
        {
            var module = CfgText.Module("name = M\nkey = a\nother = 1\nkey = b");
            var copy = B9ModuleEdit.EditedCopy(module, Data("key = c\nkey = d\nkey = e"));
            CollectionAssert.AreEqual(new[] { "c", "d", "e" }, copy.GetValues("key"));
            CollectionAssert.AreEqual(new[] { "name", "key", "key", "key", "other" }, copy.AllValues.Select(v => v.Key));
        }

        [Test] public void Edit_NullData_PlainCopy()
        {
            var copy = B9ModuleEdit.EditedCopy(CfgText.Module(Engine), null);
            Assert.AreEqual("100", copy.GetValue("maxThrust"));
            Assert.AreEqual(2, copy.GetNodes("PROPELLANT").Count());
        }

        [Test] public void OffForms_FourKinds()
        {
            CollectionAssert.AreEquivalent(
                new[] { "gimbalRange", "gimbalRangeXP", "gimbalRangeXN", "gimbalRangeYP", "gimbalRangeYN" },
                B9OffForms.For("ModuleGimbal").Select(v => v.Key));
            Assert.IsTrue(B9OffForms.For("ModuleGimbal").All(v => v.Value == "0"));
            foreach (var rcs in new[] { "ModuleRCS", "ModuleRCSFX" })
                CollectionAssert.AreEqual(new[] { "thrusterPower=0" }, B9OffForms.For(rcs).Select(v => v.Key + "=" + v.Value));
            foreach (var d in new[] { "ModuleDecouple", "ModuleAnchoredDecoupler" })
                CollectionAssert.AreEqual(new[] { "staged=false" }, B9OffForms.For(d).Select(v => v.Key + "=" + v.Value));
            foreach (var e in new[] { "ModuleEngines", "moduleenginesfx" })
                CollectionAssert.AreEqual(new[] { "maxThrust=0" }, B9OffForms.For(e).Select(v => v.Key + "=" + v.Value));
        }

        [Test] public void OffForms_None_ForConverterAndOthers()
        {
            Assert.IsNull(B9OffForms.For("ModuleResourceConverter"));
            Assert.IsNull(B9OffForms.For("ModuleDeployableEngine"));
            Assert.IsNull(B9OffForms.For(null));
        }

        [Test] public void OffForms_AsData_EditsTheCopy()
        {
            var gimbal = CfgText.Module("name = ModuleGimbal\ngimbalRange = 5\ngimbalRangeXP = 4");
            var copy = B9ModuleEdit.EditedCopy(gimbal, B9OffForms.DataNode("ModuleGimbal"));
            Assert.AreEqual("0", copy.GetValue("gimbalRange"));
            Assert.AreEqual("0", copy.GetValue("gimbalRangeXP"));
            Assert.AreEqual("0", copy.GetValue("gimbalRangeYN"));
            Assert.IsNull(B9OffForms.DataNode("ModuleResourceConverter"));
        }
    }
}
