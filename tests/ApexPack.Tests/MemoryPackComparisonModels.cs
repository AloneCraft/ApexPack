using System;
using MemoryPack;
using ApexPack;

namespace ApexPack.Tests.Comparison;

// 1. Quintessential MemoryPack Person model
[MemoryPackable]
[ApexPackable]
public partial class Person
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

// 2. Struct Model (Vector3D)
[MemoryPackable]
[ApexPackable]
public partial struct MpVector3
{
    public float X;
    public float Y;
    public float Z;
}

// 3. Multi-primitive Model
[MemoryPackable]
[ApexPackable]
public partial class MultiPrimitiveModel
{
    public int IntVal { get; set; }
    public long LongVal { get; set; }
    public double DoubleVal { get; set; }
    public float FloatVal { get; set; }
    public string StringVal { get; set; } = "";
}

// 4. Sparse / Nullable Model (where ApexPack's bit-packing excels)
[MemoryPackable]
[ApexPackable]
public partial class SparseNullableModel
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public bool HasSubscription { get; set; }
    public bool IsAdmin { get; set; }
    public int? Level { get; set; }
    public double? Rating { get; set; }
    public long? Balance { get; set; }
    public string? Nickname { get; set; }
    public string? Bio { get; set; }
}

// 5. Circular Node Model
[MemoryPackable]
[ApexPackable]
public partial class MpNode
{
    public int Id { get; set; }
    public MpNode? Next { get; set; }
}
