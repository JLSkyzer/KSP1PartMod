using System;
using KSP1PartMod.B9;
using NUnit.Framework;

namespace KSP1PartMod.Tests.B9
{
    /// <summary>The exact <c>[KSP1PartMod] b9:</c> lines of Tasks 2 to 4 of the B9 overrides plan.</summary>
    public class B9LinesTests
    {
        private const string P = "[KSP1PartMod] b9: ";

        [Test] public void Lines_Task2()
        {
            Assert.AreEqual(P + "armed: mass on, resources off, module data on, module off off, part scalars on, new part switches on",
                B9Lines.Armed(true, false, true, false, true, true));
            Assert.AreEqual(P + "Ksp1B9PartSwitchVariantBuilder.TryBuild not found, B9 overrides off (Redux's variants left as is)",
                B9Lines.MemberNotFound("Ksp1B9PartSwitchVariantBuilder.TryBuild"));
            Assert.AreEqual(P + "bdb.agena: fuelSwitch -> 3 variant(s): mass 0..0.0625 t, resources LiquidFuel, Oxidizer, scalars none",
                B9Lines.SetSummary("bdb.agena", "fuelSwitch", 3, 0, 0.0625, new[] { "LiquidFuel", "Oxidizer" }, new string[0]));
            Assert.AreEqual(P + "p: s -> 2 variant(s): mass -0.5..1.25 t, resources none, scalars maxTemp, CoMOffset",
                B9Lines.SetSummary("p", "s", 2, -0.5, 1.25, null, new[] { "maxTemp", "CoMOffset" }));
            Assert.AreEqual(P + "p: new set 'B9Switch0' for B9 #0 (2 subtypes, default 'Light')",
                B9Lines.NewSet("p", "B9Switch0", "B9 #0", 2, "Light"));
            Assert.AreEqual(P + "p: second tank switch 'mono' not applied (one tank switch per part)",
                B9Lines.PartNote("p", B9Lines.Notes.SecondTankSwitch("mono")));
            Assert.AreEqual(P + "p: transformer MassModifier dropped: boom", B9Lines.TransformerDropped("p", "MassModifier", "boom"));
            Assert.AreEqual(P + "p: failed, Redux's variants kept: System.Exception: x",
                B9Lines.PartFailed("p", new Exception("x").ToString()));
            Assert.AreEqual(P + "stopped for this session after 3 failures", B9Lines.Stopped);
            Assert.AreEqual(P + "10 part(s), 12 set(s) (4 new, 2 on parts that had no part switch): mass on 20 variant(s), resources on 8, scalars on 1; cost computed, not applied, on 15 variant(s) (no KSP2 system reads part cost)",
                B9Lines.ImportSummary(10, 12, 4, 2, 20, 8, 1, 15));
        }

        [Test] public void Lines_Notes()
        {
            Assert.AreEqual("volume of 'fuel' takes 'mount' at its default subtype 'Short' (the choice of 'mount' is not followed)",
                B9Lines.Notes.ChildVolumeAtDefault("mount", "fuel", "Short"));
            Assert.AreEqual("volume of 'fuel' cannot be read, no tank override", B9Lines.Notes.UnparseableVolume("fuel"));
            Assert.AreEqual("percentFilled 1 read as 1 % (B9's wiki: a percentage; Redux's own tank catalog reads values up to 1 as a fraction)",
                B9Lines.Notes.PercentFilledSmall(1));
            Assert.AreEqual("unknown tank type 'x', no tank resources", B9Lines.Notes.UnknownTankType("x"));
            Assert.AreEqual("tank type 'LFO' has no tankMass, dry mass 0", B9Lines.Notes.TankWithoutMass("LFO"));
            Assert.AreEqual("upgradeRequired ignored (no upgrade gate)", B9Lines.Notes.UpgradeRequiredIgnored);
            Assert.AreEqual("node/TRANSFORM not applied", B9Lines.Notes.NodeTransformNotApplied);
            Assert.AreEqual("subtype 'X': addedMass '#$m$' cannot be read, ignored", B9Lines.Notes.Unparseable("X", "addedMass", "#$m$"));
            Assert.AreEqual("B9 'e' subtype 'Sea': IDENTIFIER ModuleGimbal matches 0 module(s), ignored",
                B9Lines.Notes.IdentifierMatches("e", "Sea", "ModuleGimbal", 0));
            Assert.AreEqual("resource 'Ore' of subtype 'X' has no unitsPerVolume or maxAmount, left out",
                B9Lines.Notes.ResourceWithoutSize("X", "Ore"));
        }

