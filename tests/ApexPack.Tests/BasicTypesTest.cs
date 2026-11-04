using System;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class BasicTypesTest
{
    [Fact]
    public void UnmanagedStruct_DirectBlit_Roundtrip()
    {
        var original = new TransformData
        {
            Position = new Vector3D { X = 1.0f, Y = 2.0f, Z = 3.0f },
            Rotation = new Vector3D { X = 0.5f, Y = 1.5f, Z = 2.5f },
            Scale = new Vector3D { X = 10.0f, Y = 10.0f, Z = 10.0f }
        };

        byte[] bytes = ApexPackSerializer.Serialize(original);
        Assert.Equal(sizeof(float) * 9, bytes.Length); // Exactly 36 bytes (pure memory blit, 0 per-member overhead!)

        var deserialized = ApexPackSerializer.Deserialize<TransformData>(bytes);
        Assert.Equal(original.Position.X, deserialized.Position.X);
        Assert.Equal(original.Rotation.Y, deserialized.Rotation.Y);
        Assert.Equal(original.Scale.Z, deserialized.Scale.Z);
    }

    [Fact]
    public void SimpleEntity_Roundtrip()
    {
        var original = new SimpleEntity
        {
            Id = 42,
            Name = "ApexHero",
            Health = 99.5
        };

        byte[] bytes = ApexPackSerializer.Serialize(original);
        var deserialized = ApexPackSerializer.Deserialize<SimpleEntity>(bytes);

        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Health, deserialized.Health);
    }

    [Fact]
    public void SpanBased_ZeroAlloc_Serialization()
    {
        var original = new Vector3D { X = 10.0f, Y = 20.0f, Z = 30.0f };
        Span<byte> buffer = stackalloc byte[12];

        int written = ApexPackSerializer.Serialize(buffer, original);
        Assert.Equal(12, written);

        var deserialized = ApexPackSerializer.Deserialize<Vector3D>(buffer);
        Assert.Equal(10.0f, deserialized.X);
        Assert.Equal(20.0f, deserialized.Y);
        Assert.Equal(30.0f, deserialized.Z);
    }
}
