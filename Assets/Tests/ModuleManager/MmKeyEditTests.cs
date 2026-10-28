using KSP1PartMod.ModuleManager;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ModuleManager
{
    public class MmKeyEditTests
    {
        private static void AssertEdit(MmKeyEdit e, MmCommand command, string key, MmIndexKind index, int indexValue,
            MmOperator op, bool ours, bool passedBack)
        {
            Assert.AreEqual(command, e.Command, "command");
            Assert.AreEqual(key, e.Key, "key");
            Assert.AreEqual(index, e.Index.Kind, "index kind");
            Assert.AreEqual(indexValue, e.Index.Value, "index value");
            Assert.AreEqual(op, e.Operator, "operator");
            Assert.AreEqual(ours, e.IsOurs, "is ours");
            Assert.AreEqual(passedBack, e.PassedBack, "passed back");
        }

        [Test] public void Parse_MulWithSpace() =>
            AssertEdit(MmKeyEdit.Parse("@mass *", "2"), MmCommand.Edit, "mass", MmIndexKind.None, 0, MmOperator.Mul, true, false);

        [Test] public void Parse_MulWithoutSpace() =>
            AssertEdit(MmKeyEdit.Parse("@mass*", "2"), MmCommand.Edit, "mass", MmIndexKind.None, 0, MmOperator.Mul, true, false);

        [Test] public void Parse_IndexThenOperator() =>
            AssertEdit(MmKeyEdit.Parse("@mass,0 *", "2"), MmCommand.Edit, "mass", MmIndexKind.N, 0, MmOperator.Mul, true, false);

        [Test] public void Parse_Index() =>
            AssertEdit(MmKeyEdit.Parse("@volume,1", "5"), MmCommand.Edit, "volume", MmIndexKind.N, 1, MmOperator.None, true, false);

        [Test] public void Parse_IndexFromEnd() =>
            AssertEdit(MmKeyEdit.Parse("@volume,-1", "5"), MmCommand.Edit, "volume", MmIndexKind.FromEnd, 1, MmOperator.None, true, false);

        [Test] public void Parse_IndexAll() =>
            AssertEdit(MmKeyEdit.Parse("@key,*", "5"), MmCommand.Edit, "key", MmIndexKind.All, 0, MmOperator.None, true, false);

        [Test] public void Parse_IndexAllThenOperator() =>
            AssertEdit(MmKeyEdit.Parse("@key,* *", "5"), MmCommand.Edit, "key", MmIndexKind.All, 0, MmOperator.Mul, true, false);

        [Test] public void Parse_DeleteWithIndex() =>
            AssertEdit(MmKeyEdit.Parse("!key,2", "DEL"), MmCommand.Delete, "key", MmIndexKind.N, 2, MmOperator.None, true, false);

        [Test] public void Parse_DashDeleteWithIndex() =>
            AssertEdit(MmKeyEdit.Parse("-key,0", "DEL"), MmCommand.Delete, "key", MmIndexKind.N, 0, MmOperator.None, true, false);

        [Test] public void Parse_CreateOrEditAdd() =>
            AssertEdit(MmKeyEdit.Parse("%cost +", "10"), MmCommand.CreateOrEdit, "cost", MmIndexKind.None, 0, MmOperator.Add, true, false);

        [Test] public void Parse_SubAndDiv()
        {
            AssertEdit(MmKeyEdit.Parse("@cost -", "1"), MmCommand.Edit, "cost", MmIndexKind.None, 0, MmOperator.Sub, true, false);
            AssertEdit(MmKeyEdit.Parse("@cost /", "4"), MmCommand.Edit, "cost", MmIndexKind.None, 0, MmOperator.Div, true, false);
        }

        [Test] public void Parse_Power_CountedOnly()
        {
            var e = MmKeyEdit.Parse("@key !", "2");
            AssertEdit(e, MmCommand.Edit, "key", MmIndexKind.None, 0, MmOperator.Pow, false, false);
            Assert.IsTrue(e.CountedOnly);
        }

        [Test] public void Parse_Regex_CountedOnly()
        {
            var e = MmKeyEdit.Parse("@key ^", ":a:b:");
            AssertEdit(e, MmCommand.Edit, "key", MmIndexKind.None, 0, MmOperator.Regex, false, false);
            Assert.IsTrue(e.CountedOnly);
        }

        [Test] public void Parse_RegexWithIndex_CountedOnly()
        {
            var e = MmKeyEdit.Parse("@volume,1 ^", ":^:1.0 :");
            AssertEdit(e, MmCommand.Edit, "volume", MmIndexKind.N, 1, MmOperator.Regex, false, false);
            Assert.IsTrue(e.CountedOnly);
        }

        [Test] public void Parse_PlainEdit()
        {
            var e = MmKeyEdit.Parse("@key", "v");
            AssertEdit(e, MmCommand.Edit, "key", MmIndexKind.None, 0, MmOperator.None, false, false);
            Assert.IsFalse(e.CountedOnly);
            Assert.AreEqual("v", e.Value);
        }

        [Test] public void Parse_NoCommand() =>
            AssertEdit(MmKeyEdit.Parse("key", "v"), MmCommand.None, "key", MmIndexKind.None, 0, MmOperator.None, false, false);

        [Test] public void Parse_CommaNotAnIndex_PassedBackWithReduxKey() =>
            AssertEdit(MmKeyEdit.Parse("@a,b", "v"), MmCommand.Edit, "a", MmIndexKind.None, 0, MmOperator.None, false, true);

        [Test] public void Parse_Brackets_PassedBackWithReduxKey()
        {
            var e = MmKeyEdit.Parse("@curve[x]", "v");
            AssertEdit(e, MmCommand.Edit, "curve", MmIndexKind.None, 0, MmOperator.None, false, true);
            Assert.IsTrue(e.HasBrackets);
        }

        [Test] public void Parse_VectorComponentWithOperator_PassedBackAndFlagged()
        {
            var e = MmKeyEdit.Parse("@node_stack_top[1,,] *", "2");
            Assert.IsTrue(e.PassedBack);
            Assert.IsTrue(e.HasBrackets);
            Assert.IsFalse(e.IsOurs);
            Assert.AreEqual("node_stack_top", e.Key);
            Assert.AreEqual(MmOperator.Mul, e.BracketOperator);
            Assert.IsTrue(e.SkippedVectorEdit);
        }

        [Test] public void Parse_VectorComponentWithIndexAndOperator_Skipped()
        {
            var e = MmKeyEdit.Parse("@volume,1[1, ] *", "0.2");
            Assert.AreEqual(MmOperator.Mul, e.BracketOperator);
            Assert.IsTrue(e.SkippedVectorEdit);
            Assert.AreEqual(MmOperator.Sub, MmKeyEdit.Parse("@position[1]-", "#$x$").BracketOperator);
            Assert.AreEqual(MmOperator.Regex, MmKeyEdit.Parse("%key[0] ^", ":a:b:").BracketOperator);
        }

        [Test] public void Parse_BracketsWithoutOperator_NotSkipped()
        {
            Assert.AreEqual(MmOperator.None, MmKeyEdit.Parse("@curve[x]", "v").BracketOperator);
            Assert.IsFalse(MmKeyEdit.Parse("@curve[x]", "v").SkippedVectorEdit);
            Assert.IsFalse(MmKeyEdit.Parse("@MODULE[a],*", "v").SkippedVectorEdit, "the * of a ,* index is not an operator");
            Assert.IsFalse(MmKeyEdit.Parse("!key[x] *", "v").SkippedVectorEdit, "a delete has no operator");
            Assert.IsFalse(MmKeyEdit.Parse("@mass *", "2").SkippedVectorEdit);
        }

        [Test] public void Parse_OperatorOnAdd_IsPlainReduxKey() =>
            // no MM operator on an add line: Redux's key is the whole name
            AssertEdit(MmKeyEdit.Parse("mass *", "2"), MmCommand.None, "mass *", MmIndexKind.None, 0, MmOperator.None, false, false);

        [Test] public void Parse_TrimsAndKeepsValue()
        {
            var e = MmKeyEdit.Parse("  @mass * ", "0.5");
            AssertEdit(e, MmCommand.Edit, "mass", MmIndexKind.None, 0, MmOperator.Mul, true, false);
            Assert.AreEqual("0.5", e.Value);
        }

        [Test] public void Parse_NegativeZero_PassedBack() =>
            Assert.IsTrue(MmKeyEdit.Parse("@key,-0", "1").PassedBack);

        [Test] public void Parse_NullOrEmpty_EmptyKey()
        {
            Assert.AreEqual("", MmKeyEdit.Parse(null, "1").Key);
            Assert.AreEqual("", MmKeyEdit.Parse("@", "1").Key);
        }

        [Test] public void Parse_AllCommands()
        {
            Assert.AreEqual(MmCommand.Copy, MmKeyEdit.Parse("+key", "v").Command);
            Assert.AreEqual(MmCommand.Insert, MmKeyEdit.Parse("&key", "v").Command);
            Assert.AreEqual(MmCommand.Delete, MmKeyEdit.Parse("-key", "v").Command);
        }

        [Test] public void MightNeedUs_TrueForOperatorsAndIndices()
        {
            Assert.IsTrue(MmKeyEdit.MightNeedUs("@mass *"));
            Assert.IsTrue(MmKeyEdit.MightNeedUs("@mass*"));
            Assert.IsTrue(MmKeyEdit.MightNeedUs("@volume,1"));
            Assert.IsTrue(MmKeyEdit.MightNeedUs("@key ^"));
            Assert.IsTrue(MmKeyEdit.MightNeedUs("!key,2"));
            Assert.IsFalse(MmKeyEdit.MightNeedUs("@mass"));
            Assert.IsFalse(MmKeyEdit.MightNeedUs("mass"));
            Assert.IsFalse(MmKeyEdit.MightNeedUs("%cost"));
            Assert.IsFalse(MmKeyEdit.MightNeedUs("!node_stack_interstage*"), "a delete wildcard is not an operator");
            Assert.IsFalse(MmKeyEdit.MightNeedUs(""));
            Assert.IsFalse(MmKeyEdit.MightNeedUs(null));
        }

        [Test] public void MightNeedUs_DoesNotAllocate()
        {
            var names = new[] { "@mass", "maxThrust", "%cost", "@title", "!MODULE" };
            Assert.That(() =>
            {
                var any = false;
                for (var i = 0; i < 1000; i++)
                    foreach (var n in names) any |= MmKeyEdit.MightNeedUs(n);
                if (any) throw new System.Exception("unexpected");
            }, Is.Not.AllocatingGCMemory());
        }

        [TestCase("@mass *", MmCommand.Edit, "mass *")]
        [TestCase("@mass,0 *", MmCommand.Edit, "mass")]
        [TestCase("@volume,1", MmCommand.Edit, "volume")]
        [TestCase("%cost +", MmCommand.CreateOrEdit, "cost +")]
        [TestCase("!key,2", MmCommand.Delete, "key")]
        [TestCase("@node_stack_top[1,,] *", MmCommand.Edit, "node_stack_top")]
        [TestCase("mass", MmCommand.None, "mass")]
        public void AsRedux_ReadsTheLineAsRedux(string name, MmCommand command, string key)
        {
            var e = MmKeyEdit.AsRedux(name, "2");
            Assert.AreEqual(command, e.Command);
            Assert.AreEqual(key, e.Key);
            Assert.AreEqual(MmIndexKind.None, e.Index.Kind);
            Assert.AreEqual(MmOperator.None, e.Operator);
            Assert.IsTrue(e.PassedBack);
            Assert.IsFalse(e.IsOurs);
            Assert.IsFalse(e.SkippedVectorEdit);
            Assert.AreEqual("2", e.Value);
        }

        [Test] public void AsRedux_AppliedGivesReduxsResult()
        {
            var values = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("mass", "1"),
                new System.Collections.Generic.KeyValuePair<string, string>("mass", "3"),
            };
            var c = new MmCounters();
            Assert.IsFalse(MmValueEditor.Apply(values, MmKeyEdit.AsRedux("@mass *", "2"), true, c), "base 'mass *' matches nothing");
            Assert.IsTrue(MmValueEditor.Apply(values, MmKeyEdit.AsRedux("@mass,0 *", "2"), true, c), "Redux edits every mass");
            CollectionAssert.AreEqual(new[] { "2", "2" }, System.Linq.Enumerable.Select(values, v => v.Value));
            Assert.AreEqual(0, c.ArithmeticApplied);
        }
    }
}
