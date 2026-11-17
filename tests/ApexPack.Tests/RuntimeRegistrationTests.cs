using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ApexPack.Buffers;
using Xunit;

namespace ApexPack.Tests;

// ---- 実行時登録・参照追跡・再帰の上限など、MemoryPack 公式テストとの比較で足した機能の不変条件 ----

public class RegBox<T>
{
    public T? Value { get; set; }
}

/// <summary>ジェネリック型の一括登録に使う手書きフォーマッタ(null は 0、値は 1 + 中身)。</summary>
public sealed class RegBoxFormatter<T> : ApexFormatter<RegBox<T>>
{
    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in RegBox<T> value, ref TTracker tracker)
    {
        if (value is null)
        {
            writer.WriteByte(0);
            return;
        }
        writer.WriteByte(1);
        var inner = value.Value!;
        ApexFormatterCache<T>.Instance.Write(ref writer, in inner, ref tracker);
    }

    public override RegBox<T> Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
    {
        if (reader.ReadByte() == 0) return null!;
        return new RegBox<T> { Value = ApexFormatterCache<T>.Instance.Read(ref reader, ref tracker, ref guard) };
    }
}

public class RegFactoryBox<T>
{
    public T? Value { get; set; }
}

public interface IRegDynamic
{
}

[ApexPackable]
public partial class RegDynamicA : IRegDynamic
{
    public int A { get; set; }
}

[ApexPackable]
public partial class RegDynamicB : IRegDynamic
{
    public string? B { get; set; }
}

public interface IRegGenericDynamic<T>
{
}

[ApexPackable]
public partial class RegGenericCase<T> : IRegGenericDynamic<T>
{
    public T? Value { get; set; }
}

[ApexPackable(PreserveReferences = true)]
public partial class RegSharedNode
{
    public int Id { get; set; }
    public RegSharedNode? Parent { get; set; }
}

/// <summary>参照保持の型を含むだけの型(自分は PreserveReferences ではない)。</summary>
[ApexPackable]
public partial class RegHolder
{
    public List<RegSharedNode>? Nodes { get; set; }
    public RegSharedNode? Single { get; set; }
}

[ApexPackable]
public partial class RegCycle
{
    public int Value { get; set; }
    public RegCycle? Next { get; set; }
}

public struct CountingBufferWriter : IBufferWriter<byte>
{
    public int Written;

    public void Advance(int count) => Written += count;

    public Memory<byte> GetMemory(int sizeHint = 0) => new byte[Math.Max(sizeHint, 16)];

    public Span<byte> GetSpan(int sizeHint = 0) => new byte[Math.Max(sizeHint, 16)];
}

public class RuntimeRegistrationTests
{
    [Fact]
    public void RegisterGeneric_AppliesToEveryClosedType()
    {
        ApexFormatterRegistry.RegisterGeneric(typeof(RegBox<>), typeof(RegBoxFormatter<>));

        var a = ApexPackSerializer.Deserialize<RegBox<int>>(ApexPackSerializer.Serialize(new RegBox<int> { Value = 42 }));
        var b = ApexPackSerializer.Deserialize<RegBox<string>>(ApexPackSerializer.Serialize(new RegBox<string> { Value = "x" }));
        Assert.Equal(42, a.Value);
        Assert.Equal("x", b.Value);
        // 形式は登録したフォーマッタのもの(1 + int)
        Assert.Equal(new byte[] { 1, 42, 0, 0, 0 }, ApexPackSerializer.Serialize(new RegBox<int> { Value = 42 }));
    }

    [Fact]
    public void RegisterGeneric_RejectsClosedOrMismatchedTypes()
    {
        Assert.Throws<ArgumentException>(() => ApexFormatterRegistry.RegisterGeneric(typeof(RegBox<int>), typeof(RegBoxFormatter<>)));
        Assert.Throws<ArgumentException>(() => ApexFormatterRegistry.RegisterGeneric(typeof(RegBox<>), typeof(Dictionary<,>)));
    }

