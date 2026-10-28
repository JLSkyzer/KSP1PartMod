using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.ModuleManager;
using NUnit.Framework;

namespace KSP1PartMod.Tests.ModuleManager
{
    public class MmValueEditorTests
    {
        private static List<KeyValuePair<string, string>> Values(params string[] pairs)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < pairs.Length; i += 2) list.Add(new KeyValuePair<string, string>(pairs[i], pairs[i + 1]));
            return list;
        }

        private static string[] Flat(List<KeyValuePair<string, string>> values) =>
            values.SelectMany(v => new[] { v.Key, v.Value }).ToArray();

        private static List<KeyValuePair<string, string>> Apply(List<KeyValuePair<string, string>> values, string name,
            string value, MmCounters c, bool firstMatch = false)
        {
            MmValueEditor.Apply(values, MmKeyEdit.Parse(name, value), firstMatch, c);
            return values;
        }

        // ---- arithmetic ----

        [Test] public void Arithmetic_Mul()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "mass", "0.2" }, Flat(Apply(Values("mass", "0.1"), "@mass *", "2", c)));
            Assert.AreEqual(1, c.ArithmeticMul);
        }

        [Test] public void Arithmetic_Add()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "1.5" }, Flat(Apply(Values("cost", "1"), "@cost +", "0.5", c)));
            Assert.AreEqual(1, c.ArithmeticAdd);
        }

        [Test] public void Arithmetic_Sub()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "2" }, Flat(Apply(Values("cost", "3"), "@cost -", "1", c)));
            Assert.AreEqual(1, c.ArithmeticSub);
        }

        [Test] public void Arithmetic_Div()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "0.25" }, Flat(Apply(Values("cost", "1"), "@cost /", "4", c)));
            Assert.AreEqual(1, c.ArithmeticDiv);
        }

        [Test] public void Arithmetic_DivideByZero_NoEditCounted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "1" }, Flat(Apply(Values("cost", "1"), "@cost /", "0", c)));
            Assert.AreEqual(1, c.DivideByZero);
            Assert.AreEqual(0, c.ArithmeticApplied);
        }

        [Test] public void Arithmetic_NonNumericValue_NoEditCounted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "pos", "1,2,3" }, Flat(Apply(Values("pos", "1,2,3"), "@pos *", "2", c)));
            Assert.AreEqual(1, c.NonNumeric);
            Assert.AreEqual(0, c.ArithmeticApplied);
        }

        [Test] public void Arithmetic_VariableOperand_NoEditCounted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "mass", "1" }, Flat(Apply(Values("mass", "1"), "@mass *", "#$x$", c)));
            Assert.AreEqual(1, c.NonNumeric);
        }

        [Test] public void Arithmetic_MissingKey_Counted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "1" }, Flat(Apply(Values("cost", "1"), "@mass *", "2", c)));
            Assert.AreEqual(1, c.MissingKey);
        }

        [Test] public void Arithmetic_CreateOrEditMissingKey_NothingCreatedCounted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "cost", "1" }, Flat(Apply(Values("cost", "1"), "%mass +", "2", c)));
            Assert.AreEqual(1, c.MissingKey);
        }

        [Test] public void Arithmetic_G15Formatting()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "mass", "0.3" }, Flat(Apply(Values("mass", "0.1"), "@mass *", "3", c)));
        }

        [Test] public void Arithmetic_IsCultureInvariant()
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var c = new MmCounters();
                CollectionAssert.AreEqual(new[] { "mass", "1.25" }, Flat(Apply(Values("mass", "0.5"), "@mass *", "2.5", c)));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Test] public void Arithmetic_KeyMatchIsCaseInsensitive()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "mass", "2" }, Flat(Apply(Values("Mass", "1"), "@mass *", "2", c)));
        }

        [Test] public void Arithmetic_PowAndRegex_CountedNotApplied()
        {
            var c = new MmCounters();
            var v = Apply(Values("cost", "2"), "@cost !", "2", c);
            Apply(v, "@cost ^", ":2:3:", c);
            CollectionAssert.AreEqual(new[] { "cost", "2" }, Flat(v));
            Assert.AreEqual(1, c.NotSupportedPow);
            Assert.AreEqual(1, c.NotSupportedRegex);
        }

        // ---- index ----

        [Test] public void Index_EditsOnlyTheNth()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "volume", "1", "volume", "5", "volume", "3" },
                Flat(Apply(Values("volume", "1", "volume", "2", "volume", "3"), "@volume,1", "5", c)));
            Assert.AreEqual(1, c.IndexValueEdits);
        }

        [Test] public void Index_FromEnd()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "volume", "1", "volume", "2", "volume", "5" },
                Flat(Apply(Values("volume", "1", "volume", "2", "volume", "3"), "@volume,-1", "5", c)));
        }

        [Test] public void Index_All()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "volume", "5", "other", "x", "volume", "5" },
                Flat(Apply(Values("volume", "1", "other", "x", "volume", "3"), "@volume,*", "5", c, firstMatch: true)));
        }

        [Test] public void Index_SkipsOtherKeysWhenCounting()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "volume", "1", "other", "x", "volume", "6" },
                Flat(Apply(Values("volume", "1", "other", "x", "volume", "3"), "@volume,1 *", "2", c)));
        }

        [Test] public void Index_OutOfRange_Counted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "volume", "1", "volume", "2" },
                Flat(Apply(Values("volume", "1", "volume", "2"), "@volume,3", "5", c)));
            Assert.AreEqual(1, c.IndexOutOfRange);
            Assert.AreEqual(0, c.IndexValueEdits);
        }

        [Test] public void Index_DeleteOne()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "a", "key", "c" },
                Flat(Apply(Values("key", "a", "key", "b", "key", "c"), "!key,1", "DEL", c)));
            Assert.AreEqual(1, c.IndexValueEdits);
        }

        [Test] public void Index_DeleteFromEnd()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "a", "key", "b" },
                Flat(Apply(Values("key", "a", "key", "b", "key", "c"), "!key,-1", "DEL", c)));
        }

        [Test] public void Index_DeleteOutOfRange_Counted()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "a" }, Flat(Apply(Values("key", "a"), "!key,2", "DEL", c)));
            Assert.AreEqual(1, c.IndexOutOfRange);
        }

        // ---- default index ----

        [Test] public void Default_EditAllWhenFirstMatchOff()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "z", "key", "z" },
                Flat(Apply(Values("key", "a", "key", "b"), "@key", "z", c, firstMatch: false)));
            Assert.AreEqual(1, c.UnindexedMultiMatch);
        }

        [Test] public void Default_EditFirstWhenFirstMatchOn()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "z", "key", "b" },
                Flat(Apply(Values("key", "a", "key", "b"), "@key", "z", c, firstMatch: true)));
            Assert.AreEqual(1, c.UnindexedMultiMatch);
        }

        [Test] public void Default_ArithmeticFirstWhenFirstMatchOn()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "key", "2", "key", "2" },
                Flat(Apply(Values("key", "1", "key", "2"), "@key *", "2", c, firstMatch: true)));
        }

        [Test] public void Default_DeleteAlwaysAll()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "other", "x" },
                Flat(Apply(Values("key", "a", "other", "x", "key", "b"), "!key", "DEL", c, firstMatch: true)));
        }

        [Test] public void Default_SingleMatch_NotCountedAsMulti()
        {
            var c = new MmCounters();
            Apply(Values("key", "a"), "@key", "z", c);
            Assert.AreEqual(0, c.UnindexedMultiMatch);
        }

        // ---- parity with Redux's observed plain semantics (plan (a)) ----

        private static readonly object[] ParityCases =
        {
            // edit: every match (case-insensitive) takes the value, under the patch's spelling of the key
            new object[] { "@title", "New", new[] { "title", "Old", "TITLE", "Old2", "mass", "1" }, new[] { "title", "New", "title", "New", "mass", "1" } },
            // edit of a missing key: nothing
            new object[] { "@title", "New", new[] { "mass", "1" }, new[] { "mass", "1" } },
            // create-or-edit, existing: every match
            new object[] { "%title", "New", new[] { "title", "Old", "title", "Old2" }, new[] { "title", "New", "title", "New" } },
            // create-or-edit, missing: appended
            new object[] { "%title", "New", new[] { "mass", "1" }, new[] { "mass", "1", "title", "New" } },
            // delete, '!' and '-': every match
            new object[] { "!title", "DEL", new[] { "title", "a", "mass", "1", "Title", "b" }, new[] { "mass", "1" } },
            new object[] { "-title", "DEL", new[] { "title", "a", "mass", "1", "title", "b" }, new[] { "mass", "1" } },
            // no command, '+', '&': appended, even when the key exists
            new object[] { "title", "New", new[] { "title", "Old" }, new[] { "title", "Old", "title", "New" } },
            new object[] { "+title", "New", new[] { "title", "Old" }, new[] { "title", "Old", "title", "New" } },
            new object[] { "&title", "New", new[] { "title", "Old" }, new[] { "title", "Old", "title", "New" } },
            // passed-back names keep Redux's base name: the selector and a non-index comma are cut
            new object[] { "@curve[x]", "v", new[] { "curve", "a", "curve", "b" }, new[] { "curve", "v", "curve", "v" } },
            new object[] { "@a,b", "v", new[] { "a", "1" }, new[] { "a", "v" } },
        };

        [TestCaseSource(nameof(ParityCases))]
        public void Parity_PlainLines(string name, string value, string[] before, string[] expected)
        {
            var c = new MmCounters();
            var values = Values(before);
            MmValueEditor.Apply(values, MmKeyEdit.Parse(name, value), false, c);
            CollectionAssert.AreEqual(expected, Flat(values));
        }

        [Test] public void Parity_PassedBackIgnoresFirstMatch()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "curve", "v", "curve", "v" },
                Flat(Apply(Values("curve", "a", "curve", "b"), "@curve[x]", "v", c, firstMatch: true)));
        }

        [Test] public void VectorComponent_Skipped_NotLeftToRedux()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "node_stack_top", "0, 1, 0", "node_stack_top", "0, 2, 0" },
                Flat(Apply(Values("node_stack_top", "0, 1, 0", "node_stack_top", "0, 2, 0"), "@node_stack_top[1,,] *", "#$scale$", c)));
            Assert.AreEqual(1, c.VectorComponentSkipped);
            Assert.AreEqual(0, c.NonNumeric + c.ArithmeticApplied);
        }

        [Test] public void VectorComponentWithoutOperator_StillReduxParity()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "curve", "v" }, Flat(Apply(Values("curve", "a"), "@curve[x]", "v", c)));
            Assert.AreEqual(0, c.VectorComponentSkipped);
        }

        [Test] public void EmptyKey_NoEdit()
        {
            var c = new MmCounters();
            CollectionAssert.AreEqual(new[] { "a", "1" }, Flat(Apply(Values("a", "1"), "@", "v", c)));
        }

        [Test] public void HasVariable()
        {
            Assert.IsTrue(MmValueEditor.HasVariable("#$x$"));
            Assert.IsTrue(MmValueEditor.HasVariable(" #1.0 $vol$"));
            Assert.IsFalse(MmValueEditor.HasVariable("1.0"));
            Assert.IsFalse(MmValueEditor.HasVariable("#autoLOC_123"));
            Assert.IsFalse(MmValueEditor.HasVariable("$5"));
            Assert.IsFalse(MmValueEditor.HasVariable(null));
        }
    }
}
