using System;
using System.Collections.Generic;
using Xunit;
using ApexPack;
using ApexPack.Parallel;

namespace ApexPack.Tests;

public class ParallelTest
{
    [Fact]
    public void ParallelSerialization_LargeDataset_MatchesSequentialCorrectness()
    {
        const int count = 20_000;
        var list = new SimpleEntity[count];
        for (int i = 0; i < count; i++)
        {
            list[i] = new SimpleEntity
            {
                Id = i,
                Name = $"Entity_{i}",
                Health = i * 1.5
            };
        }

        // Parallel chunk serialization
        byte[] parallelBytes = ApexPackParallel.Serialize(list, chunkSize: 2048);
        Assert.NotNull(parallelBytes);
        Assert.True(parallelBytes.Length > 0);

        // Parallel chunk deserialization
        var deserialized = ApexPackParallel.Deserialize<SimpleEntity>(parallelBytes, chunkSize: 2048);

        Assert.Equal(count, deserialized.Length);
        for (int i = 0; i < count; i += 137) // sample check across chunks
        {
            Assert.Equal(list[i].Id, deserialized[i].Id);
            Assert.Equal(list[i].Name, deserialized[i].Name);
            Assert.Equal(list[i].Health, deserialized[i].Health);
        }
    }
}
