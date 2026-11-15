using System;
using System.Collections.Generic;
using System.Linq;
using ApexPack;
using ApexPack.Buffers;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// Nullable 要素の列(存在ビットマップ + 存在する値だけ)。AVX2 のベクトル経路(値が 4 / 8 バイト)と
/// スカラー経路(それ以外、端数、BE)が同じバイト列を出すことを、手計算の期待値と照合する。
/// </summary>
public class NullableSequenceTests
{
    private static byte[] Expected<T>(T?[] values, int valueSize, Func<T, byte[]> encode) where T : struct
    {
        var bytes = new List<byte>();
        var header = new byte[5];
        int headerSize = ApexWire.WriteCompact(ref header[0], (uint)values.Length + 1, true);
        bytes.AddRange(header.Take(headerSize));
        var bitmap = new byte[(values.Length + 7) / 8];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].HasValue) bitmap[i >> 3] |= (byte)(1 << (i & 7));
        }
        bytes.AddRange(bitmap);
        foreach (var v in values)
        {
            if (!v.HasValue) continue;
            var encoded = encode(v.Value);
            Assert.Equal(valueSize, encoded.Length);
            bytes.AddRange(encoded);
        }
        return bytes.ToArray();
    }

    public static IEnumerable<object[]> Patterns()
    {
        foreach (int count in new[] { 0, 1, 7, 8, 9, 15, 16, 17, 31, 32, 33, 64, 70 })
        {
            foreach (string pattern in new[] { "none", "all", "third", "random", "edges" })
            {
                yield return new object[] { count, pattern };
            }
        }
    }

    private static bool Present(string pattern, int i, int count, Random random) => pattern switch
    {
        "none" => false,
        "all" => true,
        "third" => i % 3 != 0,
        "edges" => i == 0 || i == count - 1 || i % 8 == 7,
        _ => random.Next(2) == 0,
    };

    private static T?[] Build<T>(int count, string pattern, Func<int, T> value) where T : struct
    {
        var random = new Random(count * 31 + pattern.Length);
        var values = new T?[count];
        for (int i = 0; i < count; i++) values[i] = Present(pattern, i, count, random) ? value(i) : null;
        return values;
    }

    private static void Check<T>(T?[] values, int size, Func<T, byte[]> encode) where T : struct
    {
        byte[] actual = ApexPackSerializer.Serialize(values);
        Assert.Equal(Expected(values, size, encode), actual);
        Assert.Equal(values, ApexPackSerializer.Deserialize<T?[]>(actual));

        var list = values.ToList();
        Assert.Equal(actual, ApexPackSerializer.Serialize(list));
        Assert.Equal(list, ApexPackSerializer.Deserialize<List<T?>>(actual));
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Int32(int count, string pattern)
        => Check(Build(count, pattern, i => i * 7 - 100), 4, BitConverter.GetBytes);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Float(int count, string pattern)
        => Check(Build(count, pattern, i => i * 0.5f), 4, BitConverter.GetBytes);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Int64(int count, string pattern)
        => Check(Build(count, pattern, i => (long)i << 40 | (uint)i), 8, BitConverter.GetBytes);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void DateTime_(int count, string pattern)
        => Check(Build(count, pattern, i => new DateTime(2026, 1, 1).AddMinutes(i)), 8, d => BitConverter.GetBytes(d.ToBinary()));

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Int16_ScalarPath(int count, string pattern)
        => Check(Build(count, pattern, i => (short)(i - 30)), 2, BitConverter.GetBytes);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Guid_ScalarPath(int count, string pattern)
        => Check(Build(count, pattern, i => new Guid(i, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)), 16, g => g.ToByteArray());

    [Fact]
    public void NonCanonicalPadding_IsIgnored()
    {
        // Nullable<int> の詰め物バイトに値が入っていても、HasValue は先頭バイトだけで判定する
        var values = new int?[16];
        for (int i = 0; i < values.Length; i += 2) values[i] = i;
        ref byte raw = ref System.Runtime.CompilerServices.Unsafe.As<int?, byte>(ref values[1]);
        System.Runtime.CompilerServices.Unsafe.Add(ref raw, 1) = 0xAA; // null 要素の詰め物
        Check(values, 4, BitConverter.GetBytes);
    }

    [Fact]
    public void ShortValueArea_IsRejectedBeforeAllocation()
    {
        // 件数 100 万を名乗るが、ビットマップの分がない
        byte[] bytes = { 0xFE, 0x41, 0x42, 0x0F, 0x00, 0xFF };
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Deserialize<int?[]>(bytes));
    }

    [Fact]
    public void TrailingBitmapBits_AreRejected()
    {
        // 件数 3 なのにビットマップの 4 ビット目以降が立っている
        byte[] bytes = new byte[2 + 12];
        bytes[0] = 4;
        bytes[1] = 0b1111_1000;
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Deserialize<int?[]>(bytes));
    }
}
