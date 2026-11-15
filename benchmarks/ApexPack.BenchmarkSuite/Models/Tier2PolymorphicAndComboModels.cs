using System;
using System.Collections.Generic;
using ApexPack;
using MemoryPack;

namespace ApexPack.BenchmarkSuite.Models;

// Tier 2 Models: Polymorphic & Combinatorial
[ApexPackable]
[MemoryPackable]
[ApexUnion(0, typeof(BenchCircle))]
[ApexUnion(1, typeof(BenchRect))]
[MemoryPackUnion(0, typeof(BenchCircle))]
[MemoryPackUnion(1, typeof(BenchRect))]
public abstract partial class BenchShape
{
    public string? Label { get; set; }
    public double Opacity { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial class BenchCircle : BenchShape
{
    public double Radius { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial class BenchRect : BenchShape
{
    public double Width { get; set; }
    public double Height { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial class BenchCombinatorialContainer
{
    public int[]? Numbers { get; set; }
    public string?[]? Tags { get; set; }
    public List<string?>? Names { get; set; }
    public Dictionary<string, int>? Scores { get; set; }
    public BenchShape[]? Shapes { get; set; }
}
