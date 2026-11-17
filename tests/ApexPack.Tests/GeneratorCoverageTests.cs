using System;
using System.Collections.Generic;
using ApexPack;
using ApexPack.Buffers;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// ジェネレーターが扱う型宣言・メンバー型の組み合わせ。v1 ではコンパイルできなかったものを含む。
/// </summary>
public class GeneratorCoverageTests
{
    private static T Roundtrip<T>(T value) where T : ISerializePackable<T>
        => ApexPackSerializer.Deserialize<T>(ApexPackSerializer.Serialize(value));

    [Fact]
    public void NestedGenericCollections()
    {
        var value = new NestedCollections
        {
            IntToString = new Dictionary<int, string> { [1] = "one", [2] = "二" },
            ListOfLists = new List<List<int>> { new() { 1, 2 }, new(), new() { 3 } },
            Jagged = new[] { new[] { 1, 2 }, Array.Empty<int>(), new[] { 3 } },
            DictOfLeaves = new Dictionary<string, Leaf> { ["a"] = new Leaf { Value = 1 } },
            NullableLeaves = new List<Leaf?> { new Leaf { Value = 2 }, null },
            Matrix = new int[,] { { 1, 2, 3 }, { 4, 5, 6 } },
            Tuple = (1, "x", 2.5),
        };
        var r = Roundtrip(value);
        Assert.Equal(value.IntToString, r.IntToString);
        Assert.Equal(value.ListOfLists, r.ListOfLists);
        Assert.Equal(value.Jagged, r.Jagged);
        Assert.Equal(1, r.DictOfLeaves!["a"].Value);
        Assert.Equal(2, r.NullableLeaves![0]!.Value);
        Assert.Null(r.NullableLeaves[1]);
        Assert.Equal(value.Matrix, r.Matrix);
        Assert.Equal(value.Tuple, r.Tuple);
    }

    [Fact]
    public void NestedType_And_GenericType()
    {
        var value = new Outer.Inner { Name = "in" };
        Assert.Equal("in", Roundtrip(value).Name);

        var box = new Box<int> { Value = 42, Items = new List<int> { 1, 2 } };
        var restored = Roundtrip(box);
        Assert.Equal(42, restored.Value);
        Assert.Equal(new List<int> { 1, 2 }, restored.Items);

        var boxOfLeaf = new Box<Leaf> { Value = new Leaf { Value = 9 } };
        Assert.Equal(9, Roundtrip(boxOfLeaf).Value!.Value);
    }

    [Fact]
    public void Records_And_Constructors()
    {
        var point = new PointRecord(3, "p");
        Assert.Equal(point, Roundtrip(point));

        var size = new SizeRecordStruct(4, 5);
        Assert.Equal(size, Roundtrip(size));

        var withCtor = new CtorSelected(7, "seven") { Extra = 1 };
        var r = Roundtrip(withCtor);
        Assert.Equal(7, r.Id);
        Assert.Equal("seven", r.Label);
        Assert.Equal(1, r.Extra);
        Assert.True(r.ConstructedByAttributeCtor);
    }

    [Fact]
    public void InitRequiredAndPrivateMembers()
    {
        var value = new InitAndPrivate { Required = "req", Init = 5 };
        value.SetSecret(99);
        var r = Roundtrip(value);
        Assert.Equal("req", r.Required);
        Assert.Equal(5, r.Init);
        Assert.Equal(99, r.GetSecret());
    }

    [Fact]
    public void ExplicitOrder_DeterminesWireLayout()
    {
        // Second(=1) が先、First(=2) が後
        byte[] bytes = ApexPackSerializer.Serialize(new Ordered { First = 1, Second = 2 });
        Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 1, 0, 0, 0 }, bytes);
    }

    [Fact]
    public void Callbacks_AreInvoked()
    {
        var value = new WithCallbacks { Value = 1 };
        var bytes = ApexPackSerializer.Serialize(value);
        Assert.Equal(new[] { "serializing", "serialized" }, value.Log);
        WithCallbacks.StaticLog.Clear();
        var r = ApexPackSerializer.Deserialize<WithCallbacks>(bytes);
        Assert.Equal(new[] { "deserializing" }, WithCallbacks.StaticLog);
        Assert.Equal(new[] { "deserialized" }, r.Log);
    }

    [Fact]
    public void CollectionGenerateType()
    {
        var list = new TagList { "a", "b" };
        var r = Roundtrip(list);
        Assert.Equal(new[] { "a", "b" }, r);
    }

    [Fact]
    public void CustomFormatterAttribute()
    {
        var r = Roundtrip(new WithCustomFormatter { Celsius = 21.5 });
        Assert.Equal(21.5, r.Celsius);
        Assert.Equal(2, ApexPackSerializer.Serialize(new WithCustomFormatter { Celsius = 21.5 }).Length - 1); // 1 + short(2)
    }

    [Fact]
    public void PreserveReferencesAttribute_TracksAutomatically()
    {
        var a = new SelfLinked { Id = 1 };
        var b = new SelfLinked { Id = 2, Next = a };
        a.Next = b;
        var r = ApexPackSerializer.Deserialize<SelfLinked>(ApexPackSerializer.Serialize(a));
        Assert.Same(r, r.Next!.Next);
    }

    [Fact]
    public void UnionWithDeeperDerivedType_MatchesMostDerived()
    {
        Animal value = new Puppy { Name = "pochi", Age = 1 };
        var r = ApexPackSerializer.Deserialize<Animal>(ApexPackSerializer.Serialize(value));
        var puppy = Assert.IsType<Puppy>(r);
        Assert.Equal(1, puppy.Age);
    }

    [Fact]
    public void GenericInterfaceUnion_ClosesTheSealedCase()
    {
        IGeneratorUnion<int> value = new GeneratorUnionCase<int> { Value = 42 };
        var restored = Assert.IsType<GeneratorUnionCase<int>>(Roundtrip(value));
        Assert.Equal(42, restored.Value);
    }
}

