using System.Collections.Generic;
using KSP1PartMod.Animation.State;
using NUnit.Framework;

namespace KSP1PartMod.Tests.Animation.State
{
    public class EntryStateCodecTests
    {
        private static readonly Dictionary<string, float> V = new Dictionary<string, float>
        {
            ["toggle:a:0.t"] = 0.25f,
            ["toggle:a:0.closed"] = 1f,
            ["deploy:dish:0.t"] = 0.1f + 0.2f,          // a float with no short decimal form
            ["color:_EmissiveColor:1.on"] = 0f,
            ["toggle:we=ird;name\\x:0.t"] = -1.5e-7f,   // '=', ';' and '\' in a clip name
        };

        private static IReadOnlyDictionary<string, float> Decode(string s) => EntryStateCodec.Decode(s, out _);

        [Test] public void Codec_RoundTrip_KeepsEveryEntry() =>
            CollectionAssert.AreEquivalent(V, EntryStateCodec.Decode(EntryStateCodec.Encode(V), out _));

        [Test] public void Codec_RoundTrip_NoError()
        {
            EntryStateCodec.Decode(EntryStateCodec.Encode(V), out var e);
            Assert.IsNull(e);
        }

        [Test] public void Codec_Encode_IsStableAndInvariant() =>
            Assert.AreEqual("a=0.5;b=-2", EntryStateCodec.Encode(new Dictionary<string, float> { ["b"] = -2f, ["a"] = 0.5f }));

        [Test] public void Codec_UnknownKey_Ignored() => Assert.AreEqual(0.5f, Decode("toggle:a:0=0.5;zzz=1")["toggle:a:0"]);

        [Test] public void Codec_Malformed_GivesDefaultsAndReports()
        {
            var d = EntryStateCodec.Decode("toggle:a:0=abc;;=", out var e);
            Assert.IsFalse(d.ContainsKey("toggle:a:0"));
            Assert.IsNotNull(e);
        }

        [Test] public void Codec_MalformedPart_KeepsTheRest()
        {
            var d = EntryStateCodec.Decode("x=abc;y=0.75;novalue", out var e);
            Assert.AreEqual((1, 0.75f), (d.Count, d["y"]));
            Assert.IsNotNull(e);
        }

        [Test] public void Codec_Empty_IsEmptyNoError()
        {
            Assert.AreEqual(0, EntryStateCodec.Decode("", out var e).Count);
            Assert.IsNull(e);
        }

        [Test] public void Codec_Null_IsEmptyNoError()
        {
            Assert.AreEqual(0, EntryStateCodec.Decode(null, out var e).Count);
            Assert.IsNull(e);
        }
    }
}
