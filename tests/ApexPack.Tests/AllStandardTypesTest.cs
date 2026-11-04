using System;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class AllStandardTypesTest
{
    [Fact]
    public void AllStandardPrimitives_Roundtrip_Succeeds()
    {
        var model = new AllStandardTypesModel
        {
            ByteVal = 250,
            SByteVal = -120,
            CharVal = 'あ',
            ShortVal = -32000,
            UShortVal = 65000,
            IntVal = -2000000000,
            UIntVal = 4000000000U,
            LongVal = -9000000000000000000L,
            ULongVal = 18000000000000000000UL,
            FloatVal = 3.14159f,
            DoubleVal = 2.718281828459045,
            BoolVal = true,
            DecimalVal = 123456789.987654321m,
            DateTimeVal = new DateTime(2026, 11, 4, 18, 30, 45, DateTimeKind.Utc),
            TimeSpanVal = TimeSpan.FromHours(48.5),
            GuidVal = Guid.Parse("d3b07384-d113-44d4-9d7a-d0c8d45e468e"),
            RankVal = TestRank.Diamond,

            NullableInt = 777,
            NullableDouble = 99.99,
            NullableGuid = Guid.Parse("12345678-1234-1234-1234-1234567890ab"),
            NullableDateTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        byte[] bytes = ApexPackSerializer.Serialize(model);
        var restored = ApexPackSerializer.Deserialize<AllStandardTypesModel>(bytes);

        Assert.Equal(model.ByteVal, restored.ByteVal);
        Assert.Equal(model.SByteVal, restored.SByteVal);
        Assert.Equal(model.CharVal, restored.CharVal);
        Assert.Equal(model.ShortVal, restored.ShortVal);
        Assert.Equal(model.UShortVal, restored.UShortVal);
        Assert.Equal(model.IntVal, restored.IntVal);
        Assert.Equal(model.UIntVal, restored.UIntVal);
        Assert.Equal(model.LongVal, restored.LongVal);
        Assert.Equal(model.ULongVal, restored.ULongVal);
        Assert.Equal(model.FloatVal, restored.FloatVal);
        Assert.Equal(model.DoubleVal, restored.DoubleVal);
        Assert.True(restored.BoolVal);
        Assert.Equal(model.DecimalVal, restored.DecimalVal);
        Assert.Equal(model.DateTimeVal, restored.DateTimeVal);
        Assert.Equal(model.TimeSpanVal, restored.TimeSpanVal);
        Assert.Equal(model.GuidVal, restored.GuidVal);
        Assert.Equal(TestRank.Diamond, restored.RankVal);

        Assert.Equal(777, restored.NullableInt);
        Assert.Equal(99.99, restored.NullableDouble);
        Assert.Equal(model.NullableGuid, restored.NullableGuid);
        Assert.Equal(model.NullableDateTime, restored.NullableDateTime);
    }
}
