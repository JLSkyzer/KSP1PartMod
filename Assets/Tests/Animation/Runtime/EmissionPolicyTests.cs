using KSP1PartMod.Animation.Runtime;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Runtime
{
    public class EmissionPolicyTests
    {
        private const string Scenery = "KSP2/Scenery/Standard (Opaque)";

        /// <summary>By default a Scenery material whose keyword is declared and on, with its map set: nothing to do.</summary>
        private static MaterialFacts Facts(string shader = Scenery, bool hasEmissionColor = true, bool keywordDeclared = true,
            bool keywordOn = true, bool hasMap = true, bool mapSet = true) =>
            new MaterialFacts(shader, hasEmissionColor, keywordDeclared, keywordOn, hasMap, mapSet);

        [Test] public void Emission_KeywordDeclaredOff_CopiesAndEnables()
        {
            var d = EmissionPolicy.Decide(Facts(keywordDeclared: true, keywordOn: false), "_EmissionColor", true);
            Assert.AreEqual((true, true), (d.CopyMaterial, d.EnableKeyword));
        }

        [Test] public void Emission_KeywordDeclaredOn_NoCopy()
        {
            var d = EmissionPolicy.Decide(Facts(), "_EmissionColor", true);
            Assert.AreEqual((false, false, false), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
            Assert.AreEqual("keyword _EMISSION declared on, _EmissionMap set -> no action", d.Action);
        }

        [Test] public void Emission_MapUnset_WhiteMap() =>
            Assert.IsTrue(EmissionPolicy.Decide(Facts(hasMap: true, mapSet: false), "_EmissionColor", true).WhiteMap);

        [Test] public void Emission_NoMapProperty_NoWhiteMap() =>
            Assert.IsFalse(EmissionPolicy.Decide(Facts(hasMap: false, mapSet: false), "_EmissionColor", true).WhiteMap);

        [Test] public void Emission_KeywordOffAndMapUnset_BothInAction()
        {
            var d = EmissionPolicy.Decide(Facts(keywordOn: false, mapSet: false), "_EmissionColor", true);
            Assert.AreEqual((true, true, true), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
            Assert.AreEqual("keyword _EMISSION declared off, _EmissionMap none -> own material copy with keyword on, white _EmissionMap", d.Action);
        }

        [Test] public void Emission_SetupOff_NoAction()
        {
            var d = EmissionPolicy.Decide(Facts(keywordDeclared: true, keywordOn: false), "_EmissionColor", false);
            Assert.AreEqual((false, false, false), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
            StringAssert.EndsWith("-> no action (emission_setup false)", d.Action);
        }

        [Test] public void Emission_PaintableShaderWithEmissiveColor_NoCopy() =>
            Assert.IsFalse(EmissionPolicy.Decide(Facts(shader: "KSP2/Parts/Paintable", hasEmissionColor: false, keywordOn: false), "_EmissiveColor", true).CopyMaterial);

        [Test] public void Emission_OtherProperty_NoAction()
        {
            var d = EmissionPolicy.Decide(Facts(keywordOn: false, mapSet: false), "_BurnColor", true);
            Assert.AreEqual((false, false, false), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
        }

        [Test] public void Emission_SceneryStandard_KeywordAbsent_ActionSaysAbsent() =>
            StringAssert.Contains("keyword _EMISSION absent",
                EmissionPolicy.Decide(Facts(shader: "KSP2/Scenery/Standard (Opaque)", keywordDeclared: false), "_EmissionColor", true).Action);

        [Test] public void Emission_KeywordAbsent_NoCopyButMapStillWhite()
        {
            var d = EmissionPolicy.Decide(Facts(keywordDeclared: false, keywordOn: false, mapSet: false), "_EmissionColor", true);
            Assert.AreEqual((false, false, true), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
            Assert.AreEqual("keyword _EMISSION absent off, _EmissionMap none -> keyword left alone (absent), white _EmissionMap", d.Action);
        }

        [Test] public void Emission_ShaderWithoutEmissionColor_NoAction()
        {
            var d = EmissionPolicy.Decide(Facts(hasEmissionColor: false, keywordOn: false, mapSet: false), "_EmissionColor", true);
            Assert.AreEqual((false, false, false), (d.CopyMaterial, d.EnableKeyword, d.WhiteMap));
        }

        [Test] public void Emission_MapPropertyAbsent_ActionSaysAbsent() =>
            StringAssert.Contains("_EmissionMap absent", EmissionPolicy.Decide(Facts(hasMap: false, mapSet: false), "_EmissionColor", true).Action);
    }
}
