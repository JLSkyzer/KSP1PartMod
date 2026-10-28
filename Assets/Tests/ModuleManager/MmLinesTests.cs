using System.Collections.Generic;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ModuleManager
{
    public class MmLinesTests
    {
        [Test] public void Armed() =>
            Assert.AreEqual("[KSP1PartMod] mm: armed: plugin safety net on, arithmetic on, index selectors off, first-match edits off, BEFORE/AFTER need their mod on",
                MmLines.Armed(true, true, false, false, true));

        [Test] public void MemberMissing() =>
            Assert.AreEqual("[KSP1PartMod] mm: Ksp1ConfigFinalizer.ModIsAvailable not found, plugin safety net off (Redux's patcher left as is)",
                MmLines.MemberMissing("Ksp1ConfigFinalizer.ModIsAvailable", "plugin safety net"));

        [Test] public void BlockedNames() =>
            Assert.AreEqual("[KSP1PartMod] mm: blocked plugin names: RealFuels, Kopernicus (curated 1, automatic 1; kept: B9PartSwitch, Waterfall)",
                MmLines.BlockedNames(new[] { "RealFuels", "Kopernicus" }, 1, 1, new[] { "B9PartSwitch", "Waterfall" }));

        [Test] public void BlockedNames_Empty() =>
            Assert.AreEqual("[KSP1PartMod] mm: blocked plugin names: none (curated 0, automatic 0; kept: none)",
                MmLines.BlockedNames(new string[0], 0, 0, new string[0]));

        [Test] public void BlockedName() =>
            Assert.AreEqual("[KSP1PartMod] mm: RealFuels: 149 NEEDS lookup(s) answered false (X false, !X true), 12 patch(es) dropped (3 FOR, 4 BEFORE/AFTER, 5 from its folder)",
                MmLines.BlockedName("RealFuels", 149, 3, 4, 5));

        [Test] public void BeforeAfterAbsent() =>
            Assert.AreEqual("[KSP1PartMod] mm: BEFORE/AFTER of an absent mod: 101 patch(es) dropped: RemoteTech 77, FerramAerospaceResearch 24",
                MmLines.BeforeAfterAbsent(new[]
                {
                    new KeyValuePair<string, int>("RemoteTech", 77),
                    new KeyValuePair<string, int>("FerramAerospaceResearch", 24),
                }));

        [Test] public void BeforeAfterAbsent_None() =>
            Assert.AreEqual("[KSP1PartMod] mm: BEFORE/AFTER of an absent mod: 0 patch(es) dropped: none",
                MmLines.BeforeAfterAbsent(new KeyValuePair<string, int>[0]));

        [Test] public void Arithmetic()
        {
            var c = new MmCounters
            {
                ArithmeticMul = 1, ArithmeticAdd = 2, ArithmeticSub = 3, ArithmeticDiv = 4,
                NonNumeric = 5, MissingKey = 6, DivideByZero = 7, NotSupportedPow = 8, NotSupportedRegex = 9,
            };
            Assert.AreEqual("[KSP1PartMod] mm: arithmetic: 10 applied (*= 1, += 2, -= 3, /= 4), skipped: 5 non-numeric, 6 missing key, 7 divide by zero; not supported, left out as before: 8 !=, 9 ^=",
                MmLines.Arithmetic(c));
        }

        [Test] public void Index()
        {
            var c = new MmCounters { IndexValueEdits = 3, IndexNodeEdits = 2, IndexOutOfRange = 1 };
            Assert.AreEqual("[KSP1PartMod] mm: index: 3 value edit(s), 2 node edit(s), 1 out of range", MmLines.Index(c));
        }

        [Test] public void UnindexedMultiMatch() =>
            Assert.AreEqual("[KSP1PartMod] mm: unindexed edits that matched more than one value/node: 42 (first-match edits off; " +
                "counted only in the patches KSP1PartMod applies, those with an operator or an index)",
                MmLines.UnindexedMultiMatch(42, false));

        [Test] public void Variables() =>
            Assert.AreEqual("[KSP1PartMod] mm: 7 value(s) with #$ variables left as text (not supported yet)", MmLines.Variables(7));

        [Test] public void VectorComponentsSkipped() =>
            Assert.AreEqual("[KSP1PartMod] mm: 12 vector component edit(s) (key[i] op= x) skipped, not supported (Redux would write the operand over every value of the key)",
                MmLines.VectorComponentsSkipped(12));

        [Test] public void PatchFailed() =>
            Assert.AreEqual("[KSP1PartMod] mm: patch failed, stopped for this session: boom", MmLines.PatchFailed("boom"));
    }
}
