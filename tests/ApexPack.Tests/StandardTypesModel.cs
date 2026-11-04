using System;
using System.Collections.Generic;
using ApexPack;

namespace ApexPack.Tests;

public enum TestRank : byte
{
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4,
    Diamond = 5
}

[ApexPackable]
public partial class AllStandardTypesModel
{
    public byte ByteVal { get; set; }
    public sbyte SByteVal { get; set; }
    public char CharVal { get; set; }
    public short ShortVal { get; set; }
    public ushort UShortVal { get; set; }
    public int IntVal { get; set; }
    public uint UIntVal { get; set; }
    public long LongVal { get; set; }
    public ulong ULongVal { get; set; }
    public float FloatVal { get; set; }
    public double DoubleVal { get; set; }
    public bool BoolVal { get; set; }
    public decimal DecimalVal { get; set; }
    public DateTime DateTimeVal { get; set; }
    public TimeSpan TimeSpanVal { get; set; }
    public Guid GuidVal { get; set; }
    public TestRank RankVal { get; set; }

    // Nullable variants (bit-packed)
    public int? NullableInt { get; set; }
    public double? NullableDouble { get; set; }
    public Guid? NullableGuid { get; set; }
    public DateTime? NullableDateTime { get; set; }
}

[ApexPackable]
public partial class StringEdgeCasesModel
{
    public string? NullString { get; set; }
    public string EmptyString { get; set; } = "";
    public string JapaneseString { get; set; } = "";
    public string EmojiString { get; set; } = "";
    public string LongString { get; set; } = "";
}

[ApexPackable]
public partial class CollectionContainerModel
{
    public int[]? UnmanagedArray { get; set; }
    public List<int>? UnmanagedList { get; set; }
    public List<SimpleEntity>? EntityList { get; set; }
}
