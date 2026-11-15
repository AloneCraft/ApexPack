using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ApexPack.Tests;

[ApexPackable]
public partial class ElementItem
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ElementItem? Child { get; set; }
}

/// <summary>
/// 不変条件: クラスの要素の並び(配列・List)は、追跡なし・信頼済みの読み取りでは型ごとの生成コードがまとめて読む。
/// まとめて読む経路と、要素ごとに読む経路(安全な読み取り)が同じ値を返し、null の要素や入れ子もそのまま戻る。
/// </summary>
public class ClassElementArrayTests
{
    public static IEnumerable<object[]> Counts() => new[] { 0, 1, 2, 3, 17 }.Select(n => new object[] { n });

    private static ElementItem?[] Make(int count)
        => Enumerable.Range(0, count)
            .Select(i => i % 4 == 2 ? null : new ElementItem { Id = i, Name = i % 3 == 0 ? null : "n" + i, Child = i % 2 == 0 ? new ElementItem { Id = -i } : null })
            .ToArray();

    private static void AssertSame(IReadOnlyList<ElementItem?> expected, IReadOnlyList<ElementItem?>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Count, actual!.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            if (e is null) { Assert.Null(a); continue; }
            Assert.NotNull(a);
            Assert.Equal(e.Id, a!.Id);
            Assert.Equal(e.Name, a.Name);
            Assert.Equal(e.Child?.Id, a.Child?.Id);
            Assert.Equal(e.Child is null, a.Child is null);
        }
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Array_RoundTrips_OnBothPaths(int count)
    {
        var values = Make(count);
        var bytes = ApexPackSerializer.Serialize(values);
        AssertSame(values, ApexPackSerializer.Deserialize<ElementItem?[]>(bytes));
        AssertSame(values, ApexPackSerializer.DeserializeSafe<ElementItem?[]>(bytes, ApexSecurityOptions.Default));
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void List_RoundTrips_OnBothPaths(int count)
    {
        var values = Make(count).ToList();
        var bytes = ApexPackSerializer.Serialize(values);
        AssertSame(values, ApexPackSerializer.Deserialize<List<ElementItem?>>(bytes));
        AssertSame(values, ApexPackSerializer.DeserializeSafe<List<ElementItem?>>(bytes, ApexSecurityOptions.Default));
    }

    [Fact]
    public void Truncated_Throws()
    {
        var bytes = ApexPackSerializer.Serialize(Make(5));
        Assert.ThrowsAny<Exception>(() => ApexPackSerializer.Deserialize<ElementItem?[]>(bytes.AsSpan(0, bytes.Length - 1)));
    }
}
