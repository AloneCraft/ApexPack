using System;
using System.Collections.Generic;
using ApexPack;
using MemoryPack;

namespace ApexPack.BenchmarkSuite.Models;

// Tier 1 Models
[ApexPackable]
[MemoryPackable]
public partial struct BenchmarkPrimitives
{
    public int IntVal { get; set; }
    public long LongVal { get; set; }
    public double DoubleVal { get; set; }
    public float FloatVal { get; set; }
    public bool BoolVal { get; set; }
    public Guid GuidVal { get; set; }
    public DateTime DateTimeVal { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial struct BenchmarkVector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial class BenchmarkPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public double Salary { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial struct BenchmarkMixedStruct
{
    public int Code { get; set; }
    public string? Tag { get; set; }
    public double Weight { get; set; }
}
