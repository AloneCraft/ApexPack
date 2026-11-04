using System;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class BitPackingTest
{
    [Fact]
    public void BitPackedEntity_AllNullAndFalse_PayloadIsExtremelyCompact()
    {
        var entity = new BitPackedEntity
        {
            Id = 100,
            Flag1 = false,
            Flag2 = false,
            Flag3 = false,
            Flag4 = false,
            NullableInt1 = null,
            NullableInt2 = null,
            OptionalString1 = null,
            OptionalString2 = null
        };

        byte[] bytes = ApexPackSerializer.Serialize(entity);

        // Expected size:
        // 1 byte (class tag: 1)
        // 1 byte (bitmask for 8 bit-packable members)
        // 4 bytes (int Id)
        // 0 bytes for all null/false fields!
        // Total = 1 + 1 + 4 = 6 bytes!
        Assert.Equal(6, bytes.Length);

        var deserialized = ApexPackSerializer.Deserialize<BitPackedEntity>(bytes);
        Assert.Equal(100, deserialized.Id);
        Assert.False(deserialized.Flag1);
        Assert.False(deserialized.Flag2);
        Assert.False(deserialized.Flag3);
        Assert.False(deserialized.Flag4);
        Assert.Null(deserialized.NullableInt1);
        Assert.Null(deserialized.NullableInt2);
        Assert.Null(deserialized.OptionalString1);
        Assert.Null(deserialized.OptionalString2);
    }

    [Fact]
    public void BitPackedEntity_MixedValues_Roundtrip()
    {
        var entity = new BitPackedEntity
        {
            Id = 200,
            Flag1 = true,
            Flag2 = false,
            Flag3 = true,
            Flag4 = false,
            NullableInt1 = 12345,
            NullableInt2 = null,
            OptionalString1 = null,
            OptionalString2 = "BitPacked!"
        };

        byte[] bytes = ApexPackSerializer.Serialize(entity);
        var deserialized = ApexPackSerializer.Deserialize<BitPackedEntity>(bytes);

        Assert.Equal(200, deserialized.Id);
        Assert.True(deserialized.Flag1);
        Assert.False(deserialized.Flag2);
        Assert.True(deserialized.Flag3);
        Assert.False(deserialized.Flag4);
        Assert.Equal(12345, deserialized.NullableInt1);
        Assert.Null(deserialized.NullableInt2);
        Assert.Null(deserialized.OptionalString1);
        Assert.Equal("BitPacked!", deserialized.OptionalString2);
    }
}
