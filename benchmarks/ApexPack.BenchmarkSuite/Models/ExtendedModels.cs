using System;
using System.Collections.Generic;
using ApexPack;
using MemoryPack;

namespace ApexPack.BenchmarkSuite.Models;

// 文字列の符号化差(ASCII / 日本語 / 混在)を測るモデル
[ApexPackable]
[MemoryPackable]
public partial class BenchStrings
{
    public string Ascii { get; set; } = "";
    public string Japanese { get; set; } = "";
    public string Mixed { get; set; } = "";
}

// パディングを含む unmanaged 構造体(MemoryPack はパディングごと blit する)
[ApexPackable]
[MemoryPackable]
public partial struct BenchPadded
{
    public byte A;
    public long B;
    public short C;
    public int D;
}

// ネストしたオブジェクト
[ApexPackable]
[MemoryPackable]
public partial class BenchAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public int Zip { get; set; }
}

[ApexPackable]
[MemoryPackable]
public partial class BenchCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public BenchAddress Address { get; set; } = new();
}

[ApexPackable]
[MemoryPackable]
public partial class BenchOrder
{
    public long OrderId { get; set; }
    public BenchCustomer Customer { get; set; } = new();
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Quantity { get; set; }
}

// オブジェクトのリスト(小 / 大ペイロード兼用)
[ApexPackable]
[MemoryPackable]
public partial class BenchPersonList
{
    public List<BenchmarkPerson> People { get; set; } = new();
}

// bool を多数含む DTO
[ApexPackable]
[MemoryPackable]
public partial class BenchFlags
{
    public int Id { get; set; }
    public bool F0 { get; set; }
    public bool F1 { get; set; }
    public bool F2 { get; set; }
    public bool F3 { get; set; }
    public bool F4 { get; set; }
    public bool F5 { get; set; }
    public bool F6 { get; set; }
    public bool F7 { get; set; }
    public bool F8 { get; set; }
    public bool F9 { get; set; }
    public bool F10 { get; set; }
    public bool F11 { get; set; }
    public bool F12 { get; set; }
    public bool F13 { get; set; }
    public bool F14 { get; set; }
    public bool F15 { get; set; }
}

// Nullable 要素の配列
[ApexPackable]
[MemoryPackable]
public partial class BenchNullableArray
{
    public int?[]? Values { get; set; }
}