        [Test] public void Lines_Task3()
        {
            Assert.AreEqual(P + "p: engineSwitch/Vac: ModuleEnginesFX -> Module_Engine.engineModes (1 field(s))",
                B9Lines.ModuleEdit("p", "engineSwitch", "Vac", "ModuleEnginesFX", "Module_Engine", new[] { "engineModes" }));
            Assert.AreEqual(P + "p: s/v: ModuleGimbal -> Module_Gimbal.gimbalRange, Module_Gimbal.gimbalRangeXP (2 field(s))",
                B9Lines.ModuleEdit("p", "s", "v", "ModuleGimbal", "Module_Gimbal", new[] { "gimbalRange", "gimbalRangeXP" }));
            Assert.AreEqual(P + "p: ModuleGimbal off in Vac -> Module_Gimbal.gimbalRange, Module_Gimbal.gimbalRangeXP",
                B9Lines.ModuleOff("p", "ModuleGimbal", "Vac", "Module_Gimbal", new[] { "gimbalRange", "gimbalRangeXP" }));
            Assert.AreEqual(P + "p: ModuleResourceConverter off in Sea: no native off form, left on",
                B9Lines.ModuleOffNone("p", "ModuleResourceConverter", "Sea"));
            Assert.AreEqual(P + "p: ModuleSimpleAdjustableFairing: no native effect (segmentLength, CONE)",
                B9Lines.NoNativeEffect("p", "ModuleSimpleAdjustableFairing", new[] { "segmentLength", "CONE" }));
            Assert.AreEqual(P + "p: Module_Engine appears 2 times, edits skipped (V-SwiFT edits the first only)",
                B9Lines.BehaviourRepeated("p", "Module_Engine", 2));
            Assert.AreEqual(P + "p: Module_Engine.engineModes edited by two sets, kept 'a'",
                B9Lines.FieldConflict("p", "Module_Engine", "engineModes", "a"));
            Assert.AreEqual(P + "module types: Module_Engine resolves true", B9Lines.ModuleTypeResolves("Module_Engine", true));
            Assert.AreEqual(P + "module types: Module_X resolves false", B9Lines.ModuleTypeResolves("Module_X", false));
            Assert.AreEqual(P + "module data: 5 transformer(s) on 3 part(s) (Module_Engine.engineModes 4, Module_Gimbal.gimbalRange 1); off forms 2, no off form 1 (ModuleResourceConverter); no native effect 6 (ModuleSimpleAdjustableFairing, ModuleConformalDecal)",
                B9Lines.ModuleDataSummary(5, 3, new[] { ("Module_Engine.engineModes", 4), ("Module_Gimbal.gimbalRange", 1) }, 2, 1,
                    new[] { "ModuleResourceConverter" }, 6, new[] { "ModuleSimpleAdjustableFairing", "ModuleConformalDecal" }));
            Assert.AreEqual(P + "module data: 0 transformer(s) on 0 part(s) (none); off forms 0, no off form 0 (none); no native effect 0 (none)",
                B9Lines.ModuleDataSummary(0, 0, null, 0, 0, null, 0, null));
        }

        [Test] public void Lines_Task4()
        {
            Assert.AreEqual("audit: B9 overrides: 7 part(s) with overrides, 0 part(s) where the default variant failed to apply",
                B9Lines.AuditSummary(7, 0));
        }

        [Test] public void Lines_Numbers_Invariant()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                StringAssert.Contains("mass 0.125..1.5 t", B9Lines.SetSummary("p", "s", 2, 0.125, 1.5, null, null));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
        }
    }
}
