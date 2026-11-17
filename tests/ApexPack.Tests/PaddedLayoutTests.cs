using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace ApexPack.Tests;

/// <summary>メモリ上は後ろに詰め物があり、ワイヤでは詰める固定長構造体(long 8 + int 4。メモリ 16 バイト、ワイヤ 12 バイト)。</summary>
[ApexPackable]
public partial struct TrailingPadded
{
    public long A;
    public int B;
}

/// <summary>
/// 不変条件: 詰め物のある固定長型(後ろだけ詰め物)を一括コピーで読み書きしても、ワイヤ形式は要素ごとに書いたときと同じ。
/// SIMD の書き込みは次の要素で上書きされる余りを使うので、要素数の境界(0 / 1 / 2 / 端数)で確かめる。
/// </summary>
public class PaddedLayoutTests
{
    public static IEnumerable<object[]> Counts() => new[] { 0, 1, 2, 3, 4, 5, 17, 64, 65 }.Select(n => new object[] { n });

    private static byte[] Header(int count)
    {
        // CompactUInt(件数 + 1)。テストの件数は 1 バイトに収まる
        return new[] { (byte)(count + 1) };
    }

    [Fact]
    public void Layouts_AreAsExpected()
    {
        Assert.Equal(16, Unsafe.SizeOf<(int, int, int)>());
        Assert.Equal(16, Unsafe.SizeOf<TrailingPadded>());
        Assert.Equal(12, TrailingPadded.ApexFixedSize);
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void TupleArray_IsPackedWithoutPadding_AndRoundTrips(int count)
    {
        var values = new (int, int, int)[count];
        for (int i = 0; i < count; i++) values[i] = (i * 3 + 1, -(i * 3 + 2), int.MaxValue - i);

        var bytes = ApexPackSerializer.Serialize(values);

        var expected = new List<byte>(Header(count));
        var buffer = new byte[4];
        foreach (var (a, b, c) in values)
        {
            foreach (var v in new[] { a, b, c })
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, v);
                expected.AddRange(buffer);
            }
        }
        Assert.Equal(expected.ToArray(), bytes);
        Assert.Equal(values, ApexPackSerializer.Deserialize<(int, int, int)[]>(bytes));
        Assert.Equal(values, ApexPackSerializer.Deserialize<List<(int, int, int)>>(ApexPackSerializer.Serialize(new List<(int, int, int)>(values))));
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void PaddedStructArray_IsPackedWithoutPadding_AndRoundTrips(int count)
    {
        var values = new TrailingPadded[count];
        for (int i = 0; i < count; i++) values[i] = new TrailingPadded { A = long.MinValue + i, B = i * 7 - 3 };

        var bytes = ApexPackSerializer.Serialize(values);

        var expected = new List<byte>(Header(count));
        var buffer = new byte[8];
        foreach (var v in values)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer, v.A);
            expected.AddRange(buffer);
            BinaryPrimitives.WriteInt32LittleEndian(buffer, v.B);
            expected.AddRange(buffer.AsSpan(0, 4).ToArray());
        }
        Assert.Equal(expected.ToArray(), bytes);
        Assert.Equal(values, ApexPackSerializer.Deserialize<TrailingPadded[]>(bytes));
    }

    [Fact]
    public void SingleValues_RoundTripWithPackedBytes()
    {
        var tuple = (1, 2, 3);
        var tupleBytes = ApexPackSerializer.Serialize(tuple);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 }, tupleBytes);
        Assert.Equal(tuple, ApexPackSerializer.Deserialize<(int, int, int)>(tupleBytes));

        var padded = new TrailingPadded { A = 0x0102030405060708, B = -1 };
        var paddedBytes = ApexPackSerializer.Serialize(padded);
        Assert.Equal(12, paddedBytes.Length);
        Assert.Equal(padded, ApexPackSerializer.Deserialize<TrailingPadded>(paddedBytes));
        // 制約なしの経路(実行時解決)も同じ形式
        Assert.Equal(paddedBytes, ApexPackSerializer.Serialize<TrailingPadded>((object)padded is TrailingPadded p ? p : default));
    }

    [Fact]
    public void ByRefSerialization_PreservesLargeRawLayoutAndPackedLayout()
    {
        var raw = new LargeRawValue { First = 0x12345678, Last = -456 };
        byte[] bytes = ApexPackSerializer.SerializeByRef(in raw);
        Assert.Equal(520, bytes.Length);
        Assert.Equal(ApexPackSerializer.Serialize(raw), bytes);
        var restored = ApexPackSerializer.Deserialize<LargeRawValue>(bytes);
        Assert.Equal(raw.First, restored.First);
        Assert.Equal(raw.Last, restored.Last);

        var packed = new TrailingPadded { A = 0x0102030405060708, B = -1 };
        Assert.Equal(ApexPackSerializer.Serialize(packed), ApexPackSerializer.SerializeByRef(in packed));
    }

    [StructLayout(LayoutKind.Explicit, Size = 520)]
    private struct LargeRawValue
    {
        [FieldOffset(0)] public int First;
        [FieldOffset(516)] public int Last;
    }

    [Fact]
    public void LargerPaddedTuples_ReadExactlyThePackedFields()
    {
        Check((1, -2, 0x1020304, int.MinValue, 5), 1, -2, 0x1020304, int.MinValue, 5);
        Check((1, -2, 0x1020304, int.MinValue, 5, 6, int.MaxValue), 1, -2, 0x1020304, int.MinValue, 5, 6, int.MaxValue);

        static void Check<T>(T expected, params int[] fields)
        {
            byte[] packed = new byte[fields.Length * sizeof(int)];
            for (int i = 0; i < fields.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(i * sizeof(int)), fields[i]);
            }

            Assert.Equal(expected, ApexPackSerializer.Deserialize<T>(packed));
            Assert.Equal(packed, ApexPackSerializer.Serialize(expected));
            Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.Deserialize<T>(packed.AsSpan(0, packed.Length - 1)));
        }
    }

    [Fact]
    public void MultiDimensionalTupleArray_RoundTrips()
    {
        var values = new (int, int, int)[3, 5];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 5; j++)
                values[i, j] = (i, j, i * j);
        var restored = ApexPackSerializer.Deserialize<(int, int, int)[,]>(ApexPackSerializer.Serialize(values));
        Assert.Equal(values, restored);
    }
}