    [Fact]
    public void RegisterGenericFactory_ReceivesTheClosedType()
    {
        Type? seen = null;
        ApexFormatterRegistry.RegisterGenericFactory(typeof(RegFactoryBox<>), closed =>
        {
            seen = closed;
            return new FactoryBoxFormatter();
        });
        var value = ApexPackSerializer.Deserialize<RegFactoryBox<long>>(ApexPackSerializer.Serialize(new RegFactoryBox<long> { Value = 7 }));
        Assert.Equal(typeof(RegFactoryBox<long>), seen);
        Assert.Equal(7, value.Value);
    }

    private sealed class FactoryBoxFormatter : ApexFormatter<RegFactoryBox<long>>
    {
        public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in RegFactoryBox<long> value, ref TTracker tracker)
            => writer.WriteInt64(value.Value);

        public override RegFactoryBox<long> Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
            => new() { Value = reader.ReadInt64() };
    }

    [Fact]
    public void DynamicUnion_AddRemove()
    {
        var formatter = new ApexDynamicUnionFormatter<IRegDynamic>((0, typeof(RegDynamicA)));
        Assert.True(ApexFormatterCache<IRegDynamic>.Register(formatter));

        var a = ApexPackSerializer.Deserialize<IRegDynamic>(ApexPackSerializer.Serialize<IRegDynamic>(new RegDynamicA { A = 5 }));
        Assert.Equal(5, Assert.IsType<RegDynamicA>(a).A);

        // 登録していない具象型は書けない
        Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.Serialize<IRegDynamic>(new RegDynamicB { B = "b" }));

        formatter.Add(1, typeof(RegDynamicB));
        var b = ApexPackSerializer.Deserialize<IRegDynamic>(ApexPackSerializer.Serialize<IRegDynamic>(new RegDynamicB { B = "b" }));
        Assert.Equal("b", Assert.IsType<RegDynamicB>(b).B);

        // null は 0
        Assert.Equal(new byte[] { 0 }, ApexPackSerializer.Serialize<IRegDynamic>(null!));

        // 外した型は書けず、そのタグは読めない
        var bytesB = ApexPackSerializer.Serialize<IRegDynamic>(new RegDynamicB { B = "b" });
        Assert.True(formatter.Remove(typeof(RegDynamicB)));
        Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.Serialize<IRegDynamic>(new RegDynamicB { B = "b" }));
        Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.Deserialize<IRegDynamic>(bytesB));

        Assert.Throws<ArgumentException>(() => formatter.Add(0, typeof(RegDynamicB))); // タグの重複
        Assert.Throws<ArgumentException>(() => formatter.Add(9, typeof(string)));     // 基底型に代入できない
    }

    [Fact]
    public void DynamicUnion_OpenGeneric_ClosesWithTheSameArguments()
    {
        ApexDynamicUnion.RegisterGeneric(typeof(IRegGenericDynamic<>), (0, typeof(RegGenericCase<>)));
        var i = ApexPackSerializer.Deserialize<IRegGenericDynamic<int>>(ApexPackSerializer.Serialize<IRegGenericDynamic<int>>(new RegGenericCase<int> { Value = 3 }));
        var s = ApexPackSerializer.Deserialize<IRegGenericDynamic<string>>(ApexPackSerializer.Serialize<IRegGenericDynamic<string>>(new RegGenericCase<string> { Value = "s" }));
        Assert.Equal(3, Assert.IsType<RegGenericCase<int>>(i).Value);
        Assert.Equal("s", Assert.IsType<RegGenericCase<string>>(s).Value);
    }

    [Fact]
    public void ReferenceTracking_StartsAtRoot_WhenAMemberTypePreservesReferences()
    {
        var parent = new RegSharedNode { Id = 1 };
        var child = new RegSharedNode { Id = 2, Parent = parent };
        var holder = new RegHolder { Nodes = new List<RegSharedNode> { parent, child, parent }, Single = child };

        var result = ApexPackSerializer.Deserialize<RegHolder>(ApexPackSerializer.Serialize(holder));

        // 兄弟(リストの要素同士、別のメンバー)の間でも同じインスタンスになる
        Assert.Same(result.Nodes![0], result.Nodes[2]);
        Assert.Same(result.Nodes[0], result.Nodes[1].Parent);
        Assert.Same(result.Nodes[1], result.Single);
    }

    [Fact]
    public void ReferenceTracking_StartsAtRoot_ForGenericCollectionsOfPreservingTypes()
    {
        var node = new RegSharedNode { Id = 1 };
        var list = new List<RegSharedNode> { node, node };
        var result = ApexPackSerializer.Deserialize<List<RegSharedNode>>(ApexPackSerializer.Serialize(list));
        Assert.Same(result[0], result[1]);
    }

    [Fact]
    public void CyclicGraph_WithoutPreserveReferences_ThrowsInsteadOfCrashing()
    {
        var a = new RegCycle { Value = 1 };
        a.Next = a;
        // スタックあふれ(プロセスが落ちる)ではなく、例外で止まる
        var error = Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Serialize(a));
        Assert.Contains("PreserveReferences", error.Message);

        // 深いが循環していないグラフは書ける
        var head = new RegCycle { Value = 0 };
        var current = head;
        for (int i = 1; i < 500; i++) current = current.Next = new RegCycle { Value = i };
        var restored = ApexPackSerializer.Deserialize<RegCycle>(ApexPackSerializer.Serialize(head));
        Assert.Equal(1, restored.Next!.Value);
    }

    [Fact]
    public void FrozenCollections_RoundTrip()
    {
        var set = new[] { 1, 2, 3 }.ToFrozenSet();
        var dictionary = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.ToFrozenDictionary();
        var set2 = ApexPackSerializer.Deserialize<FrozenSet<int>>(ApexPackSerializer.Serialize(set));
        var dictionary2 = ApexPackSerializer.Deserialize<FrozenDictionary<string, int>>(ApexPackSerializer.Serialize(dictionary));
        Assert.True(set2.SetEquals(set));
        Assert.Equal(2, dictionary2["b"]);
        Assert.Null(ApexPackSerializer.Deserialize<FrozenSet<int>?>(ApexPackSerializer.Serialize<FrozenSet<int>?>(null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(9)]
    public void FrozenSet_FromCollectionWireFormat_HandlesDuplicatesAndNull(int count)
    {
        var elements = new string?[count];
        for (int i = 1; i < count; i += 2)
        {
            elements[i] = "item";
        }

        byte[] bytes = ApexPackSerializer.Serialize(elements);
        var result = ApexPackSerializer.Deserialize<FrozenSet<string?>>(bytes);

        Assert.True(result.SetEquals(elements));
        Assert.Equal(count == 0 ? 0 : 2, result.Count);
    }

    [Fact]
    public async Task DeserializeAsync_MemoryStream_AdvancesByConsumedBytes()
    {
        var stream = new MemoryStream();
        await ApexPackSerializer.SerializeAsync(stream, new[] { 1, 2, 3 });
        await ApexPackSerializer.SerializeAsync(stream, new[] { 4, 5 });
        stream.Position = 0;
        Assert.Equal(new[] { 1, 2, 3 }, await ApexPackSerializer.DeserializeAsync<int[]>(stream));
        Assert.Equal(new[] { 4, 5 }, await ApexPackSerializer.DeserializeAsync<int[]>(stream));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Serialize_ToStructBufferWriter_UpdatesCallersVariable()
    {
        var writer = new CountingBufferWriter();
        ApexPackSerializer.Serialize(ref writer, new RegDynamicA { A = 1 });
        Assert.Equal(ApexPackSerializer.Serialize(new RegDynamicA { A = 1 }).Length, writer.Written);
    }

    [Fact]
    public void AnonymousType_IsRejectedWithAClearMessage()
    {
        var error = Assert.Throws<NotSupportedException>(() => ApexPackSerializer.Serialize(new { A = 1 }));
        Assert.Contains("anonymous type", error.Message);
    }
}
