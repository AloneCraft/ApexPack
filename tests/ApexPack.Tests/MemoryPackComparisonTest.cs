using System;
using Xunit;
using MemoryPack;
using ApexPack;
using ApexPack.Tests.Comparison;

namespace ApexPack.Tests;

public class MemoryPackComparisonTest
{
    [Fact]
    public void Person_Roundtrip_Comparison()
    {
        var person = new Person { Age = 32, Name = "Yoshifumi Kawai" };

        // MemoryPack roundtrip
        byte[] mpBytes = MemoryPackSerializer.Serialize(person);
        var mpResult = MemoryPackSerializer.Deserialize<Person>(mpBytes);

        // ApexPack roundtrip
        byte[] apexBytes = ApexPackSerializer.Serialize(person);
        var apexResult = ApexPackSerializer.Deserialize<Person>(apexBytes);

        // Verify correctness
        Assert.NotNull(mpResult);
        Assert.NotNull(apexResult);
        Assert.Equal(person.Age, mpResult.Age);
        Assert.Equal(person.Name, mpResult.Name);

        Assert.Equal(person.Age, apexResult.Age);
        Assert.Equal(person.Name, apexResult.Name);
    }

    [Fact]
    public void UnmanagedStruct_MemoryBlit_Comparison()
    {
        var vector = new MpVector3 { X = 1.23f, Y = 4.56f, Z = 7.89f };

        byte[] mpBytes = MemoryPackSerializer.Serialize(vector);
        var mpResult = MemoryPackSerializer.Deserialize<MpVector3>(mpBytes);

        byte[] apexBytes = ApexPackSerializer.Serialize(vector);
        var apexResult = ApexPackSerializer.Deserialize<MpVector3>(apexBytes);

        // Both serializers perform direct memory blit: exactly sizeof(float) * 3 = 12 bytes!
        Assert.Equal(12, mpBytes.Length);
        Assert.Equal(12, apexBytes.Length);

        Assert.Equal(vector.X, mpResult.X);
        Assert.Equal(vector.Y, mpResult.Y);
        Assert.Equal(vector.Z, mpResult.Z);

        Assert.Equal(vector.X, apexResult.X);
        Assert.Equal(vector.Y, apexResult.Y);
        Assert.Equal(vector.Z, apexResult.Z);
    }

    [Fact]
    public void MultiPrimitiveModel_Roundtrip_Comparison()
    {
        var model = new MultiPrimitiveModel
        {
            IntVal = 42,
            LongVal = 9876543210L,
            DoubleVal = 3.141592653589793,
            FloatVal = 2.718f,
            StringVal = "High Performance Serializer"
        };

        byte[] mpBytes = MemoryPackSerializer.Serialize(model);
        var mpResult = MemoryPackSerializer.Deserialize<MultiPrimitiveModel>(mpBytes);

        byte[] apexBytes = ApexPackSerializer.Serialize(model);
        var apexResult = ApexPackSerializer.Deserialize<MultiPrimitiveModel>(apexBytes);

        Assert.NotNull(mpResult);
        Assert.NotNull(apexResult);
        Assert.Equal(model.IntVal, apexResult.IntVal);
        Assert.Equal(model.LongVal, apexResult.LongVal);
        Assert.Equal(model.DoubleVal, apexResult.DoubleVal);
        Assert.Equal(model.FloatVal, apexResult.FloatVal);
        Assert.Equal(model.StringVal, apexResult.StringVal);
    }

    [Fact]
    public void SparseNullable_PayloadSize_ApexPackIsSignificantlySmallerThanMemoryPack()
    {
        // When all nullable and bool fields are null/false
        var sparse = new SparseNullableModel
        {
            Id = 100,
            IsActive = false,
            IsVerified = false,
            HasSubscription = false,
            IsAdmin = false,
            Level = null,
            Rating = null,
            Balance = null,
            Nickname = null,
            Bio = null
        };

        byte[] mpBytes = MemoryPackSerializer.Serialize(sparse);
        byte[] apexBytes = ApexPackSerializer.Serialize(sparse);

        // MemoryPack stores:
        // - Object header / member count
        // - 1 byte tag for each bool
        // - 1 byte tag for each nullable / null string
        // ApexPack stores:
        // - 2 バイトのマスク(非 null の印 1 ビット + bool 4 + Nullable 3 + string? 2 = 10 ビット)。null 用のタグは不要
        // - 4 bytes Id
        // - null / false のメンバーは 0 バイト
        Assert.True(apexBytes.Length < mpBytes.Length,
            $"ApexPack ({apexBytes.Length} bytes) should be smaller than MemoryPack ({mpBytes.Length} bytes) due to bit-packing!");

        // Verify deserialization correctness
        var apexResult = ApexPackSerializer.Deserialize<SparseNullableModel>(apexBytes);
        Assert.Equal(100, apexResult.Id);
        Assert.False(apexResult.IsActive);
        Assert.Null(apexResult.Level);
        Assert.Null(apexResult.Bio);
    }

    [Fact]
    public void SparseNullable_WithValues_ApexPackMaintainsCorrectnessAndCompaction()
    {
        var model = new SparseNullableModel
        {
            Id = 500,
            IsActive = true,
            IsVerified = false,
            HasSubscription = true,
            IsAdmin = false,
            Level = 99,
            Rating = null,
            Balance = 1_000_000L,
            Nickname = "ApexGamer",
            Bio = null
        };

        byte[] mpBytes = MemoryPackSerializer.Serialize(model);
        var mpResult = MemoryPackSerializer.Deserialize<SparseNullableModel>(mpBytes);

        byte[] apexBytes = ApexPackSerializer.Serialize(model);
        var apexResult = ApexPackSerializer.Deserialize<SparseNullableModel>(apexBytes);

        Assert.NotNull(mpResult);
        Assert.NotNull(apexResult);
        Assert.Equal(mpResult.Id, apexResult.Id);
        Assert.Equal(mpResult.IsActive, apexResult.IsActive);
        Assert.Equal(mpResult.IsVerified, apexResult.IsVerified);
        Assert.Equal(mpResult.HasSubscription, apexResult.HasSubscription);
        Assert.Equal(mpResult.IsAdmin, apexResult.IsAdmin);
        Assert.Equal(mpResult.Level, apexResult.Level);
        Assert.Equal(mpResult.Rating, apexResult.Rating);
        Assert.Equal(mpResult.Balance, apexResult.Balance);
        Assert.Equal(mpResult.Nickname, apexResult.Nickname);
        Assert.Equal(mpResult.Bio, apexResult.Bio);

        // Even with values present, bools in ApexPack take 0 payload bytes!
        Assert.True(apexBytes.Length < mpBytes.Length,
            $"ApexPack ({apexBytes.Length} B) should still be more compact than MemoryPack ({mpBytes.Length} B)");
    }
}
