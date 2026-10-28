using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="AblatorMapping"/> and <see cref="NativeShieldRef"/>: KSP1 <c>ModuleAblator</c> → <c>Module_Heatshield</c>
    /// (plan (f)).</summary>
    public class AblatorMappingTests
    {
        /// <summary>A 2.5 m heat shield as the stock cfgs write it (values written for the test).</summary>
        private const string Shield25 = @"
PART
{
    name = testShield
    MODULE
    {
        name = ModuleAblator
        ablativeResource = Ablator
        lossExp = -7500
        lossConst = 0.1
        pyrolysisLossFactor = 6000
        reentryConductivity = 0.01
        ablationTempThresh = 500
        useChar = True
        charModuleName = shieldChar
    }
    RESOURCE
    {
        name = Ablator
        amount = 800
        maxAmount = 800
    }
}";

        /// <summary>A native 2.5 m class shield: Rate × PyrolysisLossFactor per unit of flux, capacity in KSP2 units.</summary>
        private static readonly NativeShieldRef Reference = NativeShieldRef.Median(new[] { (8e-8, 0.8) });

        private static IKsp1Node Module(string text) =>
            CfgText.Parse(text).GetNodes("PART").First().GetNodes("MODULE").First(m => m.GetValue("name") == AblatorMapping.Ksp1Module);

        private static IKsp1Node Bare(string body) => Module("PART\n{\nname = p\nMODULE\n{\nname = ModuleAblator\n" + body + "\n}\n}\n");

        [Test] public void Stock25_AgainstTheReference_RateIsTheNativeRate_WhateverTheCapacity()
        {
            var m = AblatorMapping.From(Module(Shield25), 0.8, Reference, 1, n => n == "Ablator" ? "Ablator" : "?");
            Assert.AreEqual("Ablator", m.ResourceName);
            Assert.AreEqual(8e-8, m.Rate, 1e-20, "stock pyrolysisLossFactor: the reference Rate");
            Assert.AreEqual(500, m.AblationTempThreshold);
            Assert.AreEqual(1, m.PyrolysisLossFactor);
            Assert.AreEqual(1f, m.ShieldingScale);
            Assert.AreEqual((0f, -1f, 0f), m.ShieldingDirection);
            Assert.IsFalse(m.DisabledWhenRetracted);
            Assert.IsFalse(m.UseChar);
            Assert.AreEqual("", m.CharMaterialName);
            CollectionAssert.IsEmpty(m.Notes, "lossExp, lossConst and reentryConductivity go to the summary only");
            CollectionAssert.IsEmpty(m.NotReproduced);

            // The flux already grows with the shield's area: the Rate is constant per shield, whatever its capacity.
            foreach (var capacity in new[] { 0.02, 0.05, 0.2, 1.8 })
            {
                var other = AblatorMapping.From(Module(Shield25), capacity, Reference, 1);
                Assert.AreEqual(8e-8, other.Rate, 1e-20, "capacity " + capacity);
                Assert.AreEqual(capacity, other.Capacity, "logged only");
            }
        }

        [Test] public void HighPyrolysisLossFactor_Burns6000Over145833AsFast()
        {
            var stock = AblatorMapping.From(Bare("pyrolysisLossFactor = 6000"), 0.05, Reference, 1);
            var skylab = AblatorMapping.From(Bare("pyrolysisLossFactor = 145833\nlossExp = -25000\nlossConst = 50"), 0.05, Reference, 1);
            Assert.AreEqual(6000.0 / 145833.0, skylab.Rate / stock.Rate, 1e-12);
            Assert.AreEqual(145833, skylab.Ksp1PyrolysisLossFactor);
        }

        [Test] public void MissingThreshold_500_CommentIgnored()
        {
            Assert.AreEqual(500, AblatorMapping.From(Bare("ablativeResource = Ablator"), 0.8, Reference, 1).AblationTempThreshold);
            var commented = AblatorMapping.From(Bare("ablationTempThresh = 1000 //500 raise it"), 0.8, Reference, 1);
            Assert.AreEqual(1000, commented.AblationTempThreshold, "the cfg value, before its comment");
            CollectionAssert.IsEmpty(commented.Notes);

            var bad = AblatorMapping.From(Bare("ablationTempThresh = hot\npyrolysisLossFactor = 0"), 0.8, Reference, 1);
            Assert.AreEqual(500, bad.AblationTempThreshold);
            Assert.AreEqual(6000, bad.Ksp1PyrolysisLossFactor);
            CollectionAssert.AreEqual(new[]
            {
                "ModuleAblator: ablationTempThresh 'hot' is not a temperature, 500 K kept",
                "ModuleAblator: pyrolysisLossFactor '0' is not above 0, 6000 kept",
            }, bad.Notes);
        }

        [Test] public void FallbackReference_WhenNoKsp2Shield()
        {
            Assert.AreSame(NativeShieldRef.Fallback, NativeShieldRef.Median(new (double, double)[0]));
            Assert.AreSame(NativeShieldRef.Fallback, NativeShieldRef.Median(null));
            Assert.AreSame(NativeShieldRef.Fallback, NativeShieldRef.Median(new[] { (0.0, 1.0), (1.0, 0.0), (double.NaN, 1.0) }),
                "invalid pairs are ignored");
            Assert.IsTrue(NativeShieldRef.Fallback.IsFallback);

            var m = AblatorMapping.From(Module(Shield25), 0.4, null, 1);
            Assert.AreSame(NativeShieldRef.Fallback, m.Reference);
            Assert.AreEqual(NativeShieldRef.FallbackRate, m.Rate, 1e-20, "no capacity ratio");
            StringAssert.Contains(", fallback,", m.ValuesNote());
        }

        [Test] public void AblatorRateScale2_DoublesTheRate_BadScaleIs1()
        {
            var one = AblatorMapping.From(Module(Shield25), 0.8, Reference, 1);
            Assert.AreEqual(2 * one.Rate, AblatorMapping.From(Module(Shield25), 0.8, Reference, 2).Rate, 1e-20);
            Assert.AreEqual(one.Rate, AblatorMapping.From(Module(Shield25), 0.8, Reference, 0).Rate, 1e-20);
            Assert.AreEqual(one.Rate, AblatorMapping.From(Module(Shield25), 0.8, Reference, double.NaN).Rate, 1e-20);
        }

        [Test] public void Resource_MappedThroughTheInjectedMapper_DefaultAblator()
        {
            Assert.AreEqual("Ablator", AblatorMapping.From(Bare(""), 1, Reference, 1).ResourceName);
            var m = AblatorMapping.From(Bare("ablativeResource = Ablator2"), 1, Reference, 1, n => n + "_ksp2");
            Assert.AreEqual("Ablator2", m.Ksp1Resource);
            Assert.AreEqual("Ablator2_ksp2", m.ResourceName);
            Assert.AreEqual("Ablator2", AblatorMapping.From(Bare("ablativeResource = Ablator2"), 1, Reference, 1, n => null).ResourceName);
        }

        [Test] public void FieldsWithoutCounterpart_Noted_UnknownAndNodesToo_RepeatsKeepTheFirst()
        {
            var m = AblatorMapping.From(Bare("outputResource = Ore\ncharMin = 0.2\nmystery = 1\nablationTempThresh = 700\nablationTempThresh = 900\nEXTRA\n{\n}"),
                0.8, Reference, 1);
            Assert.AreEqual(700, m.AblationTempThreshold);
            CollectionAssert.AreEqual(new[] { "outputResource", "charMin", "mystery", "node EXTRA" }, m.NotReproduced);
            CollectionAssert.AreEqual(new[]
            {
                "ModuleAblator: outputResource not reproduced",
                "ModuleAblator: charMin not reproduced",
                "ModuleAblator: mystery not reproduced",
                "ModuleAblator: ablationTempThresh repeated, the first value kept",
                "ModuleAblator: node EXTRA not reproduced",
            }, m.Notes);
        }

        [Test] public void ValuesNote_IsExact()
        {
            var m = AblatorMapping.From(Module(Shield25), 0.8, Reference, 1);
            Assert.AreEqual("ModuleAblator -> Module_Heatshield: Ablator capacity 0.8, Rate 8E-08 (reference 8E-08 for 0.8, " +
                            "pyrolysisLossFactor 6000, ablator_rate_scale 1), ablation from 500 K, shielding along local -Y", m.ValuesNote());
        }

        [Test] public void Median_PicksTheMiddleShieldByRate_LowerOfAnEvenCount()
        {
            var odd = NativeShieldRef.Median(new[] { (3.0, 1.0), (1.0, 1.0), (4.0, 2.0) }); // by Rate alone: 1, 3, 4
            Assert.AreEqual((3.0, 1.0), (odd.Rate, odd.Capacity));
            Assert.AreEqual(3, odd.Count);
            Assert.IsFalse(odd.IsFallback);

            var even = NativeShieldRef.Median(new[] { (3.0, 1.0), (1.0, 1.0), (4.0, 2.0), (8.0, 2.0) }); // 1, 3, 4, 8
            Assert.AreEqual((3.0, 1.0), (even.Rate, even.Capacity));
            CollectionAssert.AreEqual(new[] { (1.0, 1.0), (3.0, 1.0), (4.0, 2.0), (8.0, 2.0) }, even.Shields);
        }

        [Test] public void Lines_AreExact()
        {
            Assert.AreEqual("[KSP1PartMod] native: heat shield reference: 3 KSP2 shield(s), median Rate 8E-08, capacity 0.8",
                NativeLines.HeatshieldReference(NativeShieldRef.Median(new[] { (8e-8, 0.8), (1e-7, 0.8), (1e-8, 0.8) })));
            Assert.AreEqual("[KSP1PartMod] native: heat shield reference: no KSP2 shield found, fallback Rate 8E-08",
                NativeLines.HeatshieldReference(NativeShieldRef.Fallback));
            Assert.AreEqual("[KSP1PartMod] native: heat shield reference: (Rate x PLF, capacity) of each: (1E-08, 0.8), (8E-08, 0.8)",
                NativeLines.HeatshieldReferenceDetail(NativeShieldRef.Median(new[] { (8e-8, 0.8), (1e-8, 0.8) })));
            Assert.AreEqual("[KSP1PartMod] native: heat shields: 21 part(s) got Module_Heatshield (1 without the resource, skipped); " +
                            "not reproduced: lossExp/lossConst shape, reentryConductivity, charMin on 2, outputResource on 1",
                NativeLines.HeatshieldSummary(21, 1, new[] { ("charMin", 2), ("outputResource", 1) }));
            Assert.AreEqual("[KSP1PartMod] native: heat shields: 0 part(s) got Module_Heatshield (0 without the resource, skipped); " +
                            "not reproduced: lossExp/lossConst shape, reentryConductivity",
                NativeLines.HeatshieldSummary(0, 0, null));
            Assert.AreEqual("ModuleAblator: the part has no Ablator resource, no Module_Heatshield (nothing to burn)",
                AblatorMapping.NoResourceNote("Ablator"));
            Assert.AreEqual("[KSP1PartMod] native: import hook failed, no native module is added for the rest of this session: boom",
                NativeLines.PatchFailed("boom"));
        }

        [Test] public void Record_FlagsWithPrefix_CountsPartsOnce()
        {
            var r = new NativeRecord();
            r.Flag("a", "hs:nr:charMin");
            r.Flag("a", "hs:nr:charMin");
            r.Flag("b", "hs:nr:charMin");
            r.Flag("b", "hs:nr:outputResource");
            r.Flag("b", "other");
            CollectionAssert.AreEqual(new[] { ("charMin", 2), ("outputResource", 1) }, r.FlagsWithPrefix("hs:nr:"));
        }

        /// <summary>Every installed <c>ModuleAblator</c> maps. The capacity is the cfg's <c>maxAmount</c> × 0.001 (KSP1's Ablator
        /// density over 1 t per KSP2 unit: the conversion Redux applies, S on the KSP2 density) against the fallback.</summary>
        [Test] public void Installed_EveryModuleMaps()
        {
            int parts = 0, noResource = 0, highFactor = 0;
            var thresholds = new SortedDictionary<double, int>();
            var notes = new List<string>();
            foreach (var path in InstalledKsp1.AllCfg())
            {
                IKsp1Node root;
                try { root = CfgText.Parse(File.ReadAllText(path)); }
                catch (Exception) { continue; }
                foreach (var part in root.GetNodes("PART"))
                {
                    var ablators = part.GetNodes("MODULE").Where(m => m.GetValue("name")?.Trim() == AblatorMapping.Ksp1Module).ToList();
                    if (ablators.Count == 0) continue;
                    parts++;
                    var resource = part.GetNodes("RESOURCE").FirstOrDefault(r => r.GetValue("name")?.Trim() == "Ablator");
                    if (resource == null || !AblatorMapping.TryNumber(resource.GetValue("maxAmount"), out var max) || !(max > 0))
                    {
                        noResource++;
                        continue;
                    }
                    var m = AblatorMapping.From(ablators[0], max * 0.001, null, 1);
                    Assert.Greater(m.Rate, 0, part.GetValue("name"));
                    Assert.AreEqual("Ablator", m.ResourceName);
                    if (m.Ksp1PyrolysisLossFactor > 6000) highFactor++;
                    thresholds[m.AblationTempThreshold] = thresholds.TryGetValue(m.AblationTempThreshold, out var n) ? n + 1 : 1;
                    notes.AddRange(m.Notes.Select(x => part.GetValue("name") + ": " + x));
                }
            }
            TestContext.WriteLine($"{parts} part(s), {noResource} without Ablator, pyrolysisLossFactor above 6000 on {highFactor}, " +
                                  "thresholds " + string.Join(", ", thresholds.Select(kv => kv.Key + " K x" + kv.Value)));
            CollectionAssert.IsEmpty(notes);
            Assert.AreEqual(21, parts);
            Assert.AreEqual(0, noResource);
            Assert.AreEqual(1, highFactor);
            CollectionAssert.AreEqual(new Dictionary<double, int> { [500] = 17, [1000] = 3, [1500] = 1 }, thresholds);
        }
    }
}
