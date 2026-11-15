using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ApexPack;
using Xunit;

namespace ApexPack.Tests;

[ApexPackable]
public partial class BclContainer
{
    public string?[]? Tags { get; set; }
    public List<string?>? Names { get; set; }
    public Dictionary<string, int>? Scores { get; set; }
    public ImmutableArray<int> Numbers { get; set; }
}

public class StandardLibraryTypesTests
{
    [Fact]
    public void BclCollections_Container_Roundtrip()
    {
        var container = new BclContainer
        {
            Tags = new[] { "Alpha", "Beta", null, "Delta 富士山" },
            Names = new List<string?> { "Alice", "Bob", null },
            Scores = new Dictionary<string, int>
            {
                ["Speed"] = 100,
                ["Memory"] = 99
            },
            Numbers = ImmutableArray.Create(1, 2, 3, 5, 8, 13, 21)
        };

        byte[] bytes = ApexPackSerializer.Serialize(container);
        var restored = ApexPackSerializer.Deserialize<BclContainer>(bytes);

        Assert.NotNull(restored);
        Assert.NotNull(restored.Tags);
        Assert.Equal(4, restored.Tags.Length);
        Assert.Equal("Alpha", restored.Tags[0]);
        Assert.Equal("Delta 富士山", restored.Tags[3]);

        Assert.NotNull(restored.Names);
        Assert.Equal(3, restored.Names.Count);
        Assert.Equal("Bob", restored.Names[1]);

        Assert.NotNull(restored.Scores);
        Assert.Equal(2, restored.Scores.Count);
        Assert.Equal(100, restored.Scores["Speed"]);
        Assert.Equal(99, restored.Scores["Memory"]);

        Assert.False(restored.Numbers.IsDefault);
        Assert.Equal(7, restored.Numbers.Length);
        Assert.Equal(13, restored.Numbers[5]);
    }
}
