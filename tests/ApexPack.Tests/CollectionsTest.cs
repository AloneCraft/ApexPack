using System;
using System.Collections.Generic;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class CollectionsTest
{
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
