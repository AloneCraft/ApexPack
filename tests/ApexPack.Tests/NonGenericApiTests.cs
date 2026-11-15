using System;
using System.Collections.Generic;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// 不変条件: Type 指定(非ジェネリック)の入口は、同じ型を型引数で指定した入口と同じバイト列を書き、同じ値を返す。
/// </summary>
public class NonGenericApiTests
{
    [Fact]
    public void SameBytesAsGeneric()
    {
        Assert.Equal(ApexPackSerializer.Serialize(42), ApexPackSerializer.Serialize(typeof(int), 42));
        Assert.Equal(ApexPackSerializer.Serialize("abc"), ApexPackSerializer.Serialize(typeof(string), "abc"));
        var list = new List<int> { 1, 2, 3 };
        Assert.Equal(ApexPackSerializer.Serialize(list), ApexPackSerializer.Serialize(typeof(List<int>), list));
        var padded = new TrailingPadded { A = 1, B = 2 };
        Assert.Equal(ApexPackSerializer.Serialize(padded), ApexPackSerializer.Serialize(typeof(TrailingPadded), padded));
    }

    [Fact]
    public void NullWritesDefault()
    {
        // 値型の null は既定値、参照型の null は null として書く
        Assert.Equal(ApexPackSerializer.Serialize(0), ApexPackSerializer.Serialize(typeof(int), null));
        Assert.Equal(ApexPackSerializer.Serialize<string?>(null), ApexPackSerializer.Serialize(typeof(string), null));
        Assert.Equal(ApexPackSerializer.Serialize<int?>(null), ApexPackSerializer.Serialize(typeof(int?), null));
    }

    [Fact]
    public void RoundTrip()
    {
        var bytes = ApexPackSerializer.Serialize(typeof(int[]), new[] { 5, 6 });
        Assert.Equal(new[] { 5, 6 }, (int[]?)ApexPackSerializer.Deserialize(typeof(int[]), bytes));
        Assert.Equal(7, ApexPackSerializer.Deserialize(typeof(int), ApexPackSerializer.Serialize(7)));
        Assert.Null(ApexPackSerializer.Deserialize(typeof(string), ApexPackSerializer.Serialize<string?>(null)));

        // 消費したバイト数は型引数つきの入口と同じ(後ろに余分なバイトがあっても読まない)
        var tail = new byte[bytes.Length + 3];
        bytes.CopyTo(tail, 0);
        ApexPackSerializer.Deserialize<int[]>(tail, out int expected);
        ApexPackSerializer.Deserialize(typeof(int[]), tail, out int consumed);
        Assert.Equal(expected, consumed);
        Assert.Equal(bytes.Length, consumed);
    }

    [Fact]
    public void Errors()
    {
        Assert.Throws<ArgumentNullException>(() => ApexPackSerializer.Serialize((Type)null!, 1));
        Assert.Throws<ArgumentNullException>(() => ApexPackSerializer.Deserialize((Type)null!, new byte[4]));
        Assert.Throws<InvalidCastException>(() => ApexPackSerializer.Serialize(typeof(int), "not int"));
    }
}
