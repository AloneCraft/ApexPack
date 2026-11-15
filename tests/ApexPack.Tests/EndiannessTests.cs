using System;
using System.Collections.Generic;
using System.Linq;
using ApexPack;
using ApexPack.Buffers;
using ApexPack.Tests.Comparison;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// ワイヤ形式はリトルエンディアン固定。BE 機ではバイト反転経路が使われる。
/// その経路を LE 機で通すため、ネイティブ表現を BE とみなす <see cref="ApexBigEndianSimWriter"/> /
/// <see cref="ApexBigEndianSimReader"/> を使う。反転経路の出力は「正規形の各数値フィールドをバイト反転したもの」になる。
/// </summary>
public class EndiannessTests
{
    private static byte[] SimSerialize<T>(in T value) where T : ISerializePackable<T>
    {
        var buffer = new byte[4096];
        var writer = new ApexBigEndianSimWriter(buffer);
        T.Serialize(ref writer, in value);
        return writer.WrittenSpan.ToArray();
    }

    private static T SimDeserialize<T>(byte[] bytes) where T : ISerializePackable<T>
    {
        var reader = new ApexBigEndianSimReader(bytes);
        T.Deserialize(ref reader, out var value);
        Assert.Equal(bytes.Length, reader.Consumed);
        return value;
    }

    [Fact]
    public void FixedStruct_ReversePath_ReversesEveryField()
    {
        var value = new EndianProbe { A = 0x01020304, B = 0x05060708090A0B0CL, C = 0x0D0E, D = 0x0F, E = 1.5f, G = new Guid("00112233-4455-6677-8899-aabbccddeeff") };
        byte[] canonical = ApexPackSerializer.Serialize(value);
        byte[] reversed = SimSerialize(value);

        // フィールド境界: int(4) long(8) short(2) byte(1) float(4) Guid(4,2,2,8 個の byte)
        var expected = canonical.ToArray();
        int offset = 0;
        foreach (int size in new[] { 4, 8, 2, 1, 4, 4, 2, 2 })
        {
            Array.Reverse(expected, offset, size);
            offset += size;
        }
        Assert.Equal(15 + 4 + 16, canonical.Length);
        Assert.Equal(expected, reversed);

        Assert.Equal(value, SimDeserialize<EndianProbe>(reversed));
        Assert.Equal(value, ApexPackSerializer.Deserialize<EndianProbe>(canonical));
    }

    [Fact]
    public void ComplexGraph_ReversePath_Roundtrips()
    {
        var model = new AllStandardTypesModel
        {
            ByteVal = 1, SByteVal = -2, CharVal = 'Ж', ShortVal = -300, UShortVal = 60000, IntVal = -123456, UIntVal = 4000000000,
            LongVal = -9_000_000_000_000L, ULongVal = 18_000_000_000_000_000_000UL, FloatVal = 3.25f, DoubleVal = -2.5e100,
            BoolVal = true, DecimalVal = 1234567.891m, DateTimeVal = new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Utc),
            TimeSpanVal = TimeSpan.FromHours(-5.5), GuidVal = Guid.NewGuid(), RankVal = TestRank.Platinum,
            NullableInt = 42, NullableDouble = null, NullableGuid = Guid.NewGuid(), NullableDateTime = DateTime.MinValue
        };

        byte[] reversed = SimSerialize(model);
        Assert.NotEqual(ApexPackSerializer.Serialize(model), reversed);
        var restored = SimDeserialize<AllStandardTypesModel>(reversed);
        Assert.Equal(model.CharVal, restored.CharVal);
        Assert.Equal(model.ULongVal, restored.ULongVal);
        Assert.Equal(model.DecimalVal, restored.DecimalVal);
        Assert.Equal(model.GuidVal, restored.GuidVal);
        Assert.Equal(model.RankVal, restored.RankVal);
        Assert.Equal(model.NullableInt, restored.NullableInt);
        Assert.Null(restored.NullableDouble);
        Assert.Equal(model.NullableGuid, restored.NullableGuid);
        Assert.Equal(model.NullableDateTime, restored.NullableDateTime);
    }

    [Fact]
    public void StringsAndCollections_ReversePath_Roundtrips()
    {
        var model = new EndianCollections
        {
            Wide = "日本語と English",
            Narrow = "Latin1 café",
            Numbers = new[] { 1, -2, 300000 },
            Doubles = new List<double> { 1.5, -0.25 },
            Optional = new int?[] { 1, null, 3 },
            Map = new Dictionary<int, string> { [7] = "seven", [8] = "八" }
        };

        byte[] reversed = SimSerialize(model);
        var restored = SimDeserialize<EndianCollections>(reversed);
        Assert.Equal(model.Wide, restored.Wide);
        Assert.Equal(model.Narrow, restored.Narrow);
        Assert.Equal(model.Numbers, restored.Numbers);
        Assert.Equal(model.Doubles, restored.Doubles);
        Assert.Equal(model.Optional, restored.Optional);
        Assert.Equal(model.Map, restored.Map);
    }

    [Fact]
    public void Person_CanonicalBytes_AreStable()
    {
        var person = new Person { Name = "Al", Age = 28 };
        // クラスはマスクなし: [1(非 null)] [int 28 LE] [文字列 ヘッダー (2<<1)+1=5, 'A','l']
        Assert.Equal(new byte[] { 1, 28, 0, 0, 0, 5, (byte)'A', (byte)'l' }, ApexPackSerializer.Serialize(person));
    }
}

[ApexPackable]
public partial struct EndianProbe
{
    public int A;
    public long B;
    public short C;
    public byte D;
    public float E;
    public Guid G;
}

[ApexPackable]
public partial class EndianCollections
{
    public string Wide { get; set; } = "";
    public string Narrow { get; set; } = "";
    public int[]? Numbers { get; set; }
    public List<double>? Doubles { get; set; }
    public int?[]? Optional { get; set; }
    public Dictionary<int, string>? Map { get; set; }
}
