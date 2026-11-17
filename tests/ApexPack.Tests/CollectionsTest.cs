using System;
using System.Buffers;
using System.Collections.Generic;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class CollectionsTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleReferenceArray_PreservesWireValueAndChecksLimits(bool nullElement)
    {
        byte[] bytes = ApexPackSerializer.Serialize(new Leaf?[] { nullElement ? null : new Leaf { Value = 42 } });
        byte[] input = new byte[bytes.Length + 1];
        bytes.CopyTo(input, 0);
        var restored = Assert.IsType<Leaf?[]>(ApexPackSerializer.Deserialize(typeof(Leaf[]), input, out int consumed));

        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(nullElement ? null : 42, Assert.Single(restored)?.Value);
        Assert.Equal(bytes, ApexPackSerializer.Serialize(restored));
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Deserialize<Leaf?[]>(bytes.AsSpan(0, 1)));
        var limit = new ApexSecurityOptions { MaxCollectionLength = 0, MaxTotalElements = 1, MaxDepth = 8 };
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeSafe<Leaf?[]>(bytes, in limit));
    }

    [Fact]
    public void SizedClassArray_MatchesBufferedWrite()
    {
        var item = new GenericArrayElement<short> { Value = 0x1234 };
        Assert.Equal(new byte[] { 2, 1, 0x34, 0x12 }, AssertArrayWire<GenericArrayElement<short>>([item]));
        AssertArrayWire<GenericArrayElement<short>>([null]);
        AssertArrayWire<GenericArrayElement<short>>([item, null]);
        AssertArrayWire<GenericArrayElement<short>>([]);
        AssertArrayWire<GenericArrayElement<short>>(null);
        AssertArrayWire<GenericArrayElement<string>>([new() { Value = "variable" }]);
    }

    [Fact]
    public void SizedClassArray_WithTrackedElements_PreservesIdentity()
    {
        var value = new TrackedArrayElement { Value = true };
        var bytes = AssertArrayWire<TrackedArrayElement>([value, value]);
        var restored = ApexPackSerializer.Deserialize<TrackedArrayElement[]>(bytes);
        Assert.Same(restored[0], restored[1]);
    }

    private static byte[] AssertArrayWire<T>(T?[]? value) where T : class
    {
        var buffer = new ArrayBufferWriter<byte>();
        ApexPackSerializer.Serialize(buffer, value);
        byte[] expected = buffer.WrittenSpan.ToArray();
        Assert.Equal(expected, ApexPackSerializer.Serialize(value));
        Assert.Equal(expected, ApexPackSerializer.Serialize(typeof(T[]), value));
        var restored = ApexPackSerializer.Deserialize<T?[]?>(expected, out int consumed);
        Assert.Equal(expected.Length, consumed);
        Assert.Equal(expected, ApexPackSerializer.Serialize(restored));
        return expected;
    }

    [Fact]
    public void EmptyRectangularArray_FitsExactlyInItsWireSize()
    {
        var value = new int[2, 0, 253];
        byte[] expected = { 3, 0, 0xFD, 0xFD, 0 };
        Assert.Equal(expected, ApexPackSerializer.Serialize(value));

        Span<byte> destination = stackalloc byte[5];
        Assert.Equal(expected.Length, ApexPackSerializer.Serialize(destination, value));
        Assert.Equal(expected, destination.ToArray());

        var restored = ApexPackSerializer.Deserialize<int[,,]>(expected);
        Assert.Equal(2, restored.GetLength(0));
        Assert.Equal(0, restored.GetLength(1));
        Assert.Equal(253, restored.GetLength(2));
    }

    [Fact]
    public void UnmanagedAndEntityCollections_Roundtrip_Succeeds()
    {
        var model = new CollectionContainerModel
        {
            UnmanagedArray = new[] { 10, 20, 30, 40, 50 },
            UnmanagedList = new List<int> { 100, 200, 300, 400, 500 },
            EntityList = new List<SimpleEntity>
            {
                new SimpleEntity { Id = 1, Name = "Alpha", Health = 100 },
                new SimpleEntity { Id = 2, Name = "Beta", Health = 85.5 }
            }
        };

        byte[] bytes = ApexPackSerializer.Serialize(model);
        var restored = ApexPackSerializer.Deserialize<CollectionContainerModel>(bytes);

        Assert.NotNull(restored.UnmanagedArray);
        Assert.Equal(model.UnmanagedArray, restored.UnmanagedArray);

        Assert.NotNull(restored.UnmanagedList);
        Assert.Equal(model.UnmanagedList, restored.UnmanagedList);

        Assert.NotNull(restored.EntityList);
        Assert.Equal(2, restored.EntityList.Count);
        Assert.Equal("Alpha", restored.EntityList[0].Name);
        Assert.Equal(85.5, restored.EntityList[1].Health);
    }

    [Fact]
    public void NullAndEmptyCollections_HandledGracefully()
    {
        var emptyModel = new CollectionContainerModel
        {
            UnmanagedArray = Array.Empty<int>(),
            UnmanagedList = new List<int>(0),
            EntityList = new List<SimpleEntity>(0)
        };

        byte[] bytes = ApexPackSerializer.Serialize(emptyModel);
        var restored = ApexPackSerializer.Deserialize<CollectionContainerModel>(bytes);

        Assert.NotNull(restored.UnmanagedArray);
        Assert.Empty(restored.UnmanagedArray);
        Assert.NotNull(restored.UnmanagedList);
        Assert.Empty(restored.UnmanagedList);
        Assert.NotNull(restored.EntityList);
        Assert.Empty(restored.EntityList);
    }
}

[ApexPackable]
internal partial class GenericArrayElement<T>
{
    public T Value { get; set; } = default!;
}

[ApexPackable(PreserveReferences = true)]
internal partial class TrackedArrayElement
{
    public bool Value { get; set; }
}
