using System;
using System.Collections.Generic;
using ApexPack;
using MemoryPack;

namespace ApexPack.BenchmarkSuite.Models;

// Tier 3 Models: Sparse Nullable, Trees, Circular
[ApexPackable]
[MemoryPackable]
public partial class BenchSparseDto
{
    public int? V1 { get; set; }
    public long? V2 { get; set; }
    public double? V3 { get; set; }
    public bool? B1 { get; set; }
    public bool? B2 { get; set; }
    public bool? B3 { get; set; }
    public string? S1 { get; set; }
    public string? S2 { get; set; }
    public Guid? G1 { get; set; }
    public DateTime? D1 { get; set; }
}

[ApexPackable(PreserveReferences = true)]
[MemoryPackable(GenerateType.CircularReference)]
public partial class BenchGraphNode
{
    [MemoryPackOrder(0)] public int Id { get; set; }
    [MemoryPackOrder(1)] public string Name { get; set; } = "";
    [MemoryPackOrder(2)] public BenchGraphNode? Next { get; set; }
    [MemoryPackOrder(3)] public BenchGraphNode? Other { get; set; }
}
