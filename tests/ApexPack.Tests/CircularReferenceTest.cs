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
}