[ApexPackable]
public partial class NestedCollections
{
    public Dictionary<int, string>? IntToString { get; set; }
    public List<List<int>>? ListOfLists { get; set; }
    public int[][]? Jagged { get; set; }
    public Dictionary<string, Leaf>? DictOfLeaves { get; set; }
    public List<Leaf?>? NullableLeaves { get; set; }
    public int[,]? Matrix { get; set; }
    public (int, string, double) Tuple { get; set; }
}

public partial class Outer
{
    [ApexPackable]
    public partial class Inner
    {
        public string Name { get; set; } = "";
    }
}

[ApexPackable]
public partial class Box<T>
{
    public T? Value { get; set; }
    public List<T>? Items { get; set; }
}

[ApexPackable]
public partial record PointRecord(int X, string Name);

[ApexPackable]
public partial record struct SizeRecordStruct(int Width, int Height);

[ApexPackable]
public partial class CtorSelected
{
    public CtorSelected() => Label = "";

    [ApexConstructor]
    public CtorSelected(int id, string label)
    {
        Id = id;
        Label = label;
        ConstructedByAttributeCtor = true;
    }

    public int Id { get; }
    public string Label { get; }
    public int Extra { get; set; }

    [ApexIgnore]
    public bool ConstructedByAttributeCtor { get; }
}

[ApexPackable]
public partial class InitAndPrivate
{
    public required string Required { get; init; }
    public int Init { get; init; }

    [ApexInclude]
    private int _secret;

    public void SetSecret(int value) => _secret = value;
    public int GetSecret() => _secret;
}

[ApexPackable]
public partial class Ordered
{
    [ApexOrder(2)] public int First { get; set; }
    [ApexOrder(1)] public int Second { get; set; }
}

[ApexPackable]
public partial class WithCallbacks
{
    public static readonly List<string> StaticLog = new();

    public int Value { get; set; }

    [ApexIgnore]
    public List<string> Log { get; } = new();

    [ApexOnSerializing] private void OnSerializing() => Log.Add("serializing");
    [ApexOnSerialized] private void OnSerialized() => Log.Add("serialized");
    [ApexOnDeserializing] private static void OnDeserializing() => StaticLog.Add("deserializing");
    [ApexOnDeserialized] private void OnDeserialized() => Log.Add("deserialized");
}

[ApexPackable(GenerateType = ApexGenerateType.Collection)]
public partial class TagList : List<string>
{
}

/// <summary>温度を 0.01 度単位の short で書く独自フォーマッタ。</summary>
public readonly struct CentiDegreeFormatter : IApexFormatter<double>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in double value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => writer.WriteInt16((short)Math.Round(value * 100));

    public static double Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        => reader.ReadInt16() / 100.0;
}

[ApexPackable]
public partial class WithCustomFormatter
{
    [ApexFormatter(typeof(CentiDegreeFormatter))]
    public double Celsius { get; set; }
}

[ApexPackable(PreserveReferences = true)]
public partial class SelfLinked
{
    public int Id { get; set; }
    public SelfLinked? Next { get; set; }
}

[ApexPackable]
[ApexUnion(0, typeof(Dog))]
[ApexUnion(1, typeof(Puppy))]
public abstract partial class Animal
{
    public string Name { get; set; } = "";
}

[ApexPackable]
public partial class Dog : Animal
{
}

[ApexPackable]
public sealed partial class Puppy : Dog
{
    public int Age { get; set; }
}

[ApexPackable]
[ApexUnion(0, typeof(GeneratorUnionCase<>))]
public partial interface IGeneratorUnion<T>
{
}

[ApexPackable]
public sealed partial class GeneratorUnionCase<T> : IGeneratorUnion<T>
{
    public T? Value { get; set; }
}
