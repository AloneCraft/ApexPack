using System;
using ApexPack;

namespace ApexPack.Tests;

[ApexPackable]
public partial struct Vector3D
{
    public float X;
    public float Y;
    public float Z;
}

[ApexPackable]
public partial struct TransformData
{
    public Vector3D Position;
    public Vector3D Rotation;
    public Vector3D Scale;
}

[ApexPackable]
public partial class SimpleEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Health { get; set; }
}

[ApexPackable]
public partial class BitPackedEntity
{
    public int Id { get; set; }
    public bool Flag1 { get; set; }
    public bool Flag2 { get; set; }
    public bool Flag3 { get; set; }
    public bool Flag4 { get; set; }
    public int? NullableInt1 { get; set; }
    public int? NullableInt2 { get; set; }
    public string? OptionalString1 { get; set; }
    public string? OptionalString2 { get; set; }
}

[ApexPackable]
public partial class Node
{
    public int Value { get; set; }
    public Node? Next { get; set; }
}
