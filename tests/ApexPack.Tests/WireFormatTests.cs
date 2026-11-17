using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ApexPack;
using ApexPack.Buffers;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// ワイヤ形式の境界値(docs/WireFormat.md)をバイト列で固定する。
/// </summary>
public class WireFormatTests
{
    private static byte[] Write(Action<ApexSpanWriterBox> action)
    {
        var box = new ApexSpanWriterBox();
        action(box);
        return box.Bytes;
    }

    public sealed class ApexSpanWriterBox
    {
        public byte[] Bytes = Array.Empty<byte>();

        public void Compact(uint value)
        {
            var buffer = new byte[8];
            var writer = new ApexSpanWriter(buffer);
            writer.WriteCompactUInt(value);
            Bytes = writer.WrittenSpan.ToArray();
        }

        public void String(string? value)
        {
            var buffer = new byte[value is null ? 8 : value.Length * 2 + 8];
            var writer = new ApexSpanWriter(buffer);
            writer.WriteString(value);
            Bytes = writer.WrittenSpan.ToArray();
        }
    }

    [Theory]
    [InlineData(0u, new byte[] { 0x00 })]
    [InlineData(0xFCu, new byte[] { 0xFC })]
    [InlineData(0xFDu, new byte[] { 0xFD, 0xFD, 0x00 })]
    [InlineData(0xFFFFu, new byte[] { 0xFD, 0xFF, 0xFF })]
    [InlineData(0x10000u, new byte[] { 0xFE, 0x00, 0x00, 0x01, 0x00 })]
    [InlineData(0xFFFFFFFFu, new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void CompactUInt_Boundaries(uint value, byte[] expected)
    {
        byte[] bytes = Write(w => w.Compact(value));
        Assert.Equal(expected, bytes);
        var reader = new ApexSpanReader(bytes);
        Assert.Equal(value, reader.ReadCompactUInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void String_NullEmptyNarrowWide()
    {
        Assert.Equal(new byte[] { 0 }, Write(w => w.String(null)));
        Assert.Equal(new byte[] { 1 }, Write(w => w.String("")));
        Assert.Equal(new byte[] { 5, (byte)'h', (byte)'i' }, Write(w => w.String("hi")));
        Assert.Equal(new byte[] { 3, 0xE9 }, Write(w => w.String("é")));                       // Latin1 は 1 バイト
        Assert.Equal(new byte[] { 4, 0x42, 0x30 }, Write(w => w.String("あ")));                 // 非 Latin1 は UTF-16LE
        Assert.Equal(new byte[] { 6, 0x3D, 0xD8, 0x00, 0xDE }, Write(w => w.String("😀")));      // サロゲートペアもそのまま
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(1000)]
    [InlineData(32766)]
    [InlineData(32767)]
    [InlineData(40000)]
    public void String_LengthBoundaries_RoundtripBothEncodings(int length)
    {
        string narrow = new string(Enumerable.Range(0, length).Select(i => (char)('a' + i % 26)).ToArray());
        // 末尾だけ非 Latin1(SIMD の途中で判定が変わる場合も含む)
        string wide = narrow.Substring(0, length - 1) + "語";

        foreach (var s in new[] { narrow, wide })
        {
            byte[] bytes = Write(w => w.String(s));
            var reader = new ApexSpanReader(bytes);
            Assert.Equal(s, reader.ReadString());
            Assert.Equal(0, reader.Remaining);
        }

        Assert.True(Write(w => w.String(narrow)).Length < length + 6);
    }

    [Theory]
    [InlineData(32_767, 'a')]
    [InlineData(32_768, 'a')]
    [InlineData(99_999, 'z')]
    [InlineData(41_999, 'é')]
    [InlineData(42_000, 'é')]
    [InlineData(99_999, 'é')]
    [InlineData(99_999, '語')]
    public void StringBuilder_LargeContents_KeepWireFormatAndCapacity(int length, char last)
    {
        string text = new string('a', length - 1) + last;
        byte[] bytes = ApexPackSerializer.Serialize(text);
        Assert.Equal(bytes, ApexPackSerializer.Serialize(new StringBuilder(text)));

        var restored = ApexPackSerializer.Deserialize<StringBuilder>(bytes);

        Assert.Equal(text, restored.ToString());
        Assert.Equal(length, restored.Capacity);
        Assert.Equal(bytes, ApexPackSerializer.Serialize(restored));
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Deserialize<StringBuilder>(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [Fact]
    public void Mask_SevenEightNineBits()
    {
        // マスクを持つクラスは bit0 が「非 null」の印
        Assert.Single(ApexPackSerializer.Serialize(new Bits6()));   // 6 + 1 = 7 ビット → 1 バイト
        Assert.Single(ApexPackSerializer.Serialize(new Bits7()));   // 7 + 1 = 8 ビット → 1 バイト
        Assert.Equal(2, ApexPackSerializer.Serialize(new Bits8()).Length);   // 8 + 1 = 9 ビット → 2 バイト
    }

    [Fact]
    public void Mask_SixtyFiveBools_Roundtrip()
    {
        var value = new Bits65();
        var props = typeof(Bits65).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).OrderBy(p => p.Name).ToArray();
        Assert.Equal(65, props.Length);
        for (int i = 0; i < props.Length; i++) props[i].SetValue(value, i % 3 != 0);

        byte[] bytes = ApexPackSerializer.Serialize(value);
        Assert.Equal(9, bytes.Length); // 65 + 1 = 66 ビット
        var restored = ApexPackSerializer.Deserialize<Bits65>(bytes);
        foreach (var p in props) Assert.Equal(p.GetValue(value), p.GetValue(restored));
    }

    [Fact]
    public void NullRoot_IsSingleZeroByte()
    {
        Assert.Equal(new byte[] { 0 }, ApexPackSerializer.Serialize<Bits6>(null!));
        Assert.Null(ApexPackSerializer.Deserialize<Bits6?>(new byte[] { 0 }));
        Assert.Equal(new byte[] { 0 }, ApexPackSerializer.Serialize<Comparison.Person>(null!));
    }

    [Fact]
    public void NullableMembers_CostOneBitEach()
    {
        var empty = new NullableAnnotations { Required = "r" };
        // マスク(印 + 4 ビット) 1 バイト + Required 文字列 2 バイト + Child なし
        Assert.Equal(3, ApexPackSerializer.Serialize(empty).Length);

        var full = new NullableAnnotations { Required = "r", OptionalText = "o", OptionalList = new List<int> { 1 }, OptionalChild = new Leaf { Value = 5 }, OptionalNumber = 9 };
        var restored = ApexPackSerializer.Deserialize<NullableAnnotations>(ApexPackSerializer.Serialize(full));
        Assert.Equal("o", restored.OptionalText);
        Assert.Equal(new List<int> { 1 }, restored.OptionalList);
        Assert.Equal(5, restored.OptionalChild!.Value);
        Assert.Equal(9, restored.OptionalNumber);
    }

    [Fact]
    public void NonNullableObjectMember_WithNull_Throws()
    {
        var value = new StrictParent { Child = null! };
        Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.Serialize(value));
    }

    [Fact]
    public void ObliviousReferenceMembers_AreTreatedAsNullable()
    {
        var value = new ObliviousModel { Text = null, Child = null };
        var restored = ApexPackSerializer.Deserialize<ObliviousModel>(ApexPackSerializer.Serialize(value));
        Assert.Null(restored.Text);
        Assert.Null(restored.Child);
    }

    [Fact]
    public void PaddedStruct_IsPacked()
    {
        var value = new Padded { A = 1, B = 2, C = 3 };
        byte[] bytes = ApexPackSerializer.Serialize(value);
        Assert.Equal(1 + 8 + 2, bytes.Length);
        Assert.Equal(value, ApexPackSerializer.Deserialize<Padded>(bytes));
        Assert.False(Padded.ApexIsRawLayout);
        Assert.True(Comparison.MpVector3.ApexIsRawLayout);
    }

    [Fact]
    public void NullClassElements_AtEndOfPayload_AreNotRejectedAsBombs()
    {
        // 回帰: 要素の最小サイズを本体サイズ(Leaf は 4 バイト)で見積もると、null 要素(1 バイト)だけの配列を誤って拒否していた
        var nulls = new Leaf?[] { null, null, null };
        Assert.Equal(nulls, ApexPackSerializer.Deserialize<Leaf?[]>(ApexPackSerializer.Serialize(nulls)));
        var holder = new NullableLeafArrayHolder { Items = new Leaf?[] { null, null } };
        Assert.Equal(2, ApexPackSerializer.Deserialize<NullableLeafArrayHolder>(ApexPackSerializer.Serialize(holder)).Items!.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void GeneratedClassArrayMember_PreservesOrderAndChecksLimits(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => i == 1 ? null : new Leaf { Value = i + 10 }).ToArray();
        byte[] bytes = ApexPackSerializer.Serialize(new NullableLeafArrayHolder { Items = items });
        var restored = ApexPackSerializer.Deserialize<NullableLeafArrayHolder>(bytes);
        Assert.Equal(items.Select(item => item?.Value), restored.Items!.Select(item => item?.Value));

        if (count > 0)
        {
            var limit = new ApexSecurityOptions { MaxCollectionLength = count - 1, MaxTotalElements = count, MaxDepth = 8 };
            Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeSafe<NullableLeafArrayHolder>(bytes, in limit));
        }
    }

    [Fact]
    public void BackReferences_AtEndOfPayload_AreNotRejectedAsBombs()
    {
        // 参照追跡では同じインスタンスの 2 回目以降は数バイトの後方参照になる
        var shared = new Leaf { Value = 7 };
        var holder = new SharedLeafHolder { Items = new List<Leaf> { shared, shared, shared, shared } };
        var restored = ApexPackSerializer.DeserializeWithReferences<SharedLeafHolder>(ApexPackSerializer.SerializeWithReferences(holder));
        Assert.Same(restored.Items[0], restored.Items[3]);
        Assert.Equal(7, restored.Items[3].Value);
    }

    [Fact]
    public void NullableArray_UsesPresenceBitmap()
    {
        var values = Enumerable.Range(0, 20).Select(i => i % 4 == 0 ? (int?)i : null).ToArray();
        byte[] bytes = ApexPackSerializer.Serialize(values);
        // ヘッダー 1 + ビットマップ 3 + 存在する 5 個 × 4
        Assert.Equal(1 + 3 + 5 * 4, bytes.Length);
        Assert.Equal(values, ApexPackSerializer.Deserialize<int?[]>(bytes));
    }
}

[ApexPackable]
public partial class Bits6
{
    public bool B0 { get; set; }
    public bool B1 { get; set; }
    public bool B2 { get; set; }
    public bool B3 { get; set; }
    public bool B4 { get; set; }
    public bool B5 { get; set; }
}

[ApexPackable]
public partial class Bits7
{
    public bool B0 { get; set; }
    public bool B1 { get; set; }
    public bool B2 { get; set; }
    public bool B3 { get; set; }
    public bool B4 { get; set; }
    public bool B5 { get; set; }
    public bool B6 { get; set; }
}

[ApexPackable]
public partial class Bits8
{
    public bool B0 { get; set; }
    public bool B1 { get; set; }
    public bool B2 { get; set; }
    public bool B3 { get; set; }
    public bool B4 { get; set; }
    public bool B5 { get; set; }
    public bool B6 { get; set; }
    public int? N7 { get; set; }
}

[ApexPackable]
public partial class Bits65
{
    public bool P00 { get; set; } public bool P01 { get; set; } public bool P02 { get; set; } public bool P03 { get; set; } public bool P04 { get; set; }
    public bool P05 { get; set; } public bool P06 { get; set; } public bool P07 { get; set; } public bool P08 { get; set; } public bool P09 { get; set; }
    public bool P10 { get; set; } public bool P11 { get; set; } public bool P12 { get; set; } public bool P13 { get; set; } public bool P14 { get; set; }
    public bool P15 { get; set; } public bool P16 { get; set; } public bool P17 { get; set; } public bool P18 { get; set; } public bool P19 { get; set; }
    public bool P20 { get; set; } public bool P21 { get; set; } public bool P22 { get; set; } public bool P23 { get; set; } public bool P24 { get; set; }
    public bool P25 { get; set; } public bool P26 { get; set; } public bool P27 { get; set; } public bool P28 { get; set; } public bool P29 { get; set; }
    public bool P30 { get; set; } public bool P31 { get; set; } public bool P32 { get; set; } public bool P33 { get; set; } public bool P34 { get; set; }
    public bool P35 { get; set; } public bool P36 { get; set; } public bool P37 { get; set; } public bool P38 { get; set; } public bool P39 { get; set; }
    public bool P40 { get; set; } public bool P41 { get; set; } public bool P42 { get; set; } public bool P43 { get; set; } public bool P44 { get; set; }
    public bool P45 { get; set; } public bool P46 { get; set; } public bool P47 { get; set; } public bool P48 { get; set; } public bool P49 { get; set; }
    public bool P50 { get; set; } public bool P51 { get; set; } public bool P52 { get; set; } public bool P53 { get; set; } public bool P54 { get; set; }
    public bool P55 { get; set; } public bool P56 { get; set; } public bool P57 { get; set; } public bool P58 { get; set; } public bool P59 { get; set; }
    public bool P60 { get; set; } public bool P61 { get; set; } public bool P62 { get; set; } public bool P63 { get; set; } public bool P64 { get; set; }
}

[ApexPackable]
public partial class Leaf
{
    public int Value { get; set; }
}

[ApexPackable]
public partial class NullableAnnotations
{
    public string Required { get; set; } = "";
    public string? OptionalText { get; set; }
    public List<int>? OptionalList { get; set; }
    public Leaf? OptionalChild { get; set; }
    public int? OptionalNumber { get; set; }
}

[ApexPackable]
public partial class NullableLeafArrayHolder
{
    public Leaf?[]? Items { get; set; }
}

[ApexPackable]
public partial class SharedLeafHolder
{
    public List<Leaf> Items { get; set; } = new();
}

[ApexPackable]
public partial class StrictParent
{
    public Leaf Child { get; set; } = new();
}

#nullable disable
[ApexPackable]
public partial class ObliviousModel
{
    public string Text { get; set; }
    public Leaf Child { get; set; }
}
#nullable restore

[ApexPackable]
public partial struct Padded : IEquatable<Padded>
{
    public byte A;
    public long B;
    public short C;

    public bool Equals(Padded other) => A == other.A && B == other.B && C == other.C;
    public override bool Equals(object? obj) => obj is Padded p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(A, B, C);
}
