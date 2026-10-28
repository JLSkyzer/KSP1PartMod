using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Native;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.Native
{
    /// <summary><see cref="IsruLines"/>: the exact lines of Tasks 2 to 4 of the converters and drills plan.</summary>
    public class IsruLinesTests
    {
        private static IKsp1Node Module(string body) => CfgText.Module(body);

        [Test] public void OreLines()
        {
            Assert.AreEqual("[KSP1PartMod] native: ore: KSP1 Ore imported as CarbonRegolith (Redux's mapping: TestRocks, which has no resource map)",
                IsruLines.OreImported("CarbonRegolith"));
            Assert.AreEqual("[KSP1PartMod] native: ore: Rock is not a game resource, Ore stays TestRocks (KSP1 drills on Ore skipped)",
                IsruLines.OreNotAResource("Rock"));
            Assert.AreEqual("[KSP1PartMod] native: ore: Redux's resource table not found, Ore stays TestRocks (KSP1 drills on Ore skipped)",
                IsruLines.OreTableNotFound);
        }

        [Test] public void ConverterLines()
        {
            var formula = ConverterMapping.From(new[] { Module(
                "name = ModuleResourceConverter\nConverterName = Lf+Ox\nGeneratesHeat = true\n" +
                "INPUT_RESOURCE\n{\nResourceName = Ore\nRatio = 0.5\n}\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 30\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = LiquidFuel\nRatio = 0.45\n}\nOUTPUT_RESOURCE\n{\nResourceName = Oxidizer\nRatio = 0.55\n}") },
                0, null, IsruFakes.Table(), 10.5, 1).Formulas[0];
            Assert.AreEqual("[KSP1PartMod] native: ISRU: converter 'Lf+Ox' -> mode 0: in CarbonRegolith 0.025, ElectricCharge 30 " +
                            "out Methane 0.9, Oxidizer 0.55 (mass in 0.025 t/s, out 0.005 t/s), heat 315 kW",
                IsruLines.ConverterMode("ISRU", formula, 0));
            Assert.AreEqual("[KSP1PartMod] native: converters: 18 part(s) got Module_ResourceConverter with 24 mode(s) " +
                            "(2 part(s) with several KSP1 converters, one runs at a time); modes dropped: 1 (unknown resource); " +
                            "not reproduced: FlowMode 10, FillAmount 3",
                IsruLines.ConvertersSummary(18, 24, 2, 1, new[] { ("FlowMode", 10), ("FillAmount", 3) }));
            StringAssert.EndsWith("not reproduced: none", IsruLines.ConvertersSummary(0, 0, 0, 0, null));
            Assert.AreEqual("[KSP1PartMod] native: converter names: 24 term(s) added to Redux's part localization",
                IsruLines.ConverterNamesAdded(24));
            Assert.AreEqual("[KSP1PartMod] native: converter names not registered (Ksp1PartLocalizationBuilder.Add not found), raw keys shown",
                IsruLines.ConverterNamesNotRegistered("Ksp1PartLocalizationBuilder.Add"));
        }

        /// <summary>Task 1 review: a mode that moves KSP1 Ore states Redux's Ore unit mass against KSP1's.</summary>
        [Test] public void ConverterMode_WithOre_StatesTheMassRatio()
        {
            var f = ConverterMapping.From(new[] { Module(
                "name = ModuleResourceConverter\nConverterName = Lf\nINPUT_RESOURCE\n{\nResourceName = Ore\nRatio = 1\n}\n" +
                "OUTPUT_RESOURCE\n{\nResourceName = LiquidFuel\nRatio = 1\n}") }, 0, null, IsruFakes.Table(), 10.5, 1).Formulas[0];
            Assert.AreEqual("[KSP1PartMod] native: p: converter 'Lf' -> mode 0: in CarbonRegolith 0.05 out Methane 2 " +
                            "(mass in 0.05 t/s, out 0.005 t/s), heat 0 kW; Ore weighs 0.05 t per KSP1 unit in Redux, 0.01 t in KSP1: Ore mass x5",
                IsruLines.ConverterMode("p", f, 0, 0.05));
            StringAssert.EndsWith("heat 0 kW", IsruLines.ConverterMode("p", f, 0, null), "unknown: no suffix");
        }

        [Test] public void ConverterMode_EmptyFlows_None_UnknownMass_QuestionMark()
        {
            var f = ConverterMapping.From(new[] { Module(
                "name = ModuleResourceConverter\nConverterName = X\nOUTPUT_RESOURCE\n{\nResourceName = Mystery\nRatio = 1\n}") },
                0, null, IsruFakes.Table(exists: n => true), 10.5, 1).Formulas[0];
            Assert.AreEqual("[KSP1PartMod] native: p: converter 'X' -> mode 1: in none out Mystery 1 (mass in 0 t/s, out ? t/s), heat 0 kW",
                IsruLines.ConverterMode("p", f, 1));
        }

        [Test] public void ReferenceLines_Native()
        {
            var r = NativeIsruRef.From(NativeIsruRefTests.NativeParts());
            var lines = IsruLines.IsruReference(r, name => 1);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("[KSP1PartMod] native: isru reference: drill drill_2v_radial 20 EC/s, 17.5 kW per EC/s, " +
                            "Water 0.155988 t/h, Ammonia 0.222984 t/h, CarbonRegolith 0.200016 t/h, Uranium 0.036 t/h; " +
                            "converters 3 mode(s), median 10.5 kW per EC/s; " +
                            "scanner scanner_1v_orbital 14 EC/s, Water, CarbonRegolith, Ammonia, Uranium, altitude 0 m to 5000 km, inclination 80° to 100°",
                lines[0]);
        }

        [Test] public void ReferenceLines_Fallback()
        {
            var lines = IsruLines.IsruReference(NativeIsruRef.Fallback, null);
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: isru reference: no KSP2 drill found, fallback drill_2v_radial 20 EC/s, 17.5 kW per EC/s, " +
                "Water 4.333E-05 units/s, Ammonia 6.194E-05 units/s, CarbonRegolith 5.556E-05 units/s, Uranium 1E-05 units/s",
                "[KSP1PartMod] native: isru reference: no KSP2 converter found, fallback median 10.5 kW per EC/s",
                "[KSP1PartMod] native: isru reference: no KSP2 scanner found, fallback scanner_1v_orbital 14 EC/s, " +
                "Water, CarbonRegolith, Ammonia, Uranium, altitude 0 m to 5000 km, inclination 80° to 100°",
            }, lines);
        }

        [Test] public void MinableLines()
        {
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] native: minable resources: Water, Uranium (from resource maps); Water map on 2 body/bodies: Duna, Kerbin",
                "[KSP1PartMod] native: minable resources: Water, Uranium (from resource maps); Uranium map on 1 body/bodies: Pol",
            }, IsruLines.Minable(new[] { "Water", "Uranium" }, MinableSource.ResourceMaps,
                new[] { ("Water", (IReadOnlyList<string>)new[] { "Duna", "Kerbin" }), ("Uranium", (IReadOnlyList<string>)new[] { "Pol" }) }));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] native: minable resources: Water, Ammonia (from the KSP2 drill)" },
                IsruLines.Minable(new[] { "Water", "Ammonia" }, MinableSource.NativeDrill, null));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] native: minable resources: Water (from fallback)" },
                IsruLines.Minable(new[] { "Water" }, MinableSource.Fallback, null));
        }

        [Test] public void DrillLines()
        {
            var m = HarvesterMapping.From(new[] { Module(
                "name = ModuleResourceHarvester\nHarvesterType = 0\nEfficiency = 1.5\nResourceName = Ore\nImpactTransform = ImpactTransform\n" +
                "ImpactRange = 5.42\nGeneratesHeat = true\nINPUT_RESOURCE\n{\nResourceName = ElectricCharge\nRatio = 15\n}") },
                IsruFakes.Table(), 0.05, 17.5, 1);
            var native = NativeIsruRef.From(NativeIsruRefTests.NativeParts()).Drill;
            Assert.AreEqual("[KSP1PartMod] native: RadialDrill: drill: Ore -> CarbonRegolith at 0.00375 units/s at density 1 " +
                            "(Efficiency 1.5 x abundance 0.05), EC 15/s, heat 262.5 kW, impact 'ImpactTransform' 5.42 m; " +
                            "KSP2 drill: 5.556E-05 units/s for 20 EC/s",
                IsruLines.Drill("RadialDrill", m, m.Outputs[0], native));
            Assert.AreEqual("[KSP1PartMod] native: drills: 2 part(s) got Module_Mine (CarbonRegolith); skipped: 3 ocean, 4 atmospheric, " +
                            "0 exospheric, 1 resource without a map; ModuleAsteroidDrill/ModuleCometDrill ignored on 2 part(s) " +
                            "(no asteroids or comets in KSP2); not reproduced: ThermalEfficiency 2",
                IsruLines.DrillsSummary(2, new[] { "CarbonRegolith" }, 3, 4, 0, 1, 2, new[] { ("ThermalEfficiency", 2) }));
        }

        [Test] public void ScannerLines()
        {
            var survey = ScannerMapping.Surveyor(Module("name = ModuleOrbitalSurveyor\nScanTime = 5"), NativeScannerRef.Fallback);
            Assert.AreEqual("[KSP1PartMod] native: SurveyScanner: scanner: surveyor -> Module_ResourceScanner: " +
                            "Water, CarbonRegolith, Ammonia, Uranium, 14 EC/s, 5 s, altitude 0 m to 5000 km, inclination 80° to 100°",
                IsruLines.Scanner("SurveyScanner", survey));
            var surface = ScannerMapping.Resource(Module("name = ModuleResourceScanner\nResourceName = Ore\nMaxAbundanceAltitude = 1000\nRequiresUnlock = false"),
                IsruFakes.Table(), NativeScannerRef.Fallback);
            Assert.AreEqual("[KSP1PartMod] native: SurfaceScanner: scanner: resource scanner -> Module_ResourceScanner: " +
                            "CarbonRegolith, 0 EC/s, 5 s, altitude 0 m to 1 km, inclination -180° to 180°",
                IsruLines.Scanner("SurfaceScanner", surface));
            Assert.AreEqual("[KSP1PartMod] native: scanners: 1 survey scanner(s) and 1 resource scanner(s) got Module_ResourceScanner; " +
                            "19 ModuleResourceScanner left out (RequiresUnlock: KSP1 reads local abundance after a survey, KSP2 has no part readout), " +
                            "0 with no scannable resource",
                IsruLines.ScannersSummary(1, 1, 19, 0));
        }

        [Test] public void Altitudes()
        {
            Assert.AreEqual("any", IsruLines.Altitude(-1, -1));
            Assert.AreEqual("no bound to 500 m", IsruLines.Altitude(-1, 500));
            Assert.AreEqual("25 km to no bound", IsruLines.Altitude(25000, -1));
        }

        [Test] public void RuntimeLines()
        {
            Assert.AreEqual("[KSP1PartMod] isru: armed: impact transforms on, deploy bridge off, active clips on", IsruLines.Armed(true, false, true));
            Assert.AreEqual("[KSP1PartMod] isru: Module_Mine.vfxArmTopTransform not found, impact transforms off (Redux's module left as is)",
                IsruLines.MemberNotFound("Module_Mine.vfxArmTopTransform", "impact transforms"));
            Assert.AreEqual("[KSP1PartMod] isru: RadialDrill: drill probe 'ImpactTransform' found, 5.42 m along +Z",
                IsruLines.DrillProbeFound("RadialDrill", "ImpactTransform", 5.42));
            Assert.AreEqual("[KSP1PartMod] isru: RadialDrill: drill probe 'ImpactTransform' not found on the part, the drill will report TooHigh",
                IsruLines.DrillProbeNotFound("RadialDrill", "ImpactTransform"));
            Assert.AreEqual("[KSP1PartMod] isru: RadialDrill: Module_Mine deploy follows MODULE[4].group",
                IsruLines.DeployFollows("RadialDrill", "Module_Mine", "MODULE[4].group"));
            Assert.AreEqual("[KSP1PartMod] isru: SurfaceScanner: Module_ResourceScanner always extended (no deploy clip)",
                IsruLines.AlwaysExtended("SurfaceScanner", "Module_ResourceScanner", ExtendedReason.NoDeployClip));
            Assert.AreEqual("[KSP1PartMod] isru: p: Module_Mine always extended (animations off)",
                IsruLines.AlwaysExtended("p", "Module_Mine", ExtendedReason.AnimationsOff));
            Assert.AreEqual("[KSP1PartMod] isru: p: Module_Mine always extended (entry gated by B9)",
                IsruLines.AlwaysExtended("p", "Module_Mine", ExtendedReason.GatedByB9));
            Assert.AreEqual("[KSP1PartMod] isru: RadialDrill: drilling on Duna: CarbonRegolith 0.002 units/s (density 0.5), ground at 3.1 m, EC 15/s",
                IsruLines.Drilling("RadialDrill", "Duna", "CarbonRegolith", 0.002, 0.5, 3.1, 15));
            Assert.AreEqual("[KSP1PartMod] anim: ISRU group 'MODULE[5].group' active clip follows Module_ResourceConverter running",
                IsruLines.ActiveClipFollows("ISRU", "MODULE[5].group", "Module_ResourceConverter"));
            Assert.AreEqual("[KSP1PartMod] isru: patch failed, stopped for this session: boom", IsruLines.PatchFailed("boom"));
        }
    }
}
