using System;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class CircularReferenceTest
{
    [Fact]
    public void StandardDag_WithoutTracking_WorksFast()
    {
        var node1 = new Node { Value = 1 };
        var node2 = new Node { Value = 2 };
        node1.Next = node2;

        byte[] bytes = ApexPackSerializer.Serialize(node1);
        var result = ApexPackSerializer.Deserialize<Node>(bytes);

        Assert.Equal(1, result.Value);
        Assert.NotNull(result.Next);
        Assert.Equal(2, result.Next.Value);
        Assert.Null(result.Next.Next);
    }

    [Fact]
    public void CircularReference_WithOptInTracking_PreservesIdentity()
    {
        var nodeA = new Node { Value = 100 };
        var nodeB = new Node { Value = 200 };
        nodeA.Next = nodeB;
        nodeB.Next = nodeA; // Circular reference: A -> B -> A

        byte[] bytes = ApexPackSerializer.SerializeWithReferences(nodeA);
        var restoredA = ApexPackSerializer.DeserializeWithReferences<Node>(bytes);

        Assert.Equal(100, restoredA.Value);
        Assert.NotNull(restoredA.Next);
        Assert.Equal(200, restoredA.Next.Value);
        Assert.NotNull(restoredA.Next.Next);
        // Identity preserved!
        Assert.Same(restoredA, restoredA.Next.Next);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(40, 35)]
    public void CircularReference_ResolvesBackReferenceAcrossTrackerCapacity(int count, int backReference)
    {
        var nodes = new Node[count];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new Node { Value = i };
            if (i > 0) nodes[i - 1].Next = nodes[i];
        }
        nodes[^1].Next = nodes[backReference];

        byte[] bytes = ApexPackSerializer.SerializeWithReferences(nodes[0]);
        Node? current = ApexPackSerializer.DeserializeWithReferences<Node>(bytes);

        Node? expectedReference = null;
        for (int i = 0; i < count; i++)
        {
            Assert.NotNull(current);
            Assert.Equal(i, current.Value);
            if (i == backReference) expectedReference = current;
            current = current.Next;
        }
        Assert.Same(expectedReference, current);
    }

    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void CircularReference_RejectsBackReferenceBeforeRegistration(byte header)
    {
        byte[] bytes = [header];
        Assert.Throws<InvalidOperationException>(() => ApexPackSerializer.DeserializeWithReferences<Node>(bytes));
    }
}
