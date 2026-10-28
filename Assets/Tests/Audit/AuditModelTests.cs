using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KSP1PartMod.Audit;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Audit
{
    public class AuditModelTests
    {
        // ---- Classifier ----
        private static AuditObservations Obs() => new AuditObservations { IsFinalized = true };

        [Test] public void Classifier_Clean_IsNone() => Assert.AreEqual(AuditKind.None, AuditClassifier.Kinds(Obs()));

        [Test] public void Classifier_ExceptionWithFinalized_GivesThrewOnly()
        {
            var o = Obs(); o.Threw = true;
            Assert.AreEqual(AuditKind.Threw, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_FairingCase_GivesThreeKinds()
        {
            var o = Obs(); o.Threw = true; o.IsFinalized = false; o.PlaceholderPresent = true;
            Assert.AreEqual(AuditKind.Threw | AuditKind.NotFinalized | AuditKind.PlaceholderLeft, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_CountsAndModules()
        {
            var o = Obs();
            o.UninitializedModules = new[] { "Module_Command" };
            o.ImportWarnings = 1; o.ImportErrors = 1; o.LoggedErrors = 2; o.Materialless = 1; o.ErrorShader = 3;
            Assert.AreEqual(AuditKind.ModuleNotInitialized | AuditKind.ImportWarning | AuditKind.ImportError
                            | AuditKind.LoggedError | AuditKind.MaterialLess | AuditKind.ErrorShader, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_NoPrefab_DoesNotAlsoSayNotFinalized()
        {
            var o = new AuditObservations { NoPrefab = true, Threw = true };
            Assert.AreEqual(AuditKind.NoPrefab | AuditKind.Threw, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_TemplateFailed()
        {
            var o = new AuditObservations { TemplateFailed = true };
            Assert.AreEqual(AuditKind.TemplateFailed, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_CrashedPreviousRun_IsAlone()
        {
            var o = new AuditObservations { CrashedPreviousRun = true };
            Assert.AreEqual(AuditKind.CrashedPreviousRun, AuditClassifier.Kinds(o));
        }

        [Test] public void Classifier_IsProblem_IgnoresImportWarningOnly()
        {
            Assert.IsFalse(AuditClassifier.IsProblem(AuditKind.None));
            Assert.IsFalse(AuditClassifier.IsProblem(AuditKind.ImportWarning));
            Assert.IsTrue(AuditClassifier.IsProblem(AuditKind.ImportWarning | AuditKind.LoggedError));
            Assert.IsTrue(AuditClassifier.IsProblem(AuditKind.Threw));
        }

        // ---- StackModuleLocator ----
        private static StackFrameInfo F(string type, bool module, string method) => new StackFrameInfo(type, module, method);

        [Test] public void Locator_FairingUnderInit_GivesFairing()
        {
            var frames = new[]
            {
                F("Module_Fairing", true, "OnInitialize"),
                F("PartBehaviourModule", true, "Init"),
                F("ObjectAssemblyPart", false, "FinalizeModules"),
                F("ObjectAssemblyPart", false, "FinalizeLoad"),
            };
            Assert.AreEqual("Module_Fairing", StackModuleLocator.FailingModule(frames));
        }

        [Test] public void Locator_ThrowInFinalizeLoad_GivesNull()
        {
            var frames = new[] { F("ObjectAssemblyPart", false, "FinalizeLoad"), F("AuditSpawner", false, "Spawn") };
            Assert.IsNull(StackModuleLocator.FailingModule(frames));
            Assert.IsNull(StackModuleLocator.FailingModule(new StackFrameInfo[0]));
            Assert.IsNull(StackModuleLocator.FailingModule(null));
        }

        [Test] public void Locator_HarmonyPatchFrame_AttributedToModule()
        {
            Assert.AreEqual("Module_Fairing", StackModuleLocator.FailingModule(new[] { F("Module_Fairing_Patch1", true, "OnInitialize") }));
            Assert.AreEqual("Module_Fairing", StackModuleLocator.FailingModule(new[] { F("DMD<Module_Fairing>", true, "OnInitialize") }));
            Assert.AreEqual("Module_Fairing", StackModuleLocator.FailingModule(new[] { F("DMD<Module_Fairing::OnInitialize>", true, "x") }));
            Assert.AreEqual("Module_Fairing", StackModuleLocator.FailingModule(new[] { F("Module_Fairing", true, "OnInitialize_Patch1") }));
        }

        // ---- Csv ----
        private static AuditRecord Rec(string part, string mod = "Squad", AuditKind kinds = AuditKind.None, double prefab = 1, double fin = 2) =>
            new AuditRecord { Part = part, Mod = mod, Kinds = kinds, PrefabMs = prefab, FinalizeMs = fin, Available = true };

        /// <summary>Minimal RFC 4180 reader.</summary>
        private static List<List<string>> ReadCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        [Test] public void Csv_HeaderMatchesLineWidth()
        {
            var rows = ReadCsv(AuditCsv.Document(new[] { Rec("a") }));
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(AuditCsv.Columns.Count, rows[0].Count);
            Assert.AreEqual(rows[0].Count, rows[1].Count);
            Assert.AreEqual("part", rows[0][0]);
        }

        [Test] public void Csv_QuotesCommaQuoteNewline_AndRoundTrips()
        {
            var r = Rec("we,ird \"name\"\nline2");
            r.ExceptionMessage = "a, b \"c\"\nd";
            var rows = ReadCsv(AuditCsv.Document(new[] { r }));
            var cols = AuditCsv.Columns.ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("we,ird \"name\"\nline2", rows[1][cols.IndexOf("part")]);
            Assert.AreEqual("a, b \"c\"\nd", rows[1][cols.IndexOf("exception_message")]);
        }

        [Test] public void Csv_PlainCell_IsNotQuoted()
        {
            StringAssert.StartsWith("plain,Squad,", AuditCsv.Line(Rec("plain")));
        }

        [Test] public void Csv_StackJoinedWithArrow()
        {
            var r = Rec("a"); r.Stack = new[] { "A.f", "B.g", "C.h" };
            var rows = ReadCsv(AuditCsv.Document(new[] { r }));
            Assert.AreEqual("A.f <- B.g <- C.h", rows[1][AuditCsv.Columns.ToList().IndexOf("stack")]);
        }

        [Test] public void Csv_CellIsCappedWithMarker()
        {
            var r = Rec("a"); r.ExceptionMessage = new string('x', 5000);
            var rows = ReadCsv(AuditCsv.Document(new[] { r }));
            var cell = rows[1][AuditCsv.Columns.ToList().IndexOf("exception_message")];
            Assert.LessOrEqual(cell.Length, 2000);
            int kept = cell.IndexOf('…');
            Assert.Greater(kept, 1900);
            Assert.AreEqual("…(+" + (5000 - kept) + ")", cell.Substring(kept));
        }

        [Test] public void Csv_ShortCell_IsKept()
        {
            var r = Rec("a"); r.ExceptionMessage = new string('x', 2000);
            var rows = ReadCsv(AuditCsv.Document(new[] { r }));
            Assert.AreEqual(2000, rows[1][AuditCsv.Columns.ToList().IndexOf("exception_message")].Length);
        }

        [Test] public void Csv_InvariantCulture_AndKindNames()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var r = Rec("a", kinds: AuditKind.Threw | AuditKind.NotFinalized, prefab: 1.5, fin: 2.25);
                var rows = ReadCsv(AuditCsv.Document(new[] { r }));
                var cols = AuditCsv.Columns.ToList();
                Assert.AreEqual("1.5", rows[1][cols.IndexOf("prefab_ms")]);
                Assert.AreEqual("2.25", rows[1][cols.IndexOf("finalize_ms")]);
                Assert.AreEqual("threw|not_finalized", rows[1][cols.IndexOf("kinds")]);
                Assert.AreEqual("true", rows[1][cols.IndexOf("available")]);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        // ---- Summary ----
        private static List<AuditRecord> Fixture()
        {
            var fairing = Rec("fairingEngine", "Squad", AuditKind.Threw | AuditKind.NotFinalized | AuditKind.PlaceholderLeft, 5, 20);
            fairing.ExceptionType = "NullReferenceException"; fairing.FailingModule = "Module_Fairing";
            fairing.ExceptionMessage = "Object reference not set\nsecond line";
            fairing.UninitializedModules = new[] { "Module_Fairing", "Module_Color" };
            var ok = Rec("ok1", "Squad", AuditKind.None, 1, 1);
            var warn = Rec("warn1", "BDB", AuditKind.ImportWarning, 2, 2); warn.ImportWarnings = 2;
            var dragon = Rec("crewDragon", "SpaceX", AuditKind.None, 3, 30);
            dragon.Crew = 7; dragon.HasCommand = true; dragon.CommandInitialized = true; dragon.Engines = 8;
            var pod = Rec("podBad", "Squad", AuditKind.ModuleNotInitialized, 1, 4);
            pod.Crew = 1; pod.HasCommand = true; pod.Engines = 1;
            pod.UninitializedModules = new[] { "Module_Command", "Module_Color" };
            var log = Rec("logErr", "BDB", AuditKind.LoggedError | AuditKind.MaterialLess, 1, 1); log.LoggedErrors = 1;
            var c1 = Rec("Control_a", "KSP2", AuditKind.None, 9, 9); c1.Control = true;
            var c2 = Rec("Control_b", "KSP2", AuditKind.Threw, 1, 1); c2.Control = true; c2.ExceptionType = "X"; c2.FailingModule = "Module_Engine";
            return new List<AuditRecord> { fairing, ok, warn, dragon, pod, log, c1, c2 };
        }

        [Test] public void Summary_Counts()
        {
            var s = AuditSummary.From(Fixture(), 12.4);
            Assert.AreEqual(6, s.Total);
            Assert.AreEqual(2, s.Ok);
            Assert.AreEqual(1, s.OkWithWarnings);
            Assert.AreEqual(3, s.WithProblem);
            Assert.AreEqual("audit: done in 12 s: 6 part(s), 2 ok, 1 ok with import warnings only, 3 with a problem; report r.csv", s.DoneLine("r.csv"));
        }

        [Test] public void Summary_ByKindLine()
        {
            Assert.AreEqual("audit: by kind: threw 1, not_finalized 1, placeholder_left 1, module_not_initialized 1, import_warning 1, logged_error 1, material_less 1",
                AuditSummary.From(Fixture(), 0).ByKindLine());
        }

        [Test] public void Summary_ByModLine_SortedByProblemsThenName()
        {
            Assert.AreEqual("audit: by mod: Squad 2/3, BDB 1/2, SpaceX 0/1", AuditSummary.From(Fixture(), 0).ByModLine());
        }

        [Test] public void Summary_ByModuleLine()
        {
            Assert.AreEqual("audit: by module: Module_Color 2, Module_Command 1, Module_Fairing 1", AuditSummary.From(Fixture(), 0).ByModuleLine());
        }

        [Test] public void Summary_Slowest_Top10_Descending()
        {
            var records = Enumerable.Range(0, 12).Select(i => Rec("p" + i, prefab: i, fin: 0)).ToList();
            var s = AuditSummary.From(records, 0);
            Assert.AreEqual(10, s.Slowest.Count);
            Assert.AreEqual("p11", s.Slowest[0].Part);
            Assert.AreEqual("p2", s.Slowest[9].Part);
            StringAssert.StartsWith("audit: slowest: p11 11 ms, p10 10 ms, ", s.SlowestLine());
        }

        [Test] public void Summary_Pods()
        {
            var s = AuditSummary.From(Fixture(), 0);
            Assert.AreEqual(2, s.PodsWithCommand);
            Assert.AreEqual(2, s.PodsWithEngine.Count);
            Assert.AreEqual(1, s.PodsCommandInitialized);
            Assert.AreEqual("audit: pods: 2 command part(s) with crew, 2 with an engine (crewDragon, podBad), 1 of those with Module_Command initialized", s.PodsLine());
        }

        [Test] public void Summary_PodNames_CappedAtTen()
        {
            var records = Enumerable.Range(0, 12).Select(i =>
            { var r = Rec("pod" + i); r.Crew = 1; r.HasCommand = true; r.Engines = 1; return r; }).ToList();
            var line = AuditSummary.From(records, 0).PodsLine();
            StringAssert.Contains("12 with an engine (pod0, pod1, pod10, pod11, pod2, pod3, pod4, pod5, pod6, pod7)", line);
            StringAssert.DoesNotContain("pod8", line);
        }

        [Test] public void Summary_Controls()
        {
            var s = AuditSummary.From(Fixture(), 0);
            Assert.AreEqual(2, s.Controls.Count);
            Assert.AreEqual(1, s.FailedControls.Count);
            Assert.AreEqual("audit: control part Control_b failed (threw): the audit harness is suspect for this kind", AuditSummary.ControlFailedLine(s.FailedControls[0]));
        }

        [Test] public void Summary_PartLine()
        {
            var f = Fixture();
            Assert.AreEqual("audit: fairingEngine (Squad): threw|not_finalized|placeholder_left: NullReferenceException in Module_Fairing: Object reference not set",
                AuditSummary.PartLine(f[0]));
            var noEx = Rec("p", "M", AuditKind.LoggedError);
            Assert.AreEqual("audit: p (M): logged_error", AuditSummary.PartLine(noEx));
            var inFin = Rec("q", "M", AuditKind.Threw); inFin.ExceptionType = "E"; inFin.ExceptionMessage = new string('m', 300);
            Assert.AreEqual("audit: q (M): threw: E in FinalizeLoad: " + new string('m', 200), AuditSummary.PartLine(inFin));
        }

        [Test] public void Summary_ProgressLine()
        {
            Assert.AreEqual("audit: 100/250 part(s), 4 with a problem, 33 s elapsed", AuditSummary.ProgressLine(100, 250, 4, 33.2));
        }

        [Test] public void Summary_Markdown_HasSections()
        {
            var md = AuditSummary.From(Fixture(), 12).Markdown("20261028-120000");
            StringAssert.Contains("# KSP1PartMod audit 20261028-120000", md);
            StringAssert.Contains("fairingEngine", md);
            StringAssert.Contains("Module_Fairing", md);
            StringAssert.Contains("| Squad | 2 | 3 |", md);
            StringAssert.Contains("Control_b", md);
            StringAssert.Contains("crewDragon", md);
        }

        // ---- Pacer ----
        [Test] public void Pacer_Continue_UntilBudget_AlwaysForFirst()
        {
            var p = new AuditPacer(8, 100, 1000);
            Assert.IsTrue(p.ShouldContinue(0, 0));
            Assert.IsTrue(p.ShouldContinue(50, 0));
            Assert.IsTrue(p.ShouldContinue(7.9, 3));
            Assert.IsFalse(p.ShouldContinue(8, 3));
            Assert.IsFalse(p.ShouldContinue(20, 1));
        }

        [Test] public void Pacer_Unload_EveryN()
        {
            var p = new AuditPacer(8, 100, 1000);
            Assert.IsFalse(p.ShouldUnload(0));
            Assert.IsFalse(p.ShouldUnload(99));
            Assert.IsTrue(p.ShouldUnload(100));
            Assert.IsTrue(p.ShouldUnload(300));
            Assert.IsFalse(p.ShouldUnload(301));
        }

        [Test] public void Pacer_Progress_Every100_AndLast()
        {
            var p = new AuditPacer(8, 100, 250);
            Assert.IsFalse(p.ShouldLogProgress(0));
            Assert.IsFalse(p.ShouldLogProgress(99));
            Assert.IsTrue(p.ShouldLogProgress(100));
            Assert.IsTrue(p.ShouldLogProgress(200));
            Assert.IsFalse(p.ShouldLogProgress(249));
            Assert.IsTrue(p.ShouldLogProgress(250));
        }

        [Test] public void Pacer_RefusesZeroUnload()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuditPacer(8, 0, 10));
        }

        // ---- Resume ----
        [Test] public void Resume_Pending_ControlsFirst_Ordinal_ExcludesReportedAndSkipped()
        {
            var all = new[]
            {
                new AuditTarget("b", false), new AuditTarget("Z", false), new AuditTarget("a", false),
                new AuditTarget("ctl2", true), new AuditTarget("ctl1", true), new AuditTarget("a", false),
                new AuditTarget("done", false), new AuditTarget("crashy", false),
            };
            var pending = AuditResume.Pending(all, new[] { "done" }, new[] { "crashy" });
            CollectionAssert.AreEqual(new[] { "ctl1", "ctl2", "Z", "a", "b" }, pending.Select(t => t.Name));
            Assert.IsTrue(pending[0].IsControl);
            Assert.IsFalse(pending[2].IsControl);
        }

        [Test] public void Resume_Pending_NullsAreEmpty()
        {
            Assert.AreEqual(0, AuditResume.Pending(null, null, null).Count);
        }

        [Test] public void Resume_Breadcrumb()
        {
            Assert.AreEqual("fairingEngine", AuditResume.FromBreadcrumb("fairingEngine\r\n"));
            Assert.AreEqual("p", AuditResume.FromBreadcrumb("  p  "));
            Assert.IsNull(AuditResume.FromBreadcrumb(""));
            Assert.IsNull(AuditResume.FromBreadcrumb("  \r\n "));
            Assert.IsNull(AuditResume.FromBreadcrumb(null));
        }

        // ---- Task 1 review follow-ups ----
        [Test] public void Csv_CellWithCarriageReturn_IsQuotedAndRoundTrips()
        {
            var r = Rec("a"); r.ExceptionMessage = "one\rtwo";
            StringAssert.Contains("\"one\rtwo\"", AuditCsv.Line(r));
            var rows = ReadCsv(AuditCsv.Document(new[] { r }));
            Assert.AreEqual("one\rtwo", rows[1][AuditCsv.Columns.ToList().IndexOf("exception_message")]);
        }

        [Test] public void Csv_Cap_NeverSplitsASurrogatePair()
        {
            // 1989 plain characters, then emoji pairs: index 1989 is a high surrogate.
            var text = new string('x', 1989) + string.Concat(Enumerable.Repeat("\U0001F680", 100));
            var capped = AuditCsv.Cap(text);
            var marker = capped.IndexOf('…');
            Assert.AreEqual(1989, marker);
            Assert.IsFalse(char.IsHighSurrogate(capped[marker - 1]));
            Assert.AreEqual("…(+" + (text.Length - 1989) + ")", capped.Substring(marker));
            var even = AuditCsv.Cap(new string('x', 1990) + string.Concat(Enumerable.Repeat("\U0001F680", 100)));
            Assert.AreEqual(1990, even.IndexOf('…'));
        }

        [Test] public void Classifier_NoPrefab_AlsoHidesPlaceholderAndModules()
        {
            var o = new AuditObservations { NoPrefab = true, PlaceholderPresent = true, UninitializedModules = new[] { "Module_X" } };
            Assert.AreEqual(AuditKind.NoPrefab, AuditClassifier.Kinds(o));
            var t = new AuditObservations { TemplateFailed = true, PlaceholderPresent = true, UninitializedModules = new[] { "Module_X" } };
            Assert.AreEqual(AuditKind.TemplateFailed, AuditClassifier.Kinds(t));
        }

        [Test] public void Summary_CrashedPreviousRun_CountsAsAProblem()
        {
            var crashed = Rec("crasher", "BDB", AuditKind.CrashedPreviousRun, 0, 0);
            var s = AuditSummary.From(new[] { crashed, Rec("ok", "BDB") }, 1);
            Assert.AreEqual(1, s.WithProblem);
            Assert.AreEqual("audit: by kind: crashed_previous_run 1", s.ByKindLine());
            Assert.AreEqual("audit: by mod: BDB 1/2", s.ByModLine());
            Assert.AreEqual("audit: crasher (BDB): crashed_previous_run", AuditSummary.PartLine(crashed));
        }

        [Test] public void Summary_SkipNote_OnlyWhenAPathIsGiven()
        {
            var s = AuditSummary.From(Fixture(), 12);
            StringAssert.DoesNotContain("delete it", s.DoneLine("r.csv"));
            StringAssert.DoesNotContain("delete it", s.Markdown("x"));
            var note = "skipped parts are listed in D:/a/skip.txt: delete it to retry these parts";
            Assert.AreEqual(note, AuditSummary.SkipNote("D:/a/skip.txt"));
            StringAssert.EndsWith("; " + note, s.DoneLine("r.csv", "D:/a/skip.txt"));
            StringAssert.Contains(note + ".\n", s.Markdown("x", "D:/a/skip.txt"));
        }

        [Test] public void Summary_DoneLine_OneDecimalForShortRuns()
        {
            StringAssert.StartsWith("audit: done in 0.4 s:", AuditSummary.From(new AuditRecord[0], 0.4).DoneLine("r"));
            StringAssert.StartsWith("audit: done in 9.9 s:", AuditSummary.From(new AuditRecord[0], 9.94).DoneLine("r"));
            StringAssert.StartsWith("audit: done in 10 s:", AuditSummary.From(new AuditRecord[0], 10.2).DoneLine("r"));
            Assert.AreEqual("audit: 1/2 part(s), 0 with a problem, 0.3 s elapsed", AuditSummary.ProgressLine(1, 2, 0, 0.3));
        }

        [Test] public void Summary_PartLine_NamesTheStepForNoPrefabAndTemplate()
        {
            var noPrefab = Rec("np", "M", AuditKind.NoPrefab | AuditKind.Threw); noPrefab.ExceptionType = "E"; noPrefab.ExceptionMessage = "boom";
            Assert.AreEqual("audit: np (M): threw|no_prefab: E in the prefab build: boom", AuditSummary.PartLine(noPrefab));
            var template = Rec("tp", "M", AuditKind.TemplateFailed | AuditKind.Threw); template.ExceptionType = "E";
            Assert.AreEqual("audit: tp (M): threw|template_failed: E in the template step", AuditSummary.PartLine(template));
        }

        [Test] public void Summary_PodsLine_NoParenthesesWithoutEngines()
        {
            var pod = Rec("pod"); pod.Crew = 1; pod.HasCommand = true; pod.CommandInitialized = true;
            Assert.AreEqual("audit: pods: 1 command part(s) with crew, 0 with an engine, 0 of those with Module_Command initialized",
                AuditSummary.From(new[] { pod }, 0).PodsLine());
        }

        [Test] public void Locator_SkipsThePartBehaviourModuleBaseFrame()
        {
            var frames = new[]
            {
                F("SomeHelper", false, "Do"),
                F("KSP.Sim.Definitions.PartBehaviourModule", true, "Init"),
                F("ObjectAssemblyPart", false, "FinalizeModules"),
            };
            Assert.IsNull(StackModuleLocator.FailingModule(frames));
            Assert.AreEqual("Module_X", StackModuleLocator.FailingModule(new[] { F("PartBehaviourModule", true, "Init"), F("Module_X", true, "OnInitialize") }));
        }

        [Test] public void Classifier_HarnessFault_IsAlone()
        {
            var o = new AuditObservations { HarnessFault = true, Threw = true, NoPrefab = true };
            Assert.AreEqual(AuditKind.HarnessFault, AuditClassifier.Kinds(o));
            Assert.AreEqual("harness_fault", AuditKinds.Name(AuditKind.HarnessFault));
            Assert.AreEqual(AuditKind.HarnessFault, AuditKinds.All[AuditKinds.All.Count - 1]);
            Assert.AreEqual(1 << 12, (int)AuditKind.HarnessFault);
        }

        // ---- Texture eviction ----
        [Test] public void Eviction_KeysAddedSinceStart_IgnoreCase()
        {
            var evict = AuditTextureEviction.KeysToEvict(new[] { "A.png", "b.png" }, new[] { "a.PNG", "B.png", "c.png", "D.png", "d.png" });
            CollectionAssert.AreEqual(new[] { "c.png", "D.png" }, evict);
        }

        [Test] public void Eviction_NullsAndNothingNew()
        {
            Assert.AreEqual(0, AuditTextureEviction.KeysToEvict(null, null).Count);
            Assert.AreEqual(0, AuditTextureEviction.KeysToEvict(new[] { "a" }, new[] { "a" }).Count);
            CollectionAssert.AreEqual(new[] { "x" }, AuditTextureEviction.KeysToEvict(null, new[] { "x" }));
        }

        // ---- engine shrouds (0.7.0) ----

        [Test] public void Csv_ReduxFairing_IsTheLastColumn()
        {
            var cols = AuditCsv.Columns.ToList();
            Assert.AreEqual("native_modules", cols[cols.Count - 2], "then b9_overrides (0.9.0)");
            var r = Rec("a"); r.ReduxFairing = true;
            var rows = ReadCsv(AuditCsv.Document(new[] { r, Rec("b") }));
            Assert.AreEqual("true", rows[1][cols.IndexOf("redux_fairing")]);
            Assert.AreEqual("false", rows[2][cols.IndexOf("redux_fairing")]);
            Assert.AreEqual(cols.IndexOf("mod_fixes") + 1, cols.IndexOf("redux_fairing"), "no older column moves");
            Assert.AreEqual(cols.IndexOf("redux_fairing") + 1, cols.IndexOf("native_modules"), "appended after the 0.7.0 columns");
        }

        [Test] public void Summary_ShroudsLine_CountsNotesAndFairings()
        {
            var kept = Rec("kept"); kept.ModFixes = new[] { "shroud_kept" };
            var partial = Rec("partial"); partial.ModFixes = new[] { "fairing", "shroud_kept", "shroud_missing" };
            var lost = Rec("lost"); lost.ModFixes = new[] { "shroud_missing", "shroud_no_node" };
            var legacy = Rec("legacy"); legacy.ReduxFairing = true;
            var control = Rec("ctrl"); control.Control = true; control.ReduxFairing = true; control.ModFixes = new[] { "shroud_kept" };
            var s = AuditSummary.From(new[] { kept, partial, lost, legacy, control, Rec("plain") }, 1);
            Assert.AreEqual("audit: shrouds: 2 part(s) with a KSP1 shroud kept, 2 with a shroud transform missing, 1 without the node, " +
                            "1 still carrying Redux's Module_Fairing", s.ShroudsLine());
            StringAssert.Contains("## Shrouds\n\n" + s.ShroudsLine() + "\n", s.Markdown("x"));
        }

        // ---- native modules (0.8.0) ----

        [Test] public void Tag_MarksMissingAndUninitializedModules()
        {
            var tagged = AuditNative.Tag(new[] { "Module_Heatshield", "Module_ToggleCrossfeed", "Module_Other" },
                new[] { "Module_Heatshield", "Module_ToggleCrossfeed" }, new[] { "Module_ToggleCrossfeed" });
            CollectionAssert.AreEqual(new[] { "Module_Heatshield", "Module_ToggleCrossfeed:uninitialized", "Module_Other:missing" }, tagged);
            Assert.IsFalse(AuditNative.IsFailed("Module_Heatshield"));
            Assert.IsTrue(AuditNative.IsFailed("Module_ToggleCrossfeed:uninitialized"));
            Assert.IsTrue(AuditNative.IsFailed("Module_Other:missing"));
            Assert.AreEqual(0, AuditNative.Tag(null, null, null).Count);
        }

        [Test] public void Csv_NativeModules_AreJoinedWithSemicolons_AndEmptyByDefault()
        {
            var cols = AuditCsv.Columns.ToList();
            var r = Rec("a"); r.NativeModules = new[] { "Module_Heatshield", "Module_ToggleCrossfeed:missing" };
            var rows = ReadCsv(AuditCsv.Document(new[] { r, Rec("b") }));
            Assert.AreEqual("Module_Heatshield;Module_ToggleCrossfeed:missing", rows[1][cols.IndexOf("native_modules")]);
            Assert.AreEqual("", rows[2][cols.IndexOf("native_modules")]);
            Assert.AreEqual(cols.Count, rows[1].Count);
        }

        [Test] public void Summary_NativeLine_CountsRunningModulesAndFailedParts_ControlsApart()
        {
            var shield = Rec("shield"); shield.NativeModules = new[] { "Module_Heatshield" };
            var both = Rec("both"); both.NativeModules = new[] { "Module_Heatshield", "Module_ToggleCrossfeed" };
            var bad = Rec("bad"); bad.NativeModules = new[] { "Module_ToggleCrossfeed:uninitialized" };
            var gone = Rec("gone"); gone.NativeModules = new[] { "Module_Heatshield:missing", "Module_ToggleCrossfeed:missing" };
            var control = Rec("ctrl"); control.Control = true; control.NativeModules = new[] { "Module_Heatshield" };
            var s = AuditSummary.From(new[] { shield, both, bad, gone, control, Rec("plain") }, 1);
            Assert.AreEqual("audit: native modules: 2 heat shield(s), 1 crossfeed toggle(s), 2 part(s) where one failed to initialize", s.NativeLine());
            StringAssert.Contains("## Native modules\n\n" + s.NativeLine() + "\n", s.Markdown("x"));
        }

        [Test] public void Summary_NativeLine_Empty()
        {
            Assert.AreEqual("audit: native modules: 0 heat shield(s), 0 crossfeed toggle(s), 0 part(s) where one failed to initialize",
                AuditSummary.From(new AuditRecord[0], 0).NativeLine());
        }

        [Test] public void Markdown_ModuleManagerLines_AreInTheHeader_OnlyWhenGiven()
        {
            var s = AuditSummary.From(new[] { Rec("a") }, 1);
            StringAssert.DoesNotContain("ModuleManager", s.Markdown("x"));
            var md = s.Markdown("x", null, new[] { "[KSP1PartMod] mm: arithmetic: 475 applied", "", "[KSP1PartMod] mm: index: 350 applied" });
            StringAssert.Contains("## ModuleManager (this session)\n\n- [KSP1PartMod] mm: arithmetic: 475 applied\n- [KSP1PartMod] mm: index: 350 applied\n\n## By kind", md);
        }

        // ---- B9 overrides (0.9.0) ----

        [Test] public void Csv_B9Overrides_IsTheLastColumn_JoinedWithSemicolons()
        {
            var cols = AuditCsv.Columns.ToList();
            Assert.AreEqual("b9_overrides", cols[cols.Count - 1]);
            Assert.AreEqual(cols.IndexOf("native_modules") + 1, cols.IndexOf("b9_overrides"), "no older column moves");
            var r = Rec("a");
            r.B9Overrides = AuditB9.Items(new[] { AuditB9.Set("tank", "LFO", new[] { AuditB9.Mass(0.125), AuditB9.Resources(new[] { "Methalox" }) }) },
                AuditB9.Counts(2, 2, 0, 1, 0, 3), null);
            var rows = ReadCsv(AuditCsv.Document(new[] { r, Rec("b") }));
            Assert.AreEqual("tank=LFO: mass+0.125, res Methalox;counts: mass 2, tank 2, scalars 0, module 1, off 0, notes 3",
                rows[1][cols.IndexOf("b9_overrides")]);
            Assert.AreEqual("", rows[2][cols.IndexOf("b9_overrides")]);
            Assert.AreEqual(cols.Count, rows[1].Count);
        }

        [Test] public void B9_Kinds_Format()
        {
            Assert.AreEqual("mass+0.125", AuditB9.Mass(0.125));
            Assert.AreEqual("mass-0.05", AuditB9.Mass(-0.05));
            Assert.AreEqual("mass+0", AuditB9.Mass(0));
            Assert.AreEqual("res Methalox+Ablator", AuditB9.Resources(new[] { "Methalox", "Ablator", "Methalox", null }));
            Assert.AreEqual("res none", AuditB9.Resources(null));
            Assert.AreEqual("scalar maxTemp", AuditB9.Scalar("maxTemp"));
            Assert.AreEqual("Module_Engine.engineModes", AuditB9.Module("Module_Engine", "engineModes"));
            Assert.AreEqual("s=v: none", AuditB9.Set("s", "v", new string[0]));
            Assert.AreEqual("s=none: mass+1", AuditB9.Set("s", null, new[] { "mass+1" }));
            Assert.AreEqual("failed: Module_PartSwitch missing", AuditB9.Failed("Module_PartSwitch missing"));
        }

        [Test] public void B9_Kinds_InvariantCulture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                Assert.AreEqual("mass+0.125", AuditB9.Mass(0.125));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
        }

        [Test] public void B9_Failure_OnlyForASpawnedPartWithOverrides()
        {
            var present = new[] { "Module_PartSwitch", "Module_Engine" };
            Assert.IsNull(AuditB9.Failure(false, true, new string[0], null, null, null), "no override set: not judged");
            Assert.IsNull(AuditB9.Failure(true, false, null, null, null, null), "not spawned: not judged");
            Assert.IsNull(AuditB9.Failure(true, true, present, new string[0], null, new[] { "Module_Engine" }));
            Assert.AreEqual("Module_PartSwitch missing", AuditB9.Failure(true, true, new[] { "Module_Engine" }, null, null, null));
            Assert.AreEqual("Module_PartSwitch threw", AuditB9.Failure(true, true, present, null, "Module_PartSwitch", null));
            Assert.AreEqual("Module_PartSwitch uninitialized", AuditB9.Failure(true, true, present, new[] { "Module_PartSwitch" }, null, null));
            Assert.AreEqual("Module_Engine threw", AuditB9.Failure(true, true, present, null, "Module_Engine", new[] { "Module_Engine" }));
            Assert.AreEqual("Module_Engine uninitialized", AuditB9.Failure(true, true, present, new[] { "Module_Engine" }, null, new[] { "Module_Engine" }));
            Assert.IsNull(AuditB9.Failure(true, true, present, new[] { "Module_Color" }, "Module_Color", new[] { "Module_Engine" }),
                "a module no override edits is the audit's other columns' business");
        }

        [Test] public void B9_Items_OrderAndFlags()
        {
            var items = AuditB9.Items(new[] { "a=x: none", "b=y: mass+1" }, "counts: mass 1, tank 0, scalars 0, module 0, off 0, notes 0", "Module_PartSwitch missing");
            CollectionAssert.AreEqual(new[] { "a=x: none", "b=y: mass+1", "counts: mass 1, tank 0, scalars 0, module 0, off 0, notes 0",
                "failed: Module_PartSwitch missing" }, items);
            Assert.IsTrue(AuditB9.HasOverrides(items));
            Assert.IsTrue(AuditB9.IsFailed(items));
            var countsOnly = AuditB9.Items(null, "counts: mass 0, tank 0, scalars 0, module 0, off 0, notes 2", null);
            Assert.IsFalse(AuditB9.HasOverrides(countsOnly), "notes alone are not an override");
            Assert.IsFalse(AuditB9.IsFailed(countsOnly));
            Assert.IsFalse(AuditB9.HasOverrides(null));
            Assert.AreEqual(0, AuditB9.Items(null, null, null).Count);
        }

        [Test] public void Summary_B9Line_CountsPartsWithOverridesAndFailures_ControlsApart()
        {
            var ok = Rec("ok"); ok.B9Overrides = AuditB9.Items(new[] { "s=a: mass+1" }, null, null);
            var bad = Rec("bad"); bad.B9Overrides = AuditB9.Items(new[] { "s=a: Module_Engine.engineModes" }, null, "Module_Engine threw");
            var notesOnly = Rec("notes"); notesOnly.B9Overrides = AuditB9.Items(null, "counts: mass 0, tank 0, scalars 0, module 0, off 0, notes 1", null);
            var control = Rec("ctrl"); control.Control = true; control.B9Overrides = AuditB9.Items(new[] { "s=a: mass+1" }, null, null);
            var s = AuditSummary.From(new[] { ok, bad, notesOnly, control, Rec("plain") }, 1);
            Assert.AreEqual("audit: B9 overrides: 2 part(s) with overrides, 1 part(s) where the default variant failed to apply", s.B9Line());
            StringAssert.Contains("## B9 overrides\n\n" + s.B9Line() + "\n", s.Markdown("x"));
            Assert.AreEqual("audit: B9 overrides: 0 part(s) with overrides, 0 part(s) where the default variant failed to apply",
                AuditSummary.From(new AuditRecord[0], 0).B9Line());
        }

        [Test] public void Markdown_B9Lines_AreInTheHeader_OnlyWhenGiven()
        {
            var s = AuditSummary.From(new[] { Rec("a") }, 1);
            StringAssert.DoesNotContain("B9PartSwitch (this session)", s.Markdown("x"));
            var md = s.Markdown("x", null, new[] { "[KSP1PartMod] mm: index: 350 applied" },
                new[] { "[KSP1PartMod] b9: 3 part(s), 4 set(s)", " ", "[KSP1PartMod] b9: module data: 1 transformer(s)" });
            StringAssert.Contains("## ModuleManager (this session)\n\n- [KSP1PartMod] mm: index: 350 applied\n\n" +
                                  "## B9PartSwitch (this session)\n\n- [KSP1PartMod] b9: 3 part(s), 4 set(s)\n- [KSP1PartMod] b9: module data: 1 transformer(s)\n\n## By kind", md);
        }

        [Test] public void Summary_ShroudsLine_Empty()
        {
            Assert.AreEqual("audit: shrouds: 0 part(s) with a KSP1 shroud kept, 0 with a shroud transform missing, 0 without the node, " +
                            "0 still carrying Redux's Module_Fairing", AuditSummary.From(new AuditRecord[0], 0).ShroudsLine());
        }
    }
}
