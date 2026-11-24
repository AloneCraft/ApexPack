using MemoryPack;

namespace ApexPack.BenchmarkSuite.Models;

[ApexPackable, MemoryPackable]
public partial struct BenchFixedValue16
{
    public int A, B, C, D;
}

[ApexPackable, MemoryPackable]
public partial struct BenchFixedValue128
{
    public BenchFixedValue16 A, B, C, D, E, F, G, H;
}

[ApexPackable, MemoryPackable]
public partial class BenchFixedClass16
{
    public int A, B, C, D;
}

[ApexPackable, MemoryPackable]
public partial class BenchFixedClass128
{
    public BenchFixedValue16 A, B, C, D, E, F, G, H;
}

[ApexPackable, MemoryPackable]
public partial struct BenchTextValue
{
    public int Id;
    public string Text;
}

[ApexPackable, MemoryPackable]
public partial class BenchTextClass
{
    public int Id;
    public string Text = "";
}

[ApexPackable, MemoryPackable]
public partial class BenchOptionalText
{
    public int Id;
    public string? Text;
}

[ApexPackable, MemoryPackable]
public partial class BenchRequiredChild
{
    public BenchFixedClass16 Child = null!;
}

[ApexPackable, MemoryPackable]
public partial class BenchOptionalChild
{
    public BenchFixedClass16? Child;
}
